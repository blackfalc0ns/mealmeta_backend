using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

/// <summary>
/// Approval persistence contract: an explicit approval is stored with actor/version/time,
/// invalidated by any terms change, and required (fresh) before automatic activation.
/// Review-window expiry never implies approval.
/// </summary>
public class CampaignApprovalLifecycleTests
{
    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly TransitionCampaignStatusCommandHandler _transitionHandler;
    private readonly CampaignLifecycleSchedulerService _scheduler;

    private static readonly DateTime Now = new(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc);

    public CampaignApprovalLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());

        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(Now);

        _transitionHandler = new TransitionCampaignStatusCommandHandler(_context, _currentUser, _clock);
        _scheduler = new CampaignLifecycleSchedulerService(
            _context, _clock, NullLogger<CampaignLifecycleSchedulerService>.Instance);
    }

    private Campaign CreateUnderReviewCampaign()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة مراجعة",
            NameEn = "Review campaign",
            Status = CampaignStatus.UnderReview,
            StartsAtUtc = Now.AddDays(2),
            EndsAtUtc = Now.AddDays(16),
            EnrollmentOpensAtUtc = Now.AddDays(-3),
            EnrollmentClosesAtUtc = Now.AddDays(-1),
            ReviewStartsAtUtc = Now.AddHours(-1),
            ReviewEndsAtUtc = Now.AddHours(1),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        _context.SaveChanges();
        return campaign;
    }

    [Fact]
    public async Task Approve_TransitionToScheduled_PersistsApprovalBoundToCurrentSnapshotVersion()
    {
        var campaign = CreateUnderReviewCampaign();

        var result = await _transitionHandler.Handle(
            new TransitionCampaignStatusCommand(campaign.Id, CampaignStatus.Scheduled), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var persisted = await _context.Campaigns.SingleAsync(c => c.Id == campaign.Id);
        persisted.Status.Should().Be(CampaignStatus.Scheduled);
        persisted.ApprovedSnapshotVersion.Should().Be(persisted.CurrentSnapshotVersion);
        persisted.ApprovedByUserId.Should().Be(_currentUser.UserId);
        persisted.ApprovedAtUtc.Should().Be(Now);

        var audit = await _context.CampaignAuditLogs
            .Where(a => a.CampaignId == campaign.Id && a.Action.StartsWith("StatusChanged"))
            .SingleAsync();
        audit.AfterStateJson.Should().Contain(nameof(Campaign.ApprovedByUserId));
    }

    [Fact]
    public async Task Approve_OutsideReviewWindow_IsRejectedWithoutApproval()
    {
        var campaign = CreateUnderReviewCampaign();
        campaign.ReviewEndsAtUtc = Now.AddMinutes(-5);
        _context.SaveChanges();

        var result = await _transitionHandler.Handle(
            new TransitionCampaignStatusCommand(campaign.Id, CampaignStatus.Scheduled), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(MessageCodes.Campaign.ReviewWindowExpired);

        var persisted = await _context.Campaigns.SingleAsync(c => c.Id == campaign.Id);
        persisted.Status.Should().Be(CampaignStatus.UnderReview);
        persisted.ApprovedSnapshotVersion.Should().BeNull();
        persisted.ApprovedByUserId.Should().BeNull();
    }

    [Fact]
    public void TermsChange_BumpsVersion_AndInvalidatesStoredApproval()
    {
        var campaign = CreateUnderReviewCampaign();
        campaign.Approve(_currentUser.UserId!.Value, Now);
        campaign.CurrentSnapshotVersion.Should().Be(1);

        campaign.BumpSnapshotVersion();

        campaign.CurrentSnapshotVersion.Should().Be(2);
        campaign.ApprovedSnapshotVersion.Should().BeNull();
        campaign.ApprovedByUserId.Should().BeNull();
        campaign.ApprovedAtUtc.Should().BeNull();
        campaign.CanActivate().IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Scheduler_WithStaleApproval_BlocksActivation_AndDoesNotAutoApprove()
    {
        var campaign = CreateUnderReviewCampaign();
        campaign.Approve(_currentUser.UserId!.Value, Now);
        campaign.TransitionTo(CampaignStatus.Scheduled, "admin", "approved");
        campaign.StartsAtUtc = Now.AddMinutes(-1);
        // Terms changed after approval: version bumped, approval invalidated.
        campaign.BumpSnapshotVersion();
        await _context.SaveChangesAsync();

        var result = await _scheduler.ProcessLifecycleTransitionsAsync();

        result.ActivatedCount.Should().Be(0);
        var persisted = await _context.Campaigns.SingleAsync(c => c.Id == campaign.Id);
        persisted.Status.Should().Be(CampaignStatus.Scheduled);

        var blocked = await _context.OutboxMessages
            .Where(o => o.EventType == "CampaignLaunchBlocked")
            .ToListAsync();
        blocked.Should().HaveCount(1);

        // Second scheduler run must not duplicate the blocked notification.
        await _scheduler.ProcessLifecycleTransitionsAsync();
        var blockedAfterRerun = await _context.OutboxMessages
            .Where(o => o.EventType == "CampaignLaunchBlocked")
            .ToListAsync();
        blockedAfterRerun.Should().HaveCount(1);
    }

    [Fact]
    public async Task Scheduler_WithValidApproval_ActivatesCampaign()
    {
        var campaign = CreateUnderReviewCampaign();
        campaign.Approve(_currentUser.UserId!.Value, Now);
        campaign.TransitionTo(CampaignStatus.Scheduled, "admin", "approved");
        campaign.StartsAtUtc = Now.AddMinutes(-1);
        await _context.SaveChangesAsync();

        var result = await _scheduler.ProcessLifecycleTransitionsAsync();

        result.ActivatedCount.Should().Be(1);
        var persisted = await _context.Campaigns.SingleAsync(c => c.Id == campaign.Id);
        persisted.Status.Should().Be(CampaignStatus.Active);
    }

    [Fact]
    public async Task Scheduler_WithOverdueReview_EmitsOneOverdueNotification_NeverApproves()
    {
        var campaign = CreateUnderReviewCampaign();
        campaign.ReviewEndsAtUtc = Now.AddMinutes(-30);
        await _context.SaveChangesAsync();

        var result = await _scheduler.ProcessLifecycleTransitionsAsync();

        result.ActivatedCount.Should().Be(0);
        var persisted = await _context.Campaigns.SingleAsync(c => c.Id == campaign.Id);
        persisted.Status.Should().Be(CampaignStatus.UnderReview);
        persisted.ApprovedSnapshotVersion.Should().BeNull();

        var overdue = await _context.OutboxMessages
            .Where(o => o.EventType == "CampaignReviewOverdue")
            .ToListAsync();
        overdue.Should().HaveCount(1);

        await _scheduler.ProcessLifecycleTransitionsAsync();
        var overdueAfterRerun = await _context.OutboxMessages
            .Where(o => o.EventType == "CampaignReviewOverdue")
            .ToListAsync();
        overdueAfterRerun.Should().HaveCount(1);
    }
}

/// <summary>Pricing policy contract: no checkout without an approved policy covering the currency.</summary>
public class CampaignPricingPolicyGateTests
{
    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ICampaignEligibilityEngine _eligibilityEngine;
    private readonly ICampaignReservationCoordinator _reservationCoordinator;
    private readonly CreateCampaignCheckoutSessionCommandHandler _handler;

    private static readonly DateTime Now = new(2026, 10, 10, 11, 0, 0, DateTimeKind.Utc);

    public CampaignPricingPolicyGateTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.UserType.Returns(UserType.Customer);

        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(Now);

        _eligibilityEngine = Substitute.For<ICampaignEligibilityEngine>();
        _eligibilityEngine.Evaluate(Arg.Any<Campaign>(), Arg.Any<CustomerEligibilityProfile>(), Arg.Any<CampaignEligibilityCriteria>(), Arg.Any<DateTime>())
            .Returns(new CampaignEligibilityResult(true, Array.Empty<string>()));

        _reservationCoordinator = Substitute.For<ICampaignReservationCoordinator>();
        _reservationCoordinator.CoordinateReservation(Arg.Any<ReservationRequest>())
            .Returns(new CampaignReservationResult(
                IsSuccess: true,
                ReservationId: Guid.NewGuid(),
                ExpiresAtUtc: Now.AddMinutes(15),
                UpdatedCapState: null,
                Allocations: Array.Empty<CampaignReservationAllocationItem>(),
                DeterministicLockTargets: Array.Empty<CapacityLockTarget>()));

        _handler = new CreateCampaignCheckoutSessionCommandHandler(
            _context, _clock,
            new CustomerCampaignEvaluationService(
                _context, _clock, _eligibilityEngine,
                new CustomerDeliveryAddressService(_context, _clock)),
            _reservationCoordinator);
    }

    private Campaign CreateActiveCampaign()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة نشطة",
            NameEn = "Active campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = Now.AddDays(-1),
            EndsAtUtc = Now.AddDays(13),
            TotalDiscountRate = Percentage.FromFraction(0.30m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.20m)
        };
        _context.Campaigns.Add(campaign);

        var program = new Program
        {
            Code = "P-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "برنامج",
            NameEn = "Program",
            Status = ProgramStatus.Active,
            IsActive = true
        };
        _context.Programs.Add(program);
        var catalogBundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "B-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "باقة",
            NameEn = "Bundle",
            MealsPerDay = 3,
            Status = ProgramStatus.Active,
            IsActive = true
        };
        _context.Bundles.Add(catalogBundle);

        var bundle = new CampaignBundle { Campaign = campaign, BundleId = catalogBundle.Id };
        _context.CampaignBundles.Add(bundle);
        _context.SaveChanges();
        var priceResult = CampaignBundleDurationPrice.Create(
            bundle.Id, 26, new Money(140m, "KWD"), source: "TestContract", version: 1, capturedAtUtc: Now);
        priceResult.IsSuccess.Should().BeTrue();
        _context.CampaignBundleDurationPrices.Add(priceResult.Value);
        _context.SaveChanges();
        return campaign;
    }

    private static CreateCampaignCheckoutSessionCommand BuildCommand(Campaign campaign, Guid bundleId)
    {
        return new CreateCampaignCheckoutSessionCommand(
            IdempotencyKey: $"test-{Guid.NewGuid():N}",
            CampaignId: campaign.Id,
            BundleId: bundleId,
            CustomerId: Guid.NewGuid(),
            DurationDays: 26,
            RequestedServiceDates: Enumerable.Range(0, 26).Select(i => DateOnly.FromDateTime(Now.Date.AddDays(i))).ToList());
    }

    [Fact]
    public async Task Checkout_WithoutAnyPolicy_IsBlockedWithHonestError()
    {
        var campaign = CreateActiveCampaign();
        var bundleId = await _context.CampaignBundles.Where(b => b.CampaignId == campaign.Id).Select(b => b.Id).SingleAsync();

        var result = await _handler.Handle(BuildCommand(campaign, bundleId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(MessageCodes.Campaign.PricingPolicyNotApproved);
        (await _context.CampaignCheckoutSessions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Checkout_WithUnapprovedPolicyOnly_IsBlocked()
    {
        var campaign = CreateActiveCampaign();
        var bundleId = await _context.CampaignBundles.Where(b => b.CampaignId == campaign.Id).Select(b => b.Id).SingleAsync();

        var policyResult = CampaignPricingPolicy.Create(
            1, CampaignPricingPolicy.ModelReferenceCustomerDiscount, "KWD",
            CampaignPricingPolicy.RoundingAwayFromZero,
            "{\"restaurantShare\":0.66}", "{\"stacking\":\"exclusive\"}", "{\"basis\":\"undeliveredDays\"}");
        policyResult.IsSuccess.Should().BeTrue();
        _context.CampaignPricingPolicies.Add(policyResult.Value);
        await _context.SaveChangesAsync();

        var result = await _handler.Handle(BuildCommand(campaign, bundleId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(MessageCodes.Campaign.PricingPolicyNotApproved);
    }

    [Fact]
    public async Task Checkout_WithApprovedPolicy_ComputesPriceViaCalculatorAndBindsPolicy()
    {
        var campaign = CreateActiveCampaign();
        var bundleId = await _context.CampaignBundles.Where(b => b.CampaignId == campaign.Id).Select(b => b.Id).SingleAsync();

        var policyResult = CampaignPricingPolicy.Create(
            1, CampaignPricingPolicy.ModelReferenceCustomerDiscount, "KWD",
            CampaignPricingPolicy.RoundingAwayFromZero,
            "{\"restaurantShare\":0.66}", "{\"stacking\":\"exclusive\"}", "{\"basis\":\"undeliveredDays\"}");
        _context.CampaignPricingPolicies.Add(policyResult.Value);
        await _context.SaveChangesAsync();
        policyResult.Value.Approve(_currentUser.UserId!.Value, Now);
        await _context.SaveChangesAsync();

        var customerUserId = Guid.NewGuid();

        var address = new MealMate.Domain.IdentityAccess.CustomerAddress
        {
            UserId = customerUserId,
            Label = "المنزل",
            AddressLine = "شارع 1",
            RegionId = Guid.NewGuid(),
            Governorate = "العاصمة",
            IsDefault = true
        };
        _context.CustomerAddresses.Add(address);
        _context.CustomerProfiles.Add(new MealMate.Domain.IdentityAccess.CustomerProfile
        {
            UserId = customerUserId,
            AllergiesCompletedAtUtc = Now.AddMinutes(-1),
            DietaryPreferencesCompletedAtUtc = Now.AddMinutes(-1),
            ActiveDeliveryAddressId = address.Id,
            ActiveDeliveryRegionId = address.RegionId
        });
        await _context.SaveChangesAsync();

        var command = BuildCommand(campaign, bundleId) with { CustomerId = customerUserId };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // 140 KWD reference with 30% headline discount => 98.000 KWD final (calculator rounding, away from zero).
        result.Value.FinalCustomerPrice.Should().Be(98.000m);
        result.Value.Currency.Should().Be("KWD");

        var session = await _context.CampaignCheckoutSessions.SingleAsync();
        session.FinalAmount.Should().Be(98.000m);
        session.CurrencyCode.Should().Be("KWD");
        session.PricingPolicyId.Should().Be(policyResult.Value.Id);
        session.PricingPolicyVersion.Should().Be(1);
        session.CampaignSnapshotVersion.Should().Be(campaign.CurrentSnapshotVersion);
    }

    [Fact]
    public void Policy_CreateRejectsUnknownModelAndMissingRules()
    {
        var missing = CampaignPricingPolicy.Create(1, CampaignPricingPolicy.ModelReferenceCustomerDiscount, "KWD",
            CampaignPricingPolicy.RoundingAwayFromZero, "", "{\"a\":1}", "{\"b\":2}");
        missing.IsFailure.Should().BeTrue();

        var badModel = CampaignPricingPolicy.Create(1, "SomeNewModel", "KWD",
            CampaignPricingPolicy.RoundingAwayFromZero, "{}", "{}", "{}");
        badModel.IsFailure.Should().BeTrue();

        var badCurrency = CampaignPricingPolicy.Create(1, CampaignPricingPolicy.ModelReferenceCustomerDiscount, "KD",
            CampaignPricingPolicy.RoundingAwayFromZero, "{}", "{}", "{}");
        badCurrency.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Policy_ApproveIsSingleShot()
    {
        var policyResult = CampaignPricingPolicy.Create(1, CampaignPricingPolicy.ModelReferenceCustomerDiscount, "KWD",
            CampaignPricingPolicy.RoundingAwayFromZero, "{}", "{}", "{}");
        var approver = Guid.NewGuid();
        policyResult.Value.Approve(approver, Now).IsSuccess.Should().BeTrue();
        policyResult.Value.IsApproved.Should().BeTrue();

        var second = policyResult.Value.Approve(Guid.NewGuid(), Now.AddMinutes(1));
        second.IsFailure.Should().BeTrue();
    }
}
