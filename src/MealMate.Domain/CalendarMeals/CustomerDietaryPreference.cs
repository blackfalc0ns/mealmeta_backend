using MealMate.Domain.Common;

namespace MealMate.Domain.CalendarMeals;

public sealed class CustomerDietaryPreference : AuditableEntity
{
    public Guid CustomerUserId { get; set; }
    public string PreferenceCode { get; set; } = default!;
}

public sealed class SubscriptionDayMealSnapshot : AuditableEntity
{
    public Guid SubscriptionId { get; set; }
    public Guid SubscriptionDayId { get; set; }
    public DateOnly ServiceDate { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid ProgramId { get; set; }
    public Guid MenuPlanVersionId { get; set; }
    public string RequiredSlotsJson { get; set; } = "[]";
    public string MealsJson { get; set; } = "[]";
    public DateTime CapturedAtUtc { get; set; }
}
