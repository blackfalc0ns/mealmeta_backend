using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.CustomerCheckout;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.OperationalPolicies;

public class DynamicCutoffIntegrationTests
{
    private readonly IDateTimeProvider _clock;
    private readonly IOperationalPolicyService _policyService;

    public DynamicCutoffIntegrationTests()
    {
        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc));
        _policyService = Substitute.For<IOperationalPolicyService>();
    }

    [Fact]
    public void Individual_schedule_does_not_allow_five_minute_policy_to_shorten_72_hour_cutoff()
    {
        var serviceDate = new DateOnly(2026, 10, 17);
        _policyService.CalculateSelectionDeadlineUtc(serviceDate)
            .Returns(new DateTime(2026, 10, 16, 20, 55, 0, DateTimeKind.Utc));
        new SubscriptionScheduleService(_clock, _policyService).GetSelectionDeadlineUtc(serviceDate)
            .Should().Be(new DateTime(2026, 10, 13, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void GetSelectionDeadlineUtc_WhenCutoffIs48Hours_Keeps72HourMinimum()
    {
        // Arrange
        var serviceDate = new DateOnly(2026, 8, 30);
        var expectedDeadline = new DateTime(2026, 8, 27, 21, 0, 0, DateTimeKind.Utc);

        _policyService.CalculateSelectionDeadlineUtc(serviceDate)
            .Returns(expectedDeadline);

        var scheduleService = new SubscriptionScheduleService(_clock, _policyService);

        // Act
        var result = scheduleService.GetSelectionDeadlineUtc(serviceDate);

        // Assert
        result.Should().Be(new DateTime(2026, 8, 26, 21, 0, 0, DateTimeKind.Utc));
        _policyService.Received(1).CalculateSelectionDeadlineUtc(serviceDate);
    }

    [Fact]
    public void GetSelectionDeadlineUtc_WhenFixedDailyHourIsTooLate_Keeps72HourMinimum()
    {
        // Arrange
        var serviceDate = new DateOnly(2026, 8, 30);
        var expectedDeadline = new DateTime(2026, 8, 27, 11, 0, 0, DateTimeKind.Utc); // 14:00 Kuwait time

        _policyService.CalculateSelectionDeadlineUtc(serviceDate)
            .Returns(expectedDeadline);

        var scheduleService = new SubscriptionScheduleService(_clock, _policyService);

        // Act
        var result = scheduleService.GetSelectionDeadlineUtc(serviceDate);

        // Assert
        result.Should().Be(new DateTime(2026, 8, 26, 21, 0, 0, DateTimeKind.Utc));
        _policyService.Received(1).CalculateSelectionDeadlineUtc(serviceDate);
    }

    [Fact]
    public void IsStartAllowed_Uses72HourMinimumInsteadOfShorterConfiguredDeadline()
    {
        // Arrange
        var serviceDate = new DateOnly(2026, 8, 30);
        var deadline = new DateTime(2026, 8, 27, 21, 0, 0, DateTimeKind.Utc);

        _policyService.CalculateSelectionDeadlineUtc(serviceDate)
            .Returns(deadline);

        var scheduleService = new SubscriptionScheduleService(_clock, _policyService);

        // Case A: Before deadline -> allowed
        _clock.UtcNow.Returns(new DateTime(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc));
        scheduleService.IsStartAllowed(serviceDate).Should().BeTrue();

        // Case B: At or after deadline -> not allowed
        _clock.UtcNow.Returns(new DateTime(2026, 8, 26, 21, 0, 0, DateTimeKind.Utc));
        scheduleService.IsStartAllowed(serviceDate).Should().BeFalse();
    }
}
