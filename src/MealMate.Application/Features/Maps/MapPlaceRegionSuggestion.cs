using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Maps;

public static class MapPlaceRegionSuggestion
{
    public static async Task<GoogleGeocodeResult> EnrichAsync(
        IAppDbContext db,
        GoogleGeocodeResult place,
        CancellationToken cancellationToken = default)
    {
        var areas = await db.Regions
            .AsNoTracking()
            .Where(r => r.IsActive
                        && r.Type == RegionType.Area
                        && r.CentroidLatitude != null
                        && r.CentroidLongitude != null)
            .Select(r => new AreaRow(
                r.Id,
                r.ParentRegionId,
                r.NameAr,
                r.NameEn,
                r.CentroidLatitude!.Value,
                r.CentroidLongitude!.Value))
            .ToListAsync(cancellationToken);

        if (areas.Count == 0)
        {
            return place;
        }

        AreaRow? matchedArea = null;
        var areaHint = place.Area?.Trim();
        if (!string.IsNullOrWhiteSpace(areaHint))
        {
            matchedArea = areas.FirstOrDefault(a =>
                NamesMatch(a.NameAr, areaHint) || NamesMatch(a.NameEn, areaHint));
        }

        if (matchedArea is null)
        {
            var nearest = areas
                .Select(a => new
                {
                    Area = a,
                    DistanceKm = KuwaitGeoBounds.HaversineKm(
                        place.Latitude, place.Longitude, a.Latitude, a.Longitude),
                })
                .OrderBy(x => x.DistanceKm)
                .First();

            if (nearest.DistanceKm <= KuwaitGeoBounds.MaxSuggestionDistanceKm)
            {
                matchedArea = nearest.Area;
            }
        }

        if (matchedArea is null)
        {
            return place;
        }

        Guid? governorateId = matchedArea.ParentRegionId;
        string? governorateNameAr = null;
        string? governorateNameEn = null;
        if (governorateId is Guid gId)
        {
            var governorate = await db.Regions
                .AsNoTracking()
                .Where(r => r.Id == gId && r.IsActive)
                .Select(r => new { r.Id, r.NameAr, r.NameEn })
                .FirstOrDefaultAsync(cancellationToken);
            if (governorate is not null)
            {
                governorateId = governorate.Id;
                governorateNameAr = governorate.NameAr;
                governorateNameEn = governorate.NameEn;
            }
        }

        Guid? blockId = null;
        string? blockNameAr = null;
        string? blockNameEn = null;
        var blockHint = place.Block?.Trim();
        var blocks = await db.Regions
            .AsNoTracking()
            .Where(r => r.IsActive
                        && r.Type == RegionType.Block
                        && r.ParentRegionId == matchedArea.Id)
            .Select(r => new { r.Id, r.NameAr, r.NameEn, r.Code })
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(blockHint) && blocks.Count > 0)
        {
            var matchedBlock = blocks.FirstOrDefault(b =>
                NamesMatch(b.NameAr, blockHint)
                || NamesMatch(b.NameEn, blockHint)
                || NamesMatch(b.Code, blockHint));
            if (matchedBlock is not null)
            {
                blockId = matchedBlock.Id;
                blockNameAr = matchedBlock.NameAr;
                blockNameEn = matchedBlock.NameEn;
            }
        }

        return place with
        {
            SuggestedRegionId = matchedArea.Id,
            SuggestedRegionNameAr = matchedArea.NameAr,
            SuggestedRegionNameEn = matchedArea.NameEn,
            SuggestedGovernorateId = governorateId,
            SuggestedGovernorateNameAr = governorateNameAr ?? place.Governorate,
            SuggestedGovernorateNameEn = governorateNameEn,
            SuggestedBlockId = blockId,
            SuggestedBlockNameAr = blockNameAr ?? place.Block,
            SuggestedBlockNameEn = blockNameEn,
            Governorate = governorateNameAr ?? place.Governorate,
            Area = matchedArea.NameAr,
            Block = blockNameAr ?? place.Block,
        };
    }

    private static bool NamesMatch(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return false;
        var a = Normalize(left);
        var b = Normalize(right);
        return a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    private static string Normalize(string value)
        => value.Trim()
            .Replace("أ", "ا", StringComparison.Ordinal)
            .Replace("إ", "ا", StringComparison.Ordinal)
            .Replace("آ", "ا", StringComparison.Ordinal)
            .Replace("ة", "ه", StringComparison.Ordinal)
            .Replace("ى", "ي", StringComparison.Ordinal);

    private sealed record AreaRow(
        Guid Id,
        Guid? ParentRegionId,
        string NameAr,
        string NameEn,
        decimal Latitude,
        decimal Longitude);
}
