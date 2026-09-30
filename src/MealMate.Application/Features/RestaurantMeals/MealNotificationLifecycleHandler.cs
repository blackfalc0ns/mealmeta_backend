using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.RestaurantMeals;

public sealed class MealNotificationLifecycleHandler : INotificationHandler<OutboxLifecycleNotification>
{
    public const string SubmittedConsumerName = "MealSubmittedNotifier";
    public const string ApprovedConsumerName = "MealApprovedNotifier";
    public const string RejectedConsumerName = "MealRejectedNotifier";

    private readonly IAppDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly IMealPushNotifier _pushNotifier;
    private readonly ILogger<MealNotificationLifecycleHandler>? _logger;

    public MealNotificationLifecycleHandler(
        IAppDbContext context,
        IDateTimeProvider clock,
        IMealPushNotifier pushNotifier,
        ILogger<MealNotificationLifecycleHandler>? logger = null)
    {
        _context = context;
        _clock = clock;
        _pushNotifier = pushNotifier;
        _logger = logger;
    }

    public async Task Handle(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        switch (notification.EventType)
        {
            case "MealSubmitted":
                await HandleSubmitted(notification, cancellationToken);
                break;
            case "MealApproved":
                await HandleApproved(notification, cancellationToken);
                break;
            case "MealRejected":
                await HandleRejected(notification, cancellationToken);
                break;
        }
    }

    private async Task HandleSubmitted(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(SubmittedConsumerName, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var mealId = root.GetProperty("MealId").GetGuid();
        var restaurantId = root.GetProperty("RestaurantId").GetGuid();
        var mealNameAr = root.TryGetProperty("MealNameAr", out var arProp) ? arProp.GetString() ?? string.Empty : string.Empty;
        var mealNameEn = root.TryGetProperty("MealNameEn", out var enProp) ? enProp.GetString() ?? string.Empty : string.Empty;

        await _pushNotifier.NotifyAdminsMealSubmittedAsync(mealId, restaurantId, mealNameAr, mealNameEn, cancellationToken);

        await RecordProcessed(SubmittedConsumerName, notification.MessageId, cancellationToken);
    }

    private async Task HandleApproved(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(ApprovedConsumerName, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var mealId = root.GetProperty("MealId").GetGuid();
        var restaurantId = root.GetProperty("RestaurantId").GetGuid();
        var mealNameAr = root.TryGetProperty("MealNameAr", out var arProp) ? arProp.GetString() ?? string.Empty : string.Empty;
        var mealNameEn = root.TryGetProperty("MealNameEn", out var enProp) ? enProp.GetString() ?? string.Empty : string.Empty;

        await _pushNotifier.NotifyRestaurantMealApprovedAsync(mealId, restaurantId, mealNameAr, mealNameEn, cancellationToken);

        await RecordProcessed(ApprovedConsumerName, notification.MessageId, cancellationToken);
    }

    private async Task HandleRejected(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (await IsAlreadyProcessed(RejectedConsumerName, notification.MessageId, cancellationToken))
            return;

        using var doc = JsonDocument.Parse(notification.PayloadJson);
        var root = doc.RootElement;
        var mealId = root.GetProperty("MealId").GetGuid();
        var restaurantId = root.GetProperty("RestaurantId").GetGuid();
        var mealNameAr = root.TryGetProperty("MealNameAr", out var arProp) ? arProp.GetString() ?? string.Empty : string.Empty;
        var mealNameEn = root.TryGetProperty("MealNameEn", out var enProp) ? enProp.GetString() ?? string.Empty : string.Empty;
        var reason = root.TryGetProperty("RejectionReason", out var rProp) ? rProp.GetString() ?? string.Empty : string.Empty;

        await _pushNotifier.NotifyRestaurantMealRejectedAsync(mealId, restaurantId, mealNameAr, mealNameEn, reason, cancellationToken);

        await RecordProcessed(RejectedConsumerName, notification.MessageId, cancellationToken);
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
