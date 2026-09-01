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
    public async Task Enrich_does_not_match_salmiya_to_shorter_salmi_prefix()
    {
        await using var db = CreateDb();
        var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "🇰🇼" };
        var jahra = new Region
        {
            CountryId = country.Id,
            Type = RegionType.Governorate,
            Code = "JAH",
            NameAr = "الجهراء",
            NameEn = "Jahra",
            IsActive = true,
        };
        var hawalli = new Region
        {
            CountryId = country.Id,
            Type = RegionType.Governorate,
            Code = "HAW",
            NameAr = "حولي",
            NameEn = "Hawalli",
            IsActive = true,
        };
        var salmi = new Region
        {
            CountryId = country.Id,
            ParentRegionId = jahra.Id,
            Type = RegionType.Area,
            Code = "SALMI",
            NameAr = "السالمي",
            NameEn = "Salmi",
            IsActive = true,
            CentroidLatitude = 29.34m,
            CentroidLongitude = 47.67m,
        };
        var salmiya = new Region
        {
            CountryId = country.Id,
            ParentRegionId = hawalli.Id,
            Type = RegionType.Area,
            Code = "SALMIYA",
            NameAr = "السالمية",
            NameEn = "Salmiya",
            IsActive = true,
            CentroidLatitude = 29.339m,
            CentroidLongitude = 48.082m,
        };
        db.AddRange(country, jahra, hawalli, salmi, salmiya);
        await db.SaveChangesAsync();

        var place = new GoogleGeocodeResult(
            "السالمية، الكويت",
            29.3394m,
            48.0818m,
            Governorate: "حولي",
            Area: "السالمية",
            Block: "قطعة 5");

        var enriched = await MapPlaceRegionSuggestion.EnrichAsync(db, place);

        Assert.Equal(salmiya.Id, enriched.SuggestedRegionId);
        Assert.Equal(hawalli.Id, enriched.SuggestedGovernorateId);
        Assert.Equal("السالمية", enriched.Area);
        Assert.Equal("حولي", enriched.Governorate);
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

    [Fact]
    public async Task Enrich_discards_far_name_match_and_uses_nearest_centroid()
    {
        await using var db = CreateDb();
        var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "🇰🇼" };
        var farNamed = new Region
        {
            CountryId = country.Id,
            Type = RegionType.Area,
            Code = "FAR-NAMED",
            NameAr = "السالمية",
            NameEn = "Salmiya",
            IsActive = true,
            // ~40km west of the pin — name matches but geo is wrong.
            CentroidLatitude = 29.34m,
            CentroidLongitude = 47.67m,
        };
        var nearUnnamed = new Region
        {
            CountryId = country.Id,
            Type = RegionType.Area,
            Code = "NEAR",
            NameAr = "حولي",
            NameEn = "Hawalli",
            IsActive = true,
            CentroidLatitude = 29.3407m,
            CentroidLongitude = 48.0839m,
        };
        db.AddRange(country, farNamed, nearUnnamed);
        await db.SaveChangesAsync();

        var place = new GoogleGeocodeResult(
            "Near Hawalli",
            29.341m,
            48.084m,
            Area: "السالمية");

        var enriched = await MapPlaceRegionSuggestion.EnrichAsync(db, place);

        Assert.Equal(nearUnnamed.Id, enriched.SuggestedRegionId);
        Assert.Equal("حولي", enriched.SuggestedRegionNameAr);
    }

    private static AppDbContext CreateDb()
        => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            Substitute.For<IDomainEventDispatcher>());
}
