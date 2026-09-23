using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record PickupBoxCommand(
    Guid BoxId,
    string BarcodeValue,
    string? ConditionPhotoStorageKey = null,
    decimal? Latitude = null,
    decimal? Longitude = null,
    Guid? DriverId = null) : ICommand<PickupBoxResponseDto>;

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

    public DriverDeliveryOrderCommandHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
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

        // Validate invoice barcode matches the box order code
        var cleanInputBarcode = request.BarcodeValue.Trim().TrimStart('#');
        var cleanOrderCode = stop.OrderCode?.Trim().TrimStart('#') ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(cleanOrderCode))
        {
            if (!cleanInputBarcode.Equals(cleanOrderCode, StringComparison.OrdinalIgnoreCase)
                && !cleanOrderCode.EndsWith(cleanInputBarcode, StringComparison.OrdinalIgnoreCase)
                && !cleanInputBarcode.EndsWith(cleanOrderCode, StringComparison.OrdinalIgnoreCase))
            {
                return Error.Validation("Orders.BarcodeMismatch", _messages.IsArabic()
                    ? "باركود الفاتورة الممسوح لا يطابق هذا البوكس. يرجى مسح باركود الفاتورة الصحيح للعميل."
                    : "Scanned invoice barcode does not match this box. Please scan the correct invoice barcode.");
            }
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

        // Record pickup scan with barcode and documented condition photo
        var scan = new PickupScan
        {
            TripId = stop.TripId,
            OrderId = stop.DailyOrderId,
            SubscriptionDayId = stop.SubscriptionDayId,
            DriverProfileId = driverId ?? Guid.Empty,
            BarcodeValue = request.BarcodeValue,
            PhotoPath = request.ConditionPhotoStorageKey,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ScannedAtUtc = now
        };
        _db.PickupScans.Add(scan);

        await _db.SaveChangesAsync(cancellationToken);

        var msg = _messages.IsArabic()
            ? $"تم تأكيد استلام البوكس {stop.OrderCode} وتوثيق حالته بنجاح"
            : $"Box {stop.OrderCode} picked up and condition photo verified successfully";

        return Result.Success(new PickupBoxResponseDto(
            BoxId: stop.Id,
            BoxCode: stop.OrderCode,
            Status: "PickedUp",
            Message: msg,
            PickedUpAtUtc: now
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

        // Check if all stops in trip are completed
        var remainingStops = await _db.TripStops
            .AnyAsync(s => s.TripId == stop.TripId && s.Id != stop.Id && s.Status != TripStopStatus.Delivered && s.Status != TripStopStatus.Failed, cancellationToken);

        if (!remainingStops && stop.Trip is not null)
        {
            stop.Trip.Status = TripStatus.Completed;
            stop.Trip.CompletedAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

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
        stop.FailureReason = $"[ReassignmentRequested: {request.Reason}] {request.Notes}";

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
            RequestId: requestId,
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
        var customerName = stop.CustomerName ?? (isAr ? "عميل ميل ميت" : "MealMate Customer");
        var customerPhone = stop.CustomerPhone ?? "+965 98765432";

        return Result.Success(new CustomerCallProxyResponseDto(
            BoxId: stop.Id,
            BoxCode: stop.OrderCode,
            CustomerName: customerName,
            CustomerPhone: customerPhone,
            IsMasked: true,
            ProxyPhone: "+965 2200 9000",
            Extension: stop.OrderCode?.Replace("BX-", "").Replace("#", ""),
            GeneratedAtUtc: DateTime.UtcNow
        ));
    }
}
