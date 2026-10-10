using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals.Events;
using MealMate.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCalendar;

public sealed class ReplacementRequiredOutboxHandler(IAppDbContext db, IDateTimeProvider clock)
    : INotificationHandler<DomainEventNotification<CustomerReplacementRequiredDomainEvent>>
{
    public async Task Handle(DomainEventNotification<CustomerReplacementRequiredDomainEvent> notification, CancellationToken ct)
    {
        var day = notification.DomainEvent;
        var dayId = day.SubscriptionDayId.ToString();
        if (await db.OutboxMessages.AnyAsync(x => x.EventType == "CustomerReplacementRequired" &&
            x.PayloadJson.Contains(dayId), ct)) return;
        db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = "CustomerReplacementRequired", AggregateType = "SubscriptionDay",
            AggregateId = dayId, DeduplicationKey = $"replacement-required:{dayId}",
            PayloadJson = JsonSerializer.Serialize(new { day.SubscriptionDayId, day.SubscriptionId, day.ServiceDate, day.Reason, day.ReasonCode }),
            CreatedAtUtc = clock.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
