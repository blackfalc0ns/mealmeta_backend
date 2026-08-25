using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class FamilyMemberPermissions : AuditableEntity
{
    public Guid FamilyMemberId { get; set; }
    public FamilyMember FamilyMember { get; set; } = default!;
    public bool EditDailyMeals { get; set; }
    public bool ChangeDeliveryTime { get; set; }
    public bool ViewProgram { get; set; }
    public bool ChangeProgram { get; set; }
    public bool EditMealsAndDays { get; set; }
    public bool EditAddress { get; set; }
    public bool UnitExchange { get; set; }
    public bool ChangePassword { get; set; }
}
