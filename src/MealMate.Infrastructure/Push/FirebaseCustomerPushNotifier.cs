using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerPush;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Push;

public sealed class FirebaseCustomerPushNotifier : ICustomerPushNotifier
{
    private readonly IAppDbContext _db;
    private readonly IFcmMessagingClient _fcm;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<FirebaseCustomerPushNotifier> _logger;

    public FirebaseCustomerPushNotifier(
        IAppDbContext db,
        IFcmMessagingClient fcm,
        IDateTimeProvider clock,
        ILogger<FirebaseCustomerPushNotifier> logger)
    {
        _db = db;
        _fcm = fcm;
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

    private async Task SendOnceAsync(
        Guid customerUserId,
        string eventKey,
        Guid subjectId,
        Func<bool, CustomerPushTemplates.PushCopy> copyFactory,
        CancellationToken cancellationToken)
    {
        if (!_fcm.IsConfigured)
            return;

        var alreadySent = await _db.CustomerPushDeliveryLogs.AsNoTracking()
            .AnyAsync(x => x.UserId == customerUserId && x.EventKey == eventKey && x.SubjectId == subjectId, cancellationToken);
        if (alreadySent)
            return;

        var tokens = await _db.CustomerDeviceTokens
            .Where(x => x.UserId == customerUserId && x.IsActive)
            .ToListAsync(cancellationToken);
        if (tokens.Count == 0)
            return;

        var preferredLanguage = await _db.CustomerProfiles.AsNoTracking()
            .Where(x => x.UserId == customerUserId)
            .Select(x => x.PreferredLanguage)
            .FirstOrDefaultAsync(cancellationToken);
        var isRtl = !string.Equals(preferredLanguage, "en", StringComparison.OrdinalIgnoreCase);
        var copy = copyFactory(isRtl);

        var sentToAny = false;
        foreach (var device in tokens)
        {
            var result = await _fcm.SendAsync(
                device.Token,
                copy.Title,
                copy.Body,
                new Dictionary<string, string>
                {
                    ["eventKey"] = eventKey,
                    ["subjectId"] = subjectId.ToString(),
                    ["deepLink"] = copy.DeepLink,
                },
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
