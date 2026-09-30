using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed record FamilyPackagePolicyResult(
    FamilySizePackageSettings EffectivePackage,
    int ParticipantCount,
    string PackageSelectionMode);

public static class FamilyPackagePolicy
{
    public static Result<FamilyPackagePolicyResult> Resolve(
        FamilyPricingSettings settings,
        FamilyGroup group,
        int eligiblePaidCount,
        int payableCount)
    {
        var activePackages = FamilyPackageResolver.ActivePackages(settings);
        if (activePackages.Count == 0)
            return Error.Validation("family.package_required", "No active family size package is configured.");

        var participantCount = Math.Max(0, eligiblePaidCount) + Math.Max(0, payableCount);
        if (group.Status == FamilyGroupStatus.Draft)
        {
            var selected = activePackages.FirstOrDefault(x => x.Id == group.SelectedFamilyPackageId);
            if (selected is null)
                return Error.Validation("family.package_not_found", "Selected family package is not available.");

            return new FamilyPackagePolicyResult(selected, participantCount, "selected");
        }

        if (group.Status == FamilyGroupStatus.Active)
        {
            var package = activePackages.FirstOrDefault(x =>
                participantCount >= x.MinMembers && participantCount <= x.MaxMembers);
            if (package is null)
                return Error.Validation("family.package_not_available_for_count", "No active family package is available for this participant count.");

            return new FamilyPackagePolicyResult(package, participantCount, "count");
        }

        return Error.Conflict("family.not_draft", "Family checkout is not available for this group status.");
    }
}
