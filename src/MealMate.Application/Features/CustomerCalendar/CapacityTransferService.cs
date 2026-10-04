using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCalendar;

public sealed class CapacityTransferService : ICapacityTransferService
{
    private readonly IAppDbContext _db;
    private readonly IApplicationLockManager _lockManager;
    private readonly IApplicationTransactionExecutor _transactionExecutor;
    private readonly IDateTimeProvider _clock;
    private readonly ICapacityConcurrencyLock _capacityLock;
    private readonly IRestaurantCapacityReader _capacityReader;

    public CapacityTransferService(
        IAppDbContext db,
        IApplicationLockManager lockManager,
        IApplicationTransactionExecutor transactionExecutor,
        IDateTimeProvider clock,
        ICapacityConcurrencyLock capacityLock,
        IRestaurantCapacityReader capacityReader)
    {
        _db = db;
        _lockManager = lockManager;
        _transactionExecutor = transactionExecutor;
        _clock = clock;
        _capacityLock = capacityLock;
        _capacityReader = capacityReader;
    }

    public async Task<Result> TransferCapacityAsync(
        Guid subscriptionId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        Guid? oldRestaurantId,
        Guid newRestaurantId,
        int quantity = 1,
        CancellationToken ct = default)
    {
        return await _transactionExecutor.ExecuteAsync(async transactionCt =>
        {
            // 1. Same advisory-lock protocol as checkout/payment/expiry (G28), acquired in
            //    deterministic restaurant-then-date order so opposite transfers cannot
            //    deadlock; the legacy row locks are kept on top of it.
            var lockKeys = new List<(Guid RestaurantId, DateOnly ServiceDate)>
            {
                (newRestaurantId, serviceDate)
            };
            if (oldRestaurantId.HasValue && oldRestaurantId.Value != Guid.Empty && oldRestaurantId.Value != newRestaurantId)
            {
                lockKeys.Add((oldRestaurantId.Value, serviceDate));
            }

            foreach (var key in lockKeys.OrderBy(k => k.RestaurantId).ThenBy(k => k.ServiceDate))
            {
                await _capacityLock.AcquireAsync(key.RestaurantId, [key.ServiceDate], transactionCt);
            }
            await _lockManager.LockRestaurantCapacityAsync(lockKeys, transactionCt);

            // 2. Re-read capacity through the unified reader now that the locks are held.
            var unitsByDate = await _capacityReader.ReadAsync([(newRestaurantId, serviceDate)], transactionCt);
            var units = unitsByDate[(newRestaurantId, serviceDate)];
            if (units.LimitBoxes is not { } limit || limit <= 0)
            {
                return Result.Failure(Error.Conflict("RESTAURANT_CAPACITY_FULL", "Restaurant capacity is full or unavailable for the selected date."));
            }

            // 3. Load existing active hold
            var existingHold = await _db.CapacityHolds
                .FirstOrDefaultAsync(h => h.SubscriptionId == subscriptionId
                    && h.ServiceDate == serviceDate
                    && h.Status != CapacityHoldStatus.Released
                    && h.Status != CapacityHoldStatus.Expired, transactionCt);

            var requiredQuantity = existingHold != null ? existingHold.Quantity : Math.Max(1, quantity);

            // 4. Moving an identity must not count it against its own target (G29):
            //    subtract it when this identity is already committed at the target
            //    through its hold or its day.
            var dayAtTarget = await _db.SubscriptionDays.AsNoTracking()
                .AnyAsync(d => d.SubscriptionId == subscriptionId
                    && d.ServiceDate == serviceDate
                    && d.RestaurantId == newRestaurantId
                    && !d.IsDeleted, transactionCt);
            var ownIdentityCountedAtTarget = (existingHold != null && existingHold.RestaurantId == newRestaurantId) || dayAtTarget;
            var committedExcludingOwn = Math.Max(0, units.TotalCommittedUnits - (ownIdentityCountedAtTarget ? 1 : 0));

            if (committedExcludingOwn + requiredQuantity > limit)
            {
                return Result.Failure(Error.Conflict("RESTAURANT_CAPACITY_FULL", "Restaurant capacity is full for the selected date."));
            }

            // 5. Atomic swap / update
            if (existingHold != null)
            {
                existingHold.RestaurantId = newRestaurantId;
                existingHold.Status = CapacityHoldStatus.Confirmed;
                existingHold.ConfirmedAtUtc = _clock.UtcNow;
            }
            else
            {
                var newHold = new CapacityHold
                {
                    RestaurantId = newRestaurantId,
                    SubscriptionId = subscriptionId,
                    ServiceDate = serviceDate,
                    Quantity = requiredQuantity,
                    Status = CapacityHoldStatus.Confirmed,
                    ConfirmedAtUtc = _clock.UtcNow,
                    ExpiresAtUtc = _clock.UtcNow.AddDays(7)
                };
                _db.CapacityHolds.Add(newHold);
            }

            await _db.SaveChangesAsync(transactionCt);
            return Result.Success();
        }, ct);
    }
}
