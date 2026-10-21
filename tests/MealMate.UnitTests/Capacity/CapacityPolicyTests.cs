using FluentAssertions;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Xunit;

namespace MealMate.UnitTests.Capacity;

public class CapacityPolicyTests
{
    [Fact]
    public void RestaurantCapacityPolicy_MustHavePositiveBoxLimit()
    {
        var resultZero = RestaurantCapacityPolicy.Create(Guid.NewGuid(), 0, DateOnly.FromDateTime(DateTime.UtcNow));
        resultZero.IsFailure.Should().BeTrue();
        resultZero.Error.Should().Contain("positive integer");

        var resultNegative = RestaurantCapacityPolicy.Create(Guid.NewGuid(), -10, DateOnly.FromDateTime(DateTime.UtcNow));
        resultNegative.IsFailure.Should().BeTrue();

        var resultValid = RestaurantCapacityPolicy.Create(Guid.NewGuid(), 50, DateOnly.FromDateTime(DateTime.UtcNow));
        resultValid.IsSuccess.Should().BeTrue();
        resultValid.Value.DailyBoxLimit.Should().Be(50);
    }

    [Fact]
    public void PlatformCapacityPolicy_CalculatesRenewalReserveCeilCorrectly()
    {
        // 100 limit with 15% reserve -> 15 boxes reserved
        var policy = PlatformCapacityPolicy.Create(100, 15.0m).Value;
        policy.CalculateRenewalReserveUnits().Should().Be(15);

        // 33 limit with 15% reserve -> 33 * 0.15 = 4.95 -> ceiling is 5 boxes
        var oddPolicy = PlatformCapacityPolicy.Create(33, 15.0m).Value;
        oddPolicy.CalculateRenewalReserveUnits().Should().Be(5);
    }

    [Fact]
    public void ServiceCapacityCommitment_ValidTransitions_Succeed()
    {
        var commitment = new ServiceCapacityCommitment
        {
            ServiceDate = new DateOnly(2026, 10, 22),
            CustomerUserId = Guid.NewGuid(),
            SubscriptionId = Guid.NewGuid(),
            Source = CapacityCommitmentSource.PaidSubscription,
            Status = CapacityCommitmentStatus.Reserved
        };

        commitment.CanTransitionTo(CapacityCommitmentStatus.Confirmed).Should().BeTrue();
        var confirmResult = commitment.TransitionTo(CapacityCommitmentStatus.Confirmed);
        confirmResult.IsSuccess.Should().BeTrue();
        commitment.Status.Should().Be(CapacityCommitmentStatus.Confirmed);

        commitment.CanTransitionTo(CapacityCommitmentStatus.Fulfilled).Should().BeTrue();
        var fulfillResult = commitment.TransitionTo(CapacityCommitmentStatus.Fulfilled);
        fulfillResult.IsSuccess.Should().BeTrue();
        commitment.Status.Should().Be(CapacityCommitmentStatus.Fulfilled);

        // Terminal state cannot transition
        commitment.CanTransitionTo(CapacityCommitmentStatus.Cancelled).Should().BeFalse();
        var invalidResult = commitment.TransitionTo(CapacityCommitmentStatus.Cancelled);
        invalidResult.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CapacityReductionRequest_Workflow_AllowsWithdrawAndReview()
    {
        var request = new CapacityReductionRequest
        {
            RestaurantId = Guid.NewGuid(),
            CurrentCapacity = 100,
            RequestedCapacity = 80,
            Reason = "Kitchen staff shortage",
            RequestedEffectiveDate = new DateOnly(2026, 11, 1),
            Status = CapacityReductionStatus.Pending
        };

        request.CanWithdraw.Should().BeTrue();

        var adminId = Guid.NewGuid();
        var approveResult = request.Approve(adminId, new DateOnly(2026, 11, 1), "Approved after checking upcoming obligations");
        approveResult.IsSuccess.Should().BeTrue();
        request.Status.Should().Be(CapacityReductionStatus.Approved);
        request.ReviewedByAdminUserId.Should().Be(adminId);
        request.CanWithdraw.Should().BeFalse();

        // Cannot withdraw already-approved request
        var withdrawResult = request.Withdraw();
        withdrawResult.IsFailure.Should().BeTrue();
    }
}
