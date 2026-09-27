using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.CalendarMeals;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Trips;

public sealed record CreateTripStopInput(
    string OrderCode,
    string CustomerMaskedId,
    string? CustomerName,
    string? CustomerPhone,
    string ZoneNameAr,
    string ZoneNameEn,
    string MealSummaryAr,
    string MealSummaryEn,
    int BoxCount,
    string SlotLabel,
    Guid? DailyOrderId = null,
    Guid? SubscriptionDayId = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

public sealed record CreateRestaurantTripCommand(
    Guid RestaurantId,
    Guid DriverId,
    IReadOnlyList<CreateTripStopInput> Stops,
    string? ShiftLabel = null,
    string? Notes = null) : ICommand<Guid>;

public sealed record UpdateTripStatusCommand(
    Guid TripId,
    string Status) : ICommand;

public sealed record UpdateTripStopStatusCommand(
    Guid TripId,
    Guid StopId,
    string Status,
    string? FailureReason = null) : ICommand;

public sealed class CreateRestaurantTripCommandHandler : ICommandHandler<CreateRestaurantTripCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public CreateRestaurantTripCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(
        CreateRestaurantTripCommand request,
        CancellationToken cancellationToken)
    {
        var restaurantId = request.RestaurantId;
        if (restaurantId == Guid.Empty)
        {
            var userProfile = await _db.RestaurantProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == _currentUser.UserId, cancellationToken);

            restaurantId = userProfile?.Id ?? (await _db.RestaurantProfiles
                .AsNoTracking()
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken));
        }

        if (restaurantId == Guid.Empty)
        {
            return Error.Failure("Trips.RestaurantNotFound", "لم يتم العثور على المطعم المرتبط بالعملية.");
        }

        // Validate driver exists
        var driverExists = await _db.DriverProfiles
            .AnyAsync(d => d.Id == request.DriverId && !d.IsDeleted, cancellationToken);

        if (!driverExists)
        {
            return Error.Failure("Trips.DriverNotFound", "السائق المحدد غير موجود أو غير نشط.");
        }

        if (request.Stops.Count == 0)
        {
            return Error.Validation("Trips.StopsRequired", "يجب اختيار محطة توصيل واحدة على الأقل لإنشاء الرحلة.");
        }

        var count = await _db.Trips.CountAsync(t => t.RestaurantId == restaurantId, cancellationToken);
        var tripCode = $"TRP-{(count + 101):D3}";
        var now = DateTime.UtcNow;

        var trip = new Trip
        {
            RestaurantId = restaurantId,
            DispatcherUserId = _currentUser.UserId,
            Code = tripCode,
            Status = TripStatus.Assigned,
            PlannedDate = DateOnly.FromDateTime(now),
            ShiftName = request.ShiftLabel ?? "الوردية الصباحية",
            Notes = request.Notes ?? "تم إنشاء الرحلة وتعيين السائق — بانتظار الاستلام.",
            CreatedAtUtc = now,
            CreatedByUserId = _currentUser.UserId
        };

        var dailyOrderIds = request.Stops.Where(s => s.DailyOrderId.HasValue).Select(s => s.DailyOrderId!.Value).Distinct().ToList();
        var subDayByDailyOrderId = dailyOrderIds.Count > 0
            ? await _db.RestaurantDailyOrders
                .AsNoTracking()
                .Where(r => dailyOrderIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.SubscriptionDayId, cancellationToken)
            : new Dictionary<Guid, Guid>();

        var allSubDayIds = request.Stops.Where(s => s.SubscriptionDayId.HasValue).Select(s => s.SubscriptionDayId!.Value)
            .Concat(subDayByDailyOrderId.Values)
            .Distinct()
            .ToList();

        var subDays = allSubDayIds.Count > 0
            ? await _db.SubscriptionDays
                .AsNoTracking()
                .Where(d => allSubDayIds.Contains(d.Id) && d.DeliveryAddressId.HasValue)
                .Select(d => new { d.Id, DeliveryAddressId = d.DeliveryAddressId!.Value })
                .ToListAsync(cancellationToken)
            : new();

        var addressIds = subDays.Select(d => d.DeliveryAddressId).Distinct().ToList();
        var addressCoords = addressIds.Count > 0
            ? await _db.CustomerAddresses
                .AsNoTracking()
                .Where(a => addressIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => new { a.Latitude, a.Longitude }, cancellationToken)
            : new();

        var subDayToAddressId = subDays.ToDictionary(d => d.Id, d => d.DeliveryAddressId);

        int seq = 1;
        foreach (var stopInput in request.Stops)
        {
            var subDayId = stopInput.SubscriptionDayId;
            if (!subDayId.HasValue && stopInput.DailyOrderId.HasValue && subDayByDailyOrderId.TryGetValue(stopInput.DailyOrderId.Value, out var sid))
            {
                subDayId = sid;
            }

            decimal? stopLat = stopInput.Latitude;
            decimal? stopLng = stopInput.Longitude;
            if (!stopLat.HasValue && subDayId.HasValue && subDayToAddressId.TryGetValue(subDayId.Value, out var addrId) && addressCoords.TryGetValue(addrId, out var coords))
            {
                stopLat = coords.Latitude;
                stopLng = coords.Longitude;
            }

            var stop = new TripStop
            {
                TripId = trip.Id,
                DailyOrderId = stopInput.DailyOrderId,
                SubscriptionDayId = subDayId,
                OrderCode = stopInput.OrderCode,
                CustomerMaskedId = stopInput.CustomerMaskedId,
                CustomerName = stopInput.CustomerName,
                CustomerPhone = stopInput.CustomerPhone,
                ZoneNameAr = stopInput.ZoneNameAr,
                ZoneNameEn = stopInput.ZoneNameEn,
                MealSummaryAr = stopInput.MealSummaryAr,
                MealSummaryEn = stopInput.MealSummaryEn,
                BoxCount = stopInput.BoxCount > 0 ? stopInput.BoxCount : 1,
                SlotLabel = stopInput.SlotLabel,
                SequenceNumber = seq++,
                Status = TripStopStatus.Pending,
                Latitude = stopLat,
                Longitude = stopLng,
                CreatedAtUtc = now,
                CreatedByUserId = _currentUser.UserId
            };

            trip.Stops.Add(stop);
        }

        // Add DriverAssignment
        var assignment = new DriverAssignment
        {
            TripId = trip.Id,
            DriverProfileId = request.DriverId,
            AssignedAtUtc = now,
            Status = "Assigned",
            CreatedAtUtc = now,
            CreatedByUserId = _currentUser.UserId
        };
        trip.DriverAssignments.Add(assignment);

        _db.Trips.Add(trip);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(trip.Id);
    }
}

