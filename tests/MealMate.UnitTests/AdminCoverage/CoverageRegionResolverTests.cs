using MealMate.Application.Features.AdminCoverage;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.RestaurantOperations;
using FluentAssertions;

namespace MealMate.UnitTests.AdminCoverage;

public sealed class CoverageRegionResolverTests
{
    [Fact]
    public void Resolve_DeduplicatesBlockLinksAndDoesNotCountPartialAreaAsFull()
    {
        var (governorate, area, block) = CreateHierarchy();
        var restaurantId = Guid.NewGuid();
        var result = CoverageRegionResolver.Resolve(
            [governorate, area, block],
            [new CoverageRestaurantEligibility(restaurantId, true, true)],
            [Link(restaurantId, block.Id), Link(restaurantId, block.Id)]);

        result[area.Id].FullRestaurantIds.Should().BeEmpty();
        result[area.Id].PartialRestaurantIds.Should().Equal(restaurantId);
        CoverageRegionResolver.Metrics(result).PartiallyCoveredAreaCount.Should().Be(1);
        CoverageRegionResolver.Metrics(result).FullyCoveredAreaPercent.Should().Be(0m);
    }

    [Fact]
    public void Resolve_GovernorateLinkExpandsToAreaByParentIdsRegardlessOfNames()
    {
        var (governorate, area, block) = CreateHierarchy();
        area.NameAr = "اسم جديد";
        area.NameEn = "Renamed";
        var restaurantId = Guid.NewGuid();
        var result = CoverageRegionResolver.Resolve(
            [governorate, area, block],
            [new CoverageRestaurantEligibility(restaurantId, true, true)],
            [Link(restaurantId, governorate.Id)]);

        result[area.Id].FullRestaurantIds.Should().Equal(restaurantId);
        result[area.Id].PartialRestaurantIds.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_ExcludesInactiveLinksAncestorsAndIneligibleRestaurants()
    {
        var (governorate, area, block) = CreateHierarchy();
        var restaurantId = Guid.NewGuid();
        var suspendedRestaurantId = Guid.NewGuid();
        var inactiveArea = NewRegion(RegionType.Area, governorate.Id, isActive: false);
        var nestedBlock = NewRegion(RegionType.Block, inactiveArea.Id);
        var inactiveLink = Link(restaurantId, block.Id);
        inactiveLink.IsActive = false;

        var result = CoverageRegionResolver.Resolve(
            [governorate, area, block, inactiveArea, nestedBlock],
            [new CoverageRestaurantEligibility(restaurantId, true, true), new CoverageRestaurantEligibility(suspendedRestaurantId, true, false)],
            [inactiveLink, Link(restaurantId, nestedBlock.Id), Link(suspendedRestaurantId, governorate.Id)]);

        result[area.Id].FullRestaurantIds.Should().BeEmpty();
        result[area.Id].PartialRestaurantIds.Should().BeEmpty();
    }

    [Fact]
    public void Metrics_ReturnNullPercentWhenThereAreNoActiveAreas()
    {
        var metrics = CoverageRegionResolver.Metrics(new Dictionary<Guid, CoverageAreaResult>());

        metrics.ActiveAreaCount.Should().Be(0);
        metrics.FullyCoveredAreaPercent.Should().BeNull();
    }

    [Fact]
    public void Resolve_ExcludesAreasWithMissingOrInactiveAncestors()
    {
        var missingParentId = Guid.NewGuid();
        var orphanArea = NewRegion(RegionType.Area, missingParentId);
        var inactiveGovernorate = NewRegion(RegionType.Governorate, null, isActive: false);
        var inactiveParentArea = NewRegion(RegionType.Area, inactiveGovernorate.Id);
        var restaurantId = Guid.NewGuid();

        var result = CoverageRegionResolver.Resolve(
            [orphanArea, inactiveGovernorate, inactiveParentArea],
            [new CoverageRestaurantEligibility(restaurantId, true, true)],
            [Link(restaurantId, orphanArea.Id), Link(restaurantId, inactiveParentArea.Id)]);

        result.Should().BeEmpty();
    }

    private static (Region Governorate, Region Area, Region Block) CreateHierarchy()
    {
        var governorate = NewRegion(RegionType.Governorate, null);
        var area = NewRegion(RegionType.Area, governorate.Id);
        var block = NewRegion(RegionType.Block, area.Id);
        return (governorate, area, block);
    }

    private static Region NewRegion(RegionType type, Guid? parentId, bool isActive = true) => new()
    {
        Type = type,
        ParentRegionId = parentId,
        NameAr = "منطقة",
        NameEn = "Area",
        IsActive = isActive,
    };

    private static RestaurantServiceRegion Link(Guid restaurantId, Guid regionId) => new()
    {
        RestaurantId = restaurantId,
        RegionId = regionId,
        IsActive = true,
    };
}
