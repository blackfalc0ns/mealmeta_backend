using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using MealMate.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.Seeding;

public sealed class RestaurantDeliveryCoverageBackfillTests
{
    [Fact]
    public async Task Seed_normalizes_block_coverage_to_parent_area_and_fills_coordinates()
    {
        await using var db = CreateDb();
        var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "🇰🇼" };
        var area = new Region
        {
            CountryId = country.Id,
            Type = RegionType.Area,
            Code = "KW-SL",
            NameAr = "السالمية",
            NameEn = "Salmiya",
            IsActive = true,
            CentroidLatitude = 29.3390m,
            CentroidLongitude = 48.0760m,
        };
        var block = new Region
        {
            CountryId = country.Id,
            Type = RegionType.Block,
            Code = "KW-SL-BLK-01",
            NameAr = "قطعة 1",
            NameEn = "Block 1",
            IsActive = true,
        };
        db.AddRange(country, area);
        await db.SaveChangesAsync();
        block.ParentRegionId = area.Id;
        db.Add(block);

        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "مطعم",
            LegalNameEn = "Restaurant",
            TradeNameAr = "مطعم",
            TradeNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
        };
        db.Add(restaurant);
        await db.SaveChangesAsync();
        db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
        {
            RestaurantId = restaurant.Id,
            RegionId = block.Id,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        await RestaurantDeliveryCoverageBackfill.SeedAsync(db);

        var coverage = await db.RestaurantServiceRegions
            .Where(x => x.RestaurantId == restaurant.Id && x.IsActive)
            .ToListAsync();
        Assert.Contains(coverage, x => x.RegionId == area.Id);
        var updated = await db.RestaurantProfiles.SingleAsync(x => x.Id == restaurant.Id);
        Assert.Equal(29.3390m, updated.Latitude);
        Assert.Equal(48.0760m, updated.Longitude);
        Assert.Equal(area.Id, updated.PrimaryRegionId);
    }

    [Fact]
    public async Task Seed_assigns_nearby_areas_when_restaurant_has_no_coverage()
    {
        await using var db = CreateDb();
        var country = new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "🇰🇼" };
        var near = new Region
        {
            CountryId = country.Id,
            Type = RegionType.Area,
            Code = "KW-SL",
            NameAr = "السالمية",
            NameEn = "Salmiya",
            IsActive = true,
            CentroidLatitude = 29.3390m,
            CentroidLongitude = 48.0760m,
        };
        var far = new Region
        {
            CountryId = country.Id,
            Type = RegionType.Area,
            Code = "KW-JAH-SALMI",
            NameAr = "السالمي",
            NameEn = "Salmi",
            IsActive = true,
            CentroidLatitude = 29.1000m,
            CentroidLongitude = 46.7000m,
        };
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "مطعم",
            LegalNameEn = "Restaurant",
            TradeNameAr = "مطعم",
            TradeNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
            Latitude = 29.3400m,
            Longitude = 48.0800m,
        };
        db.AddRange(country, near, far, restaurant);
        await db.SaveChangesAsync();

        await RestaurantDeliveryCoverageBackfill.SeedAsync(db);

        var coverageIds = await db.RestaurantServiceRegions
            .Where(x => x.RestaurantId == restaurant.Id && x.IsActive)
            .Select(x => x.RegionId)
            .ToListAsync();
        Assert.Contains(near.Id, coverageIds);
        Assert.DoesNotContain(far.Id, coverageIds);
    }

    private static AppDbContext CreateDb()
        => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            Substitute.For<IDomainEventDispatcher>());
}
