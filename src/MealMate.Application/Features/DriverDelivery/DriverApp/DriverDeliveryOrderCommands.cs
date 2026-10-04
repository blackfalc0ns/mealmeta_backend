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

    public DriverDeliveryOrderCommandHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IRestaurantDeliveryNotifier? restaurantNotifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _restaurantNotifier = restaurantNotifier;
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

        // 1. Mandatory proof photo at delivery (توثيق حالة البوكس بالصورة عند التسليم إجباري)
        if (string.IsNullOrWhiteSpace(request.ProofPhotoStorageKey))
        {
            return Error.Validation("Orders.ProofPhotoRequired", _messages.IsArabic()
                ? "يجب التقاط صورة لتوثيق تسليم البوكس وحالته عند العميل."
                : "A photo documenting box delivery and condition is required.");
        }

        // 2. Barcode & OTP are completely optional at delivery.
        // If a barcode is scanned, we verify it matches the order code to prevent accidental mixup.
        if (!string.IsNullOrWhiteSpace(request.BarcodeValue))
        {
            var cleanInputBarcode = request.BarcodeValue.Trim().TrimStart('#');
            var cleanOrderCode = stop.OrderCode?.Trim().TrimStart('#') ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(cleanOrderCode)
                && !cleanInputBarcode.Equals(cleanOrderCode, StringComparison.OrdinalIgnoreCase)
                && !cleanOrderCode.EndsWith(cleanInputBarcode, StringComparison.OrdinalIgnoreCase)
                && !cleanInputBarcode.EndsWith(cleanOrderCode, StringComparison.OrdinalIgnoreCase))
            {
                return Error.Validation("Orders.BarcodeMismatch", _messages.IsArabic()
                    ? "باركود الفاتورة الممسوح عند التسليم لا يطابق هذا العميل."
                    : "Scanned delivery barcode does not match this customer's box.");
            }
        }

        var now = DateTime.UtcNow;
        stop.Status = TripStopStatus.Delivered;
        stop.DeliveredAtUtc = now;

        // Update driver work session completed count
        var driverId = request.DriverId;
        if (!driverId.HasValue && _currentUser.UserId.HasValue)
        {
            driverId = await _db.DriverProfiles
                .Where(d => d.UserId == _currentUser.UserId.Value)
                .Select(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

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

        // Check if all stops in trip are completed
        var remainingStops = await _db.TripStops
            .AnyAsync(s => s.TripId == stop.TripId && s.Id != stop.Id && s.Status != TripStopStatus.Delivered && s.Status != TripStopStatus.Failed, cancellationToken);

        var tripJustCompleted = false;
        if (!remainingStops && stop.Trip is not null)
        {
            stop.Trip.Status = TripStatus.Completed;
            stop.Trip.CompletedAtUtc = now;
            tripJustCompleted = true;
        }

        await _db.SaveChangesAsync(cancellationToken);

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

        var msg = _messages.IsArabic()
            ? $"تم تسليم البوكس {stop.OrderCode} وتوثيق الإثبات بنجاح"
            : $"Box {stop.OrderCode} delivered and proof recorded successfully";

        return Result.Success(new CompleteDeliveryResponseDto(
            BoxId: stop.Id,
            BoxCode: stop.OrderCode,
            Status: "Delivered",
            Message: msg,
            DeliveredAtUtc: now
        ));
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
        var customerName = stop.CustomerName ?? (isAr ? "عميل ميل ميت" : "MealMate Customer");
        var msg = isAr ? $"تم تأكيد الوصول لموقع العميل {customerName}" : $"Arrival confirmed at customer {customerName}";

        return Result.Success(new CustomerArrivalResponseDto(
            BoxId: stop.Id,
            BoxCode: stop.OrderCode,
            CustomerName: customerName,
            CustomerPhone: stop.CustomerPhone,
            Status: "Arrived",
            Message: msg,
            ArrivedAtUtc: DateTime.UtcNow
        ));
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
        var stop = await _db.TripStops
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الطلب المحدد.");
        }

        var isAr = _messages.IsArabic();
        var customerName = stop.CustomerName ?? string.Empty;
        var customerPhone = stop.CustomerPhone ?? string.Empty;

        return Result.Success(new CustomerCallProxyResponseDto(
            BoxId: stop.Id,
            BoxCode: stop.OrderCode ?? string.Empty,
            CustomerName: customerName,
            CustomerPhone: customerPhone,
            IsMasked: false,
            ProxyPhone: null,
            Extension: null,
            GeneratedAtUtc: DateTime.UtcNow
        ));
    }
}
