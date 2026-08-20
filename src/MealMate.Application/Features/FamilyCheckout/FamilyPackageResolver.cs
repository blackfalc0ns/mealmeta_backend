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

        if (active.Count > 0)
            return active;

        // Legacy readers / tests may still send only scalar family fields.
        return
        [
            new FamilySizePackageSettings
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                NameAr = "باقة عائلية",
                NameEn = "Family package",
                MinMembers = Math.Clamp(settings.MinMembersForDiscount <= 0 ? 2 : settings.MinMembersForDiscount, 2, 12),
                MaxMembers = Math.Clamp(settings.MaxMembers <= 0 ? 4 : settings.MaxMembers, 2, 12),
                DiscountPercent = Math.Clamp(settings.DiscountPercent, 0m, 1m),
                IsActive = true,
                SortOrder = 0,
            },
        ];
    }

    public static FamilySizePackageSettings? FindActive(FamilyPricingSettings settings, Guid? packageId)
    {
        if (packageId is null || packageId == Guid.Empty)
            return null;
        return ActivePackages(settings).FirstOrDefault(x => x.Id == packageId.Value);
    }

    public static FamilySizePackageSettings? DefaultActive(FamilyPricingSettings settings)
        => ActivePackages(settings).FirstOrDefault();

    public static Result<FamilySizePackageSettings> ResolveForGroup(FamilyPricingSettings settings, Guid? selectedPackageId)
    {
        var selected = FindActive(settings, selectedPackageId) ?? DefaultActive(settings);
        if (selected is null)
            return Error.Validation("family.package_required", "No active family size package is configured.");
        return selected;
    }
}
