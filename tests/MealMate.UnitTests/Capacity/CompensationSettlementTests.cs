using FluentAssertions;
using MealMate.Application.Features.CalendarMeals;
using MealMate.Application.Features.RestaurantOperations.Availability;
using Xunit;

namespace MealMate.UnitTests.Capacity;

public class CompensationSettlementTests
{
    [Fact]
    public void CalculateDayAllocation_MatchesSpecWorkedExample()
    {
        // Example from spec Section 14 / plan M7:
        // Day 1: gross 60 KWD, 20% general discount, 5 KWD shared delivery share
        // Day 2: gross 40 KWD, 20% general discount, 5 KWD shared delivery share
        var day1 = ServiceCompensationSettlement.CalculateDayAllocation(60m, 0.20m, 5m);
        var day2 = ServiceCompensationSettlement.CalculateDayAllocation(40m, 0.20m, 5m);

        day1.DiscountAmount.Should().Be(12m);
        day1.NetValue.Should().Be(53m, "Net = (60 - 12) + 5 = 53 KWD");

        day2.DiscountAmount.Should().Be(8m);
        day2.NetValue.Should().Be(37m, "Net = (40 - 8) + 5 = 37 KWD");

        // Total of net allocations equals total invoice: 53 + 37 = 90
        (day1.NetValue + day2.NetValue).Should().Be(90m);

        // When Day 1 fails delivery:
        var compensation = ServiceCompensationSettlement.CalculateCompensation(
            netDayValue: day1.NetValue,
            failureConfirmedAtUtc: new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc),
            apologyCreditPercent: 10m);

        compensation.RefundAmount.Should().Be(53m);
        compensation.ApologyCreditAmount.Should().Be(5.30m, "10% apology credit on 53 KWD net is 5.30 KWD");
        compensation.ApologyCreditValidityDays.Should().Be(90);
    }

    [Fact]
    public void CalculateCompensation_Sets48HourChoiceAnd24HourRefundDeadlines()
    {
        var failureTime = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

        var result = ServiceCompensationSettlement.CalculateCompensation(
            netDayValue: 50m,
            failureConfirmedAtUtc: failureTime);

        // 48 hours for choice
        result.ChoiceDeadlineUtc.Should().Be(failureTime.AddHours(48));

        // 24 hours after choice deadline for automated refund initiation
        result.RefundInitiationDeadlineUtc.Should().Be(failureTime.AddHours(72));
    }

    [Fact]
    public void SameDayReplacement_WhenPrepStarted_BlocksAutomatedTransferToPreventDoubleDispatch()
    {
        var sut = new SameDayReplacementService();
        var regionId = Guid.NewGuid();
        var failedRestaurantId = Guid.NewGuid();

        var availableRestaurants = new[]
        {
            new RestaurantCapacityDto(Guid.NewGuid(), 20, regionId, ["Standard"], CurrentOccupancy: 5)
        };

        var result = sut.FindReplacementRestaurant(
            failedRestaurantId,
            regionId,
            requiredDietaryRestrictions: ["Standard"],
            availableRestaurants,
            isPreparationStarted: true);

        result.Success.Should().BeFalse();
        result.RequiresAdminEscalation.Should().BeTrue();
        result.Message.Should().Contain("Preparation has already commenced");
    }

    [Fact]
    public void SameDayReplacement_BeforePrep_ReassignsToEligibleRestaurantInSameRegion()
    {
        var sut = new SameDayReplacementService();
        var regionId = Guid.NewGuid();
        var failedRestaurantId = Guid.NewGuid();
        var candidateRestaurantId = Guid.NewGuid();

        var availableRestaurants = new[]
        {
            new RestaurantCapacityDto(candidateRestaurantId, 20, regionId, ["Standard", "GlutenFree"], CurrentOccupancy: 10)
        };

        var result = sut.FindReplacementRestaurant(
            failedRestaurantId,
            regionId,
            requiredDietaryRestrictions: ["GlutenFree"],
            availableRestaurants,
            isPreparationStarted: false);

        result.Success.Should().BeTrue();
        result.ReplacementRestaurantId.Should().Be(candidateRestaurantId);
        result.RequiresAdminEscalation.Should().BeFalse();
    }
}
