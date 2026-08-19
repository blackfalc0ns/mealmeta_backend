using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;

namespace MealMate.Application.Features.FamilyCheckout;

public static class FamilyMemberPlanRules
{
    public static FamilyMemberSetupStatus ResolveSetupStatus(FamilyMember member)
    {
        var hasAnyPlan = member.ProgramId.HasValue || member.BundleId.HasValue || member.SelectedTier.HasValue || member.DurationDays.HasValue;
        var complete = member.ProgramId.HasValue && member.BundleId.HasValue && member.SelectedTier.HasValue
            && member.DurationDays.HasValue && member.DeliveryAddressId.HasValue
            && !string.IsNullOrWhiteSpace(member.DisplayName) && member.AgeYears is >= 1 and <= 120;
        if (complete) return FamilyMemberSetupStatus.Ready;
        return hasAnyPlan ? FamilyMemberSetupStatus.InProgress : FamilyMemberSetupStatus.NotStarted;
    }
}
