using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.CustomerCompensations;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Capacity;

public sealed class CustomerCompensationServiceTests
{
    private readonly AppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IOperationalPolicyService _policyService;
    private readonly CustomerCompensationService _service;

    public CustomerCompensationServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _clock = Substitute.For<IDateTimeProvider>();
        _policyService = Substitute.For<IOperationalPolicyService>();
        _policyService.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new OperationalPolicySettings());
        _db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>(), _clock);
        _service = new CustomerCompensationService(_db, _clock, _policyService);
    }

    [Fact]
    public async Task GetCompensations_WhenWithin48Hours_ReturnsPendingChoiceWithAccurateBreakdown()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var failureTime = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
        var currentTime = failureTime.AddHours(20); // 20 hours in -> 28 hours remaining
        _clock.UtcNow.Returns(currentTime);

        var compensation = new CustomerServiceCompensation
        {
            Id = Guid.NewGuid(),
            CustomerUserId = customerId,
            SubscriptionId = subscriptionId,
            FailedServiceDate = new DateOnly(2026, 10, 20),
            Reason = "KitchenCapacityDeficit",
            NetDayValue = 4.500m,
            Currency = "KWD",
            CardRefundAmount = 3.000m,
            WalletRefundAmount = 1.500m,
            ApologyCreditAmount = 0.450m,
            FailureConfirmedAtUtc = failureTime,
            ChoiceDeadlineUtc = failureTime.AddHours(48),
            RefundInitiationDeadlineUtc = failureTime.AddHours(72),
            Status = CustomerCompensationStatus.PendingChoice
        };

        _db.CustomerServiceCompensations.Add(compensation);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetCompensationsAsync(customerId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);

        var item = result.Value[0];
        item.Status.Should().Be(CustomerCompensationStatus.PendingChoice);
        item.RefundBreakdown.CardRefundAmount.Should().Be(3.000m);
        item.RefundBreakdown.WalletRefundAmount.Should().Be(1.500m);
        item.RefundBreakdown.ApologyCreditAmount.Should().Be(0.450m);
        item.RemainingSeconds.Should().Be(28 * 3600);
    }

    [Fact]
    public async Task GetCompensations_WhenDeadlinePassed_AutomaticallyTransitionsToAutoRefundInitiated()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var failureTime = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
        var currentTime = failureTime.AddHours(49); // 49 hours in -> expired!
        _clock.UtcNow.Returns(currentTime);

        var compensation = new CustomerServiceCompensation
        {
            Id = Guid.NewGuid(),
            CustomerUserId = customerId,
            SubscriptionId = Guid.NewGuid(),
            FailedServiceDate = new DateOnly(2026, 10, 20),
            NetDayValue = 4.500m,
            FailureConfirmedAtUtc = failureTime,
            ChoiceDeadlineUtc = failureTime.AddHours(48),
            RefundInitiationDeadlineUtc = failureTime.AddHours(72),
            Status = CustomerCompensationStatus.PendingChoice
        };

        _db.CustomerServiceCompensations.Add(compensation);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetCompensationsAsync(customerId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value[0].Status.Should().Be(CustomerCompensationStatus.AutoRefundInitiated);
        result.Value[0].RemainingSeconds.Should().Be(0);

        var dbRecord = await _db.CustomerServiceCompensations.FindAsync(compensation.Id);
        dbRecord!.Status.Should().Be(CustomerCompensationStatus.AutoRefundInitiated);
    }

    [Fact]
    public async Task GetAlternativeDates_WhenPlanExcludesFriday_OmitsFridayFromResults()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        // Payment on Tuesday 10:00 -> 72h lead threshold is Friday 10:00
        var now = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc); // Tuesday
        _clock.UtcNow.Returns(now);

        var subscription = new Subscription
        {
            Id = subscriptionId,
            CustomerUserId = customerId,
            IncludeFriday = false, // Plan strictly excludes Friday
            StartDate = new DateOnly(2026, 10, 20),
            EndDate = new DateOnly(2026, 11, 20)
        };
        _db.Subscriptions.Add(subscription);

        var compensation = new CustomerServiceCompensation
        {
            Id = Guid.NewGuid(),
            CustomerUserId = customerId,
            SubscriptionId = subscriptionId,
            Status = CustomerCompensationStatus.PendingChoice
        };
        _db.CustomerServiceCompensations.Add(compensation);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetAlternativeDatesAsync(compensation.Id, customerId, count: 7);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        // Friday must be completely omitted
        result.Value.Any(d => d.DayOfWeek == (int)DayOfWeek.Friday).Should().BeFalse();
        // First deliverable day should jump across Friday to Saturday
        result.Value[0].Date.Should().Be(new DateOnly(2026, 10, 24)); // Saturday
        result.Value[0].DayOfWeek.Should().Be((int)DayOfWeek.Saturday);
    }

    [Fact]
    public async Task ChooseCompensation_WhenChoosingCashRefund_TransitionsToCashRefundChosen()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        _clock.UtcNow.Returns(now);

        var compensation = new CustomerServiceCompensation
        {
            Id = Guid.NewGuid(),
            CustomerUserId = customerId,
            SubscriptionId = Guid.NewGuid(),
            FailedServiceDate = new DateOnly(2026, 10, 20),
            ChoiceDeadlineUtc = now.AddHours(24),
            Status = CustomerCompensationStatus.PendingChoice
        };
        _db.CustomerServiceCompensations.Add(compensation);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ChooseCompensationAsync(
            compensation.Id,
            customerId,
            new ChooseCompensationRequest("CashRefund"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CustomerCompensationStatus.CashRefundChosen);

        var dbRecord = await _db.CustomerServiceCompensations.FindAsync(compensation.Id);
        dbRecord!.Status.Should().Be(CustomerCompensationStatus.CashRefundChosen);
    }

    [Fact]
    public async Task ChooseCompensation_WhenChoosingAlternativeDay_CreatesIndependentCapacityCommitment()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc); // Wednesday
        _clock.UtcNow.Returns(now);

        var subscription = new Subscription
        {
            Id = subscriptionId,
            CustomerUserId = customerId,
            IncludeFriday = true,
            StartDate = new DateOnly(2026, 10, 15),
            EndDate = new DateOnly(2026, 11, 15)
        };
        _db.Subscriptions.Add(subscription);

        var compensation = new CustomerServiceCompensation
        {
            Id = Guid.NewGuid(),
            CustomerUserId = customerId,
            SubscriptionId = subscriptionId,
            FailedServiceDate = new DateOnly(2026, 10, 20),
            ChoiceDeadlineUtc = now.AddHours(24),
            Status = CustomerCompensationStatus.PendingChoice
        };
        _db.CustomerServiceCompensations.Add(compensation);
        await _db.SaveChangesAsync();

        var targetDate = new DateOnly(2026, 10, 26); // Monday next week (> 72h)

        // Act
        var result = await _service.ChooseCompensationAsync(
            compensation.Id,
            customerId,
            new ChooseCompensationRequest("AlternativeDay", targetDate));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CustomerCompensationStatus.AlternativeDayChosen);
        result.Value.SelectedAlternativeDate.Should().Be(targetDate);

        // Verify independent capacity commitment created in DB
        var commitment = await _db.ServiceCapacityCommitments
            .FirstOrDefaultAsync(c => c.SubscriptionId == subscriptionId && c.ServiceDate == targetDate);

        commitment.Should().NotBeNull();
        commitment!.Source.Should().Be(CapacityCommitmentSource.IndependentDayCompensation);
        commitment.Status.Should().Be(CapacityCommitmentStatus.Reserved);
    }

    [Fact]
    public async Task ChooseCompensation_WhenAttemptedAfter48HourDeadline_ReturnsConflictAndInitiatesAutoRefund()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var failureTime = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
        var now = failureTime.AddHours(48).AddMinutes(1); // 1 minute past 48 hours!
        _clock.UtcNow.Returns(now);

        var compensation = new CustomerServiceCompensation
        {
            Id = Guid.NewGuid(),
            CustomerUserId = customerId,
            SubscriptionId = Guid.NewGuid(),
            FailedServiceDate = new DateOnly(2026, 10, 20),
            FailureConfirmedAtUtc = failureTime,
            ChoiceDeadlineUtc = failureTime.AddHours(48),
            Status = CustomerCompensationStatus.PendingChoice
        };
        _db.CustomerServiceCompensations.Add(compensation);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ChooseCompensationAsync(
            compensation.Id,
            customerId,
            new ChooseCompensationRequest("CashRefund"));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("COMPENSATION_CHOICE_WINDOW_EXPIRED");

        var dbRecord = await _db.CustomerServiceCompensations.FindAsync(compensation.Id);
        dbRecord!.Status.Should().Be(CustomerCompensationStatus.AutoRefundInitiated);
    }

    [Fact]
    public async Task ChooseCompensation_WhenNotEligibleForAlternativeDay_ReturnsValidationError()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        _clock.UtcNow.Returns(now);

        var compensation = new CustomerServiceCompensation
        {
            Id = Guid.NewGuid(),
            CustomerUserId = customerId,
            SubscriptionId = Guid.NewGuid(),
            FailedServiceDate = new DateOnly(2026, 10, 20),
            ChoiceDeadlineUtc = now.AddHours(24),
            Status = CustomerCompensationStatus.PendingChoice,
            EligibleForAlternativeDay = false // Strictly cash refund only!
        };
        _db.CustomerServiceCompensations.Add(compensation);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ChooseCompensationAsync(
            compensation.Id,
            customerId,
            new ChooseCompensationRequest("AlternativeDay", new DateOnly(2026, 10, 26)));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ALTERNATIVE_DAY_NOT_ELIGIBLE");
    }

    [Fact]
    public async Task ChooseCompensation_WhenCapacityDepletedOnDate_ReturnsConflict()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var now = new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
        _clock.UtcNow.Returns(now);

        var targetDate = new DateOnly(2026, 10, 26);

        var subscription = new Subscription
        {
            Id = subscriptionId,
            CustomerUserId = customerId,
            IncludeFriday = true,
            StartDate = new DateOnly(2026, 10, 15),
            EndDate = new DateOnly(2026, 11, 15)
        };
        _db.Subscriptions.Add(subscription);

        var compensation = new CustomerServiceCompensation
        {
            Id = Guid.NewGuid(),
            CustomerUserId = customerId,
            SubscriptionId = subscriptionId,
            FailedServiceDate = new DateOnly(2026, 10, 20),
            ChoiceDeadlineUtc = now.AddHours(24),
            Status = CustomerCompensationStatus.PendingChoice,
            EligibleForAlternativeDay = true
        };
        _db.CustomerServiceCompensations.Add(compensation);

        // Pre-fill 1000 commitments to simulate exhausted capacity
        for (int i = 0; i < 1000; i++)
        {
            _db.ServiceCapacityCommitments.Add(new ServiceCapacityCommitment
            {
                Id = Guid.NewGuid(),
                ServiceDate = targetDate,
                CustomerUserId = Guid.NewGuid(),
                SubscriptionId = subscriptionId,
                Status = CapacityCommitmentStatus.Reserved
            });
        }
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.ChooseCompensationAsync(
            compensation.Id,
            customerId,
            new ChooseCompensationRequest("AlternativeDay", targetDate));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("CAPACITY_DEPLETED_FOR_DATE");
    }

    [Fact]
    public async Task GetAlternativeDates_WhenAdminUpdatesPolicyLeadTimeTo48Hours_HonorsAdminConfiguredHours()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        // Tuesday 08:00 UTC -> with 48h lead, threshold is Thursday 08:00 UTC (before 09:00 UTC delivery start)
        var now = new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc); // Tuesday
        _clock.UtcNow.Returns(now);

        _policyService.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new OperationalPolicySettings(
                Cutoff: new CutoffPolicySettings(CutoffLeadHours: 48),
                Cancellation: new CancellationPolicySettings(),
                Freeze: new FreezePolicySettings(),
                Version: 5));

        var subscription = new Subscription
        {
            Id = subscriptionId,
            CustomerUserId = customerId,
            IncludeFriday = true,
            StartDate = new DateOnly(2026, 10, 20),
            EndDate = new DateOnly(2026, 11, 20)
        };
        _db.Subscriptions.Add(subscription);

        var compensation = new CustomerServiceCompensation
        {
            Id = Guid.NewGuid(),
            CustomerUserId = customerId,
            SubscriptionId = subscriptionId,
            Status = CustomerCompensationStatus.PendingChoice,
            EligibleForAlternativeDay = true
        };
        _db.CustomerServiceCompensations.Add(compensation);
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.GetAlternativeDatesAsync(compensation.Id, customerId, count: 7);

        // Assert
        result.IsSuccess.Should().BeTrue();
        // With 48h lead time configured by admin, Thursday 2026-10-22 is eligible!
        result.Value[0].Date.Should().Be(new DateOnly(2026, 10, 22)); // Thursday
    }
}
