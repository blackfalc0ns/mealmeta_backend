using MealMate.Domain.ComplaintsSupport;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed partial class SupportCaseService
{
    private async Task<(Guid? CountryId, Guid? RegionId)> ResolveTriageGeographyAsync(
        SupportCaseChannel channel, Guid requesterUserId, CancellationToken ct)
    {
        Guid? countryId = null;
        Guid? regionId = null;
        if (channel == SupportCaseChannel.Customer)
        {
            var profile = await _db.CustomerProfiles.AsNoTracking().Where(x => x.UserId == requesterUserId)
                .Select(x => new { x.ActiveDeliveryRegionId, x.DefaultRegionId }).SingleOrDefaultAsync(ct);
            var selected = profile?.ActiveDeliveryRegionId ?? profile?.DefaultRegionId;
            if (selected is { } selectedRegion)
                regionId = await _db.Regions.AsNoTracking().Where(x => x.Id == selectedRegion && x.IsActive && !x.IsDeleted)
                    .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            if (regionId is { } knownRegion)
                countryId = await _db.Regions.AsNoTracking().Where(x => x.Id == knownRegion)
                    .Select(x => (Guid?)x.CountryId).SingleOrDefaultAsync(ct);
        }
        else if (channel == SupportCaseChannel.Driver)
        {
            var profile = await _db.DriverProfiles.AsNoTracking().Where(x => x.UserId == requesterUserId && !x.IsDeleted)
                .Select(x => new { x.CountryId, x.RegionId }).SingleOrDefaultAsync(ct);
            countryId = profile?.CountryId;
            regionId = profile?.RegionId;
        }

        if (regionId is { } region && countryId is { } country
            && !await _db.Regions.AsNoTracking().AnyAsync(x => x.Id == region && x.CountryId == country && x.IsActive && !x.IsDeleted, ct))
            regionId = null;
        if (countryId is { } countryIdValue
            && !await _db.Countries.AsNoTracking().AnyAsync(x => x.Id == countryIdValue && x.IsActive && !x.IsDeleted, ct))
            return (null, null);
        return (countryId, regionId);
    }
}
