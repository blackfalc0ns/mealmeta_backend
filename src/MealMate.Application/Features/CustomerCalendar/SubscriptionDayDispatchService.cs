using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;

namespace MealMate.Application.Features.CustomerCalendar;

public interface ISubscriptionDayDispatchService
{
    Task LockAndSendToRestaurantAsync(SubscriptionDay day, Guid subscriptionId, CancellationToken cancellationToken);
}

public sealed class SubscriptionDayDispatchService : ISubscriptionDayDispatchService
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IRestaurantOrderNotifier _notifier;

    public SubscriptionDayDispatchService(
        IAppDbContext db,
        IDateTimeProvider clock,
        IRestaurantOrderNotifier? notifier = null)
    {
        _db = db;
        _clock = clock;
        _notifier = notifier ?? NullRestaurantOrderNotifier.Instance;
    }

    public async Task LockAndSendToRestaurantAsync(
        SubscriptionDay day,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        if (day.Status == SubscriptionDayStatus.SentToRestaurant
            || day.Status == SubscriptionDayStatus.ConfirmedByRestaurant
            || day.Status == SubscriptionDayStatus.Preparing
            || day.Status == SubscriptionDayStatus.OutForDelivery
            || day.Status == SubscriptionDayStatus.Delivered)
            return;

        day.LockAtUtc ??= _clock.UtcNow;
        day.Status = SubscriptionDayStatus.SentToRestaurant;

        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = subscriptionId,
            OperationType = "ORDER_SENT_TO_RESTAURANT",
            PreviousStatus = SubscriptionStatus.Active,
            NewStatus = SubscriptionStatus.Active,
            Reason = $"DayId={day.Id}; ServiceDate={day.ServiceDate}; RestaurantId={day.RestaurantId}; LockedAt={day.LockAtUtc:O}",
            PerformedByUserId = Guid.Empty,
            PerformedByUserType = UserType.Admin,
            TimestampUtc = _clock.UtcNow
        });

        if (day.RestaurantId is { } restaurantId)
        {
            await _notifier.NotifyOrderSentToRestaurantAsync(
                restaurantId,
                day.Id,
                day.ServiceDate,
                cancellationToken);
        }
    }
}

public sealed class NullSubscriptionDayDispatchService : ISubscriptionDayDispatchService
{
    public static readonly NullSubscriptionDayDispatchService Instance = new();

    public Task LockAndSendToRestaurantAsync(
        SubscriptionDay day,
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        if (day.Status == SubscriptionDayStatus.SentToRestaurant
            || day.Status == SubscriptionDayStatus.ConfirmedByRestaurant
            || day.Status == SubscriptionDayStatus.Preparing
            || day.Status == SubscriptionDayStatus.OutForDelivery
            || day.Status == SubscriptionDayStatus.Delivered)
            return Task.CompletedTask;

        day.LockAtUtc ??= DateTime.UtcNow;
        day.Status = SubscriptionDayStatus.SentToRestaurant;
        return Task.CompletedTask;
    }
}
