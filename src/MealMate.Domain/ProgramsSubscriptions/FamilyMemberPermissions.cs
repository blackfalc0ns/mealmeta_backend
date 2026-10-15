using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class FamilyMemberPermissions : AuditableEntity
{
    public Guid FamilyMemberId { get; set; }
    public long Version { get; set; }
    public FamilyMember FamilyMember { get; set; } = default!;
    public bool EditDailyMeals { get; set; }
    public bool ChangeDeliveryTime { get; set; }
    public bool ViewProgram { get; set; } = true;
    public bool ChangeProgram { get; set; }
    public bool EditMealsAndDays { get; set; }
    public bool EditAddress { get; set; }
    public bool UnitExchange { get; set; }
    public bool ChangePassword { get; set; }

    // ── Restoration stubs (2026-10-06): the consolidation commit shipped
    // FamilyActionPolicy.cs consuming these flags without their declarations.
    // The owning feature session should reconcile the intended set.
    public bool ViewCalendar { get; set; }
    public bool ViewInvoices { get; set; }
    public bool SelectRestaurant { get; set; }
    public bool ConfirmMeals { get; set; }
    public bool RequestFreeze { get; set; }
    public bool RequestRenewal { get; set; }
    public bool RequestUpgrade { get; set; }
    public bool CancelSubscription { get; set; }
    public bool ViewFamilyCalendars { get; set; }
    public bool ViewFamilyPlans { get; set; }
    public bool ViewFamilyInvoices { get; set; }
}
