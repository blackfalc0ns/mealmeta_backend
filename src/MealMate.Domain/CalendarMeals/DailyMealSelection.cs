using MealMate.Domain.Common;

namespace MealMate.Domain.CalendarMeals;

public sealed class DailyMealSelection : AuditableEntity
{
    public Guid SubscriptionDayId { get; set; }

    public SubscriptionDay SubscriptionDay { get; set; } = default!;

    public Guid MealId { get; set; }

    public int SortOrder { get; set; }

    public DateTime SelectedAtUtc { get; set; }
}
