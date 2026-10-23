using MealMate.Application.Features.DriverDelivery.SupervisorNotifications;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record PickupBoxCommand(
    Guid BoxId,
    string? BarcodeValue = null,
    string? ConditionPhotoStorageKey = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    Guid? DriverId = null,
    bool IsManualEntry = false,
    string? ManualBoxCode = null,
    string? ManualEntryReason = null) : ICommand<PickupBoxResponseDto>;

public sealed record CompleteBoxDeliveryCommand(
    Guid BoxId,
    string? BarcodeValue = null,
    string? ProofPhotoStorageKey = null,
    string? DeliveryOtp = null,
    string? HandoverNotes = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    Guid? DriverId = null) : ICommand<CompleteDeliveryResponseDto>;

public sealed record ConfirmCustomerArrivalCommand(
    Guid BoxId,
    decimal? Latitude = null,
    decimal? Longitude = null,
    Guid? DriverId = null) : ICommand<CustomerArrivalResponseDto>;

public sealed record ReportDeliveryDelayCommand(
    Guid BoxId,
    int DelayMinutes,
    string? Reason = null,
    Guid? DriverId = null) : ICommand<bool>;

public sealed record FailDeliveryCommand(
    Guid BoxId,
    string ReasonCategory,
    string? Notes = null,
    string? ProofPhotoStorageKey = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    Guid? DriverId = null) : ICommand<FailedDeliveryResponseDto>;

public sealed record ReturnBoxToKitchenCommand(
    Guid BoxId,
    string? Reason = null,
    string? Notes = null,
    string? ReturnProofPhotoStorageKey = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    Guid? DriverId = null) : ICommand<ReturnBoxResponseDto>;

public sealed record RequestReassignmentCommand(
    Guid BoxId,
    string Reason,
    string? Notes = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    Guid? DriverId = null) : ICommand<ReassignmentRequestResponseDto>;

public sealed record GetCustomerCallProxyQuery(
    Guid BoxId,
    Guid? DriverId = null) : IQuery<CustomerCallProxyResponseDto>;

public sealed record ReportDriverIssueCommand(
    Guid BoxId,
    string IssueType,
    string? Description = null,
    Guid? DriverId = null,
    string? EvidencePhotoStorageKey = null,
    string[]? EvidencePhotoStorageKeys = null,
    string Severity = "Medium") : ICommand<Guid>;

