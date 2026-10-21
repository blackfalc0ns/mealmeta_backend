using FluentAssertions;
using MealMate.Application.Features.RestaurantOperations.Availability;
using Xunit;

namespace MealMate.UnitTests.Capacity;

public class PlatformGuaranteeTests
{
    private readonly PlatformCapacityGuaranteeService _sut = new();

    [Fact]
    public void EvaluatePlatformCeiling_EnforcesRenewalReserveProtection()
    {
        // Platform ceiling 100, 15% reserve, 80 current occupancy, 10 protected renewal units
        // Percent reserve = ceil(100 * 0.15) = 15 units. Max(15, 10) = 15 units.
        // Available for new sale = 100 - (80 + 15) = 5 units.
        var resultExcess = _sut.EvaluatePlatformCeiling(
            platformCeiling: 100,
            renewalReservePercent: 15.0m,
            currentOccupancy: 80,
            protectedRenewalUnits: 10,
            requestedUnits: 6);

        resultExcess.IsGuaranteed.Should().BeFalse();
        resultExcess.AvailablePlatformCapacity.Should().Be(5);
        resultExcess.RenewalReserveUnits.Should().Be(15);
        resultExcess.FailureReason.Should().Contain("exceed available platform capacity");

        var resultExact = _sut.EvaluatePlatformCeiling(
            platformCeiling: 100,
            renewalReservePercent: 15.0m,
            currentOccupancy: 80,
            protectedRenewalUnits: 10,
            requestedUnits: 5);

        resultExact.IsGuaranteed.Should().BeTrue();
    }

    [Fact]
    public void CanSatisfyDemand_Fails_WhenDietaryRestrictionCannotBeMet_DespiteSufficientAggregateCount()
    {
        var regionId = Guid.NewGuid();
        var serviceDate = new DateOnly(2026, 10, 22);

        // Demand requires Gluten-Free and Keto
        var demands = new[]
        {
            new CommitmentDemand(Guid.NewGuid(), null, regionId, ["GlutenFree", "Keto"], serviceDate)
        };

        // Restaurant has 10 available slots in this region, but only supports Standard and Vegan
        var restaurants = new[]
        {
            new RestaurantCapacityDto(Guid.NewGuid(), 20, regionId, ["Standard", "Vegan"], CurrentOccupancy: 10)
        };

        var canSatisfy = _sut.CanSatisfyDemand(demands, restaurants, out var failureReason);

        canSatisfy.Should().BeFalse();
        failureReason.Should().Contain("GlutenFree, Keto");
    }

    [Fact]
    public void CanSatisfyDemand_Succeeds_WhenMatchingRestaurantHasAvailableCapacity()
    {
        var regionId = Guid.NewGuid();
        var serviceDate = new DateOnly(2026, 10, 22);

        var demands = new[]
        {
            new CommitmentDemand(Guid.NewGuid(), null, regionId, ["GlutenFree"], serviceDate),
            new CommitmentDemand(Guid.NewGuid(), null, regionId, ["Standard"], serviceDate)
        };

        var restaurants = new[]
        {
            new RestaurantCapacityDto(Guid.NewGuid(), 10, regionId, ["Standard", "GlutenFree"], CurrentOccupancy: 8) // 2 left
        };

        var canSatisfy = _sut.CanSatisfyDemand(demands, restaurants, out var failureReason);

        canSatisfy.Should().BeTrue();
        failureReason.Should().BeNull();
    }

    [Fact]
    public void CapacityReductionPlanner_IdentifiesFirstSafeDateCorrectly()
    {
        var nov1 = new DateOnly(2026, 11, 1);
        var nov2 = new DateOnly(2026, 11, 2);
        var nov3 = new DateOnly(2026, 11, 3);
        var nov4 = new DateOnly(2026, 11, 4);

        var occupancies = new[]
        {
            new DateOccupancy(nov1, 60), // Exceeds 50
            new DateOccupancy(nov2, 55), // Exceeds 50
            new DateOccupancy(nov3, 40), // <= 50 (safe)
            new DateOccupancy(nov4, 45)  // <= 50 (safe)
        };

        // Requested 50 boxes starting Nov 1
        var plan = CapacityReductionPlanner.EvaluateReduction(
            requestedLimit: 50,
            requestedEffectiveDate: nov1,
            upcomingOccupancies: occupancies);

        plan.CanApplyImmediately.Should().BeFalse();
        plan.OverCapacityDates.Should().HaveCount(2);
        plan.FirstSafeDate.Should().Be(nov3, "Earliest safe date is the day after the last over-capacity date (Nov 2 + 1 = Nov 3)");
    }
}
