using MealMate.Domain.Common;

namespace MealMate.Domain.CalendarMeals.Events;

public sealed record CustomerReplacementRequiredDomainEvent(
    Guid SubscriptionDayId,
    Guid SubscriptionId,
    DateOnly ServiceDate,
    string Reason,
    string? ReasonCode = null
) : DomainEvent;
