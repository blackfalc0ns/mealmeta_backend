using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public enum ReplacementStatus
{
    ReplacedWithAlternative = 1,
    NoEligibleAlternative = 2,
    DayNotFound = 3
}

public sealed record ReplacementResult(
    ReplacementStatus Status,
    Guid? NewRestaurantId = null,
    string? NewRestaurantName = null,
    string? Message = null);

public interface IOrderReplacementOrchestrator
{
    Task<ReplacementResult> AttemptAutoReplacementAsync(
        Guid subscriptionDayId,
        CancellationToken cancellationToken = default);
}

public sealed class OrderReplacementOrchestrator : IOrderReplacementOrchestrator
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IRestaurantOrderNotifier _restaurantNotifier;
    private readonly ICustomerPushNotifier _customerPush;

    public OrderReplacementOrchestrator(
        IAppDbContext db,
        IDateTimeProvider clock,
        IRestaurantOrderNotifier? restaurantNotifier = null,
        ICustomerPushNotifier? customerPush = null)
    {
        _db = db;
        _clock = clock;
        _restaurantNotifier = restaurantNotifier ?? NullRestaurantOrderNotifier.Instance;
        _customerPush = customerPush ?? NullCustomerPushNotifier.Instance;
    }

    public async Task<ReplacementResult> AttemptAutoReplacementAsync(
        Guid subscriptionDayId,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        var day = await _db.SubscriptionDays
            .FirstOrDefaultAsync(d => d.Id == subscriptionDayId, cancellationToken);

        if (day == null)
        {
            return new ReplacementResult(ReplacementStatus.DayNotFound, Message: "Subscription day not found");
        }

        var currentRestaurantId = day.RestaurantId;

        // 1. Try finding alternative candidate from SubscriptionRestaurantSnapshots
        var snapshots = await _db.SubscriptionRestaurantSnapshots
            .AsNoTracking()
            .Where(s => s.SubscriptionId == day.SubscriptionId && s.RestaurantId != currentRestaurantId)
            .ToListAsync(cancellationToken);

        var candidateRestaurantIds = snapshots.Select(s => s.RestaurantId).ToList();
        var candidateProfiles = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(r => candidateRestaurantIds.Contains(r.Id) && !r.IsDeleted && r.IsOperationallyActive)
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        RestaurantProfile? chosenProfile = null;

        var matchingSnapshot = snapshots.FirstOrDefault(s => candidateProfiles.ContainsKey(s.RestaurantId));
        if (matchingSnapshot != null)
        {
            chosenProfile = candidateProfiles[matchingSnapshot.RestaurantId];
        }

        // 2. If no candidate from snapshots, fallback to any active restaurant in the system
        if (chosenProfile == null)
        {
            chosenProfile = await _db.RestaurantProfiles
                .AsNoTracking()
                .Where(r => r.Id != currentRestaurantId && !r.IsDeleted && r.IsOperationallyActive)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // 3. If no alternative restaurant found at all
        if (chosenProfile == null)
        {
            day.Status = SubscriptionDayStatus.ReplacementRequired;
            var currentDailyOrder = await _db.RestaurantDailyOrders
                .FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id, cancellationToken);
            if (currentDailyOrder != null)
            {
                currentDailyOrder.Status = OrderStatus.RestaurantRejected;
                currentDailyOrder.ModifiedAtUtc = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            return new ReplacementResult(ReplacementStatus.NoEligibleAlternative, Message: "No eligible active alternative restaurant found.");
        }

        // 4. Re-route to chosen alternate restaurant
        var newRestaurantId = chosenProfile.Id;
        var newRestaurantName = chosenProfile.TradeNameAr ?? chosenProfile.TradeNameEn ?? "المطعم البديل";

        day.RestaurantId = newRestaurantId;
        day.Status = SubscriptionDayStatus.SentToRestaurant;
        day.LockAtUtc = now;

        var dailyOrder = await _db.RestaurantDailyOrders
            .FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id, cancellationToken);

        if (dailyOrder != null)
        {
            dailyOrder.RestaurantId = newRestaurantId;
            dailyOrder.Status = OrderStatus.AwaitingRestaurantConfirmation;
            dailyOrder.ModifiedAtUtc = now;
        }
        else
        {
            dailyOrder = new RestaurantDailyOrder
            {
                RestaurantId = newRestaurantId,
                SubscriptionDayId = day.Id,
                ServiceDate = day.ServiceDate,
                Status = OrderStatus.AwaitingRestaurantConfirmation,
                BoxCount = 1,
                CreatedAtUtc = now
            };
            _db.RestaurantDailyOrders.Add(dailyOrder);
        }

        _db.DailyRestaurantSelections.Add(new DailyRestaurantSelection
        {
            SubscriptionDayId = day.Id,
            RestaurantId = newRestaurantId,
            SelectionSource = "AutoReplacement",
            SelectedAtUtc = now
        });

        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = day.SubscriptionId,
            OperationType = "ORDER_AUTO_REPLACED_ALTERNATIVE_RESTAURANT",
            PreviousStatus = SubscriptionStatus.Active,
            NewStatus = SubscriptionStatus.Active,
            Reason = $"DayId={day.Id}; ServiceDate={day.ServiceDate}; FromRestaurant={currentRestaurantId}; ToRestaurant={newRestaurantId}; Auto re-routed.",
            PerformedByUserId = Guid.Empty,
            PerformedByUserType = UserType.Admin,
            TimestampUtc = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Notify new alternative restaurant
        await _restaurantNotifier.NotifyOrderSentToRestaurantAsync(
            newRestaurantId,
            day.Id,
            day.ServiceDate,
            cancellationToken);

        // Notify customer
        var subscription = await _db.Subscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == day.SubscriptionId, cancellationToken);

        if (subscription != null)
        {
            await _customerPush.NotifyOrderReplacedAsync(
                subscription.CustomerUserId,
                day.Id,
                day.ServiceDate,
                newRestaurantName,
                cancellationToken);
        }

        return new ReplacementResult(
            ReplacementStatus.ReplacedWithAlternative,
            newRestaurantId,
            newRestaurantName,
            "Successfully re-routed order to alternative restaurant.");
    }
}