public sealed class DriverDeliveryOrderCommandHandlers
    : ICommandHandler<PickupBoxCommand, PickupBoxResponseDto>,
      ICommandHandler<CompleteBoxDeliveryCommand, CompleteDeliveryResponseDto>,
      ICommandHandler<ConfirmCustomerArrivalCommand, CustomerArrivalResponseDto>,
      ICommandHandler<ReportDeliveryDelayCommand, bool>,
      ICommandHandler<FailDeliveryCommand, FailedDeliveryResponseDto>,
      ICommandHandler<ReturnBoxToKitchenCommand, ReturnBoxResponseDto>,
      ICommandHandler<RequestReassignmentCommand, ReassignmentRequestResponseDto>,
      IQueryHandler<GetCustomerCallProxyQuery, CustomerCallProxyResponseDto>,
      ICommandHandler<ReportDriverIssueCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IRestaurantDeliveryNotifier? _restaurantNotifier;
    private readonly ICustomerPushNotifier? _customerPush;
    private readonly IFileStorage? _fileStorage;
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;
    private readonly MealMate.Application.Common.Storage.SupportEvidenceScanService? _evidenceScans;

    /// <summary>Text cap matching the 250-character counter on screens 08.01/08.02.</summary>
    private const int MaxIssueTextLength = 250;

    /// <summary>Evidence photo cap on the report-issue request (screen 08.01 design).</summary>
    private const int MaxIssueEvidencePhotos = 3;

    public DriverDeliveryOrderCommandHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IRestaurantDeliveryNotifier? restaurantNotifier = null,
        ICustomerPushNotifier? customerPushNotifier = null,
        IFileStorage? fileStorage = null,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null,
        MealMate.Application.Common.Storage.SupportEvidenceScanService? evidenceScans = null)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _restaurantNotifier = restaurantNotifier;
        _customerPush = customerPushNotifier;
        _fileStorage = fileStorage;
        _transactions = transactions;
        _locks = locks;
        _evidenceScans = evidenceScans;
    }

    public Task<Result<PickupBoxResponseDto>> Handle(PickupBoxCommand request, CancellationToken cancellationToken)
        => ExecuteAssignedStopMutationAsync(request.BoxId, request.DriverId,
            token => PickupBoxCoreAsync(request, token), cancellationToken);

    private async Task<Result<PickupBoxResponseDto>> PickupBoxCoreAsync(PickupBoxCommand request, CancellationToken cancellationToken)
    {
        var stop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        string codeToVerify;
        if (request.IsManualEntry)
        {
            // 1. Manual entry validation: code must be supplied
            var manualCode = !string.IsNullOrWhiteSpace(request.ManualBoxCode)
                ? request.ManualBoxCode
                : request.BarcodeValue;

            if (string.IsNullOrWhiteSpace(manualCode))
            {
                return Error.Validation("Orders.ManualBoxCodeRequired", _messages.IsArabic()
                    ? "يرجى إدخال كود الصندوق يدوياً."
                    : "Please enter the box code manually.");
            }

            // 2. Strict Requirement: Condition/Label photo is MANDATORY for manual entry for administrative review and audit!
            if (string.IsNullOrWhiteSpace(request.ConditionPhotoStorageKey))
            {
                return Error.Validation("Orders.ManualEntryLabelPhotoRequired", _messages.IsArabic()
                    ? "عند إدخال كود الصندوق يدوياً، يلزم إلزامياً تصوير ملصق البوكس بوضوح للمراجعة والتدقيق الإداري."
                    : "When entering the box code manually, a clear photo of the box label is strictly required for administrative audit review.");
            }

            codeToVerify = manualCode;
        }
        else
        {
            // 1. Mandatory barcode scan verification
            if (string.IsNullOrWhiteSpace(request.BarcodeValue))
            {
                return Error.Validation("Orders.BarcodeRequired", _messages.IsArabic()
                    ? "يجب مسح باركود فاتورة البوكس لتوثيق الاستلام."
                    : "Invoice barcode scan is required to confirm box pickup.");
            }

            // 2. Mandatory box condition photo
            if (string.IsNullOrWhiteSpace(request.ConditionPhotoStorageKey))
            {
                return Error.Validation("Orders.ConditionPhotoRequired", _messages.IsArabic()
                    ? "يجب التقاط صورة لتوثيق حالة البوكس على حدة قبل الاستلام."
                    : "A condition photo documenting each box is required before pickup.");
            }

            codeToVerify = request.BarcodeValue;
        }

        var driverId = request.DriverId;
        if (!driverId.HasValue && _currentUser.UserId.HasValue)
        {
            driverId = await _db.DriverProfiles.AsNoTracking()
                .Where(d => d.UserId == _currentUser.UserId.Value && !d.IsDeleted)
                .Select(d => (Guid?)d.Id).FirstOrDefaultAsync(cancellationToken);
        }
        if (!driverId.HasValue || string.IsNullOrWhiteSpace(request.ConditionPhotoStorageKey))
            return Error.Forbidden("Orders.ProofPhotoForbidden", _messages.IsArabic() ? "تعذر التحقق من حساب السائق أو صورة الحالة." : "The driver account or condition photo could not be verified.");
        var photoError = await ValidateProofPhotoAsync(request.ConditionPhotoStorageKey, driverId, _messages.IsArabic(), cancellationToken);
        if (photoError is not null) return photoError.Error;

        // Validate code matches the box order code or subscription day
        var cleanInputCode = codeToVerify.Trim().TrimStart('#');
        var cleanOrderCode = stop.OrderCode?.Trim().TrimStart('#') ?? string.Empty;

        var isMatch = string.IsNullOrWhiteSpace(cleanOrderCode)
            || cleanInputCode.Equals(cleanOrderCode, StringComparison.OrdinalIgnoreCase)
            || cleanOrderCode.EndsWith(cleanInputCode, StringComparison.OrdinalIgnoreCase)
            || cleanInputCode.EndsWith(cleanOrderCode, StringComparison.OrdinalIgnoreCase);

        if (!isMatch && stop.SubscriptionDayId.HasValue)
        {
            var dayIdStr = stop.SubscriptionDayId.Value.ToString("N");
            if (cleanInputCode.Contains(dayIdStr, StringComparison.OrdinalIgnoreCase))
            {
                isMatch = true;
            }
            else
            {
                isMatch = await _db.MealBarcodes
                    .AnyAsync(mb => mb.SubscriptionDayId == stop.SubscriptionDayId.Value
                        && (mb.BarcodeValue == cleanInputCode || cleanInputCode.EndsWith(mb.BarcodeValue) || mb.BarcodeValue.EndsWith(cleanInputCode)),
                        cancellationToken);
            }
        }

        if (!isMatch)
        {
            return Error.Validation(
                request.IsManualEntry ? "Orders.ManualCodeMismatch" : "Orders.BarcodeMismatch",
                _messages.IsArabic()
                    ? (request.IsManualEntry
                        ? "كود الصندوق المدخل يدوياً لا يطابق هذا البوكس. يرجى التحقق من الملصق والمحاولة مجدداً."
                        : "باركود الفاتورة الممسوح لا يطابق هذا البوكس. يرجى مسح باركود الفاتورة الصحيح للعميل.")
                    : (request.IsManualEntry
                        ? "The manually entered box code does not match this box. Please verify the label and try again."
                        : "Scanned invoice barcode does not match this box. Please scan the correct invoice barcode."));
        }

        var now = DateTime.UtcNow;
        stop.Status = TripStopStatus.PickedUp;
        if (stop.Trip is not null && stop.Trip.Status != TripStatus.InProgress)
        {
            stop.Trip.Status = TripStatus.InProgress;
            stop.Trip.StartedAtUtc ??= now;
        }

        // Record pickup scan with barcode/manual-code, documented condition photo, and manual entry audit tracking
        var scan = new PickupScan
        {
            TripId = stop.TripId,
            OrderId = stop.DailyOrderId,
            SubscriptionDayId = stop.SubscriptionDayId,
            DriverProfileId = driverId.Value,
            BarcodeValue = cleanInputCode,
            PhotoPath = request.ConditionPhotoStorageKey,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsManualEntry = request.IsManualEntry,
            ManualEntryReason = request.ManualEntryReason?.Trim(),
            ScannedAtUtc = now
        };
        _db.PickupScans.Add(scan);

        Guid? dailyOrderId = stop.DailyOrderId;
        if (!dailyOrderId.HasValue && stop.SubscriptionDayId.HasValue)
        {
            dailyOrderId = await _db.RestaurantDailyOrders
                .Where(r => r.SubscriptionDayId == stop.SubscriptionDayId.Value)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (stop.SubscriptionDayId.HasValue)
        {
            var day = await _db.SubscriptionDays
                .FirstOrDefaultAsync(d => d.Id == stop.SubscriptionDayId.Value, cancellationToken);
            if (day != null)
            {
                day.Status = SubscriptionDayStatus.OutForDelivery;
            }
        }

        if (dailyOrderId.HasValue)
        {
            var dailyOrder = await _db.RestaurantDailyOrders
                .FirstOrDefaultAsync(r => r.Id == dailyOrderId.Value, cancellationToken);
            if (dailyOrder != null)
            {
                dailyOrder.Status = OrderStatus.OnTheWay;
                dailyOrder.ModifiedAtUtc = now;
            }

            _db.OrderHandovers.Add(new OrderHandover
            {
                DailyOrderId = dailyOrderId.Value,
                TripId = stop.TripId,
                DriverProfileId = driverId,
                HandedOverAtUtc = now,
                Status = "Completed"
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var msg = _messages.IsArabic()
            ? (request.IsManualEntry
                ? $"تم تأكيد استلام البوكس {stop.OrderCode} يدوياً وتوثيق صورة الملصق للمراجعة بنجاح"
                : $"تم تأكيد استلام البوكس {stop.OrderCode} وتوثيق حالته بنجاح")
            : (request.IsManualEntry
                ? $"Box {stop.OrderCode} manually verified and label photo captured for audit successfully"
                : $"Box {stop.OrderCode} picked up and condition photo verified successfully");

        return Result.Success(new PickupBoxResponseDto(
            BoxId: stop.Id,
            BoxCode: stop.OrderCode,
            Status: "PickedUp",
            Message: msg,
            PickedUpAtUtc: now,
            IsManualEntry: request.IsManualEntry
        ));
    }

    public Task<Result<CompleteDeliveryResponseDto>> Handle(CompleteBoxDeliveryCommand request, CancellationToken cancellationToken)
        => ExecuteAssignedStopMutationAsync(request.BoxId, request.DriverId,
            token => CompleteBoxDeliveryCoreAsync(request, token), cancellationToken,
            allowDeliveredReplay: true);

    private async Task<Result<CompleteDeliveryResponseDto>> CompleteBoxDeliveryCoreAsync(CompleteBoxDeliveryCommand request, CancellationToken cancellationToken)
    {
        var stop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        var isAr = _messages.IsArabic();

        // Optional GPS pair: either both coordinates or neither, and within WGS-84
        // ranges. Rejected before any state changes (also applied to repeats so a
        // malformed body never silently "succeeds").
        var coordinatesError = ValidateOptionalCoordinates(request.Latitude, request.Longitude, isAr);
        if (coordinatesError is not null)
        {
            return coordinatesError;
        }

        // Stop ownership: an authenticated caller with a driver profile may only act
        // on stops of trips carrying an active assignment for that profile. Callers
        // without a driver profile (admin/service tokens) keep working as before.
        var driverId = request.DriverId;
        if (!driverId.HasValue && _currentUser.UserId.HasValue)
        {
            driverId = await _db.DriverProfiles
                .Where(d => d.UserId == _currentUser.UserId.Value)
                .Select(d => (Guid?)d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (driverId.HasValue)
        {
            var hasAssignment = await _db.DriverAssignments
                .AnyAsync(da => da.TripId == stop.TripId
                    && da.DriverProfileId == driverId.Value
                    && (da.Status == "Active" || stop.Status == TripStopStatus.Delivered && da.Status == "Completed"), cancellationToken);
            if (!hasAssignment)
            {
                return Error.Forbidden("Orders.DeliveryForbidden", isAr
                    ? "هذا البوكس ليس ضمن توصيلاتك النشطة."
                    : "This box is not part of your active deliveries.");
            }
        }

        // Idempotent repeat: the stop is already delivered. Return the persisted
        // original delivery time and the current per-trip completion state WITHOUT
        // touching counters, scan records, statuses, or notifications. (Concurrent
        // duplicates are additionally serialized by the TripStop xmin token; the
        // loser reloads and lands on this same path.)
        if (stop.Status == TripStopStatus.Delivered)
        {
            var existingRemaining = await CountRemainingStopsAsync(stop.TripId, stop.Id, cancellationToken);
            return Result.Success(new CompleteDeliveryResponseDto(
                BoxId: stop.Id,
                BoxCode: stop.OrderCode,
                Status: "Delivered",
                Message: isAr
                    ? $"تم تسليم البوكس {stop.OrderCode} مسبقًا"
                    : $"Box {stop.OrderCode} was already delivered",
                DeliveredAtUtc: stop.DeliveredAtUtc ?? DateTime.UtcNow,
                TripId: stop.TripId,
                IsTripCompleted: existingRemaining == 0,
                RemainingStopsCount: existingRemaining,
                DriverId: driverId,
                IsFirstDelivery: false));
        }

        // 1. Mandatory proof photo at delivery (توثيق حالة البوكس بالصورة عند التسليم إجباري)
        if (string.IsNullOrWhiteSpace(request.ProofPhotoStorageKey))
        {
            return Error.Validation("Orders.ProofPhotoRequired", isAr
                ? "يجب التقاط صورة لتوثيق تسليم البوكس وحالته عند العميل."
                : "A photo documenting box delivery and condition is required.");
        }

        // 2. Barcode & OTP are completely optional at delivery.
        // If a barcode is scanned, we verify it matches the order code to prevent accidental mixup.
        // NOTE: DeliveryOtp is deliberately NOT read anywhere — no verification system
        // exists yet, so no OTP support may be advertised (screens-07 contract).
        if (!string.IsNullOrWhiteSpace(request.BarcodeValue))
        {
            var cleanInputBarcode = request.BarcodeValue.Trim().TrimStart('#');
            var cleanOrderCode = stop.OrderCode?.Trim().TrimStart('#') ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(cleanOrderCode)
                && !cleanInputBarcode.Equals(cleanOrderCode, StringComparison.OrdinalIgnoreCase)
                && !cleanOrderCode.EndsWith(cleanInputBarcode, StringComparison.OrdinalIgnoreCase)
                && !cleanInputBarcode.EndsWith(cleanOrderCode, StringComparison.OrdinalIgnoreCase))
            {
                return Error.Validation("Orders.BarcodeMismatch", isAr
                    ? "باركود الفاتورة الممسوح عند التسليم لا يطابق هذا العميل."
                    : "Scanned delivery barcode does not match this customer's box.");
            }
        }

        // 3. Proof photo binding: the key must reference a real upload recorded by
        // the generic upload endpoint, owned by the delivering driver, and be an
        // accepted image. A non-empty key string or folder name alone proves nothing.
        var proofError = await ValidateProofPhotoAsync(
            request.ProofPhotoStorageKey.Trim(), driverId, isAr, cancellationToken);
        if (proofError is not null)
        {
            return proofError;
        }

        var now = DateTime.UtcNow;
        stop.Status = TripStopStatus.Delivered;
        stop.DeliveredAtUtc = now;

        // Update driver work session completed count
        if (driverId.HasValue)
        {
            var activeSession = await _db.DriverWorkSessions
                .FirstOrDefaultAsync(s => s.DriverProfileId == driverId.Value && s.Status == "Active", cancellationToken);

            if (activeSession is not null)
            {
                activeSession.CompletedOrdersCount += 1;
            }
        }

        // Record proof of delivery scan
        var confirmationScan = new DeliveryConfirmationScan
        {
            OrderId = stop.DailyOrderId,
            SubscriptionDayId = stop.SubscriptionDayId,
            DriverProfileId = driverId ?? Guid.Empty,
            BarcodeValue = request.BarcodeValue ?? stop.OrderCode ?? "DELIVERED",
            PhotoPath = request.ProofPhotoStorageKey,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ScannedAtUtc = now
        };
        _db.DeliveryConfirmationScans.Add(confirmationScan);

        // Synchronize linked SubscriptionDay and RestaurantDailyOrder
        if (stop.SubscriptionDayId.HasValue)
        {
            var day = await _db.SubscriptionDays
                .FirstOrDefaultAsync(d => d.Id == stop.SubscriptionDayId.Value, cancellationToken);
            if (day != null)
            {
                day.Status = SubscriptionDayStatus.Delivered;
            }
        }

        Guid? dailyOrderId = stop.DailyOrderId;
        if (!dailyOrderId.HasValue && stop.SubscriptionDayId.HasValue)
        {
            dailyOrderId = await _db.RestaurantDailyOrders
                .Where(r => r.SubscriptionDayId == stop.SubscriptionDayId.Value)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (dailyOrderId.HasValue)
        {
            var dailyOrder = await _db.RestaurantDailyOrders
                .FirstOrDefaultAsync(r => r.Id == dailyOrderId.Value, cancellationToken);
            if (dailyOrder != null)
            {
                dailyOrder.Status = OrderStatus.Delivered;
                dailyOrder.ModifiedAtUtc = now;
            }
        }

        // One count drives both the trip completion transition and the response:
        // same final-state definition (Delivered/Failed are final) for both.
        var remainingStops = await CountRemainingStopsAsync(stop.TripId, stop.Id, cancellationToken);
        var tripJustCompleted = remainingStops == 0 && stop.Trip is not null;
        if (tripJustCompleted && stop.Trip is not null)
        {
            stop.Trip.Status = TripStatus.Completed;
            stop.Trip.CompletedAtUtc = now;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Concurrent duplicate deliver: the xmin token rejected our write because
            // the other request already completed this stop. Re-read the committed
            // row and answer with the same idempotent contract instead of a 409/500.
            var fresh = await _db.TripStops
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == stop.Id, cancellationToken);

            if (fresh is not null && fresh.Status == TripStopStatus.Delivered)
            {
                var racedRemaining = await CountRemainingStopsAsync(fresh.TripId, fresh.Id, cancellationToken);
                return Result.Success(new CompleteDeliveryResponseDto(
                    BoxId: fresh.Id,
                    BoxCode: fresh.OrderCode,
                    Status: "Delivered",
                    Message: isAr
                        ? $"تم تسليم البوكس {fresh.OrderCode} مسبقًا"
                        : $"Box {fresh.OrderCode} was already delivered",
                    DeliveredAtUtc: fresh.DeliveredAtUtc ?? DateTime.UtcNow,
                    TripId: fresh.TripId,
                    IsTripCompleted: racedRemaining == 0,
                    RemainingStopsCount: racedRemaining,
                    DriverId: driverId,
                    IsFirstDelivery: false));
            }

            throw;
        }

        var customerUserId = Guid.Empty;
        var serviceDate = default(DateOnly);
        if (stop.SubscriptionDayId.HasValue)
        {
            var dayInfo = await _db.SubscriptionDays
                .Where(d => d.Id == stop.SubscriptionDayId.Value)
                .Join(_db.Subscriptions, d => d.SubscriptionId, s => s.Id,
                    (d, s) => new { CustomerUserId = s.CustomerUserId, d.ServiceDate })
                .FirstOrDefaultAsync(cancellationToken);
            if (dayInfo is not null)
            {
                customerUserId = dayInfo.CustomerUserId;
                serviceDate = dayInfo.ServiceDate;
            }
        }

        // Consolidated restaurant alert: only when the whole trip is delivered —
        // normal in-flight progress never pings the restaurant.
        if (_restaurantNotifier is not null && tripJustCompleted && stop.Trip is not null)
        {
            var deliveredCount = await _db.TripStops
                .CountAsync(s => s.TripId == stop.TripId && s.Status == TripStopStatus.Delivered, cancellationToken);
            var driverName = await _db.DriverProfiles.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == (driverId ?? Guid.Empty), cancellationToken);

            await _restaurantNotifier.NotifyTripDeliveredAsync(
                stop.Trip.RestaurantId,
                stop.TripId,
                stop.Trip?.Code ?? string.Empty,
                driverName?.FullNameAr ?? string.Empty,
                driverName?.FullNameEn ?? string.Empty,
                deliveredCount,
                cancellationToken);
        }

        // Customer push on the real customer channel, first delivery only. Push
        // failures never roll back the saved delivery (notifier contract).
        if (_customerPush is not null && customerUserId != Guid.Empty)
        {
            await _customerPush.NotifyOrderDeliveredAsync(customerUserId, stop.SubscriptionDayId!.Value, serviceDate, cancellationToken);
        }

        var msg = isAr
            ? $"تم تسليم البوكس {stop.OrderCode} وتوثيق الإثبات بنجاح"
            : $"Box {stop.OrderCode} delivered and proof recorded successfully";

        return Result.Success(new CompleteDeliveryResponseDto(
            BoxId: stop.Id,
            BoxCode: stop.OrderCode,
            Status: "Delivered",
            Message: msg,
            DeliveredAtUtc: now,
            TripId: stop.TripId,
            IsTripCompleted: tripJustCompleted,
            RemainingStopsCount: remainingStops,
            DriverId: driverId,
            IsFirstDelivery: true));
    }

    /// <summary>Non-final stops of the trip (excluding the given stop): the same
    /// final-state rule (Delivered/Failed) that decides trip completion.</summary>
    private Task<int> CountRemainingStopsAsync(Guid tripId, Guid excludeStopId, CancellationToken cancellationToken)
        => _db.TripStops
            .CountAsync(s => s.TripId == tripId
                && s.Id != excludeStopId
                && s.Status != TripStopStatus.Delivered
                && s.Status != TripStopStatus.Failed,
                cancellationToken);

    /// <summary>Optional coordinates: null pair is valid; a half pair or an out-of-range
    /// value is rejected. Returns null when the pair is acceptable.</summary>
    private static Result<CompleteDeliveryResponseDto>? ValidateOptionalCoordinates(
        decimal? latitude, decimal? longitude, bool isAr)
    {
        if (!latitude.HasValue && !longitude.HasValue)
        {
            return null;
        }

        if (latitude.HasValue != longitude.HasValue)
        {
            return Error.Validation("Orders.CoordinatesPairRequired", isAr
                ? "يجب إرسال خط العرض وخط الطول معًا، أو حذفهما معًا."
                : "Latitude and longitude must be sent together, or both omitted.");
        }

        const decimal MinLatitude = -90m, MaxLatitude = 90m;
        const decimal MinLongitude = -180m, MaxLongitude = 180m;
        if (latitude.Value < MinLatitude || latitude.Value > MaxLatitude
            || longitude.Value < MinLongitude || longitude.Value > MaxLongitude)
        {
            return Error.Validation("Orders.CoordinatesOutOfRange", isAr
                ? "إحداثيات الموقع خارج النطاق المسموح."
                : "Location coordinates are out of the allowed range.");
        }

        return null;
    }

    /// <summary>
    /// Proof binding: existence in the upload metadata (real persistence written at
    /// upload time), ownership by the delivering driver, accepted image type, and
    /// readability in the actual storage. Returns null when the proof is acceptable.
    /// </summary>
    private async Task<Result<CompleteDeliveryResponseDto>?> ValidateProofPhotoAsync(
        string proofPhotoStorageKey, Guid? driverId, bool isAr, CancellationToken cancellationToken)
    {
        var record = await _db.UploadFileRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.StorageKey == proofPhotoStorageKey, cancellationToken);

        if (record is null)
        {
            return Error.Validation("Orders.ProofPhotoNotFound", isAr
                ? "مفتاح صورة الإثبات لا يشير إلى ملفًا مرفوعًا فعليًا. ارفع الصورة عبر مسار الرفع المعتمد أولًا."
                : "The proof photo key does not reference a real uploaded file. Upload the photo through the approved upload endpoint first.");
        }

        if (record.Scope != "drivers" || record.Folder != "delivery"
            || !proofPhotoStorageKey.StartsWith("uploads/drivers/delivery/", StringComparison.Ordinal))
            return Error.Validation("Orders.ProofPhotoNotFound", isAr ? "الغرض من صورة الإثبات غير صالح." : "The proof photo purpose is invalid.");

        if (driverId.HasValue)
        {
            var ownedByDriver = record.UploadedByUserId.HasValue && await _db.DriverProfiles
                .AnyAsync(d => d.Id == driverId.Value
                    && d.UserId == record.UploadedByUserId.Value, cancellationToken);
            if (!ownedByDriver)
            {
                return Error.Forbidden("Orders.ProofPhotoForbidden", isAr
                    ? "صورة الإثبات لا تنتمي إلى حسابك. ارفع صورة الإثبات من حسابك نفسه."
                    : "The proof photo does not belong to your account. Upload the proof photo from your own account.");
            }
        }

        var contentType = record.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (contentType != "image/jpeg" && contentType != "image/jpg"
            && contentType != "image/png" && contentType != "image/webp")
        {
            return Error.Validation("Orders.ProofPhotoInvalidType", isAr
                ? "صورة الإثبات يجب أن تكون بصيغة JPEG أو PNG أو WebP."
                : "The proof photo must be a JPEG, PNG, or WebP image.");
        }

        if (_fileStorage is not null && !await _fileStorage.ExistsAsync(proofPhotoStorageKey, cancellationToken))
        {
            return Error.Validation("Orders.ProofPhotoNotFound", isAr
                ? "ملف صورة الإثبات لم يعد موجودًا في التخزين. أعد رفع الصورة."
                : "The proof photo file is no longer available in storage. Please re-upload the photo.");
        }
        if (_evidenceScans is null) return Error.Failure("UPLOAD_EVIDENCE_SCAN_FAILED", "Evidence scanning is unavailable.");
        var scan = await _evidenceScans.ReadCleanAsync(record, cancellationToken);
        if (scan.Bytes is null || !MealMate.Application.Features.UnifiedSupport.SupportCaseService.HasValidSignature(
                scan.Bytes, contentType == "image/jpg" ? "image/jpeg" : contentType))
            return Error.Validation("Orders.ProofPhotoInvalidType", isAr ? "صورة الإثبات غير صالحة." : "The proof photo is invalid.");

        return null;
    }

    public Task<Result<Guid>> Handle(ReportDriverIssueCommand request, CancellationToken cancellationToken)
        => ExecuteAssignedStopMutationAsync(request.BoxId, request.DriverId,
            token => ReportDriverIssueCoreAsync(request, token), cancellationToken);

    private async Task<Result<Guid>> ReportDriverIssueCoreAsync(ReportDriverIssueCommand request, CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        // 1. Fixed value sets (screen 08.01 design). Values are matched
        // case-insensitively and persisted in canonical machine form.
        var issueType = DriverIssueCatalog.NormalizeIssueType(request.IssueType);
        if (issueType is null)
        {
            return Error.Validation(MessageCodes.DriverIssues.Report.IssueTypeInvalid, isAr
                ? $"نوع المشكلة غير مسموح. القيم المسموحة: {string.Join("، ", DriverIssueCatalog.IssueTypes.Select(t => t.Value))}."
                : $"Issue type is not allowed. Allowed values: {string.Join(", ", DriverIssueCatalog.IssueTypes.Select(t => t.Value))}.");
        }

        var severity = DriverIssueCatalog.NormalizeSeverity(request.Severity);
        if (severity is null)
        {
            return Error.Validation(MessageCodes.DriverIssues.Report.SeverityInvalid, isAr
                ? "الخطورة غير مسموحة. القيم المسموحة: Low، Medium، High."
                : "Severity is not allowed. Allowed values: Low, Medium, High.");
        }

        // 2. Description is optional (design shows a 250-character counter).
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description is not null && description.Length > MaxIssueTextLength)
        {
            return Error.Validation(MessageCodes.DriverIssues.Report.DescriptionTooLong, isAr
                ? $"وصف المشكلة يجب ألا يتجاوز {MaxIssueTextLength} حرفًا."
                : $"The issue description must not exceed {MaxIssueTextLength} characters.");
        }

        // 3. Evidence photos: merge the legacy singular key (first) with the
        // multi-key array, drop empties and duplicates, and cap at three.
        var photoKeys = MergeEvidencePhotoKeys(request.EvidencePhotoStorageKey, request.EvidencePhotoStorageKeys);
        if (photoKeys.Count > MaxIssueEvidencePhotos)
        {
            return Error.Validation(MessageCodes.DriverIssues.Report.PhotoKeysLimit, isAr
                ? $"الحد الأقصى لصور إثبات المشكلة {MaxIssueEvidencePhotos} صور."
                : $"At most {MaxIssueEvidencePhotos} evidence photos are allowed.");
        }

        // 4. The reported delivery record must exist and belong to the caller.
        var stop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);
        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        var driverId = await ResolveDriverIdentityOrNotFoundAsync(request.DriverId, cancellationToken);
        if (driverId.IsFailure)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        if (driverId.Value.HasValue)
        {
            var hasAssignment = await _db.DriverAssignments
                .AnyAsync(da => da.TripId == stop.TripId
                    && da.DriverProfileId == driverId.Value.Value
                    && da.Status == "Active", cancellationToken);
            if (!hasAssignment)
            {
                return Error.Forbidden(MessageCodes.DriverIssues.Report.StopForbidden, isAr
                    ? "هذا البوكس ليس ضمن توصيلاتك النشطة."
                    : "This box is not part of your active deliveries.");
            }
        }

        // 5. Bind every photo to a real upload owned by the reporting driver.
        foreach (var photoKey in photoKeys)
        {
            var photoError = await ValidateEvidencePhotoAsync(photoKey, driverId.Value, isAr, cancellationToken);
            if (photoError is not null)
            {
                return photoError;
            }
        }

        // 6. Persist the real support issue the dispatcher reads (04.11/04.12).
        var now = DateTime.UtcNow;
        var (labelAr, labelEn) = DriverIssueCatalog.DescribeIssueType(issueType);
        var issueLabel = isAr ? labelAr : labelEn;
        var customerUserId = await ResolveStopCustomerUserIdAsync(stop, cancellationToken);

        var complaint = new Complaint
        {
            CustomerUserId = customerUserId,
            RestaurantId = stop.Trip?.RestaurantId,
            OrderId = stop.DailyOrderId ?? stop.Id,
            SubscriptionDayId = stop.SubscriptionDayId,
            DriverProfileId = driverId.Value,
            Category = issueType,
            Title = isAr
                ? $"بلاغ سائق: {issueLabel} – {stop.OrderCode}"
                : $"Driver issue: {issueLabel} – {stop.OrderCode}",
            Description = description
                ?? (isAr
                    ? $"بلاغ سائق بدون وصف إضافي. النوع: {issueLabel}، الخطورة: {severity}، البوكس: {stop.OrderCode}."
                    : $"Driver issue without additional description. Type: {issueLabel}, Severity: {severity}, Box: {stop.OrderCode}."),
            Status = ComplaintStatus.Submitted,
            SubmittedAtUtc = now
        };
        complaint.Classifications.Add(new ComplaintClassification
        {
            CategoryCode = issueType,
            Severity = severity,
            ClassifiedByUserId = _currentUser.UserId,
            ClassifiedAtUtc = now
        });
        foreach (var photoKey in photoKeys)
        {
            var contentType = await GetUploadContentTypeAsync(photoKey, cancellationToken);
            complaint.EvidencePhotos.Add(new ComplaintEvidencePhoto
            {
                StoragePath = photoKey,
                ContentType = contentType,
                UploadedAtUtc = now
            });
        }
        _db.Complaints.Add(complaint);

        // Keep the stop-level marker other dispatcher surfaces already read.
        stop.FailureReason = $"[{issueType}] {description}".TrimEnd();

        _db.AuditLogs.Add(new MealMate.Domain.IdentityAccess.AuditLog
        {
            Action = "DriverReportedIssue",
            EntityType = "TripStop",
            EntityId = request.BoxId,
            Reason = stop.FailureReason,
            ActorUserId = _currentUser.UserId
        });

        await AddDriverSupportIssueEventAsync(complaint, stop, "created", now, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // The response identifier is the persisted complaint id — the same id the
        // dispatcher support center uses for this issue.
        return Result.Success(complaint.Id);
    }

    public Task<Result<CustomerArrivalResponseDto>> Handle(ConfirmCustomerArrivalCommand request, CancellationToken cancellationToken)
        => ExecuteAssignedStopMutationAsync(request.BoxId, request.DriverId,
            token => ConfirmCustomerArrivalCoreAsync(request, token), cancellationToken);

    private async Task<Result<CustomerArrivalResponseDto>> ConfirmCustomerArrivalCoreAsync(ConfirmCustomerArrivalCommand request, CancellationToken cancellationToken)
    {
        var stop = await _db.TripStops
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        var isAr = _messages.IsArabic();

        // Optional GPS pair — validated with the same rule as delivery, even though
        // the current arrival model does not persist these query coordinates.
        var coordinatesError = ValidateArrivalCoordinates(request.Latitude, request.Longitude, isAr);
        if (coordinatesError is not null)
        {
            return coordinatesError;
        }

        // Arrival state belongs to the assigned driver only: resolve the caller's
        // driver profile and require an active assignment on the stop's trip — the
        // response never discloses the stored customer phone (F01 containment), and
        // a rejected request saves nothing and emits nothing.
        var driverId = request.DriverId;
        if (!driverId.HasValue && _currentUser.UserId.HasValue)
        {
            driverId = await _db.DriverProfiles
                .Where(d => d.UserId == _currentUser.UserId.Value)
                .Select(d => (Guid?)d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (!driverId.HasValue)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        var hasAssignment = await _db.DriverAssignments
            .AnyAsync(da => da.TripId == stop.TripId
                && da.DriverProfileId == driverId.Value
                && da.Status == "Active", cancellationToken);

        if (!hasAssignment)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        // Idempotent repeat: return the persisted first-arrival instant, never a new
        // one, and never re-emit the arrival notification.
        if (stop.ArrivedAtUtc.HasValue)
        {
            var customerName = stop.CustomerName ?? (isAr ? "عميل ميل ميت" : "MealMate Customer");
            var repeatMsg = isAr
                ? $"تم تأكيد الوصول لموقع العميل {customerName} مسبقًا"
                : $"Arrival at customer {customerName} was already confirmed";

            return Result.Success(new CustomerArrivalResponseDto(
                BoxId: stop.Id,
                BoxCode: stop.OrderCode,
                CustomerName: customerName,
                Status: "Arrived",
                Message: repeatMsg,
                ArrivedAtUtc: stop.ArrivedAtUtc.Value,
                DriverId: driverId,
                IsFirstArrival: false));
        }

        var now = DateTime.UtcNow;
        stop.ArrivedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);

        var arrivalCustomerName = stop.CustomerName ?? (isAr ? "عميل ميل ميت" : "MealMate Customer");
        var msg = isAr
            ? $"تم تأكيد الوصول لموقع العميل {arrivalCustomerName}"
            : $"Arrival confirmed at customer {arrivalCustomerName}";

        // Customer push on the real channel, first arrival only.
        if (_customerPush is not null && stop.SubscriptionDayId.HasValue)
        {
            var dayInfo = await _db.SubscriptionDays
                .Where(d => d.Id == stop.SubscriptionDayId.Value)
                .Join(_db.Subscriptions, d => d.SubscriptionId, s => s.Id,
                    (d, s) => new { CustomerUserId = s.CustomerUserId, d.ServiceDate })
                .FirstOrDefaultAsync(cancellationToken);

            if (dayInfo is not null && dayInfo.CustomerUserId != Guid.Empty)
            {
                await _customerPush.NotifyDriverArrivedAsync(
                    dayInfo.CustomerUserId, stop.SubscriptionDayId.Value, dayInfo.ServiceDate, cancellationToken);
            }
        }

        return Result.Success(new CustomerArrivalResponseDto(
            BoxId: stop.Id,
            BoxCode: stop.OrderCode,
            CustomerName: arrivalCustomerName,
            Status: "Arrived",
            Message: msg,
            ArrivedAtUtc: now,
            DriverId: driverId,
            IsFirstArrival: true));
    }

    /// <summary>Arrival coordinates share the delivery pair/range rule.</summary>
    private static Result<CustomerArrivalResponseDto>? ValidateArrivalCoordinates(
        decimal? latitude, decimal? longitude, bool isAr)
    {
        if (!latitude.HasValue && !longitude.HasValue)
        {
            return null;
        }

        if (latitude.HasValue != longitude.HasValue)
        {
            return Error.Validation("Orders.CoordinatesPairRequired", isAr
                ? "يجب إرسال خط العرض وخط الطول معًا، أو حذفهما معًا."
                : "Latitude and longitude must be sent together, or both omitted.");
        }

        const decimal MinLatitude = -90m, MaxLatitude = 90m;
        const decimal MinLongitude = -180m, MaxLongitude = 180m;
        if (latitude.Value < MinLatitude || latitude.Value > MaxLatitude
            || longitude.Value < MinLongitude || longitude.Value > MaxLongitude)
        {
            return Error.Validation("Orders.CoordinatesOutOfRange", isAr
                ? "إحداثيات الموقع خارج النطاق المسموح."
                : "Location coordinates are out of the allowed range.");
        }

        return null;
    }

    public Task<Result<bool>> Handle(ReportDeliveryDelayCommand request, CancellationToken cancellationToken)
        => ExecuteAssignedStopMutationAsync(request.BoxId, request.DriverId,
            token => ReportDeliveryDelayCoreAsync(request, token), cancellationToken);

    private async Task<Result<bool>> ReportDeliveryDelayCoreAsync(ReportDeliveryDelayCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var descriptionPrefix = $"Delay reported: {request.DelayMinutes} minutes.";
        var description = reason is null ? descriptionPrefix : $"{descriptionPrefix} Reason: {reason}";
        if (request.DelayMinutes is < 1 or > 1440 || description.Length > MaxIssueTextLength)
        {
            return Error.Validation("DRIVER_DELAY_INVALID", _messages.GetMessage("driver_issue.delay_invalid", "Delay must be between 1 and 1440 minutes and the reason cannot exceed 250 characters."));
        }
        var stop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }
        var driverId = await _db.DriverProfiles.AsNoTracking()
            .Where(d => d.UserId == _currentUser.UserId.Value && !d.IsDeleted)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!driverId.HasValue || !await _db.DriverAssignments.AsNoTracking().AnyAsync(a =>
                a.TripId == stop.TripId && a.DriverProfileId == driverId.Value && a.Status == "Active", cancellationToken))
        {
            return Error.Forbidden(MessageCodes.DriverIssues.Report.StopForbidden,
                _messages.GetMessage(MessageCodes.DriverIssues.Report.StopForbidden, "This stop is not assigned to your active trip."));
        }
        if (stop.Status is TripStopStatus.Delivered or TripStopStatus.ReturnedToKitchen)
        {
            return Error.Conflict("DRIVER_DELAY_STOP_TERMINAL", _messages.GetMessage("driver_issue.delay_stop_terminal", "A delay cannot be reported for a completed stop."));
        }

        var complaint = await Handle(new ReportDriverIssueCommand(request.BoxId, "SevereDelay", description,
            driverId.Value, Severity: "Medium"), cancellationToken);
        if (complaint.IsFailure) return complaint.Error;
        return Result.Success(true);
    }

    public Task<Result<FailedDeliveryResponseDto>> Handle(FailDeliveryCommand request, CancellationToken cancellationToken)
        => ExecuteAssignedStopMutationAsync(request.BoxId, request.DriverId,
            token => FailDeliveryCoreAsync(request, token), cancellationToken);

    private async Task<Result<FailedDeliveryResponseDto>> FailDeliveryCoreAsync(FailDeliveryCommand request, CancellationToken cancellationToken)
    {
        var stop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        var now = DateTime.UtcNow;
        stop.Status = TripStopStatus.Failed;
        stop.FailureReason = $"[{request.ReasonCategory}] {request.Notes}";

        // Synchronize linked SubscriptionDay and RestaurantDailyOrder
        if (stop.SubscriptionDayId.HasValue)
        {
            var day = await _db.SubscriptionDays
                .FirstOrDefaultAsync(d => d.Id == stop.SubscriptionDayId.Value, cancellationToken);
            if (day != null)
            {
                day.Status = SubscriptionDayStatus.Failed;
            }
        }

        Guid? dailyOrderId = stop.DailyOrderId;
        if (!dailyOrderId.HasValue && stop.SubscriptionDayId.HasValue)
        {
            dailyOrderId = await _db.RestaurantDailyOrders
                .Where(r => r.SubscriptionDayId == stop.SubscriptionDayId.Value)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (dailyOrderId.HasValue)
        {
            var dailyOrder = await _db.RestaurantDailyOrders
                .FirstOrDefaultAsync(r => r.Id == dailyOrderId.Value, cancellationToken);
            if (dailyOrder != null)
            {
                dailyOrder.Status = OrderStatus.DeliveryFailed;
                dailyOrder.ModifiedAtUtc = now;
            }
        }

        _db.AuditLogs.Add(new MealMate.Domain.IdentityAccess.AuditLog
        {
            Action = "DeliveryFailed",
            EntityType = "TripStop",
            EntityId = request.BoxId,
            Reason = stop.FailureReason,
            ActorUserId = _currentUser.UserId
        });

        await _db.SaveChangesAsync(cancellationToken);

        var isAr = _messages.IsArabic();
        var msg = isAr
            ? $"تم تسجيل تعذر التسليم للبوكس {stop.OrderCode} وإشعار المشرف."
            : $"Delivery failed registered for box {stop.OrderCode} and dispatcher notified.";

        return Result.Success(new FailedDeliveryResponseDto(
            BoxId: stop.Id,
            BoxCode: stop.OrderCode,
            Status: "Failed",
            ReasonCategory: request.ReasonCategory,
            Message: msg,
            FailedAtUtc: now
        ));
    }

    public Task<Result<ReturnBoxResponseDto>> Handle(ReturnBoxToKitchenCommand request, CancellationToken cancellationToken)
        => ExecuteAssignedStopMutationAsync(request.BoxId, request.DriverId,
            token => ReturnBoxToKitchenCoreAsync(request, token), cancellationToken);

    private async Task<Result<ReturnBoxResponseDto>> ReturnBoxToKitchenCoreAsync(ReturnBoxToKitchenCommand request, CancellationToken cancellationToken)
    {
        var stop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        var now = DateTime.UtcNow;
        stop.Status = TripStopStatus.ReturnedToKitchen;
        stop.FailureReason = $"ReturnedToKitchen: [{request.Reason}] {request.Notes}";

        // Synchronize linked SubscriptionDay and RestaurantDailyOrder
        if (stop.SubscriptionDayId.HasValue)
        {
            var day = await _db.SubscriptionDays
                .FirstOrDefaultAsync(d => d.Id == stop.SubscriptionDayId.Value, cancellationToken);
            if (day != null)
            {
                day.Status = SubscriptionDayStatus.Preparing;
            }
        }

        Guid? dailyOrderId = stop.DailyOrderId;
        if (!dailyOrderId.HasValue && stop.SubscriptionDayId.HasValue)
        {
            dailyOrderId = await _db.RestaurantDailyOrders
                .Where(r => r.SubscriptionDayId == stop.SubscriptionDayId.Value)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (dailyOrderId.HasValue)
        {
            var dailyOrder = await _db.RestaurantDailyOrders
                .FirstOrDefaultAsync(r => r.Id == dailyOrderId.Value, cancellationToken);
            if (dailyOrder != null)
            {
                dailyOrder.Status = OrderStatus.Hold;
                dailyOrder.ModifiedAtUtc = now;
            }
        }

        _db.AuditLogs.Add(new MealMate.Domain.IdentityAccess.AuditLog
        {
            Action = "BoxReturnedToKitchen",
            EntityType = "TripStop",
            EntityId = request.BoxId,
            Reason = stop.FailureReason,
            ActorUserId = _currentUser.UserId
        });

        await _db.SaveChangesAsync(cancellationToken);

        var isAr = _messages.IsArabic();
        var msg = isAr
            ? $"تم إيداع البوكس {stop.OrderCode} بالمطبخ وإغلاق العهدة بنجاح."
            : $"Box {stop.OrderCode} returned to kitchen successfully.";

        return Result.Success(new ReturnBoxResponseDto(
            BoxId: stop.Id,
            BoxCode: stop.OrderCode,
            Status: "ReturnedToKitchen",
            Message: msg,
            ReturnedAtUtc: now
        ));
    }

    public Task<Result<ReassignmentRequestResponseDto>> Handle(RequestReassignmentCommand request, CancellationToken cancellationToken)
        => ExecuteAssignedStopMutationAsync(request.BoxId, request.DriverId,
            token => RequestReassignmentCoreAsync(request, token), cancellationToken);

    private async Task<Result<ReassignmentRequestResponseDto>> RequestReassignmentCoreAsync(RequestReassignmentCommand request, CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        // Optional GPS pair validated before any state work (same rule as delivery).
        var coordinatesError = ValidateReassignmentCoordinates(request.Latitude, request.Longitude, isAr);
        if (coordinatesError is not null)
        {
            return coordinatesError;
        }

        var stop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        // The requesting driver must resolve from the token/query; the reassignment
        // record is meaningless without its requester (the dispatcher replacement
        // flow refuses to assign the same driver back).
        var driverId = await ResolveDriverIdentityOrNotFoundAsync(request.DriverId, cancellationToken);
        if (driverId.IsFailure || !driverId.Value.HasValue)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        // Duplicate-active check runs before the ownership check on purpose: after a
        // successful request the trip assignment is already marked Reassigned, so a
        // network retry of the same request must land on the stable 409 contract,
        // never on a confusing 403.
        var orderIdentity = stop.DailyOrderId ?? stop.Id;
        var hasActiveRequest = await _db.Complaints
            .AnyAsync(c => c.Category == "DriverReassignmentRequested"
                && c.OrderId == orderIdentity
                && (c.Status == ComplaintStatus.Submitted || c.Status == ComplaintStatus.UnderReview),
                cancellationToken);
        if (hasActiveRequest)
        {
            return Error.Conflict(MessageCodes.DriverIssues.Reassignment.RequestAlreadyActive, isAr
                ? "يوجد طلب إعادة إسناد نشط لهذا الطلب بالفعل. انتظر معالجة الطلب الحالي من فريق التشغيل."
                : "An active reassignment request already exists for this box. Wait for the dispatcher to process the current request.");
        }

        if (driverId.Value.HasValue)
        {
            var hasAssignment = await _db.DriverAssignments
                .AnyAsync(da => da.TripId == stop.TripId
                    && da.DriverProfileId == driverId.Value.Value
                    && da.Status == "Active", cancellationToken);
            if (!hasAssignment)
            {
                return Error.Forbidden(MessageCodes.DriverIssues.Reassignment.StopForbidden, isAr
                    ? "هذا البوكس ليس ضمن توصيلاتك النشطة."
                    : "This box is not part of your active deliveries.");
            }
        }

        // Only a box the driver physically holds can be handed over: the request
        // resets the stop to Pending for dispatcher re-routing, which is meaningful
        // only from PickedUp. Final states and not-yet-picked-up stops are refused.
        if (stop.Status != TripStopStatus.PickedUp)
        {
            return Error.Conflict(MessageCodes.DriverIssues.Reassignment.StopStateNotAllowed, isAr
                ? $"لا يمكن طلب إعادة إسناد في حالة الطلب الحالية ({stop.Status}). الحالة المسموحة: PickedUp."
                : $"Reassignment cannot be requested in the stop's current state ({stop.Status}). Allowed state: PickedUp.");
        }

        var reason = DriverIssueCatalog.NormalizeReassignmentReason(request.Reason);
        if (reason is null)
        {
            return Error.Validation(MessageCodes.DriverIssues.Reassignment.ReasonInvalid, isAr
                ? $"سبب إعادة الإسناد غير مسموح. القيم المسموحة: {string.Join("، ", DriverIssueCatalog.ReassignmentReasons.Select(r => r.Value))}."
                : $"Reassignment reason is not allowed. Allowed values: {string.Join(", ", DriverIssueCatalog.ReassignmentReasons.Select(r => r.Value))}.");
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is not null && notes.Length > MaxIssueTextLength)
        {
            return Error.Validation(MessageCodes.DriverIssues.Reassignment.NotesTooLong, isAr
                ? $"الملاحظات يجب ألا تتجاوز {MaxIssueTextLength} حرفًا."
                : $"The notes must not exceed {MaxIssueTextLength} characters.");
        }

        var now = DateTime.UtcNow;
        var (labelAr, labelEn) = DriverIssueCatalog.DescribeReassignmentReason(reason);
        var reasonLabel = isAr ? labelAr : labelEn;
        var customerUserId = await ResolveStopCustomerUserIdAsync(stop, cancellationToken);

        stop.FailureReason = $"[ReassignmentRequested: {reason}] {notes}".TrimEnd();

        // Create the support issue the dispatcher replacement flow (04.13) resolves.
        var complaint = new Complaint
        {
            CustomerUserId = customerUserId,
            RestaurantId = stop.Trip?.RestaurantId,
            OrderId = orderIdentity,
            SubscriptionDayId = stop.SubscriptionDayId,
            DriverProfileId = driverId.Value,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Category = "DriverReassignmentRequested",
            ReassignmentTripStopId = stop.Id,
            ReassignmentReason = reason,
            ReassignmentNotes = notes,
            Title = isAr
                ? $"إعادة إسناد الطلب {stop.OrderCode} – {reasonLabel}"
                : $"Reassignment requested for box {stop.OrderCode} – {reasonLabel}",
            Description = isAr
                ? $"[إعادة إسناد: {reason}]{(notes is null ? string.Empty : $" {notes}")}"
                : $"[Reassignment: {reason}]{(notes is null ? string.Empty : $" {notes}")}",
            Status = ComplaintStatus.Submitted,
            SubmittedAtUtc = now
        };
        _db.Complaints.Add(complaint);

        _db.AuditLogs.Add(new MealMate.Domain.IdentityAccess.AuditLog
        {
            Action = "DriverRequestedReassignment",
            EntityType = "TripStop",
            EntityId = request.BoxId,
            Reason = stop.FailureReason,
            ActorUserId = _currentUser.UserId
        });

        await AddDriverSupportIssueEventAsync(complaint, stop, "reassignment-requested", now, cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // TripStop carries an xmin concurrency token (screens-07): in a parallel
            // duplicate submission the loser's stop write is rejected. Re-read the
            // committed state: if the winner created the active request, answer with
            // the same 409 duplicate contract; anything else is a real conflict.
            var hasRequest = await _db.Complaints
                .AsNoTracking()
                .AnyAsync(c => c.Category == "DriverReassignmentRequested"
                    && c.OrderId == orderIdentity
                    && (c.Status == ComplaintStatus.Submitted || c.Status == ComplaintStatus.UnderReview),
                    cancellationToken);
            if (hasRequest)
            {
                return Error.Conflict(MessageCodes.DriverIssues.Reassignment.RequestAlreadyActive, isAr
                    ? "يوجد طلب إعادة إسناد نشط لهذا الطلب بالفعل. انتظر معالجة الطلب الحالي من فريق التشغيل."
                    : "An active reassignment request already exists for this box. Wait for the dispatcher to process the current request.");
            }

            throw;
        }
        catch (DbUpdateException ex) when (IsActiveReassignmentUniqueViolation(ex))
        {
            // A parallel submission created the active request between our pre-check
            // and this save; the partial unique index rejected the second row.
            return Error.Conflict(MessageCodes.DriverIssues.Reassignment.RequestAlreadyActive, isAr
                ? "يوجد طلب إعادة إسناد نشط لهذا الطلب بالفعل. انتظر معالجة الطلب الحالي من فريق التشغيل."
                : "An active reassignment request already exists for this box. Wait for the dispatcher to process the current request.");
        }

        var msg = isAr
            ? $"تم إرسال طلب إعادة الإسناد للبوكس {stop.OrderCode} بنجاح. سيتم إشعارك فور قبول الطلب من سائق آخر."
            : $"Reassignment requested for box {stop.OrderCode} successfully. You will be notified once accepted.";

        return Result.Success(new ReassignmentRequestResponseDto(
            RequestId: complaint.Id,
            BoxId: stop.Id,
            BoxCode: stop.OrderCode,
            Status: "ReassignmentRequested",
            Message: msg,
            RequestedAtUtc: now
        ));
    }

    /// <summary>Reassignment pair/range rule (same as delivery).</summary>
    private static Result<ReassignmentRequestResponseDto>? ValidateReassignmentCoordinates(
        decimal? latitude, decimal? longitude, bool isAr)
    {
        if (!latitude.HasValue && !longitude.HasValue)
        {
            return null;
        }

        if (latitude.HasValue != longitude.HasValue)
        {
            return Error.Validation(MessageCodes.Orders.CoordinatesPairRequired, isAr
                ? "يجب إرسال خط العرض وخط الطول معًا، أو حذفهما معًا."
                : "Latitude and longitude must be sent together, or both omitted.");
        }

        const decimal MinLatitude = -90m, MaxLatitude = 90m;
        const decimal MinLongitude = -180m, MaxLongitude = 180m;
        if (latitude.Value < MinLatitude || latitude.Value > MaxLatitude
            || longitude.Value < MinLongitude || longitude.Value > MaxLongitude)
        {
            return Error.Validation(MessageCodes.Orders.CoordinatesOutOfRange, isAr
                ? "إحداثيات الموقع خارج النطاق المسموح."
                : "Location coordinates are out of the allowed range.");
        }

        return null;
    }

    /// <summary>Resolves only the authenticated driver's persisted profile. A client
    /// supplied profile ID is a consistency hint and must match that identity.</summary>
    private async Task<Result<Guid?>> ResolveDriverIdentityOrNotFoundAsync(Guid? explicitDriverId, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue || _currentUser.UserType != UserType.Driver)
        {
            return Result.Failure<Guid?>(Error.Unauthorized(MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "An authenticated driver is required.")));
        }

        var driverId = await _db.DriverProfiles
            .Where(d => d.UserId == _currentUser.UserId.Value && !d.IsDeleted)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!driverId.HasValue)
            return Result.Failure<Guid?>(Error.NotFound(MessageCodes.Orders.NotFound, _messages.GetMessage(MessageCodes.Orders.NotFound, "Driver profile was not found.")));
        if (explicitDriverId.HasValue && explicitDriverId.Value != driverId.Value)
            return Result.Failure<Guid?>(Error.Forbidden("DRIVER_IDENTITY_MISMATCH", _messages.GetMessage("driver.identity_mismatch", "The supplied driver ID does not match the authenticated driver.")));
        var activeAccount = await _db.GetActiveAccountUserIdsAsync(new[] { _currentUser.UserId.Value }, cancellationToken);
        if (!activeAccount.Contains(_currentUser.UserId.Value))
            return Result.Failure<Guid?>(Error.Forbidden("DRIVER_ACCOUNT_INACTIVE", _messages.GetMessage("driver.account_inactive", "The driver account is not active.")));
        return Result.Success<Guid?>(driverId.Value);
    }

    private async Task<Result<T>> ExecuteAssignedStopMutationAsync<T>(Guid stopId, Guid? suppliedDriverId,
        Func<CancellationToken, Task<Result<T>>> mutation, CancellationToken cancellationToken,
        bool allowDeliveredReplay = false)
    {
        if (_transactions is null || _locks is null)
            return Error.Conflict("DELIVERY_TRANSACTION_UNAVAILABLE", _messages.GetMessage("driver_support_handover.transaction_unavailable", "Delivery mutation requires a transaction and application locks."));
        async Task<Result<T>> ExecuteAsync(CancellationToken ct)
        {
            if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue || _currentUser.UserType != UserType.Driver)
                return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "An authenticated driver is required."));
            var identity = await _db.DriverProfiles.AsNoTracking()
                .Where(x => x.UserId == _currentUser.UserId.Value && !x.IsDeleted)
                .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (!identity.HasValue || suppliedDriverId.HasValue && suppliedDriverId.Value != identity.Value)
                return Error.Forbidden("DRIVER_IDENTITY_MISMATCH", _messages.GetMessage("driver.identity_mismatch", "The supplied driver ID does not match the authenticated driver."));
            if (!(await _db.GetActiveAccountUserIdsAsync(new[] { _currentUser.UserId.Value }, ct)).Contains(_currentUser.UserId.Value))
                return Error.Forbidden("DRIVER_ACCOUNT_INACTIVE", _messages.GetMessage("driver.account_inactive", "The driver account is not active."));
            var tripId = await _db.TripStops.AsNoTracking().Where(x => x.Id == stopId).Select(x => (Guid?)x.TripId).FirstOrDefaultAsync(ct);
            if (!tripId.HasValue) return Error.NotFound(MessageCodes.Orders.NotFound, _messages.GetMessage(MessageCodes.Orders.NotFound, "Delivery stop was not found."));
            await _locks.LockTripAsync(tripId.Value, ct);
            var currentTripId = await _db.TripStops.AsNoTracking().Where(x => x.Id == stopId).Select(x => (Guid?)x.TripId).FirstOrDefaultAsync(ct);
            if (currentTripId != tripId) return Error.Conflict("DELIVERY_CUSTODY_CHANGED", _messages.GetMessage("driver_support_handover.custody_changed", "The box changed trips while this request was waiting. Reload delivery state."));
            var tripStatus = await _db.Trips.AsNoTracking().Where(x => x.Id == tripId.Value).Select(x => x.Status).FirstOrDefaultAsync(ct);
            var stopStatus = await _db.TripStops.AsNoTracking().Where(x => x.Id == stopId).Select(x => x.Status).FirstOrDefaultAsync(ct);
            var deliveredReplay = allowDeliveredReplay && stopStatus == TripStopStatus.Delivered;
            if (!deliveredReplay && tripStatus is TripStatus.Completed or TripStatus.Cancelled)
                return Error.Conflict("TRIP_TERMINAL", _messages.GetMessage("driver_support_handover.trip_terminal", "A terminal trip cannot accept delivery mutations."));
            var trackedStop = await _db.TripStops.FirstOrDefaultAsync(x => x.Id == stopId, ct);
            if (trackedStop is not null) await _db.ReloadEntityAsync(trackedStop, ct);
            await _locks.LockDriverAsync(identity.Value, ct);
            var ownsTrip = await _db.DriverAssignments.AsNoTracking().AnyAsync(x => x.TripId == tripId.Value
                && x.DriverProfileId == identity.Value && (x.Status == "Active" || x.Status == "Assigned"
                    || deliveredReplay && x.Status == "Completed"), ct);
            if (!ownsTrip) return Error.Forbidden("Orders.DeliveryForbidden", _messages.GetMessage("Orders.DeliveryForbidden", "This stop is not assigned to your active trip."));
            if (await DriverSupportHandoverMutationGuard.HasActiveHandoverForTripAsync(_db, tripId.Value, ct))
                return Error.Conflict("HANDOVER_PENDING", _messages.GetMessage("driver_support_handover.pending", "The active support handover must be completed or cancelled first."));
            if (await DriverSupportHandoverMutationGuard.HasActiveHoldForStopAsync(_db, stopId, ct))
                return Error.Conflict("DELIVERY_HOLD_ACTIVE", _messages.GetMessage("driver_support_handover.followup_active", "This box has an unresolved support follow-up."));
            return await mutation(ct);
        }
        return await _transactions.ExecuteResultAsync(ExecuteAsync, cancellationToken);
    }

    private async Task<Guid> ResolveStopCustomerUserIdAsync(TripStop stop, CancellationToken cancellationToken)
    {
        if (!stop.SubscriptionDayId.HasValue)
        {
            return Guid.Empty;
        }

        return await _db.SubscriptionDays
            .Where(d => d.Id == stop.SubscriptionDayId.Value)
            .Join(_db.Subscriptions, d => d.SubscriptionId, s => s.Id, (d, s) => s.CustomerUserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Merges the legacy singular evidence key (kept for backward
    /// compatibility, listed first) with the multi-key array: trims, drops empty
    /// entries, removes duplicates in order.</summary>
    private static List<string> MergeEvidencePhotoKeys(string? legacyKey, IEnumerable<string>? keys)
    {
        var merged = new List<string>();
        void Add(string? candidate)
        {
            var trimmed = candidate?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return;
            }

            // Storage keys are case-sensitive: only an exact repeat is a duplicate.
            if (merged.Contains(trimmed, StringComparer.Ordinal))
            {
                return;
            }

            merged.Add(trimmed);
        }

        Add(legacyKey);
        if (keys is not null)
        {
            foreach (var key in keys)
            {
                Add(key);
            }
        }

        return merged;
    }

    /// <summary>Evidence binding (same contract as the delivery proof photo): a real
    /// upload record owned by the reporting driver, accepted image type, and a file
    /// still present in storage. Returns null when the photo is acceptable.</summary>
    private async Task<Result<Guid>?> ValidateEvidencePhotoAsync(
        string photoKey, Guid? driverId, bool isAr, CancellationToken cancellationToken)
    {
        var record = await _db.UploadFileRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.StorageKey == photoKey, cancellationToken);

        if (record is null)
        {
            return Error.Validation(MessageCodes.DriverIssues.Report.PhotoNotFound, isAr
                ? "مفتاح صورة البلاغ لا يشير إلى ملفًا مرفوعًا فعليًا. ارفع الصورة عبر مسار الرفع المعتمد أولًا."
                : "The evidence photo key does not reference a real uploaded file. Upload the photo through the approved upload endpoint first.");
        }

        if (record.Scope != "drivers" || record.Folder != "issue-evidence"
            || !photoKey.StartsWith("uploads/drivers/issue-evidence/", StringComparison.Ordinal))
            return Error.Validation(MessageCodes.DriverIssues.Report.PhotoNotFound, isAr ? "الغرض من صورة البلاغ غير صالح." : "The evidence photo purpose is invalid.");

        if (driverId.HasValue)
        {
            var ownedByDriver = record.UploadedByUserId.HasValue && await _db.DriverProfiles
                .AnyAsync(d => d.Id == driverId.Value
                    && d.UserId == record.UploadedByUserId.Value, cancellationToken);
            if (!ownedByDriver)
            {
                return Error.Forbidden(MessageCodes.DriverIssues.Report.PhotoForbidden, isAr
                    ? "صورة البلاغ لا تنتمي إلى حسابك. ارفع صور البلاغ من حسابك نفسه."
                    : "The evidence photo does not belong to your account. Upload evidence photos from your own account.");
            }
        }

        var contentType = record.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (contentType != "image/jpeg" && contentType != "image/jpg"
            && contentType != "image/png" && contentType != "image/webp")
        {
            return Error.Validation(MessageCodes.DriverIssues.Report.PhotoInvalidType, isAr
                ? "صور البلاغ يجب أن تكون بصيغة JPEG أو PNG أو WebP."
                : "Evidence photos must be JPEG, PNG, or WebP images.");
        }

        if (_fileStorage is not null && !await _fileStorage.ExistsAsync(photoKey, cancellationToken))
        {
            return Error.Validation(MessageCodes.DriverIssues.Report.PhotoNotFound, isAr
                ? "ملف صورة البلاغ لم يعد موجودًا في التخزين. أعد رفع الصورة."
                : "The evidence photo file is no longer available in storage. Please re-upload the photo.");
        }
        if (_evidenceScans is null) return Error.Failure("UPLOAD_EVIDENCE_SCAN_FAILED", "Evidence scanning is unavailable.");
        var scan = await _evidenceScans.ReadCleanAsync(record, cancellationToken);
        if (scan.Bytes is null || !MealMate.Application.Features.UnifiedSupport.SupportCaseService.HasValidSignature(
                scan.Bytes, contentType == "image/jpg" ? "image/jpeg" : contentType))
            return Error.Validation(MessageCodes.DriverIssues.Report.PhotoInvalidType, isAr ? "صورة البلاغ غير صالحة." : "The evidence photo is invalid.");

        return null;
    }

    private async Task<string> GetUploadContentTypeAsync(string photoKey, CancellationToken cancellationToken)
    {
        var contentType = await _db.UploadFileRecords
            .AsNoTracking()
            .Where(r => r.StorageKey == photoKey)
            .Select(r => r.ContentType)
            .FirstOrDefaultAsync(cancellationToken);
        return contentType ?? "application/octet-stream";
    }

    private async Task AddDriverSupportIssueEventAsync(Complaint complaint, TripStop stop, string eventName, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        if (complaint.RestaurantId is not Guid restaurantId) return;
        var driverId = complaint.DriverProfileId;
        var titleAr = _messages.GetArabicMessage("driver_support_issue.notification_title", "تحديث بلاغ دعم السائق");
        var titleEn = _messages.GetEnglishMessage("driver_support_issue.notification_title_en", "Driver support issue update");
        var bodyAr = _messages.GetArabicMessage("driver_support_issue.notification_created", "تم تسجيل بلاغ دعم جديد للسائق.");
        var bodyEn = _messages.GetEnglishMessage("driver_support_issue.notification_created_en", "A driver support issue was recorded.");
        if (driverId.HasValue)
        {
            _db.DriverNotifications.Add(new DriverNotification
            {
                UserId = _currentUser.UserId!.Value, DriverProfileId = driverId.Value, Type = "driver-support-issue",
                TitleAr = titleAr, TitleEn = titleEn, BodyAr = bodyAr, BodyEn = bodyEn,
                Section = "support", SubjectId = complaint.Id, DeepLink = $"/driver/support/issues/{complaint.Id:D}", SentAtUtc = occurredAtUtc
            });
        }
        var managerIds = await _db.GetDeliveryManagerUserIdsAsync(restaurantId, cancellationToken);
        foreach (var userId in managerIds)
        {
            DeliveryManagerSupportPushOutbox.Add(_db, new DeliveryManagerNotification
            {
                UserId = userId, RestaurantId = restaurantId, Type = "driver-support-issue",
                TitleAr = titleAr, TitleEn = titleEn, BodyAr = bodyAr, BodyEn = bodyEn,
                Section = "support", SubjectId = complaint.Id, DeepLink = $"/dispatcher/support/issues/{complaint.Id:D}", SentAtUtc = occurredAtUtc
            });
        }
        _db.OutboxMessages.Add(new MealMate.Domain.Common.OutboxMessage
        {
            EventType = "driver-support-issue-changed.v1", AggregateType = nameof(Complaint), AggregateId = complaint.Id.ToString("D"),
            DeduplicationKey = $"driver-support-issue:{complaint.Id:N}:{eventName}",
            PayloadJson = JsonSerializer.Serialize(new { IssueId = complaint.Id, RestaurantId = restaurantId, DriverProfileId = driverId, Category = complaint.Category, Event = eventName }),
            CreatedAtUtc = occurredAtUtc
        });
    }

    /// <summary>True when the failure is the partial unique index that guards one
    /// active reassignment request per order identity.</summary>
    private static bool IsActiveReassignmentUniqueViolation(DbUpdateException ex)
        => ex.InnerException?.Message?.Contains("IX_Complaints_Active_ReassignmentRequest", StringComparison.OrdinalIgnoreCase) == true
            || ex.InnerException?.Message?.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) == true
                && ex.InnerException.Message.Contains("Complaints", StringComparison.OrdinalIgnoreCase);

    public async Task<Result<CustomerCallProxyResponseDto>> Handle(GetCustomerCallProxyQuery request, CancellationToken cancellationToken)
    {
        // F01 containment: this legacy endpoint disclosed the stored customer phone to
        // any authenticated caller who knew the stop ID, and even the assigned driver
        // received the raw number outside the audited reveal flow. The endpoint is
        // retired for everyone: drivers use the in-app voice-call flow, and any future
        // phone disclosure goes through the authorized reveal endpoint with audit.
        var stop = await _db.TripStops
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        var isAr = _messages.IsArabic();
        return Error.NotFound(
            "driver.call_proxy_retired",
            isAr
                ? "لم يعد مسار الاتصال بالوكيل مدعومًا. استخدم المكالمة الصوتية داخل التطبيق للتواصل مع العميل."
                : "The proxy call path is no longer supported. Use the in-app voice call to contact the customer.");
    }
}
