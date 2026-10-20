using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.CalendarMeals;
using MealMate.Application.Features.DriverDelivery.DriverApp;
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
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;

    public CreateRestaurantTripCommandHandler(IAppDbContext db, ICurrentUser currentUser,
        IApplicationTransactionExecutor? transactions = null, IApplicationLockManager? locks = null)
    {
        _db = db;
        _currentUser = currentUser;
        _transactions = transactions;
        _locks = locks;
    }

    public async Task<Result<Guid>> Handle(
        CreateRestaurantTripCommand request,
        CancellationToken cancellationToken)
    {
        if (_transactions is null || _locks is null)
            return Error.Conflict("TRIP_TRANSACTION_UNAVAILABLE", "Trip assignment requires transactional locks.");
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue
            || _currentUser.UserType is not (UserType.Restaurant or UserType.DeliveryManager or UserType.Admin))
            return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", "Authenticated restaurant staff or dispatcher access is required.");
        var restaurantId = _currentUser.UserType switch
        {
            UserType.Admin => request.RestaurantId,
            UserType.DeliveryManager => _currentUser.RestaurantId.GetValueOrDefault(),
            _ => await _db.RestaurantProfiles.AsNoTracking().Where(r => r.UserId == _currentUser.UserId.Value)
                .Select(r => r.Id).FirstOrDefaultAsync(cancellationToken)
        };
        if (restaurantId == Guid.Empty || (_currentUser.UserType != UserType.Admin
            && request.RestaurantId != Guid.Empty && request.RestaurantId != restaurantId))
            return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", "A restaurant in the authenticated dispatcher scope is required.");

        return await _transactions.ExecuteResultAsync(async ct =>
        {
            var dailyOrderIds = request.Stops.Where(s => s.DailyOrderId.HasValue).Select(s => s.DailyOrderId!.Value).Distinct().ToArray();
            var subDayIds = request.Stops.Where(s => s.SubscriptionDayId.HasValue).Select(s => s.SubscriptionDayId!.Value).Distinct().ToArray();
            var linkedSubDays = dailyOrderIds.Length == 0 ? Array.Empty<Guid>() : await _db.RestaurantDailyOrders.AsNoTracking()
                .Where(x => dailyOrderIds.Contains(x.Id)).Select(x => x.SubscriptionDayId).Distinct().ToArrayAsync(ct);
            var allSubDayIds = subDayIds.Concat(linkedSubDays).Distinct().ToArray();
            var sourceTripIds = await _db.TripStops.AsNoTracking()
                .Where(s => (s.DailyOrderId.HasValue && dailyOrderIds.Contains(s.DailyOrderId.Value))
                    || (s.SubscriptionDayId.HasValue && allSubDayIds.Contains(s.SubscriptionDayId.Value)))
                .Select(s => s.TripId).Distinct().OrderBy(id => id).ToListAsync(ct);
            foreach (var sourceTripId in sourceTripIds) await _locks.LockTripAsync(sourceTripId, ct);
            foreach (var subDayId in allSubDayIds.OrderBy(id => id))
                await _locks.LockPickupBoxAsync(subDayId, ct);
            await _locks.LockDriverAsync(request.DriverId, ct);
            return await CreateCoreAsync(request, restaurantId, sourceTripIds, allSubDayIds, ct);
        }, cancellationToken);
    }

    private async Task<Result<Guid>> CreateCoreAsync(CreateRestaurantTripCommand request, Guid restaurantId, IReadOnlyCollection<Guid> lockedSourceTripIds, IReadOnlyCollection<Guid> canonicalSubDayIds, CancellationToken cancellationToken)
    {
        if (await DriverSupportHandoverMutationGuard.IsReservedReplacementAsync(_db, request.DriverId, cancellationToken))
            return Error.Conflict("DRIVER_RESERVED_FOR_HANDOVER", "This driver is reserved for an active support handover.");
        if (await _db.DriverAssignments.AsNoTracking().AnyAsync(a => a.DriverProfileId == request.DriverId
            && (a.Status == "Active" || a.Status == "Assigned")
            && _db.Trips.Any(t => t.Id == a.TripId && t.Status != TripStatus.Completed && t.Status != TripStatus.Cancelled), cancellationToken))
            return Error.Conflict("DRIVER_HAS_ACTIVE_TRIP", "The selected driver already has an active trip.");

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

        var requestedDailyOrderIds = request.Stops.Where(s => s.DailyOrderId.HasValue).Select(s => s.DailyOrderId!.Value).Distinct().ToArray();
        var subDayIds = request.Stops.Where(s => s.SubscriptionDayId.HasValue).Select(s => s.SubscriptionDayId!.Value).Distinct().ToArray();
        if (requestedDailyOrderIds.Length > 0 && await _db.RestaurantDailyOrders.AsNoTracking().AnyAsync(x => requestedDailyOrderIds.Contains(x.Id) && x.RestaurantId != restaurantId, cancellationToken))
            return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", "A selected daily order is outside the restaurant scope.");
        if (requestedDailyOrderIds.Length > 0 && await _db.RestaurantDailyOrders.AsNoTracking().CountAsync(x => requestedDailyOrderIds.Contains(x.Id), cancellationToken) != requestedDailyOrderIds.Length)
            return Error.NotFound("DAILY_ORDER_NOT_FOUND", "A selected daily order was not found.");
        var canonicalDays = subDayIds.ToList();
        if (requestedDailyOrderIds.Length > 0)
            canonicalDays.AddRange(await _db.RestaurantDailyOrders.AsNoTracking().Where(x => requestedDailyOrderIds.Contains(x.Id))
                .Select(x => x.SubscriptionDayId).Distinct().ToListAsync(cancellationToken));
        var canonicalDayRows = canonicalDays.Count == 0 ? new List<(Guid Id, Guid? RestaurantId)>() : (await _db.SubscriptionDays.AsNoTracking()
            .Where(x => canonicalDays.Contains(x.Id)).Select(x => new { x.Id, x.RestaurantId }).ToListAsync(cancellationToken))
            .Select(x => (x.Id, x.RestaurantId)).ToList();
        if (canonicalDayRows.Count != canonicalDays.Distinct().Count())
            return Error.NotFound("SUBSCRIPTION_DAY_NOT_FOUND", "A selected subscription day was not found.");
        if (canonicalDayRows.Any(x => x.RestaurantId.HasValue && x.RestaurantId.Value != restaurantId))
            return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", "A selected subscription day is outside the restaurant scope.");
        var legacyUnscopedDayIds = canonicalDayRows.Where(x => !x.RestaurantId.HasValue).Select(x => x.Id).ToArray();
        if (legacyUnscopedDayIds.Length > 0 && !await _db.RestaurantDailyOrders.AsNoTracking()
            .AnyAsync(x => legacyUnscopedDayIds.Contains(x.SubscriptionDayId) && x.RestaurantId == restaurantId, cancellationToken))
            return Error.Forbidden("SUBSCRIPTION_DAY_SCOPE_UNAVAILABLE", "A subscription day has no verifiable restaurant ownership.");
        var sourceStops = await _db.TripStops.AsNoTracking().Include(s => s.Trip)
            .Where(s => (s.DailyOrderId.HasValue && requestedDailyOrderIds.Contains(s.DailyOrderId.Value))
                || (s.SubscriptionDayId.HasValue && canonicalDays.Contains(s.SubscriptionDayId.Value)))
            .ToListAsync(cancellationToken);
        if (sourceStops.Any(s => !lockedSourceTripIds.Contains(s.TripId)))
            return Error.Conflict("SOURCE_TRIP_CHANGED", "An order changed trips while the new assignment was being prepared.");
        foreach (var sourceStop in sourceStops)
        {
            if (sourceStop.Trip.RestaurantId != restaurantId)
                return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", "An order is outside the authenticated restaurant scope.");
            if (sourceStop.Status == TripStopStatus.Delivered || sourceStop.Status == TripStopStatus.ReturnedToKitchen
                || sourceStop.Trip.Status is TripStatus.Completed or TripStatus.Cancelled)
                return Error.Conflict("SOURCE_STOP_TERMINAL", "A completed or returned order cannot be duplicated into a new trip.");
            if (await DriverSupportHandoverMutationGuard.HasActiveHandoverForTripAsync(_db, sourceStop.TripId, cancellationToken)
                || await DriverSupportHandoverMutationGuard.HasActiveHandoverForStopAsync(_db, sourceStop.Id, cancellationToken)
                || await DriverSupportHandoverMutationGuard.HasActiveHoldForStopAsync(_db, sourceStop.Id, cancellationToken))
                return Error.Conflict("SUPPORT_CUSTODY_BLOCKED", "An order with active support custody or a delivery hold cannot be assigned again.");
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
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;

    public UpdateTripStatusCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ICustomerPushNotifier? customerPush = null,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null)
    {
        _db = db;
        _currentUser = currentUser;
        _customerPush = customerPush ?? NullCustomerPushNotifier.Instance;
        _transactions = transactions;
        _locks = locks;
    }

    public Task<Result> Handle(UpdateTripStatusCommand request, CancellationToken cancellationToken)
    {
        if (_transactions is null || _locks is null) return Task.FromResult<Result>(Error.Conflict("TRIP_LOCK_UNAVAILABLE", "Trip status changes require transaction and lock services."));
        return _transactions.ExecuteAsync<Result>(async ct =>
        {
            if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue || _currentUser.UserType is not (UserType.Restaurant or UserType.DeliveryManager or UserType.Admin))
                return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", "Authenticated dispatcher access is required.");
            var restaurantId = await _db.Trips.AsNoTracking().Where(x => x.Id == request.TripId).Select(x => (Guid?)x.RestaurantId).FirstOrDefaultAsync(ct);
            if (!restaurantId.HasValue) return Error.NotFound("Trips.NotFound", "Trip was not found.");
            if (_currentUser.UserType == UserType.DeliveryManager && _currentUser.RestaurantId != restaurantId
                || _currentUser.UserType == UserType.Restaurant && !await _db.RestaurantProfiles.AnyAsync(r => r.Id == restaurantId && r.UserId == _currentUser.UserId.Value, ct))
                return Error.Forbidden("DISPATCHER_SCOPE_FORBIDDEN", "This trip is outside the dispatcher's restaurant scope.");
            await _locks.LockTripAsync(request.TripId, ct);
            if (await DriverSupportHandoverMutationGuard.HasActiveHandoverForTripAsync(_db, request.TripId, ct))
                return Error.Conflict("HANDOVER_PENDING", "An active support handover blocks trip status changes.");
            if (await _db.DeliveryHolds.AnyAsync(x => x.TripId == request.TripId && x.Status == "Active" && x.ReleasedAtUtc == null && x.ResolvedAtUtc == null, ct))
                return Error.Conflict("DELIVERY_HOLD_ACTIVE", "An unresolved support follow-up blocks trip status changes.");
            var refreshedTrip = await _db.Trips.FirstOrDefaultAsync(x => x.Id == request.TripId, ct);
            if (refreshedTrip is not null) await _db.ReloadEntityAsync(refreshedTrip, ct);
            return await HandleLockedAsync(request, ct);
        }, cancellationToken);
    }

    private async Task<Result> HandleLockedAsync(
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
        if (trip.Status is TripStatus.Completed or TripStatus.Cancelled && newStatus != trip.Status)
            return Error.Conflict("TRIP_TERMINAL", "A completed or cancelled trip cannot be reopened.");
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

        if (newStatus is TripStatus.Completed or TripStatus.Cancelled)
        {
            var assignments = await _db.DriverAssignments.Where(x => x.TripId == trip.Id
                && (x.Status == "Active" || x.Status == "Assigned")).ToListAsync(cancellationToken);
            foreach (var assignment in assignments) assignment.Status = newStatus == TripStatus.Completed ? "Completed" : "Cancelled";
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
    private readonly IApplicationTransactionExecutor? _transactions;
    private readonly IApplicationLockManager? _locks;

    public UpdateTripStopStatusCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ICustomerPushNotifier? customerPush = null,
        IApplicationTransactionExecutor? transactions = null,
        IApplicationLockManager? locks = null)
    {
        _db = db;
        _currentUser = currentUser;
        _customerPush = customerPush ?? NullCustomerPushNotifier.Instance;
        _transactions = transactions;
        _locks = locks;
    }

    public Task<Result> Handle(UpdateTripStopStatusCommand request, CancellationToken cancellationToken)
    {
        if (_transactions is null || _locks is null) return Task.FromResult<Result>(Error.Conflict("TRIP_LOCK_UNAVAILABLE", "Trip stop changes require transaction and lock services."));
        return _transactions.ExecuteAsync<Result>(async ct =>
        {
            if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue || _currentUser.UserType is not (UserType.Restaurant or UserType.DeliveryManager or UserType.Admin))
                return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", "Authenticated dispatcher access is required.");
            var restaurantId = await _db.Trips.AsNoTracking().Where(x => x.Id == request.TripId).Select(x => (Guid?)x.RestaurantId).FirstOrDefaultAsync(ct);
            if (!restaurantId.HasValue) return Error.NotFound("Trips.NotFound", "Trip was not found.");
            if (_currentUser.UserType == UserType.DeliveryManager && _currentUser.RestaurantId != restaurantId
                || _currentUser.UserType == UserType.Restaurant && !await _db.RestaurantProfiles.AnyAsync(r => r.Id == restaurantId && r.UserId == _currentUser.UserId.Value, ct))
                return Error.Forbidden("DISPATCHER_SCOPE_FORBIDDEN", "This trip is outside the dispatcher's restaurant scope.");
            await _locks.LockTripAsync(request.TripId, ct);
            if (await DriverSupportHandoverMutationGuard.HasActiveHandoverForTripAsync(_db, request.TripId, ct))
                return Error.Conflict("HANDOVER_PENDING", "An active support handover blocks trip stop changes.");
            if (await DriverSupportHandoverMutationGuard.HasActiveHoldForStopAsync(_db, request.StopId, ct))
                return Error.Conflict("DELIVERY_HOLD_ACTIVE", "An unresolved support follow-up blocks trip stop changes.");
            var current = await _db.TripStops.AsNoTracking().Where(s => s.Id == request.StopId && s.TripId == request.TripId).Select(s => new { s.Status, s.TripId }).FirstOrDefaultAsync(ct);
            if (current is null) return Error.NotFound("Trips.StopNotFound", "Delivery stop was not found.");
            var tripStatus = await _db.Trips.AsNoTracking().Where(t => t.Id == request.TripId).Select(t => t.Status).FirstAsync(ct);
            if (tripStatus is TripStatus.Completed or TripStatus.Cancelled)
                return Error.Conflict("TRIP_TERMINAL", "A terminal trip cannot have its stops changed.");
            if (current.Status is TripStopStatus.Delivered or TripStopStatus.ReturnedToKitchen)
                return Error.Conflict("TERMINAL_BOX_CANNOT_TRANSFER", "A delivered or returned stop cannot be reopened.");
            var refreshedStop = await _db.TripStops.FirstOrDefaultAsync(x => x.Id == request.StopId, ct);
            if (refreshedStop is not null) await _db.ReloadEntityAsync(refreshedStop, ct);
            return await HandleLockedAsync(request, ct);
        }, cancellationToken);
    }

    private async Task<Result> HandleLockedAsync(
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
            _ => throw new InvalidOperationException("Unsupported delivery stop status.")
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
