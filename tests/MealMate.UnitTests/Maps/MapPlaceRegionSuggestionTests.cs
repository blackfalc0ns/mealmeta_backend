using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Maps;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.Maps;

public sealed class MapPlaceRegionSuggestionTests
{
    [Fact]
    public async Task Enrich_prefers_name_match_over_nearest_centroid()
    {
        await using var db = CreateDb();
        var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "🇰🇼" };
        var governorate = new Region
        {
            CountryId = country.Id,
            Type = RegionType.Governorate,
            Code = "HAW",
            NameAr = "حولي",
            NameEn = "Hawalli",
            IsActive = true,
        };
        var nearWrong = new Region
        {
            CountryId = country.Id,
            ParentRegionId = governorate.Id,
            Type = RegionType.Area,
            Code = "NEAR",
            NameAr = "حولي",
            NameEn = "Hawally",
            IsActive = true,
            CentroidLatitude = 29.3407m,
            CentroidLongitude = 48.0839m,
        };
        var named = new Region
        {
            CountryId = country.Id,
            ParentRegionId = governorate.Id,
            Type = RegionType.Area,
            Code = "NAMED",
            NameAr = "السالمية",
            NameEn = "Salmiya",
            IsActive = true,
            CentroidLatitude = 29.30m,
            CentroidLongitude = 48.05m,
        };
        db.AddRange(country, governorate, nearWrong, named);
        await db.SaveChangesAsync();

        var place = new GoogleGeocodeResult(
            "Salmiya, Kuwait",
            29.3407m,
            48.0839m,
            Area: "السالمية");

        var enriched = await MapPlaceRegionSuggestion.EnrichAsync(db, place);

        Assert.Equal(named.Id, enriched.SuggestedRegionId);
        Assert.Equal("السالمية", enriched.SuggestedRegionNameAr);
        Assert.Equal(governorate.Id, enriched.SuggestedGovernorateId);
    }

    [Fact]
    public async Task Enrich_uses_nearest_centroid_when_name_missing()
    {
        await using var db = CreateDb();
        var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "🇰🇼" };
        var far = new Region
        {
            CountryId = country.Id,
            Type = RegionType.Area,
            Code = "FAR",
            NameAr = "الجهراء",
            NameEn = "Jahra",
            IsActive = true,
            CentroidLatitude = 29.34m,
            CentroidLongitude = 47.67m,
        };
        var near = new Region
        {
            CountryId = country.Id,
            Type = RegionType.Area,
            Code = "NEAR",
            NameAr = "السالمية",
            NameEn = "Salmiya",
            IsActive = true,
            CentroidLatitude = 29.3407m,
            CentroidLongitude = 48.0839m,
        };
        db.AddRange(country, far, near);
        await db.SaveChangesAsync();

        var place = new GoogleGeocodeResult("Somewhere", 29.341m, 48.084m);
        var enriched = await MapPlaceRegionSuggestion.EnrichAsync(db, place);

        Assert.Equal(near.Id, enriched.SuggestedRegionId);
    }

    private static AppDbContext CreateDb()
        => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            Substitute.For<IDomainEventDispatcher>());
}
