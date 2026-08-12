using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CalendarMeals;

public sealed class DailyMealSelection : AuditableEntity
{
    public Guid SubscriptionDayId { get; set; }

    public SubscriptionDay SubscriptionDay { get; set; } = default!;

    public Guid MealId { get; set; }

    public Guid? ResolvedMenuPlanVersionId { get; set; }

    public Guid? ResolvedMenuDayId { get; set; }

    public Guid? ResolvedProgramId { get; set; }

    public ScheduleMode? ScheduleMode { get; set; }

    public int SortOrder { get; set; }

    public DateTime SelectedAtUtc { get; set; }
}
