using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

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
    private readonly ICustomerDeliveryAddressService _deliveryAddresses;
    private readonly ICustomerPushNotifier _customerPush;

    public SubscriptionDayDispatchService(
        IAppDbContext db,
        IDateTimeProvider clock,
        ICustomerDeliveryAddressService deliveryAddresses,
        IRestaurantOrderNotifier? notifier = null,
        ICustomerPushNotifier? customerPush = null)
    {
        _db = db;
        _clock = clock;
        _deliveryAddresses = deliveryAddresses;
        _notifier = notifier ?? NullRestaurantOrderNotifier.Instance;
        _customerPush = customerPush ?? NullCustomerPushNotifier.Instance;
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

        var subscription = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.Id == subscriptionId)
            .Select(x => new { x.CustomerUserId })
            .FirstOrDefaultAsync(cancellationToken);
        if (subscription is not null)
        {
            await _deliveryAddresses.SnapshotDayDeliveryAsync(
                day,
                subscription.CustomerUserId,
                _clock.UtcNow,
                cancellationToken);
        }

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

        if (subscription is not null)
        {
            await _customerPush.NotifyOrderLockedAsync(
                subscription.CustomerUserId,
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
