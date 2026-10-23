using FluentAssertions;
using MealMate.Application.Features.UnifiedSupport.Timing;
using MealMate.Domain.ComplaintsSupport;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ComplaintsSupport;

public sealed class SupportTimingPolicyTests
{
    private readonly DateTime _baseTime = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RuleMatches_ExactChannelPriorityAndCategory_ReturnsTrue()
    {
        var rule = SupportTimingRule.Create(
            Guid.NewGuid(),
            SupportCaseChannel.Customer,
            SupportCasePriority.P0,
            "Delivery",
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(2),
            TimeSpan.FromMinutes(30),
            requiresImmediateAck: true,
            evaluationOrder: 1);

        rule.Matches(SupportCaseChannel.Customer, SupportCasePriority.P0, "Delivery").Should().BeTrue();
        rule.Matches(SupportCaseChannel.Driver, SupportCasePriority.P0, "Delivery").Should().BeFalse();
        rule.Matches(SupportCaseChannel.Customer, SupportCasePriority.P1, "Delivery").Should().BeFalse();
        rule.Matches(SupportCaseChannel.Customer, SupportCasePriority.P0, "Billing").Should().BeFalse();
    }

    [Fact]
    public void RuleMatches_WildcardRule_MatchesAnyClassification()
    {
        var wildcard = SupportTimingRule.Create(
            Guid.NewGuid(),
            channel: null,
            priority: null,
            category: null,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(4),
            requiresImmediateAck: false,
            evaluationOrder: 99);

        wildcard.Matches(SupportCaseChannel.Restaurant, SupportCasePriority.P2, "FoodQuality").Should().BeTrue();
        wildcard.Matches(SupportCaseChannel.Driver, null, "VehicleBreakdown").Should().BeTrue();
    }

