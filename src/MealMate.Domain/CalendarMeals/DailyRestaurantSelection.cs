using MealMate.Domain.Common;

namespace MealMate.Domain.CalendarMeals;

public sealed class DailyRestaurantSelection : AuditableEntity
{
    public Guid SubscriptionDayId { get; set; }

    public SubscriptionDay SubscriptionDay { get; set; } = default!;

    public Guid RestaurantId { get; set; }

    public Guid? SelectedByUserId { get; set; }

    public DateTime SelectedAtUtc { get; set; }

    /// <summary>Selection source: Manual, Auto, or Admin.</summary>
    public string SelectionSource { get; set; } = default!;
}
