using FluentAssertions;
using MealMate.Application.Features.CalendarMeals;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class AdminCompensationServiceTests
{
    private readonly IAdminCompensationService _service = new AdminCompensationService();

    [Fact]
    public void DetermineEscalationStatus_WhenRestaurantExceedsSla_ReturnsUnresponsive()
    {
        var dispatchedAtUtc = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
        var slaHours = 3;
        var currentUtc = new DateTime(2026, 9, 14, 14, 0, 0, DateTimeKind.Utc); // 4h later -> expired

        var isBreached = _service.IsSlaBreached(dispatchedAtUtc, slaHours, currentUtc);

        isBreached.Should().BeTrue();
    }

    [Fact]
    public void DetermineEscalationStatus_WhenWithinSla_ReturnsFalse()
    {
        var dispatchedAtUtc = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);
        var slaHours = 3;
        var currentUtc = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc); // 2h later -> OK

        var isBreached = _service.IsSlaBreached(dispatchedAtUtc, slaHours, currentUtc);

        isBreached.Should().BeFalse();
    }

    [Fact]
    public void BuildCompensationAction_Reassign_GeneratesCorrectAuditEntry()
    {
        var dayId = Guid.NewGuid();
        var newRestaurantId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var result = _service.CreateReassignAction(dayId, newRestaurantId, adminId, "Restaurant unresponsive before cutoff");

        result.DayId.Should().Be(dayId);
        result.Type.Should().Be(CompensationType.ReassignRestaurant);
        result.AdminUserId.Should().Be(adminId);
        result.TargetRestaurantId.Should().Be(newRestaurantId);
    }

    [Fact]
    public void BuildCompensationAction_WalletRefund_GeneratesCorrectAuditEntry()
    {
        var dayId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var amount = 15.5m;

        var result = _service.CreateWalletRefundAction(dayId, customerId, amount, adminId, "Customer compensated with wallet credit");

        result.DayId.Should().Be(dayId);
        result.Type.Should().Be(CompensationType.WalletCredit);
        result.Amount.Should().Be(15.5m);
        result.CustomerId.Should().Be(customerId);
    }

    [Fact]
    public void BuildCompensationAction_ExtendSubscription_GeneratesCorrectAuditEntry()
    {
        var dayId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var result = _service.CreateSubscriptionExtensionAction(dayId, subscriptionId, additionalDays: 1, adminId, "Extended subscription by 1 day");

        result.DayId.Should().Be(dayId);
        result.Type.Should().Be(CompensationType.SubscriptionExtension);
        result.AdditionalDays.Should().Be(1);
        result.SubscriptionId.Should().Be(subscriptionId);
    }
}
