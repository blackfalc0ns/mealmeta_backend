using MealMate.Domain.Common;

namespace MealMate.Domain.CalendarMeals;

public sealed class SubscriptionCalendar : AuditableEntity
{
    public Guid SubscriptionId { get; set; }

    public DateTime GeneratedAtUtc { get; set; }

    public int TotalServiceDays { get; set; }

    public ICollection<SubscriptionDay> Days { get; set; } = new List<SubscriptionDay>();
}