public sealed class UpdateTripStatusCommandHandler : ICommandHandler<UpdateTripStatusCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerPushNotifier _customerPush;

    public UpdateTripStatusCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ICustomerPushNotifier? customerPush = null)
    {
        _db = db;
        _currentUser = currentUser;
        _customerPush = customerPush ?? NullCustomerPushNotifier.Instance;
    }

    public async Task<Result> Handle(
        UpdateTripStatusCommand request,
        CancellationToken cancellationToken)
    {
        var trip = await _db.Trips
            .FirstOrDefaultAsync(t => t.Id == request.TripId && !t.IsDeleted, cancellationToken);

        if (trip == null)
        {
            return Error.NotFound("Trips.NotFound", "الرحلة غير موجودة.");
        }

        var newStatus = GetRestaurantTripsQueryHandler.MapClientStatus(request.Status);
        trip.Status = newStatus;
        trip.ModifiedAtUtc = DateTime.UtcNow;
        trip.ModifiedByUserId = _currentUser.UserId;

        if (newStatus == TripStatus.InProgress)
        {
            if (!trip.StartedAtUtc.HasValue)
            {
                trip.StartedAtUtc = DateTime.UtcNow;
            }

            // Sync assigned orders & subscription days to OutForDelivery / OnTheWay
            var stops = await _db.TripStops
                .Where(s => s.TripId == trip.Id && !s.IsDeleted)
                .ToListAsync(cancellationToken);

            var stopDailyOrderIds = stops.Where(s => s.DailyOrderId.HasValue).Select(s => s.DailyOrderId!.Value).Distinct().ToList();
            var stopSubDayIds = stops.Where(s => s.SubscriptionDayId.HasValue).Select(s => s.SubscriptionDayId!.Value).Distinct().ToList();

            if (stopDailyOrderIds.Count > 0 || stopSubDayIds.Count > 0)
            {
                var dailyOrders = await _db.RestaurantDailyOrders
                    .Where(r => stopDailyOrderIds.Contains(r.Id) || (stopDailyOrderIds.Contains(r.SubscriptionDayId)) || stopSubDayIds.Contains(r.SubscriptionDayId))
                    .ToListAsync(cancellationToken);

                foreach (var dOrder in dailyOrders)
                {
                    dOrder.Status = OrderStatus.OnTheWay;
                    dOrder.ModifiedAtUtc = DateTime.UtcNow;
                    dOrder.ModifiedByUserId = _currentUser.UserId;

                    if (!stopSubDayIds.Contains(dOrder.SubscriptionDayId))
                    {
                        stopSubDayIds.Add(dOrder.SubscriptionDayId);
                    }
                }
            }

            if (stopSubDayIds.Count > 0)
            {
                var subDays = await _db.SubscriptionDays
                    .Where(sd => stopSubDayIds.Contains(sd.Id))
                    .ToListAsync(cancellationToken);

                var subIds = subDays.Select(s => s.SubscriptionId).Distinct().ToList();
                var customerUserIdBySubId = await _db.Subscriptions
                    .AsNoTracking()
                    .Where(s => subIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.CustomerUserId, cancellationToken);

                foreach (var sd in subDays)
                {
                    sd.Status = SubscriptionDayStatus.OutForDelivery;
                    sd.ModifiedAtUtc = DateTime.UtcNow;
                    sd.ModifiedByUserId = _currentUser.UserId;

                    if (customerUserIdBySubId.TryGetValue(sd.SubscriptionId, out var customerUserId) && customerUserId != Guid.Empty)
                    {
                        await _customerPush.NotifyOrderOutForDeliveryAsync(
                            customerUserId,
                            sd.Id,
                            sd.ServiceDate,
                            cancellationToken);
                    }
                }
            }
        }
        else if (newStatus == TripStatus.Completed && !trip.CompletedAtUtc.HasValue)
        {
            trip.CompletedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class UpdateTripStopStatusCommandHandler : ICommandHandler<UpdateTripStopStatusCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerPushNotifier _customerPush;

    public UpdateTripStopStatusCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ICustomerPushNotifier? customerPush = null)
    {
        _db = db;
        _currentUser = currentUser;
        _customerPush = customerPush ?? NullCustomerPushNotifier.Instance;
    }

    public async Task<Result> Handle(
        UpdateTripStopStatusCommand request,
        CancellationToken cancellationToken)
    {
        var stop = await _db.TripStops
            .FirstOrDefaultAsync(s => s.Id == request.StopId && s.TripId == request.TripId && !s.IsDeleted, cancellationToken);

        if (stop == null)
        {
            return Error.NotFound("Trips.StopNotFound", "محطة التوصيل غير موجودة.");
        }

        stop.Status = request.Status.ToLowerInvariant() switch
        {
            "picked_up" => TripStopStatus.PickedUp,
            "delivered" => TripStopStatus.Delivered,
            "failed" => TripStopStatus.Failed,
            _ => TripStopStatus.Pending
        };

        if (stop.Status == TripStopStatus.Delivered)
        {
            stop.DeliveredAtUtc = DateTime.UtcNow;

            // Synchronize linked SubscriptionDay and RestaurantDailyOrder to Delivered
            Guid? subDayId = stop.SubscriptionDayId;

            if (stop.DailyOrderId.HasValue)
            {
                var dailyOrder = await _db.RestaurantDailyOrders
                    .FirstOrDefaultAsync(r => r.Id == stop.DailyOrderId.Value, cancellationToken);
                if (dailyOrder != null)
                {
                    dailyOrder.Status = OrderStatus.Delivered;
                    dailyOrder.ModifiedAtUtc = DateTime.UtcNow;
                    dailyOrder.ModifiedByUserId = _currentUser.UserId;
                    subDayId ??= dailyOrder.SubscriptionDayId;
                }
            }

            if (!subDayId.HasValue && !string.IsNullOrEmpty(stop.OrderCode))
            {
                if (Guid.TryParse(stop.OrderCode, out var parsedId))
                {
                    subDayId = parsedId;
                }
                else
                {
                    var rdo = await _db.RestaurantDailyOrders
                        .FirstOrDefaultAsync(r => r.Id.ToString() == stop.OrderCode, cancellationToken);
                    if (rdo != null)
                    {
                        subDayId = rdo.SubscriptionDayId;
                    }
                }
            }

            if (subDayId.HasValue)
            {
                var subDay = await _db.SubscriptionDays
                    .FirstOrDefaultAsync(sd => sd.Id == subDayId.Value, cancellationToken);
                if (subDay != null)
                {
                    subDay.Status = SubscriptionDayStatus.Delivered;
                    subDay.ModifiedAtUtc = DateTime.UtcNow;
                    subDay.ModifiedByUserId = _currentUser.UserId;

                    var customerUserId = await _db.Subscriptions
                        .AsNoTracking()
                        .Where(s => s.Id == subDay.SubscriptionId)
                        .Select(s => s.CustomerUserId)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (customerUserId != Guid.Empty)
                    {
                        await _customerPush.NotifyOrderDeliveredAsync(
                            customerUserId,
                            subDay.Id,
                            subDay.ServiceDate,
                            cancellationToken);
                    }
                }

                var linkedOrders = await _db.RestaurantDailyOrders
                    .Where(r => r.SubscriptionDayId == subDayId.Value && r.Status != OrderStatus.Delivered)
                    .ToListAsync(cancellationToken);
                foreach (var lo in linkedOrders)
                {
                    lo.Status = OrderStatus.Delivered;
                    lo.ModifiedAtUtc = DateTime.UtcNow;
                    lo.ModifiedByUserId = _currentUser.UserId;
                }
            }
        }
        else if (stop.Status == TripStopStatus.Failed)
        {
            stop.FailureReason = request.FailureReason;
        }

        stop.ModifiedAtUtc = DateTime.UtcNow;
        stop.ModifiedByUserId = _currentUser.UserId;

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
