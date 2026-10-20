using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record AssignBoxToDriverCommand(
    Guid BoxId,
    Guid DriverId,
    string? Notes = null) : ICommand<AssignBoxResponse>;

public sealed class AssignBoxToDriverCommandHandler : ICommandHandler<AssignBoxToDriverCommand, AssignBoxResponse>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IDriverPushNotifier? _driverPushNotifier;
    private readonly ICurrentUser? _currentUser;
    private readonly IApplicationTransactionExecutor? _txExecutor;
    private readonly IApplicationLockManager? _locks;
    private readonly IUserAccountService? _users;

    public AssignBoxToDriverCommandHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages,
        IDriverPushNotifier? driverPushNotifier = null,
        ICurrentUser? currentUser = null,
        IApplicationTransactionExecutor? txExecutor = null,
        IUserAccountService? users = null,
        IApplicationLockManager? locks = null)
    {
        _db = db;
        _messages = messages;
        _driverPushNotifier = driverPushNotifier;
        _currentUser = currentUser;
        _txExecutor = txExecutor;
        _locks = locks;
        _users = users;
    }

    public Task<Result<AssignBoxResponse>> Handle(
        AssignBoxToDriverCommand request,
        CancellationToken cancellationToken)
    {
        if (_txExecutor is null || _locks is null) return Task.FromResult<Result<AssignBoxResponse>>(Error.Conflict("TRIP_LOCK_UNAVAILABLE", _messages.GetMessage("driver_support_handover.transaction_unavailable", "Assignment requires transactional locks.")));
        return _txExecutor.ExecuteResultAsync(async ct =>
        {
            var tripIds = await _db.TripStops.AsNoTracking().Where(s => s.Id == request.BoxId || s.SubscriptionDayId == request.BoxId)
                .Select(s => s.TripId).Distinct().OrderBy(id => id).ToListAsync(ct);
            var targetTripIds = await _db.DriverAssignments.AsNoTracking()
                .Where(x => x.DriverProfileId == request.DriverId && (x.Status == "Active" || x.Status == "Assigned"))
                .Select(x => x.TripId).Distinct().ToListAsync(ct);
            tripIds = tripIds.Concat(targetTripIds).Distinct().OrderBy(id => id).ToList();
            foreach (var tripId in tripIds) await _locks.LockTripAsync(tripId, ct);
            await _locks.LockDriverAsync(request.DriverId, ct);
            foreach (var tripId in tripIds)
            {
                var status = await _db.Trips.AsNoTracking().Where(x => x.Id == tripId).Select(x => (TripStatus?)x.Status).FirstOrDefaultAsync(ct);
                if (status is TripStatus.Completed or TripStatus.Cancelled)
                    return Error.Conflict("TRIP_TERMINAL", _messages.GetMessage("driver_support_handover.trip_terminal", "A terminal trip cannot be reopened by assignment."));
                if (await DriverSupportHandoverMutationGuard.HasActiveHandoverAsync(_db, tripId, ct))
                    return Error.Conflict("HANDOVER_PENDING", _messages.GetMessage("driver_support_handover.pending", "Complete or cancel the active handover before changing assignments."));
            }
            if (await DriverSupportHandoverMutationGuard.IsReservedReplacementAsync(_db, request.DriverId, ct))
                return Error.Conflict("REPLACEMENT_DRIVER_RESERVED", _messages.GetMessage("driver_support_handover.replacement_driver_busy", "This driver is reserved for an active support handover."));
            var stopId = await _db.TripStops.AsNoTracking().Where(s => s.Id == request.BoxId || s.SubscriptionDayId == request.BoxId)
                .Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            if (stopId.HasValue && await DriverSupportHandoverMutationGuard.HasActiveHoldForStopAsync(_db, stopId.Value, ct))
                return Error.Conflict("DELIVERY_HOLD_ACTIVE", _messages.GetMessage("driver_support_handover.followup_active", "This box has an unresolved support follow-up."));
            return await HandleCoreAsync(request, ct);
        }, cancellationToken);
    }

    private async Task<Result<AssignBoxResponse>> HandleCoreAsync(
        AssignBoxToDriverCommand request,
        CancellationToken cancellationToken)
    {
        var driver = await _db.DriverProfiles
            .FirstOrDefaultAsync(d => d.Id == request.DriverId, cancellationToken);

        if (driver is null || driver.IsDeleted)
        {
            return Error.NotFound(
                MessageCodes.Dispatcher.DriverNotFound,
                _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "السائق غير موجود."));
        }

        var isAr = _messages.IsArabic();
        var now = DateTime.UtcNow;

        var eligibility = await DriverOperationalEligibility.CheckEligibilityAsync(
            _db, _users, driver, now, isAr, requireApproved: true, cancellationToken: cancellationToken);
        if (eligibility.IsFailure)
        {
            return eligibility.Error;
        }

        var driverName = driver.FullNameAr ?? driver.FullNameEn ?? "السائق المعين";

        var stop = await _db.TripStops
            .Include(s => s.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        SubscriptionDay? day = null;
        if (stop is null)
        {
            day = await _db.SubscriptionDays
                .Include(d => d.MealSelections)
                .FirstOrDefaultAsync(d => d.Id == request.BoxId, cancellationToken);
        }

        // Strict Zero Mock Data: return 404 if neither stop nor subscription day exists
        if (stop is null && day is null)
        {
            return Error.NotFound(
                MessageCodes.Common.NotFound,
                _messages.IsArabic()
                    ? "لم يتم العثور على الصندوق أو اليوم المطلوب للإسناد."
                    : "Requested box or subscription day was not found.");
        }
        if (stop?.Status is TripStopStatus.Delivered or TripStopStatus.ReturnedToKitchen)
            return Error.Conflict("DELIVERY_STOP_TERMINAL", _messages.GetMessage("driver_support_handover.terminal_stop", "Delivered or returned stops cannot be assigned again; create a new delivery attempt."));

        // Vehicle Capacity Check
        var vehicleType = driver.VehicleType?.Trim().ToLowerInvariant() ?? "car";
        var maxBoxes = vehicleType switch
        {
            "motorcycle" or "دراجة" or "دراجة نارية" or "bike" => 5,
            "sedan" or "car" or "سيارة" => 15,
            "van" or "فان" or "truck" or "شاحنة" => 35,
            _ => 25
        };

        var dayDailyOrder = day != null
            ? await _db.RestaurantDailyOrders.FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id, cancellationToken)
            : null;

        var requestedBoxes = stop != null
            ? Math.Max(1, stop.BoxCount)
            : Math.Max(1, dayDailyOrder?.BoxCount ?? Math.Max(1, (day!.MealSelections?.Count ?? 0) / 2));

        var currentActiveBoxes = await _db.TripStops
            .Where(s => s.Id != (stop != null ? stop.Id : Guid.Empty)
                        && s.Trip.DriverAssignments.Any(da => da.DriverProfileId == driver.Id && da.Status == "Active")
                        && (s.Status == TripStopStatus.Pending || s.Status == TripStopStatus.PickedUp))
            .SumAsync(s => s.BoxCount, cancellationToken);

        if (currentActiveBoxes + requestedBoxes > maxBoxes)
        {
            return Error.Validation(
                "VEHICLE_CAPACITY_EXCEEDED",
                _messages.IsArabic()
                    ? $"تجاوزت الحمولة السعة القصوى لمركبة السائق ({maxBoxes} صندوق كحد أقصى لـ {driver.VehicleType ?? "المركبة"}). الحمولة الحالية: {currentActiveBoxes}، المطلوبة: {requestedBoxes}."
                    : $"Assignment exceeds driver vehicle capacity ({maxBoxes} boxes max for {driver.VehicleType ?? "vehicle"}). Current: {currentActiveBoxes}, Requested: {requestedBoxes}.");
        }

        // Find active MealBarcode for linking
        var targetDayId = stop?.SubscriptionDayId ?? day?.Id;
        MealBarcode? activeBarcode = null;
        if (targetDayId.HasValue && targetDayId.Value != Guid.Empty)
        {
            activeBarcode = await _db.MealBarcodes
                .Where(b => b.SubscriptionDayId == targetDayId.Value
                         && b.Status != MealBarcodeStatus.Revoked
                         && b.Status != MealBarcodeStatus.Archived)
                .OrderByDescending(b => b.GeneratedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Verify Restaurant Fleet boundary
        var restaurantId = stop?.Trip?.RestaurantId ?? day?.RestaurantId ?? Guid.Empty;

        // Caller Restaurant Isolation
        var callerRestId = _currentUser?.RestaurantId;
        var isAdmin = _currentUser != null && (_currentUser.IsInRole("Admin") || _currentUser.UserType == UserType.Admin);
        if (callerRestId.HasValue && callerRestId.Value != Guid.Empty && !isAdmin)
        {
            if (restaurantId != Guid.Empty && callerRestId.Value != restaurantId)
            {
                return Error.Forbidden(
                    "Dispatcher.ForbiddenRestaurant",
                    _messages.IsArabic()
                        ? "ليس لديك صلاحية لإسناد طلبات لمطعم آخر."
                        : "You are not authorized to assign orders for another restaurant.");
            }
        }

        // 1. Driver Fleet Affiliation Check
        if (restaurantId != Guid.Empty && driver.RestaurantId.HasValue && driver.RestaurantId.Value != Guid.Empty && driver.RestaurantId.Value != restaurantId)
        {
            var hasLink = await _db.RestaurantDriverLinks
                .AnyAsync(l => l.RestaurantId == restaurantId && l.DriverProfileId == driver.Id && l.IsActive, cancellationToken);
            if (!hasLink)
            {
                return Error.Validation(
                    "Driver.NotAffiliatedWithRestaurant",
                    _messages.IsArabic()
                        ? "السائق غير مسجل في أسطول هذا المطعم."
                        : "Driver is not affiliated with this restaurant's fleet.");
            }
        }

        // 2. Readiness & Barcode Enforcement for subscription boxes
        var dailyOrder = targetDayId.HasValue
            ? await _db.RestaurantDailyOrders.FirstOrDefaultAsync(r => r.SubscriptionDayId == targetDayId.Value, cancellationToken)
            : dayDailyOrder;

        if (targetDayId.HasValue)
        {
            if (dailyOrder == null)
            {
                return Error.NotFound(
                    "Box.NotFound",
                    _messages.IsArabic()
                        ? "لم يتم العثور على الصندوق المطلوب."
                        : "The requested box was not found.");
            }

            if (dailyOrder.Status == OrderStatus.AssignedToDriver)
            {
                return Error.Conflict(
                    "Box.AlreadyAssigned",
                    _messages.IsArabic()
                        ? "تم إسناد هذا الصندوق لسائق بالفعل."
                        : "This box is already assigned to a driver.");
            }

            if (dailyOrder.Status != OrderStatus.ReadyForPickup)
            {
                return Error.Validation(
                    "Box.NotReadyForPickup",
                    _messages.IsArabic()
                        ? "لا يمكن إسناد الصندوق للسائق قبل أن يصبح جاهزاً للاستلام من المطبخ (ReadyForPickup)."
                        : "Cannot assign box to driver before it is ready for pickup from the kitchen.");
            }

            if (activeBarcode == null)
            {
                return Error.Validation(
                    "Box.MissingActiveBarcode",
                    _messages.IsArabic()
                        ? "لا يمكن إسناد الصندوق دون وجود باركود نشط."
                        : "Cannot assign box without an active canonical barcode.");
            }
        }

        string boxCode = activeBarcode?.BarcodeValue ?? stop?.OrderCode ?? $"#BX-{request.BoxId.ToString()[..8].ToUpperInvariant()}";
        string zoneName = "منطقة العاصمة";

        if (stop is not null)
        {
            zoneName = stop.ZoneNameAr ?? "منطقة العاصمة";

            stop.Trip.Status = TripStatus.Assigned;
            stop.Status = TripStopStatus.Pending;
            stop.FailureReason = null;
            stop.OrderCode = boxCode;
            if (activeBarcode is not null)
            {
                stop.MealBarcodeId = activeBarcode.Id;
                activeBarcode.TripStopId = stop.Id;
            }

            // Reassignment handling: deactivate any previous active assignment for other drivers
            foreach (var prevAssignment in stop.Trip.DriverAssignments.Where(da => da.Status == "Active" && da.DriverProfileId != request.DriverId))
            {
                prevAssignment.Status = "Reassigned";
            }

            var existingAssignment = stop.Trip.DriverAssignments
                .FirstOrDefault(da => da.DriverProfileId == request.DriverId);

            if (existingAssignment is null)
            {
                var assignment = new DriverAssignment
                {
                    TripId = stop.TripId,
                    DriverProfileId = request.DriverId,
                    AssignedAtUtc = now,
                    Status = "Active"
                };
                _db.DriverAssignments.Add(assignment);
            }
            else
            {
                existingAssignment.Status = "Active";
                existingAssignment.AssignedAtUtc = now;
            }

            if (stop.SubscriptionDayId.HasValue)
            {
                var linkedDay = await _db.SubscriptionDays
                    .FirstOrDefaultAsync(d => d.Id == stop.SubscriptionDayId.Value, cancellationToken);
                if (linkedDay != null && linkedDay.Status == SubscriptionDayStatus.ConfirmedByRestaurant)
                {
                    linkedDay.Status = SubscriptionDayStatus.Preparing;
                }
            }

            if (stop.DailyOrderId.HasValue)
            {
                var linkedDailyOrder = await _db.RestaurantDailyOrders
                    .FirstOrDefaultAsync(r => r.Id == stop.DailyOrderId.Value, cancellationToken);
                if (linkedDailyOrder != null)
                {
                    linkedDailyOrder.Status = OrderStatus.AssignedToDriver;
                    linkedDailyOrder.ModifiedAtUtc = now;
                }
            }

            _db.DriverNotifications.Add(new MealMate.Domain.IdentityAccess.DriverNotification
            {
                UserId = driver.UserId != Guid.Empty ? driver.UserId : driver.Id,
                DriverProfileId = request.DriverId,
                Type = "box-assigned",
                TitleAr = "تم إسناد طلب جديد",
                TitleEn = "New Order Assigned",
                BodyAr = $"تم إسناد الطلب {boxCode} إليك للتوصيل",
                BodyEn = $"Order {boxCode} has been assigned to you for delivery",
                Section = "orders",
                SubjectId = request.BoxId,
                DeepLink = $"/driver/orders/{boxCode}",
                IsRead = false,
                SentAtUtc = now
            });

            var outboxPayload = JsonSerializer.Serialize(new
            {
                DriverUserId = driver.UserId != Guid.Empty ? driver.UserId : driver.Id,
                DriverProfileId = request.DriverId,
                BoxId = request.BoxId,
                BoxCode = boxCode,
                ZoneName = zoneName
            });

            _db.OutboxMessages.Add(new MealMate.Domain.Common.OutboxMessage
            {
                EventType = "DriverBoxAssigned",
                PayloadJson = outboxPayload,
                CreatedAtUtc = now,
                NextAttemptAtUtc = now,
                RetryCount = 0
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Error.Conflict(
                    "Box.AlreadyAssigned",
                    _messages.IsArabic()
                        ? "تم إسناد هذا الصندوق بالفعل لسائق آخر."
                        : "This box has already been assigned to another driver.");
            }
        }
        else if (day is not null)
        {
            // Race condition check: check if an active stop already exists for this day
            var existingStop = await _db.TripStops
                .Include(s => s.Trip)
                    .ThenInclude(t => t.DriverAssignments)
                .FirstOrDefaultAsync(s => s.SubscriptionDayId == day.Id && s.Status != TripStopStatus.Delivered
                    && s.Status != TripStopStatus.ReturnedToKitchen && s.Status != TripStopStatus.Failed, cancellationToken);

            if (existingStop is not null)
            {
                boxCode = activeBarcode.BarcodeValue;
                zoneName = existingStop.ZoneNameAr ?? "منطقة العاصمة";

                existingStop.Trip.Status = TripStatus.Assigned;
                existingStop.Status = TripStopStatus.Pending;
                existingStop.FailureReason = null;
                existingStop.OrderCode = boxCode;
                existingStop.MealBarcodeId = activeBarcode.Id;
                activeBarcode.TripStopId = existingStop.Id;

                foreach (var prevAssignment in existingStop.Trip.DriverAssignments.Where(da => da.Status == "Active" && da.DriverProfileId != request.DriverId))
                {
                    prevAssignment.Status = "Reassigned";
                }

                var existingAssign = existingStop.Trip.DriverAssignments.FirstOrDefault(da => da.DriverProfileId == request.DriverId);
                if (existingAssign is null)
                {
                    _db.DriverAssignments.Add(new DriverAssignment
                    {
                        TripId = existingStop.TripId,
                        DriverProfileId = request.DriverId,
                        AssignedAtUtc = now,
                        Status = "Active"
                    });
                }
                else
                {
                    existingAssign.Status = "Active";
                    existingAssign.AssignedAtUtc = now;
                }
            }
            else
            {
                boxCode = activeBarcode.BarcodeValue;
                MealMate.Domain.Foundation.Region? stopRegion = null;
                if (day.DeliveryRegionId.HasValue)
                {
                    stopRegion = await _db.Regions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == day.DeliveryRegionId.Value, cancellationToken);
                }

                var (zoneAr, zoneEn) = TripStopNaming.ResolveZone(day, stopRegion);
                zoneName = zoneAr;

                var targetRestId = day.RestaurantId ?? Guid.Empty;
                var trip = await _db.Trips
                    .Include(t => t.Stops)
                    .Include(t => t.DriverAssignments)
                    .FirstOrDefaultAsync(t => t.RestaurantId == targetRestId
                        && !t.IsDeleted
                        && t.Status != TripStatus.Completed
                        && t.Status != TripStatus.Cancelled
                        && t.PlannedDate == day.ServiceDate
                        && t.DriverAssignments.Any(da => da.DriverProfileId == request.DriverId && da.Status == "Active"), cancellationToken);

                if (trip == null)
                {
                    trip = new Trip
                    {
                        RestaurantId = targetRestId,
                        Code = $"TRP-{DateTime.UtcNow:yyyyMMdd}-{Math.Abs(Guid.NewGuid().GetHashCode()) % 9000 + 1000}",
                        Status = TripStatus.Assigned,
                        PlannedDate = day.ServiceDate,
                        CreatedAtUtc = now
                    };
                    _db.Trips.Add(trip);

                    _db.DriverAssignments.Add(new DriverAssignment
                    {
                        TripId = trip.Id,
                        DriverProfileId = request.DriverId,
                        AssignedAtUtc = now,
                        Status = "Active"
                    });
                }

                dailyOrder = await _db.RestaurantDailyOrders
                    .FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id, cancellationToken);

                if (dailyOrder != null)
                {
                    dailyOrder.Status = OrderStatus.AssignedToDriver;
                    dailyOrder.ModifiedAtUtc = now;
                }

                MealMate.Domain.IdentityAccess.CustomerAddress? customerAddr = null;
                if (day.DeliveryAddressId.HasValue)
                {
                    customerAddr = await _db.CustomerAddresses
                        .AsNoTracking()
                        .FirstOrDefaultAsync(a => a.Id == day.DeliveryAddressId.Value, cancellationToken);
                }

                string? customerName = null;
                if (_users is not null)
                {
                    var customerUserId = await _db.Subscriptions
                        .AsNoTracking()
                        .Where(s => s.Id == day.SubscriptionId)
                        .Select(s => s.CustomerUserId)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (customerUserId != Guid.Empty)
                    {
                        var identity = (await _users.GetUserIdentitiesAsync(new[] { customerUserId }, cancellationToken))
                            .FirstOrDefault();
                        if (identity is not null)
                        {
                            customerName = identity.FullName;
                        }
                    }
                }

                var nextSeq = (trip.Stops?.Count ?? 0) + 1;
                var mealCount = day.MealSelections?.Count ?? 0;
                if (mealCount == 0 && dailyOrder != null && dailyOrder.BoxCount > 0)
                {
                    mealCount = dailyOrder.BoxCount * 2;
                }
                var boxCount = Math.Max(1, dailyOrder?.BoxCount ?? Math.Max(1, mealCount / 2));

                var newStop = new TripStop
                {
                    TripId = trip.Id,
                    SubscriptionDayId = day.Id,
                    MealBarcodeId = activeBarcode.Id,
                    DailyOrderId = dailyOrder?.Id,
                    OrderCode = boxCode,
                    CustomerMaskedId = $"CUST-{Math.Abs(day.SubscriptionId.GetHashCode()) % 9000 + 1000}",
                    CustomerName = customerName,
                    ZoneNameAr = zoneAr,
                    ZoneNameEn = zoneEn,
                    FullAddress = day.DeliveryAddressLine,
                    MealSummaryAr = $"{Math.Max(1, mealCount)} وجبات",
                    MealSummaryEn = $"{Math.Max(1, mealCount)} meals",
                    BoxCount = boxCount,
                    SlotLabel = !string.IsNullOrWhiteSpace(day.DeliveryAddressLine) ? "09:30-10:30 ص" : "12:00-14:00 م",
                    SequenceNumber = nextSeq,
                    Status = TripStopStatus.Pending,
                    Latitude = customerAddr?.Latitude,
                    Longitude = customerAddr?.Longitude,
                    CreatedAtUtc = now
                };
                _db.TripStops.Add(newStop);
                activeBarcode.TripStopId = newStop.Id;
            }

            if (day.Status == SubscriptionDayStatus.ConfirmedByRestaurant)
            {
                day.Status = SubscriptionDayStatus.Preparing;
            }

            _db.DriverNotifications.Add(new MealMate.Domain.IdentityAccess.DriverNotification
            {
                UserId = driver.UserId != Guid.Empty ? driver.UserId : driver.Id,
                DriverProfileId = request.DriverId,
                Type = "box-assigned",
                TitleAr = "تم إسناد طلب جديد",
                TitleEn = "New Order Assigned",
                BodyAr = $"تم إسناد الطلب {boxCode} إليك للتوصيل",
                BodyEn = $"Order {boxCode} has been assigned to you for delivery",
                Section = "orders",
                SubjectId = day.Id,
                DeepLink = $"/driver/orders/{boxCode}",
                IsRead = false,
                SentAtUtc = now
            });

            var outboxPayload = JsonSerializer.Serialize(new
            {
                DriverUserId = driver.UserId != Guid.Empty ? driver.UserId : driver.Id,
                DriverProfileId = request.DriverId,
                BoxId = day.Id,
                BoxCode = boxCode,
                ZoneName = zoneName
            });

            _db.OutboxMessages.Add(new MealMate.Domain.Common.OutboxMessage
            {
                EventType = "DriverBoxAssigned",
                PayloadJson = outboxPayload,
                CreatedAtUtc = now,
                NextAttemptAtUtc = now,
                RetryCount = 0
            });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                return Error.Conflict(
                    "Box.AlreadyAssigned",
                    _messages.IsArabic()
                        ? "تم إسناد هذا الصندوق بالفعل لسائق آخر."
                        : "This box has already been assigned to another driver.");
            }
        }
        else
        {
            return Error.NotFound(
                MessageCodes.Common.NotFound,
                _messages.IsArabic()
                    ? "لم يتم العثور على الصندوق أو اليوم المطلوب للإسناد."
                    : "Requested box or subscription day was not found.");
        }

        var message = _messages.GetMessage(MessageCodes.DispatcherOrders.BoxAssignedSuccess, "تم إسناد البوكس للسائق بنجاح");

        return Result.Success(new AssignBoxResponse(
            BoxId: request.BoxId,
            BoxCode: boxCode,
            DriverId: request.DriverId,
            DriverName: driverName,
            AssignedAtUtc: now,
            Message: message));
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner != null)
        {
            var sqlStateProp = inner.GetType().GetProperty("SqlState");
            if (sqlStateProp != null && sqlStateProp.GetValue(inner) as string == "23505")
            {
                return true;
            }
        }
        return ex.Message.Contains("IX_TripStops_Active_SubscriptionDayId")
            || ex.InnerException?.Message.Contains("IX_TripStops_Active_SubscriptionDayId") == true
            || ex.Message.Contains("23505")
            || ex.InnerException?.Message.Contains("23505") == true;
    }
}
