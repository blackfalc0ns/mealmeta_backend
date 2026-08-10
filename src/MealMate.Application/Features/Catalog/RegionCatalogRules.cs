using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Catalog;

public static class RegionCatalogRules
{
    public static async Task<(Error? Error, List<Guid> Ids)> ValidateRestaurantSelectionAsync(
        IAppDbContext db,
        Guid? countryId,
        Guid? primaryRegionId,
        IEnumerable<string>? rawIds,
        bool requireSelection,
        Guid expectedCountryId,
        CancellationToken cancellationToken)
    {
        var raw = rawIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];
        if (!countryId.HasValue && !primaryRegionId.HasValue && raw.Count == 0)
            return (null, []);

        var country = await db.Countries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == expectedCountryId && x.Code == "KW" && x.IsActive, cancellationToken);
        if (country is null || countryId != expectedCountryId)
            return (Error.Validation("catalog.restaurant_country_invalid"), []);
        if (!primaryRegionId.HasValue && requireSelection)
            return (Error.Validation("catalog.primary_region_required"), []);

        var availableRegions = await db.Regions
            .AsNoTracking()
            .Where(region => region.IsActive
                && region.CountryId == country.Id
                && region.Type == RegionType.Area)
            .ToListAsync(cancellationToken);

        var ids = new List<Guid>();
        foreach (var value in raw)
        {
            var trimmed = value.Trim();
            Region? matchedRegion = null;
            if (Guid.TryParse(trimmed, out var parsedId))
            {
                matchedRegion = availableRegions.FirstOrDefault(region => region.Id == parsedId);
            }
            else
            {
                var matches = availableRegions
                    .Where(region => region.Code.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
                        || region.NameEn.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
                        || region.NameAr.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
                        || region.Code.EndsWith($"-{trimmed}", StringComparison.OrdinalIgnoreCase)
                        || region.Code.Replace("KW-", string.Empty, StringComparison.OrdinalIgnoreCase)
                            .Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToList();
                if (matches.Count == 1)
                {
                    matchedRegion = matches[0];
                }
            }

            if (matchedRegion is null)
            {
                return (Error.Validation("catalog.region_id_invalid"), []);
            }

            if (!ids.Contains(matchedRegion.Id))
            {
                ids.Add(matchedRegion.Id);
            }
        }
        if (primaryRegionId.HasValue && !ids.Contains(primaryRegionId.Value))
            return (Error.Validation("catalog.primary_region_must_be_selected"), []);
        if (requireSelection && ids.Count == 0)
            return (Error.Validation("catalog.service_regions_required"), []);

        return (null, ids);
    }
}
