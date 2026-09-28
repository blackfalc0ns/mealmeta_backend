using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
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

    public CapacityTransferService(
        IAppDbContext db,
        IApplicationLockManager lockManager,
        IApplicationTransactionExecutor transactionExecutor,
        IDateTimeProvider clock)
    {
        _db = db;
        _lockManager = lockManager;
        _transactionExecutor = transactionExecutor;
        _clock = clock;
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
            // 1. Deadlock-free ordered capacity locks
            var lockKeys = new List<(Guid RestaurantId, DateOnly ServiceDate)>
            {
                (newRestaurantId, serviceDate)
            };
            if (oldRestaurantId.HasValue && oldRestaurantId.Value != Guid.Empty && oldRestaurantId.Value != newRestaurantId)
            {
                lockKeys.Add((oldRestaurantId.Value, serviceDate));
            }

            await _lockManager.LockRestaurantCapacityAsync(lockKeys, transactionCt);

            // 2. Load existing active hold
            var existingHold = await _db.CapacityHolds
                .FirstOrDefaultAsync(h => h.SubscriptionId == subscriptionId
                    && h.ServiceDate == serviceDate
                    && h.Status != CapacityHoldStatus.Released
                    && h.Status != CapacityHoldStatus.Expired, transactionCt);

            var requiredQuantity = existingHold != null ? existingHold.Quantity : Math.Max(1, quantity);

            // 3. Query maximum daily capacity
            var limit = await _db.DailyCapacities
                .Where(x => x.RestaurantId == newRestaurantId && x.ServiceDate == serviceDate && x.CapacityType == CapacityType.DateOverride)
                .Select(x => (int?)x.MaxBoxes)
                .FirstOrDefaultAsync(transactionCt)
                ?? await _db.DailyCapacities
                    .Where(x => x.RestaurantId == newRestaurantId && x.CapacityType == CapacityType.Base && x.ServiceDate <= serviceDate)
                    .OrderByDescending(x => x.ServiceDate)
                    .Select(x => (int?)x.MaxBoxes)
                    .FirstOrDefaultAsync(transactionCt);

            if (limit is null || limit <= 0)
            {
                return Result.Failure(Error.Conflict("RESTAURANT_CAPACITY_FULL", "Restaurant capacity is full or unavailable for the selected date."));
            }

            // 4. Sum active CapacityHold.Quantity for new restaurant
            var nowUtc = _clock.UtcNow;
            var activeReserved = await _db.CapacityHolds
                .Where(h => h.RestaurantId == newRestaurantId
                    && h.ServiceDate == serviceDate
                    && ((h.Status == CapacityHoldStatus.PendingPayment && h.ExpiresAtUtc > nowUtc)
                        || h.Status == CapacityHoldStatus.PaymentConfirmed
                        || h.Status == CapacityHoldStatus.Confirmed))
                .SumAsync(h => (int?)h.Quantity, transactionCt) ?? 0;

            if (activeReserved + requiredQuantity > limit.Value)
            {
                return Result.Failure(Error.Conflict("RESTAURANT_CAPACITY_FULL", "Restaurant capacity is full for the selected date."));
            }

            // 5. Atomic swap / update
            if (existingHold != null)
            {
                existingHold.RestaurantId = newRestaurantId;
                existingHold.Status = CapacityHoldStatus.Confirmed;
                existingHold.ConfirmedAtUtc = nowUtc;
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
                    ConfirmedAtUtc = nowUtc,
                    ExpiresAtUtc = nowUtc.AddDays(7)
                };
                _db.CapacityHolds.Add(newHold);
            }

            await _db.SaveChangesAsync(transactionCt);
            return Result.Success();
        }, ct);
    }
}
