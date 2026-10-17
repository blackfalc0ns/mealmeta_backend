using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;

namespace MealMate.Application.Features.FamilyCheckout;

public static class FamilyMemberPlanRules
{
    public static FamilyMemberSetupStatus ResolveSetupStatus(FamilyMember member)
    {
        var hasAnyPlan = member.ProgramId.HasValue || member.BundleId.HasValue || member.SelectedTier.HasValue || member.DurationDays.HasValue;
        var complete = HasPricablePlan(member)
            && member.DeliveryAddressId.HasValue
            && !string.IsNullOrWhiteSpace(member.DisplayName)
            // The manager is the signed-in account owner; a birthdate is not
            // required to consider their seat ready for subscription.
            && (member.Role == FamilyMemberRole.Manager || member.AgeYears is >= 1 and <= 120);
        if (complete) return FamilyMemberSetupStatus.Ready;
        return hasAnyPlan ? FamilyMemberSetupStatus.InProgress : FamilyMemberSetupStatus.NotStarted;
    }

    /// <summary>
    /// Program + bundle + tier + duration are enough to price and open payment.
    /// Address/age remain optional for Ready badge / profile completeness.
    /// </summary>
    public static bool HasPricablePlan(FamilyMember member)
        => member.ProgramId.HasValue
            && member.BundleId.HasValue
            && member.SelectedTier.HasValue
            && member.DurationDays is > 0;
}