    [Fact]
    public void PolicyFindMatchingRule_SelectsLowestEvaluationOrder()
    {
        var policy = SupportTimingPolicy.Create(
            version: 1,
            name: "Default SLA",
            description: null,
            publishedByUserId: Guid.NewGuid(),
            publicationReason: "Initial launch",
            idempotencyKey: "sla-init-key-1",
            publishedAtUtc: _baseTime);

        // Wildcard with high evaluation order
        policy.AddRule(
            channel: null,
            priority: null,
            category: null,
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(6),
            requiresImmediateAck: false,
            evaluationOrder: 10);

        // P0 specific with low evaluation order
        policy.AddRule(
            channel: SupportCaseChannel.Customer,
            priority: SupportCasePriority.P0,
            category: null,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(20),
            requiresImmediateAck: true,
            evaluationOrder: 1);

        var matched = policy.FindMatchingRule(SupportCaseChannel.Customer, SupportCasePriority.P0, "General");

        matched.Should().NotBeNull();
        matched!.EvaluationOrder.Should().Be(1);
        matched.FirstResponseDeadline.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void CycleFirstResponse_RecordedAfterDeadline_MarksBreached()
    {
        var started = _baseTime;
        var deadline = started.AddMinutes(30);
        var resolutionDeadline = started.AddHours(4);

        var cycle = SupportCaseTimingCycle.StartConfigured(
            Guid.NewGuid(),
            cycleNumber: 1,
            Guid.NewGuid(),
            policyVersion: 1,
            Guid.NewGuid(),
            started,
            deadline,
            resolutionDeadline);

        // First response at 45 minutes (15 min breach)
        var respondedAt = started.AddMinutes(45);
        cycle.RecordFirstResponse(respondedAt).Should().BeTrue();

        cycle.FirstResponseAtUtc.Should().Be(respondedAt);
        cycle.IsFirstResponseBreached.Should().BeTrue();
        cycle.IsResolutionBreached.Should().BeFalse();
    }

    [Fact]
    public void CycleResolution_RecordedWithinDeadline_NotBreached()
    {
        var started = _baseTime;
        var deadline = started.AddMinutes(30);
        var resolutionDeadline = started.AddHours(4);

        var cycle = SupportCaseTimingCycle.StartConfigured(
            Guid.NewGuid(),
            cycleNumber: 1,
            Guid.NewGuid(),
            policyVersion: 1,
            Guid.NewGuid(),
            started,
            deadline,
            resolutionDeadline);

        var resolvedAt = started.AddHours(2);
        cycle.RecordResolution(resolvedAt).Should().BeTrue();

        cycle.ResolvedAtUtc.Should().Be(resolvedAt);
        cycle.IsResolutionBreached.Should().BeFalse();
    }

    [Fact]
    public void CycleResolution_RecordedAfterDeadline_MarksBreached()
    {
        var started = _baseTime;
        var deadline = started.AddMinutes(30);
        var resolutionDeadline = started.AddHours(4);

        var cycle = SupportCaseTimingCycle.StartConfigured(
            Guid.NewGuid(),
            cycleNumber: 1,
            Guid.NewGuid(),
            policyVersion: 1,
            Guid.NewGuid(),
            started,
            deadline,
            resolutionDeadline);

        var resolvedAt = started.AddHours(5);
        cycle.RecordResolution(resolvedAt).Should().BeTrue();

        cycle.ResolvedAtUtc.Should().Be(resolvedAt);
        cycle.IsResolutionBreached.Should().BeTrue();
    }

    [Fact]
    public void CycleStartUnconfigured_FlagsPolicyUnconfigured()
    {
        var caseId = Guid.NewGuid();
        var cycle = SupportCaseTimingCycle.StartUnconfigured(
            caseId,
            cycleNumber: 1,
            _baseTime,
            reason: "No active support timing policy published.");

        cycle.IsPolicyUnconfigured.Should().BeTrue();
        cycle.FirstResponseDeadlineUtc.Should().BeNull();
        cycle.ResolutionDeadlineUtc.Should().BeNull();
        cycle.Notes.Should().Contain("No active support timing policy");
    }

    [Fact]
    public void CycleEscalate_UpdatesStateAndReason()
    {
        var cycle = SupportCaseTimingCycle.StartUnconfigured(Guid.NewGuid(), 1, _baseTime);
        var escalateTime = _baseTime.AddHours(1);

        cycle.Escalate("Urgent customer priority", escalateTime);

        cycle.IsEscalated.Should().BeTrue();
        cycle.EscalatedAtUtc.Should().Be(escalateTime);
        cycle.EscalationReason.Should().Be("Urgent customer priority");
    }

    [Fact]
    public void RuleCreation_WithNegativeDeadlines_ThrowsArgumentException()
    {
        var act = () => SupportTimingRule.Create(
            Guid.NewGuid(),
            SupportCaseChannel.Customer,
            SupportCasePriority.P0,
            "General",
            TimeSpan.FromMinutes(-5),
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(10),
            false,
            1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*First response deadline must be positive*");
    }

    [Fact]
    public void RuleCreation_WithFirstResponseExceedingResolution_ThrowsArgumentException()
    {
        var act = () => SupportTimingRule.Create(
            Guid.NewGuid(),
            SupportCaseChannel.Customer,
            SupportCasePriority.P0,
            "General",
            TimeSpan.FromHours(5),
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(10),
            false,
            1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*First response deadline cannot exceed resolution deadline*");
    }

    [Fact]
    public async Task PolicyService_PublishRejectsUnauthenticated()
    {
        var currentUser = NSubstitute.Substitute.For<MealMate.Application.Common.Abstractions.ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);
        var service = new SupportTimingPolicyService(
            NSubstitute.Substitute.For<MealMate.Application.Common.Abstractions.IAppDbContext>(),
            NSubstitute.Substitute.For<MealMate.Application.Common.Abstractions.IUnitOfWork>(),
            Substitute.For<MealMate.Application.Common.Abstractions.IApplicationTransactionExecutor>(),
            currentUser,
            TimeProvider.System,
            NSubstitute.Substitute.For<MealMate.Application.Common.Abstractions.IApplicationLockManager>());

        var result = await service.PublishPolicyAsync(new PublishSupportTimingPolicyRequest(
            "Name", "Desc", "Reason", "key-1", 1, []));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MealMate.Application.Features.UnifiedSupport.SupportCaseProblemCodes.AuthenticationRequired);
    }

    [Fact]
    public async Task PolicyService_PublishRejectsNonAdmin()
    {
        var currentUser = NSubstitute.Substitute.For<MealMate.Application.Common.Abstractions.ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.Roles.Returns(new HashSet<string>());
        currentUser.UserType.Returns(MealMate.Domain.Enums.UserType.Customer);
        var service = new SupportTimingPolicyService(
            NSubstitute.Substitute.For<MealMate.Application.Common.Abstractions.IAppDbContext>(),
            NSubstitute.Substitute.For<MealMate.Application.Common.Abstractions.IUnitOfWork>(),
            Substitute.For<MealMate.Application.Common.Abstractions.IApplicationTransactionExecutor>(),
            currentUser,
            TimeProvider.System,
            NSubstitute.Substitute.For<MealMate.Application.Common.Abstractions.IApplicationLockManager>());

        var result = await service.PublishPolicyAsync(new PublishSupportTimingPolicyRequest(
            "Name", "Desc", "Reason", "key-1", 1, []));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MealMate.Application.Features.UnifiedSupport.SupportCaseProblemCodes.AccessDenied);
    }
}
