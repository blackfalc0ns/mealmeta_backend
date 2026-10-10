using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCalendar;

public sealed class ReplacementRequiredNotificationHandler(IAppDbContext db, ICustomerPushNotifier notifier)
    : INotificationHandler<OutboxLifecycleNotification>, INotificationHandler<OutboxMessageNotification>
{
    public Task Handle(OutboxLifecycleNotification notification, CancellationToken ct)
        => HandleAsync(notification.EventType, notification.PayloadJson, ct);

    public Task Handle(OutboxMessageNotification notification, CancellationToken ct)
        => HandleAsync(notification.EventType, notification.PayloadJson, ct);

    private async Task HandleAsync(string eventType, string payloadJson, CancellationToken ct)
    {
        if (eventType != "CustomerReplacementRequired") return;
        var payload = JsonSerializer.Deserialize<ReplacementPayload>(payloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (payload is null || payload.SubscriptionDayId == Guid.Empty || payload.SubscriptionId == Guid.Empty)
            throw new InvalidOperationException("Replacement event is missing its subscription/day identity.");

        var day = await db.SubscriptionDays.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == payload.SubscriptionDayId && x.SubscriptionId == payload.SubscriptionId, ct);
        if (day is null) throw new InvalidOperationException("Replacement event does not reference an existing subscription day.");
        if (day.Status != SubscriptionDayStatus.ReplacementRequired) return;
        var customerId = await db.Subscriptions.AsNoTracking().Where(x => x.Id == day.SubscriptionId)
            .Select(x => (Guid?)x.CustomerUserId).SingleOrDefaultAsync(ct);
        if (customerId is null || customerId == Guid.Empty)
            throw new InvalidOperationException("Replacement event has no subscription customer.");
        await notifier.NotifyReplacementRequiredAsync(customerId.Value, day.Id, day.ServiceDate, ct);
    }

    private sealed record ReplacementPayload(Guid SubscriptionDayId, Guid SubscriptionId);
}
