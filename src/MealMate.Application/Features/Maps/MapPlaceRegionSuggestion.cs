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

        var parentIds = areas
            .Where(a => a.ParentRegionId is not null)
            .Select(a => a.ParentRegionId!.Value)
            .Distinct()
            .ToList();
        var parents = parentIds.Count == 0
            ? new Dictionary<Guid, (string NameAr, string NameEn)>()
            : await db.Regions
                .AsNoTracking()
                .Where(r => parentIds.Contains(r.Id) && r.IsActive)
                .ToDictionaryAsync(
                    r => r.Id,
                    r => (r.NameAr, r.NameEn),
                    cancellationToken);

        AreaRow? matchedArea = null;
        var areaHint = place.Area?.Trim();
        var governorateHint = place.Governorate?.Trim();
        if (!string.IsNullOrWhiteSpace(areaHint))
        {
            matchedArea = areas
                .Select(a =>
                {
                    var nameScore = Math.Max(
                        NameMatchScore(a.NameAr, areaHint),
                        NameMatchScore(a.NameEn, areaHint));
                    var distanceKm = KuwaitGeoBounds.HaversineKm(
                        place.Latitude, place.Longitude, a.Latitude, a.Longitude);
                    var govBoost = 0;
                    if (!string.IsNullOrWhiteSpace(governorateHint)
                        && a.ParentRegionId is Guid parentId
                        && parents.TryGetValue(parentId, out var parent))
                    {
                        if (NameMatchScore(parent.NameAr, governorateHint) >= 1000
                            || NameMatchScore(parent.NameEn, governorateHint) >= 1000)
                        {
                            govBoost = 50;
                        }
                    }

                    return new
                    {
                        Area = a,
                        Score = nameScore + govBoost,
                        DistanceKm = distanceKm,
                    };
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.DistanceKm)
                .Select(x => x.Area)
                .FirstOrDefault();
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
        if (governorateId is Guid gId && parents.TryGetValue(gId, out var gov))
        {
            governorateNameAr = gov.NameAr;
            governorateNameEn = gov.NameEn;
        }
        else if (governorateId is Guid missingParentId)
        {
            var governorate = await db.Regions
                .AsNoTracking()
                .Where(r => r.Id == missingParentId && r.IsActive)
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
            var matchedBlock = blocks
                .Select(b => new
                {
                    Block = b,
                    Score = Math.Max(
                        Math.Max(NameMatchScore(b.NameAr, blockHint), NameMatchScore(b.NameEn, blockHint)),
                        NameMatchScore(b.Code, blockHint)),
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Block)
                .FirstOrDefault();
            if (matchedBlock is not null)
            {
                blockId = matchedBlock.Id;
                blockNameAr = matchedBlock.NameAr;
                blockNameEn = matchedBlock.NameEn;
            }
            else
            {
                // "قطعة 5" / "Block 5" / bare "5"
                var digits = ExtractDigits(blockHint);
                if (!string.IsNullOrWhiteSpace(digits))
                {
                    matchedBlock = blocks.FirstOrDefault(b =>
                        ExtractDigits(b.NameAr) == digits
                        || ExtractDigits(b.NameEn) == digits
                        || ExtractDigits(b.Code) == digits);
                    if (matchedBlock is not null)
                    {
                        blockId = matchedBlock.Id;
                        blockNameAr = matchedBlock.NameAr;
                        blockNameEn = matchedBlock.NameEn;
                    }
                }
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

    /// <summary>
    /// Exact match scores highest. Fuzzy contains prefers the longer catalog name
    /// so "السالمية" does not wrongly bind to shorter "السالمي".
    /// </summary>
    private static int NameMatchScore(string? catalog, string hint)
    {
        if (string.IsNullOrWhiteSpace(catalog)) return 0;
        var a = Normalize(catalog);
        var b = Normalize(hint);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1000 + a.Length;

        var shorter = Math.Min(a.Length, b.Length);
        var longer = Math.Max(a.Length, b.Length);
        if (shorter < 3) return 0;
        if (!(a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)))
        {
            return 0;
        }

        // Reject weak overlaps (e.g. short accidental substrings).
        if ((double)shorter / longer < 0.7) return 0;

        // Longer catalog names rank above shorter prefix collisions.
        return 100 + a.Length;
    }

    private static string? ExtractDigits(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var chars = value
            .Select(ch => ch switch
            {
                '٠' => '0',
                '١' => '1',
                '٢' => '2',
                '٣' => '3',
                '٤' => '4',
                '٥' => '5',
                '٦' => '6',
                '٧' => '7',
                '٨' => '8',
                '٩' => '9',
                _ => ch,
            })
            .Where(char.IsDigit)
            .ToArray();
        return chars.Length == 0 ? null : new string(chars);
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
