using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CalendarMeals;

public sealed class MealChangeRequest : AuditableEntity
{
    public Guid SubscriptionDayId { get; set; }

    public SubscriptionDay SubscriptionDay { get; set; } = default!;

    public Guid? FromMealId { get; set; }

    public Guid? ToMealId { get; set; }

    public string? Reason { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public Guid? RequestedByUserId { get; set; }
}
