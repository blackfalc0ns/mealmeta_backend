using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;

namespace MealMate.Application.Features.FamilyCheckout;

public static class FamilyPackageResolver
{
    public static IReadOnlyList<FamilySizePackageSettings> ActivePackages(FamilyPricingSettings settings)
    {
        var active = (settings.Packages ?? new List<FamilySizePackageSettings>())
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.MinMembers)
            .ToList();

        return active;
    }

    public static FamilySizePackageSettings? FindActive(FamilyPricingSettings settings, Guid? packageId)
    {
        if (packageId is null || packageId == Guid.Empty)
            return null;
        return ActivePackages(settings).FirstOrDefault(x => x.Id == packageId.Value);
    }

    public static FamilySizePackageSettings? DefaultActive(FamilyPricingSettings settings)
        => ActivePackages(settings).FirstOrDefault();

    public static FamilySizePackageSettings? ResolveForCount(FamilyPricingSettings settings, int memberCount)
        => ActivePackages(settings).FirstOrDefault(x => memberCount >= x.MinMembers && memberCount <= x.MaxMembers);

    public static Result<FamilySizePackageSettings> ResolveForGroup(FamilyPricingSettings settings, Guid? selectedPackageId)
    {
        var selected = FindActive(settings, selectedPackageId) ?? DefaultActive(settings);
        if (selected is null)
            return Error.Validation("family.package_required", "No active family size package is configured.");
        return selected;
    }
}
