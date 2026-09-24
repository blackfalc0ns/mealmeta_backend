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
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DriverId, cancellationToken);

        var driverName = driver?.FullNameAr ?? driver?.FullNameEn ?? "السائق المعين";
        var now = DateTime.UtcNow;

        var stop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        string boxCode;
        string zoneName = "منطقة العاصمة";

        if (stop is not null)
        {
            boxCode = stop.OrderCode ?? $"#BX-{Math.Abs(request.BoxId.GetHashCode()) % 9000 + 1000}";
            zoneName = stop.ZoneNameAr ?? "منطقة العاصمة";

            stop.Trip.Status = TripStatus.Assigned;

            var existingAssignment = await _db.DriverAssignments
                .FirstOrDefaultAsync(da => da.TripId == stop.TripId && da.DriverProfileId == request.DriverId, cancellationToken);

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
                if (linkedDay != null && (linkedDay.Status == SubscriptionDayStatus.ConfirmedByRestaurant || linkedDay.Status == SubscriptionDayStatus.Preparing))
                {
                    linkedDay.Status = SubscriptionDayStatus.OutForDelivery;
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
                UserId = driver?.UserId ?? Guid.Empty,
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
        else
        {
            var day = await _db.SubscriptionDays
                .Include(d => d.MealSelections)
                .FirstOrDefaultAsync(d => d.Id == request.BoxId, cancellationToken);

            if (day is not null)
            {
                boxCode = $"#BX-{Math.Abs(day.Id.GetHashCode()) % 9000 + 1000}";
                zoneName = !string.IsNullOrWhiteSpace(day.DeliveryAddressLine)
                    ? (day.DeliveryAddressLine.StartsWith("منطقة") ? day.DeliveryAddressLine : $"منطقة {day.DeliveryAddressLine}")
                    : "منطقة العاصمة";

                var restaurantId = day.RestaurantId ?? Guid.Empty;
                var trip = await _db.Trips
                    .Include(t => t.Stops)
                    .Include(t => t.DriverAssignments)
                    .FirstOrDefaultAsync(t => t.RestaurantId == restaurantId
                        && !t.IsDeleted
                        && t.Status != TripStatus.Completed
                        && t.Status != TripStatus.Cancelled
                        && t.PlannedDate == day.ServiceDate
                        && t.DriverAssignments.Any(da => da.DriverProfileId == request.DriverId && da.Status == "Active"), cancellationToken);

                if (trip == null)
                {
                    trip = new Trip
                    {
                        RestaurantId = restaurantId,
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

                var nextSeq = (trip.Stops?.Count ?? 0) + 1;
                var newStop = new TripStop
                {
                    TripId = trip.Id,
                    SubscriptionDayId = day.Id,
                    DailyOrderId = dailyOrder?.Id,
                    OrderCode = boxCode,
                    CustomerMaskedId = $"CUST-{Math.Abs(day.SubscriptionId.GetHashCode()) % 9000 + 1000}",
                    ZoneNameAr = zoneName,
                    ZoneNameEn = zoneName,
                    MealSummaryAr = $"{Math.Max(1, day.MealSelections?.Count ?? 1)} وجبات",
                    MealSummaryEn = $"{Math.Max(1, day.MealSelections?.Count ?? 1)} meals",
                    BoxCount = Math.Max(1, (day.MealSelections?.Count ?? 1) / 2),
                    SlotLabel = "09:30-10:30 ص",
                    SequenceNumber = nextSeq,
                    Status = TripStopStatus.Pending,
                    CreatedAtUtc = now
                };
                _db.TripStops.Add(newStop);

                day.Status = SubscriptionDayStatus.OutForDelivery;

                _db.DriverNotifications.Add(new MealMate.Domain.IdentityAccess.DriverNotification
                {
                    UserId = driver?.UserId ?? Guid.Empty,
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
                boxCode = $"#BX-{Math.Abs(request.BoxId.GetHashCode()) % 9000 + 1000}";
            }
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
