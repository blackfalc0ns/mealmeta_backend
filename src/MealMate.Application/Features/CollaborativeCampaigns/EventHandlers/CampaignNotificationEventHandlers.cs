using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.CollaborativeCampaigns.EventHandlers;

public sealed class CampaignInvitationNotificationHandler :
    INotificationHandler<OutboxLifecycleNotification>,
    INotificationHandler<OutboxMessageNotification>
{
    public const string ConsumerName = "CampaignInvitationNotifier";
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ICampaignNotificationSender _sender;
    private readonly ILogger<CampaignInvitationNotificationHandler>? _logger;

    public CampaignInvitationNotificationHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        ICampaignNotificationSender sender,
        ILogger<CampaignInvitationNotificationHandler>? logger = null)
    {
        _context = context;
        _clock = clock;
        _sender = sender;
        _logger = logger;
    }

    public Task Handle(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
        => HandleInternal(notification.MessageId, notification.EventType, notification.PayloadJson, cancellationToken);

    public Task Handle(OutboxMessageNotification notification, CancellationToken cancellationToken)
        => HandleInternal(notification.MessageId, notification.EventType, notification.PayloadJson, cancellationToken);

    private async Task HandleInternal(Guid messageId, string eventType, string payloadJson, CancellationToken cancellationToken)
    {
        if (eventType != "CampaignInvitationSent")
        {
            return;
        }

        var alreadyProcessed = await _context.InboxProcessedEvents
            .AnyAsync(e => e.ConsumerName == ConsumerName && e.EventId == messageId, cancellationToken);

        if (alreadyProcessed)
        {
            _logger?.LogDebug("Consumer {Consumer} already processed event {EventId}, skipping duplicate execution.", ConsumerName, messageId);
            return;
        }

        string recipientEmail = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("RecipientEmail", out var prop))
            {
                recipientEmail = prop.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            recipientEmail = string.Empty;
        }

        await _sender.SendEmailAsync(
            recipientEmail,
            "MealMate: Collaborative Campaign Invitation",
            "You have been invited to participate in a collaborative discount campaign.",
            cancellationToken);

        _context.InboxProcessedEvents.Add(new InboxProcessedEvent
        {
            ConsumerName = ConsumerName,
            EventId = messageId,
            ProcessedAtUtc = _clock.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class CapacityChangeAlertNotificationHandler :
    INotificationHandler<OutboxLifecycleNotification>,
    INotificationHandler<OutboxMessageNotification>
{
    public const string ConsumerName = "CapacityChangeAlertNotifier";
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ICampaignNotificationSender _sender;
    private readonly ILogger<CapacityChangeAlertNotificationHandler>? _logger;

    public CapacityChangeAlertNotificationHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        ICampaignNotificationSender sender,
        ILogger<CapacityChangeAlertNotificationHandler>? logger = null)
    {
        _context = context;
        _clock = clock;
        _sender = sender;
        _logger = logger;
    }

    public Task Handle(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
        => HandleInternal(notification.MessageId, notification.EventType, notification.PayloadJson, cancellationToken);

    public Task Handle(OutboxMessageNotification notification, CancellationToken cancellationToken)
        => HandleInternal(notification.MessageId, notification.EventType, notification.PayloadJson, cancellationToken);

    private async Task HandleInternal(Guid messageId, string eventType, string payloadJson, CancellationToken cancellationToken)
    {
        if (eventType != "CampaignCapacityChangeAlert")
        {
            return;
        }

        var alreadyProcessed = await _context.InboxProcessedEvents
            .AnyAsync(e => e.ConsumerName == ConsumerName && e.EventId == messageId, cancellationToken);

        if (alreadyProcessed)
        {
            return;
        }

        string phone = string.Empty;
        string message = "MealMate: Campaign capacity update alert";
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("RecipientPhone", out var pProp))
            {
                phone = pProp.GetString() ?? string.Empty;
            }
            if (doc.RootElement.TryGetProperty("Message", out var mProp))
            {
                message = mProp.GetString() ?? message;
            }
        }
        catch (JsonException)
        {
            // fallback defaults
        }

        await _sender.SendSmsAsync(phone, message, cancellationToken);

        _context.InboxProcessedEvents.Add(new InboxProcessedEvent
        {
            ConsumerName = ConsumerName,
            EventId = messageId,
            ProcessedAtUtc = _clock.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }
}

public sealed class CampaignLaunchNoticeNotificationHandler :
    INotificationHandler<OutboxLifecycleNotification>,
    INotificationHandler<OutboxMessageNotification>
{
    public const string ConsumerName = "CampaignLaunchNoticeNotifier";
    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ICampaignNotificationSender _sender;
    private readonly ILogger<CampaignLaunchNoticeNotificationHandler>? _logger;

    public CampaignLaunchNoticeNotificationHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        ICampaignNotificationSender sender,
        ILogger<CampaignLaunchNoticeNotificationHandler>? logger = null)
    {
        _context = context;
        _clock = clock;
        _sender = sender;
        _logger = logger;
    }

    public Task Handle(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
        => HandleInternal(notification.MessageId, notification.EventType, notification.PayloadJson, cancellationToken);

    public Task Handle(OutboxMessageNotification notification, CancellationToken cancellationToken)
        => HandleInternal(notification.MessageId, notification.EventType, notification.PayloadJson, cancellationToken);

    private async Task HandleInternal(Guid messageId, string eventType, string payloadJson, CancellationToken cancellationToken)
    {
        if (eventType != "CampaignLaunchNotice")
        {
            return;
        }

        var alreadyProcessed = await _context.InboxProcessedEvents
            .AnyAsync(e => e.ConsumerName == ConsumerName && e.EventId == messageId, cancellationToken);

        if (alreadyProcessed)
        {
            return;
        }

        string userId = string.Empty;
        string title = "Campaign Launched!";
        string body = "A new collaborative discount campaign is active.";
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("RecipientUserId", out var uProp))
            {
                userId = uProp.GetString() ?? string.Empty;
            }
            if (doc.RootElement.TryGetProperty("Title", out var tProp))
            {
                title = tProp.GetString() ?? title;
            }
            if (doc.RootElement.TryGetProperty("Body", out var bProp))
            {
                body = bProp.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // fallback defaults
        }

        await _sender.SendPushNotificationAsync(userId, title, body, cancellationToken);

        _context.InboxProcessedEvents.Add(new InboxProcessedEvent
        {
            ConsumerName = ConsumerName,
            EventId = messageId,
            ProcessedAtUtc = _clock.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }
}
