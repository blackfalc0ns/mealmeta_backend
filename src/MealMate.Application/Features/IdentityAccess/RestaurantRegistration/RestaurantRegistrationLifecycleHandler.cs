using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

public sealed class RestaurantRegistrationLifecycleHandler : INotificationHandler<OutboxLifecycleNotification>
{
    public const string SubmittedConsumerName = "RestaurantRegistrationSubmittedNotifier";
    public const string ApprovedConsumerName = "RestaurantRegistrationApprovedNotifier";
    public const string RejectedConsumerName = "RestaurantRegistrationRejectedNotifier";
    public const string ChangesRequestedConsumerName = "RestaurantRegistrationChangesRequestedNotifier";

    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationPushNotificationService _pushNotifier;
    private readonly IRegistrationReviewNotifier _notifier;
    private readonly ILogger<RestaurantRegistrationLifecycleHandler>? _logger;

    public RestaurantRegistrationLifecycleHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        IRegistrationPushNotificationService pushNotifier,
        IRegistrationReviewNotifier notifier,
        ILogger<RestaurantRegistrationLifecycleHandler>? logger = null)
    {
        _context = context;
        _clock = clock;
        _pushNotifier = pushNotifier;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task Handle(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        switch (notification.EventType)
        {
            case "RestaurantRegistrationSubmitted":
                await HandleSubmitted(notification, cancellationToken);
                break;
            case "RestaurantRegistrationApproved":
                await HandleApproved(notification, cancellationToken);
                break;
            case "RestaurantRegistrationRejected":
                await HandleRejected(notification, cancellationToken);
                break;
            case "RestaurantRegistrationChangesRequested":
                await HandleChangesRequested(notification, cancellationToken);
                break;
        }
    }

    private async Task HandleSubmitted(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(SubmittedConsumerName, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var registrationId = root.GetProperty("RegistrationId").GetGuid();
        var restaurantName = root.TryGetProperty("RestaurantName", out var nameProp) ? nameProp.GetString() ?? "مطعم جديد" : "مطعم جديد";

        await _pushNotifier.NotifyAdminsNewRegistrationAsync(registrationId, restaurantName, cancellationToken);

        await RecordProcessed(SubmittedConsumerName, notification.MessageId, cancellationToken);
    }

    private async Task HandleApproved(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(ApprovedConsumerName, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var registrationId = root.GetProperty("RegistrationId").GetGuid();
        var ownerUserId = root.TryGetProperty("OwnerUserId", out var userProp) && userProp.ValueKind == JsonValueKind.String
            ? userProp.GetGuid()
            : Guid.Empty;

        if (ownerUserId != Guid.Empty)
        {
            await _notifier.NotifyRegistrationUpdated(ownerUserId, registrationId, "approved", cancellationToken);
            await _pushNotifier.NotifyRestaurantRegistrationApprovedAsync(ownerUserId, cancellationToken);
        }

        await RecordProcessed(ApprovedConsumerName, notification.MessageId, cancellationToken);
    }

    private async Task HandleRejected(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(RejectedConsumerName, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var registrationId = root.GetProperty("RegistrationId").GetGuid();
        var ownerUserId = root.TryGetProperty("OwnerUserId", out var userProp) && userProp.ValueKind == JsonValueKind.String
            ? userProp.GetGuid()
            : Guid.Empty;
        var reason = root.TryGetProperty("Reason", out var reasonProp) ? reasonProp.GetString() ?? string.Empty : string.Empty;

        if (ownerUserId != Guid.Empty)
        {
            await _notifier.NotifyRegistrationUpdated(ownerUserId, registrationId, "rejected", cancellationToken);
            await _pushNotifier.NotifyRestaurantRegistrationRejectedAsync(ownerUserId, reason, cancellationToken);
        }

        await RecordProcessed(RejectedConsumerName, notification.MessageId, cancellationToken);
    }

    private async Task HandleChangesRequested(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(ChangesRequestedConsumerName, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var registrationId = root.GetProperty("RegistrationId").GetGuid();
        var ownerUserId = root.TryGetProperty("OwnerUserId", out var userProp) && userProp.ValueKind == JsonValueKind.String
            ? userProp.GetGuid()
            : Guid.Empty;
        var reason = root.TryGetProperty("Reason", out var reasonProp) ? reasonProp.GetString() ?? string.Empty : string.Empty;

        if (ownerUserId != Guid.Empty)
        {
            await _notifier.NotifyRegistrationUpdated(ownerUserId, registrationId, "changes-requested", cancellationToken);
            await _pushNotifier.NotifyRestaurantRegistrationChangesRequestedAsync(ownerUserId, reason, cancellationToken);
        }

        await RecordProcessed(ChangesRequestedConsumerName, notification.MessageId, cancellationToken);
    }

    private async Task<bool> IsAlreadyProcessed(string consumerName, Guid messageId, CancellationToken cancellationToken)
    {
        var processed = await _context.InboxProcessedEvents
            .AnyAsync(e => e.ConsumerName == consumerName && e.EventId == messageId, cancellationToken);

        if (processed)
        {
            _logger?.LogDebug("Consumer {Consumer} already processed event {EventId}, skipping duplicate execution.", consumerName, messageId);
        }

        return processed;
    }

    private async Task RecordProcessed(string consumerName, Guid messageId, CancellationToken cancellationToken)
    {
        _context.InboxProcessedEvents.Add(new InboxProcessedEvent
        {
            ConsumerName = consumerName,
            EventId = messageId,
            ProcessedAtUtc = _clock.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }
}
