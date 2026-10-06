using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

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
    string Description,
    Guid? DriverId = null,
    string? EvidencePhotoStorageKey = null,
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

    public DriverDeliveryOrderCommandHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IRestaurantDeliveryNotifier? restaurantNotifier = null,
        ICustomerPushNotifier? customerPushNotifier = null,
        IFileStorage? fileStorage = null)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _restaurantNotifier = restaurantNotifier;
        _customerPush = customerPushNotifier;
        _fileStorage = fileStorage;
    }

    public async Task<Result<PickupBoxResponseDto>> Handle(PickupBoxCommand request, CancellationToken cancellationToken)
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

        var driverId = request.DriverId;
        if (!driverId.HasValue && _currentUser.UserId.HasValue)
        {
            driverId = await _db.DriverProfiles
                .Where(d => d.UserId == _currentUser.UserId.Value)
                .Select(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Record pickup scan with barcode/manual-code, documented condition photo, and manual entry audit tracking
        var scan = new PickupScan
        {
            TripId = stop.TripId,
            OrderId = stop.DailyOrderId,
            SubscriptionDayId = stop.SubscriptionDayId,
            DriverProfileId = driverId ?? Guid.Empty,
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

    public async Task<Result<CompleteDeliveryResponseDto>> Handle(CompleteBoxDeliveryCommand request, CancellationToken cancellationToken)
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
                    && da.Status == "Active", cancellationToken);
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

        return null;
    }

    public async Task<Result<Guid>> Handle(ReportDriverIssueCommand request, CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        var stop = await _db.TripStops
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is not null)
        {
            stop.FailureReason = $"[{request.IssueType}] {request.Description}";
        }

        var issueId = Guid.NewGuid();

        // Add audit log for tracking
        _db.AuditLogs.Add(new MealMate.Domain.IdentityAccess.AuditLog
        {
            Action = "DriverReportedIssue",
            EntityType = "TripStop",
            EntityId = request.BoxId,
            Reason = $"[{request.IssueType}] {request.Description}",
            ActorUserId = _currentUser.UserId
        });

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(issueId);
    }

    public async Task<Result<CustomerArrivalResponseDto>> Handle(ConfirmCustomerArrivalCommand request, CancellationToken cancellationToken)
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

    public async Task<Result<bool>> Handle(ReportDeliveryDelayCommand request, CancellationToken cancellationToken)
    {
        var stop = await _db.TripStops
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        _db.AuditLogs.Add(new MealMate.Domain.IdentityAccess.AuditLog
        {
            Action = "DeliveryDelayReported",
            EntityType = "TripStop",
            EntityId = request.BoxId,
            Reason = $"Delayed {request.DelayMinutes}m: {request.Reason}",
            ActorUserId = _currentUser.UserId
        });

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }

    public async Task<Result<FailedDeliveryResponseDto>> Handle(FailDeliveryCommand request, CancellationToken cancellationToken)
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

    public async Task<Result<ReturnBoxResponseDto>> Handle(ReturnBoxToKitchenCommand request, CancellationToken cancellationToken)
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

    public async Task<Result<ReassignmentRequestResponseDto>> Handle(RequestReassignmentCommand request, CancellationToken cancellationToken)
    {
        var stop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        var now = DateTime.UtcNow;
        var requestId = Guid.NewGuid();
        stop.Status = TripStopStatus.Pending;
        stop.FailureReason = $"[ReassignmentRequested: {request.Reason}] {request.Notes}";

        if (stop.Trip != null)
        {
            var driverAssignments = await _db.DriverAssignments
                .Where(da => da.TripId == stop.TripId && da.Status == "Active")
                .ToListAsync(cancellationToken);

            foreach (var da in driverAssignments)
            {
                da.Status = "Reassigned";
            }
        }

        var restaurantId = stop.Trip?.RestaurantId;
        Guid customerUserId = Guid.Empty;
        if (stop.SubscriptionDayId.HasValue)
        {
            customerUserId = await _db.SubscriptionDays
                .Where(d => d.Id == stop.SubscriptionDayId.Value)
                .Join(_db.Subscriptions, d => d.SubscriptionId, s => s.Id, (d, s) => s.CustomerUserId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Create a Support Issue / Complaint so the dispatcher sees it immediately in issues & queue
        var complaint = new Complaint
        {
            CustomerUserId = customerUserId,
            RestaurantId = restaurantId,
            Title = $"إعادة إسناد الطلب {stop.OrderCode}",
            OrderId = stop.DailyOrderId ?? stop.Id,
            SubscriptionDayId = stop.SubscriptionDayId,
            DriverProfileId = request.DriverId,
            Category = "DriverReassignmentRequested",
            Description = $"[إعادة إسناد: {request.Reason}] {request.Notes}",
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

        await _db.SaveChangesAsync(cancellationToken);

        var isAr = _messages.IsArabic();
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
