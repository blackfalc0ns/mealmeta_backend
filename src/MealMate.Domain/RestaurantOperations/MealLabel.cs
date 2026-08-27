using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealLabel : AuditableEntity
{
    public Guid SubscriptionDayId { get; set; }

    public MealLabelKind Kind { get; set; } = MealLabelKind.Box;

    public Guid? MealId { get; set; }

    public string LabelPayloadJson { get; set; } = "{}";

    public DateTime GeneratedAtUtc { get; set; }

    public Guid? TemplateId { get; set; }

    public int? TemplateVersion { get; set; }
}
