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
    public void GetSelectionDeadlineUtc_WhenCutoffIs48Hours_Returns48HoursBeforeMidnight()
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
        result.Should().Be(expectedDeadline);
        _policyService.Received(1).CalculateSelectionDeadlineUtc(serviceDate);
    }

    [Fact]
    public void GetSelectionDeadlineUtc_WhenFixedDailyHourEnabled_ReturnsFixedDailyHourUtc()
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
        result.Should().Be(expectedDeadline);
        _policyService.Received(1).CalculateSelectionDeadlineUtc(serviceDate);
    }

    [Fact]
    public void IsStartAllowed_UsesDynamicCutoffDeadline()
    {
        // Arrange
        var serviceDate = new DateOnly(2026, 8, 30);
        var deadline = new DateTime(2026, 8, 27, 21, 0, 0, DateTimeKind.Utc);

        _policyService.CalculateSelectionDeadlineUtc(serviceDate)
            .Returns(deadline);

        var scheduleService = new SubscriptionScheduleService(_clock, _policyService);

        // Case A: Before deadline -> allowed
        _clock.UtcNow.Returns(deadline.AddHours(-1));
        scheduleService.IsStartAllowed(serviceDate).Should().BeTrue();

        // Case B: At or after deadline -> not allowed
        _clock.UtcNow.Returns(deadline.AddHours(1));
        scheduleService.IsStartAllowed(serviceDate).Should().BeFalse();
    }
}
