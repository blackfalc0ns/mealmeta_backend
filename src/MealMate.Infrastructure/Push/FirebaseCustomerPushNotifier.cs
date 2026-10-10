using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerNotifications;
using MealMate.Application.Features.CustomerPush;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

public sealed class FirebaseCustomerPushNotifier : ICustomerPushNotifier
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly ICustomerNotificationWriter _inbox;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<FirebaseCustomerPushNotifier> _logger;

    public FirebaseCustomerPushNotifier(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        ICustomerNotificationWriter inbox,
        IDateTimeProvider clock,
        ILogger<FirebaseCustomerPushNotifier> logger)
    {
        _db = db;
        _fcm = fcm;
        _inbox = inbox;
        _clock = clock;
        _logger = logger;
    }

    public Task NotifySubscriptionPaymentConfirmedAsync(
        Guid customerUserId,
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
        => SendOnceAsync(
            customerUserId,
            CustomerPushEventKeys.SubscriptionPaymentConfirmed,
            subscriptionId,
            isRtl => CustomerPushTemplates.SubscriptionPaymentConfirmed(isRtl),
            cancellationToken);

    public async Task NotifyReplacementRequiredAsync(Guid customerUserId, Guid subscriptionDayId,
        DateOnly serviceDate, CancellationToken cancellationToken = default)
    {
        var reasonCode = await _db.SubscriptionDays.AsNoTracking().Where(x => x.Id == subscriptionDayId)
            .Select(x => x.FailureReasonCode).SingleOrDefaultAsync(cancellationToken);
        await SendOnceAsync(customerUserId, CustomerPushEventKeys.ReplacementRequired, subscriptionDayId,
            isRtl => CustomerPushTemplates.ReplacementRequired(serviceDate, isRtl, reasonCode), cancellationToken, reasonCode);
    }

    public Task NotifyOrderLockedAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
        => SendOnceAsync(
            customerUserId,
            CustomerPushEventKeys.OrderLocked72h,
            subscriptionDayId,
            isRtl => CustomerPushTemplates.OrderLocked72h(serviceDate, isRtl),
            cancellationToken);

    public Task NotifyOrderConfirmedByRestaurantAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
        => SendOnceAsync(
            customerUserId,
            CustomerPushEventKeys.OrderConfirmedByRestaurant,
            subscriptionDayId,
            isRtl => CustomerPushTemplates.OrderConfirmedByRestaurant(serviceDate, isRtl),
            cancellationToken);

    public Task NotifyDeliveryPrepReminderAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
        => SendOnceAsync(
            customerUserId,
            CustomerPushEventKeys.DeliveryPrep24h,
            subscriptionDayId,
            isRtl => CustomerPushTemplates.DeliveryPrep24h(serviceDate, isRtl),
            cancellationToken);

    public Task NotifyOrderOutForDeliveryAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
        => SendOnceAsync(
            customerUserId,
            CustomerPushEventKeys.OrderOutForDelivery,
            subscriptionDayId,
            isRtl => CustomerPushTemplates.OrderOutForDelivery(serviceDate, isRtl),
            cancellationToken);

    public Task NotifyDriverArrivedAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
        => SendOnceAsync(
            customerUserId,
            CustomerPushEventKeys.DriverArrived,
            subscriptionDayId,
            isRtl => CustomerPushTemplates.DriverArrived(serviceDate, isRtl),
            cancellationToken);

    public Task NotifyOrderDeliveredAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        CancellationToken cancellationToken = default)
        => SendOnceAsync(
            customerUserId,
            CustomerPushEventKeys.OrderDelivered,
            subscriptionDayId,
            isRtl => CustomerPushTemplates.OrderDelivered(serviceDate, isRtl),
            cancellationToken);

    public Task NotifyInfluencerPromotedAsync(
        Guid customerUserId,
        string influencerCode,
        decimal commissionRate,
        CancellationToken cancellationToken = default)
        => SendOnceAsync(
            customerUserId,
            CustomerPushEventKeys.InfluencerPromoted,
            customerUserId,
            isRtl => CustomerPushTemplates.InfluencerPromoted(influencerCode, commissionRate, isRtl),
            cancellationToken);

    public Task NotifyOrderReplacedAsync(
        Guid customerUserId,
        Guid subscriptionDayId,
        DateOnly serviceDate,
        string newRestaurantName,
        CancellationToken cancellationToken = default)
        => SendOnceAsync(
            customerUserId,
            CustomerPushEventKeys.OrderReplaced,
            subscriptionDayId,
            isRtl => CustomerPushTemplates.OrderReplaced(serviceDate, subscriptionDayId, newRestaurantName, isRtl),
            cancellationToken);

    private async Task SendOnceAsync(
        Guid customerUserId,
        string eventKey,
        Guid subjectId,
        Func<bool, CustomerPushTemplates.PushCopy> copyFactory,
        CancellationToken cancellationToken,
        string? failureReasonCode = null)
    {
        // Inbox entry first: the customer must see a real business event even without
        // FCM configuration or device tokens. The delivery-log idempotency below only
        // gates the push channel, never the inbox.
        await _inbox.EnsureCreatedAsync(customerUserId, eventKey, subjectId, cancellationToken);

        if (eventKey == CustomerPushEventKeys.ReplacementRequired &&
            !await _db.SubscriptionDays.AsNoTracking().AnyAsync(x => x.Id == subjectId &&
                x.Status == MealMate.Domain.Enums.SubscriptionDayStatus.ReplacementRequired, cancellationToken)) return;

        var alreadySent = await _db.CustomerPushDeliveryLogs.AsNoTracking()
            .AnyAsync(x => x.UserId == customerUserId && x.EventKey == eventKey && x.SubjectId == subjectId, cancellationToken);
        if (alreadySent)
            return;

        if (!_fcm.IsConfigured)
        {
            // No simulated delivery: the inbox already holds the event, and a push
            // delivery log may only be written after a real FCM success.
            _logger.LogInformation(
                "FCM is not configured; inbox notification stored without push delivery for user {UserId}, event {EventKey}, subject {SubjectId}.",
                customerUserId,
                eventKey,
                subjectId);
            return;
        }

        var tokens = await _db.CustomerDeviceTokens
            .Where(x => x.UserId == customerUserId && x.IsActive)
            .ToListAsync(cancellationToken);
        if (tokens.Count == 0)
        {
            _logger.LogWarning(
                "Skipping push notification delivery for user {UserId}, event {EventKey}: No active device tokens found in CustomerDeviceTokens.",
                customerUserId,
                eventKey);
            return;
        }

        var preferredLanguage = await _db.CustomerProfiles.AsNoTracking()
            .Where(x => x.UserId == customerUserId)
            .Select(x => x.PreferredLanguage)
            .FirstOrDefaultAsync(cancellationToken);
        var isRtl = !string.Equals(preferredLanguage, "en", StringComparison.OrdinalIgnoreCase);
        var copy = copyFactory(isRtl);
        var data = new Dictionary<string, string>
        {
            ["eventKey"] = eventKey,
            ["subjectId"] = subjectId.ToString(),
            ["deepLink"] = copy.DeepLink,
        };
        if (failureReasonCode is not null) data["failureReasonCode"] = failureReasonCode;

        var sentToAny = false;
        foreach (var device in tokens)
        {
            var result = await _fcm.SendAsync(
                device.Token,
                copy.Title,
                copy.Body,
                data,
                cancellationToken);

            if (result.IsSuccess)
            {
                sentToAny = true;
                device.LastSeenAtUtc = _clock.UtcNow;
                continue;
            }

            if (result.IsInvalidToken)
            {
                device.IsActive = false;
                device.ModifiedAtUtc = _clock.UtcNow;
                _logger.LogInformation("Deactivated invalid FCM token for user {UserId}.", customerUserId);
            }
            else
            {
                _logger.LogWarning(
                    "FCM send failed for user {UserId}, event {EventKey}: {Error}",
                    customerUserId,
                    eventKey,
                    result.ErrorMessage);
            }
        }

        if (sentToAny)
        {
            _db.CustomerPushDeliveryLogs.Add(new CustomerPushDeliveryLog
            {
                UserId = customerUserId,
                EventKey = eventKey,
                SubjectId = subjectId,
                SentAtUtc = _clock.UtcNow,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
