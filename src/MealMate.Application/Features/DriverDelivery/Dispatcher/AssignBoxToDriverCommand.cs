using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
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

    public AssignBoxToDriverCommandHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages,
        IDriverPushNotifier? driverPushNotifier = null)
    {
        _db = db;
        _messages = messages;
        _driverPushNotifier = driverPushNotifier;
    }

    public async Task<Result<AssignBoxResponse>> Handle(
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

        if (driver.VerificationStatus == VerificationStatus.Rejected)
        {
            return Error.Validation(
                "Driver.Rejected",
                _messages.IsArabic()
                    ? "لا يمكن إسناد طلبات لحساب سائق مرفوض."
                    : "Cannot assign orders to a rejected driver account.");
        }

        var now = DateTime.UtcNow;

        if (driver.LicenseExpiryUtc.HasValue && driver.LicenseExpiryUtc.Value < now)
        {
            return Error.Validation(
                "Driver.LicenseExpired",
                _messages.IsArabic()
                    ? "رخصة قيادة السائق منتهية الصلاحية."
                    : "Driver driving license has expired.");
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
                .FirstOrDefaultAsync(b => b.SubscriptionDayId == targetDayId.Value
                                       && b.Status != MealBarcodeStatus.Revoked
                                       && b.Status != MealBarcodeStatus.Archived, cancellationToken);
        }

        // Verify Restaurant Fleet boundary
        var restaurantId = stop?.Trip?.RestaurantId ?? day?.RestaurantId ?? Guid.Empty;
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

        string boxCode;
        string zoneName = "منطقة العاصمة";

        if (stop is not null)
        {
            boxCode = stop.OrderCode ?? $"#BX-{Math.Abs(request.BoxId.GetHashCode()) % 9000 + 1000}";
            zoneName = stop.ZoneNameAr ?? "منطقة العاصمة";

            stop.Trip.Status = TripStatus.Assigned;
            stop.Status = TripStopStatus.Pending;
            stop.FailureReason = null;
            if (activeBarcode != null)
            {
                stop.MealBarcodeId = activeBarcode.Id;
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

            if (_driverPushNotifier != null)
            {
                await _driverPushNotifier.NotifyBoxAssignedAsync(
                    request.DriverId,
                    boxCode,
                    zoneName,
                    cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
        else if (day is not null)
        {
            // Race condition check: check if an active stop already exists for this day
            var existingStop = await _db.TripStops
                .Include(s => s.Trip)
                    .ThenInclude(t => t.DriverAssignments)
                .FirstOrDefaultAsync(s => s.SubscriptionDayId == day.Id && s.Status != TripStopStatus.Delivered && s.Status != TripStopStatus.Failed, cancellationToken);

            if (existingStop is not null)
            {
                boxCode = existingStop.OrderCode ?? $"#BX-{Math.Abs(day.Id.GetHashCode()) % 9000 + 1000}";
                zoneName = existingStop.ZoneNameAr ?? "منطقة العاصمة";

                existingStop.Trip.Status = TripStatus.Assigned;
                existingStop.Status = TripStopStatus.Pending;
                existingStop.FailureReason = null;
                if (activeBarcode != null)
                {
                    existingStop.MealBarcodeId = activeBarcode.Id;
                }

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
                boxCode = $"#BX-{Math.Abs(day.Id.GetHashCode()) % 9000 + 1000}";
                zoneName = !string.IsNullOrWhiteSpace(day.DeliveryAddressLine)
                    ? (day.DeliveryAddressLine.StartsWith("منطقة") ? day.DeliveryAddressLine : $"منطقة {day.DeliveryAddressLine}")
                    : "منطقة العاصمة";

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

                var dailyOrder = await _db.RestaurantDailyOrders
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
                    MealBarcodeId = activeBarcode?.Id,
                    DailyOrderId = dailyOrder?.Id,
                    OrderCode = boxCode,
                    CustomerMaskedId = $"CUST-{Math.Abs(day.SubscriptionId.GetHashCode()) % 9000 + 1000}",
                    ZoneNameAr = zoneName,
                    ZoneNameEn = zoneName,
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

            if (_driverPushNotifier != null)
            {
                await _driverPushNotifier.NotifyBoxAssignedAsync(
                    request.DriverId,
                    boxCode,
                    zoneName,
                    cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);
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
}
