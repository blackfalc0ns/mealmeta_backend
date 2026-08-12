using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CalendarMeals;

public sealed class SubscriptionDay : AuditableEntity
{
    public Guid SubscriptionId { get; set; }

    public Guid? CalendarId { get; set; }

    public SubscriptionCalendar? Calendar { get; set; }

    public DateOnly ServiceDate { get; set; }

    public SubscriptionDayStatus Status { get; set; } = SubscriptionDayStatus.Open;

    public DateTime? SelectionDeadlineUtc { get; set; }

    public Guid? RestaurantId { get; set; }

    /// <summary>Approved menu version fixed when payment confirms the subscription.</summary>
    public Guid? ResolvedMenuPlanVersionId { get; set; }

    public DateTime? LockAtUtc { get; set; }

    public DateTime? ConfirmedAtUtc { get; set; }

    public bool IsAutoSelected { get; set; }

    public ICollection<DailyRestaurantSelection> RestaurantSelections { get; set; } = new List<DailyRestaurantSelection>();

    public ICollection<DailyMealSelection> MealSelections { get; set; } = new List<DailyMealSelection>();

    public ICollection<MealChangeRequest> MealChangeRequests { get; set; } = new List<MealChangeRequest>();
}
