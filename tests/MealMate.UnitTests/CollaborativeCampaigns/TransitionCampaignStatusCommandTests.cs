using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class TransitionCampaignStatusCommandTests
{
    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly TransitionCampaignStatusCommandHandler _handler;

    public TransitionCampaignStatusCommandTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());

        _handler = new TransitionCampaignStatusCommandHandler(_context, _currentUser);
    }

    [Fact]
    public async Task Handle_ValidTransition_UpdatesStatusAndPersistsAuditLog()
    {
        var now = DateTime.UtcNow;
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.Draft,
            EnrollmentOpensAtUtc = now.AddDays(1),
            EnrollmentClosesAtUtc = now.AddDays(3),
            ReviewStartsAtUtc = now.AddDays(3),
            ReviewEndsAtUtc = now.AddDays(4),
            StartsAtUtc = now.AddDays(5),
            EndsAtUtc = now.AddDays(25),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var command = new TransitionCampaignStatusCommand(
            CampaignId: campaign.Id,
            TargetStatus: CampaignStatus.OpenForJoining,
            Reason: "Opening for restaurant participation"
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.OpenForJoining);

        var audit = await _context.CampaignAuditLogs
            .FirstOrDefaultAsync(a => a.CampaignId == campaign.Id);
        audit.Should().NotBeNull();
        audit!.Action.Should().Contain("Draft->OpenForJoining");
        audit.JustificationReason.Should().Be("Opening for restaurant participation");
    }

    [Fact]
    public async Task Handle_CancelWithReason_Succeeds()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(5),
            EndsAtUtc = DateTime.UtcNow.AddDays(25),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var command = new TransitionCampaignStatusCommand(
            CampaignId: campaign.Id,
            TargetStatus: CampaignStatus.Cancelled,
            Reason: "Budget cut by executive committee"
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_CancelWithoutReason_ReturnsValidationFailure()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.Draft,
            StartsAtUtc = DateTime.UtcNow.AddDays(5),
            EndsAtUtc = DateTime.UtcNow.AddDays(25),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var command = new TransitionCampaignStatusCommand(
            CampaignId: campaign.Id,
            TargetStatus: CampaignStatus.Cancelled,
            Reason: null
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("Reason is mandatory");
    }

    [Fact]
    public async Task Handle_NeedsChangesWithoutReason_ReturnsValidationFailure()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.UnderReview,
            StartsAtUtc = DateTime.UtcNow.AddDays(5),
            EndsAtUtc = DateTime.UtcNow.AddDays(25),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var command = new TransitionCampaignStatusCommand(
            CampaignId: campaign.Id,
            TargetStatus: CampaignStatus.NeedsChanges,
            Reason: " "
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("Reason is mandatory");
    }

    [Fact]
    public async Task Handle_InvalidStateTransition_StoppedToActive_ReturnsFailure()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.Stopped,
            StartsAtUtc = DateTime.UtcNow.AddDays(-5),
            EndsAtUtc = DateTime.UtcNow.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var command = new TransitionCampaignStatusCommand(
            CampaignId: campaign.Id,
            TargetStatus: CampaignStatus.Active,
            Reason: "Attempting resumption"
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("not allowed");
    }

    [Fact]
    public async Task Handle_InvalidStateTransition_ActiveToCancelled_ReturnsFailure()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var command = new TransitionCampaignStatusCommand(
            CampaignId: campaign.Id,
            TargetStatus: CampaignStatus.Cancelled,
            Reason: "Attempting cancellation of active campaign"
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("not allowed");
    }

    [Fact]
    public async Task Handle_WhenUnauthenticated_ReturnsUnauthorized()
    {
        _currentUser.IsAuthenticated.Returns(false);

        var command = new TransitionCampaignStatusCommand(
            CampaignId: Guid.NewGuid(),
            TargetStatus: CampaignStatus.OpenForJoining
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(MealMate.Application.Common.Models.ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Handle_WhenCampaignNotFound_ReturnsNotFound()
    {
        var command = new TransitionCampaignStatusCommand(
            CampaignId: Guid.NewGuid(),
            TargetStatus: CampaignStatus.OpenForJoining
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(MealMate.Application.Common.Models.ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_TransitionToScheduled_BeforeWindowStarts_IsRejected_AndStateUnaffected()
    {
        var reviewStarts = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var reviewEnds = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        var campaign = SeedCampaignUnderReview(reviewStarts, reviewEnds);
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(reviewStarts.AddTicks(-1));

        var syncService = Substitute.For<ICampaignOfferSyncService>();
        var handler = new TransitionCampaignStatusCommandHandler(_context, _currentUser, clock, syncService);

        var command = new TransitionCampaignStatusCommand(campaign.Id, CampaignStatus.Scheduled);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowNotStarted);

        var unaffected = await _context.Campaigns.FindAsync(campaign.Id);
        unaffected!.Status.Should().Be(CampaignStatus.UnderReview);

        var audits = await _context.CampaignAuditLogs.Where(a => a.CampaignId == campaign.Id).ToListAsync();
        audits.Should().BeEmpty();

        await syncService.DidNotReceive().SyncCampaignToOfferAsync(Arg.Any<Campaign>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TransitionToScheduled_AtWindowStart_Succeeds_AndPersistsAuditAndSyncs()
    {
        var reviewStarts = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var reviewEnds = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        var campaign = SeedCampaignUnderReview(reviewStarts, reviewEnds);
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(reviewStarts);

        var syncService = Substitute.For<ICampaignOfferSyncService>();
        var handler = new TransitionCampaignStatusCommandHandler(_context, _currentUser, clock, syncService);

        var command = new TransitionCampaignStatusCommand(campaign.Id, CampaignStatus.Scheduled, "Approved within window");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.Scheduled);

        var audit = await _context.CampaignAuditLogs.FirstOrDefaultAsync(a => a.CampaignId == campaign.Id);
        audit.Should().NotBeNull();
        audit!.Action.Should().Contain("UnderReview->Scheduled");

        await syncService.Received(1).SyncCampaignToOfferAsync(Arg.Is<Campaign>(c => c.Id == campaign.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TransitionToScheduled_AtWindowEndMinusTick_Succeeds()
    {
        var reviewStarts = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var reviewEnds = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        var campaign = SeedCampaignUnderReview(reviewStarts, reviewEnds);
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(reviewEnds.AddTicks(-1));

        var handler = new TransitionCampaignStatusCommandHandler(_context, _currentUser, clock);

        var command = new TransitionCampaignStatusCommand(campaign.Id, CampaignStatus.Scheduled);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.Scheduled);
    }

    [Fact]
    public async Task Handle_TransitionToScheduled_AtWindowEnd_IsRejected_AndStateUnaffected()
    {
        var reviewStarts = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var reviewEnds = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        var campaign = SeedCampaignUnderReview(reviewStarts, reviewEnds);
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(reviewEnds);

        var syncService = Substitute.For<ICampaignOfferSyncService>();
        var handler = new TransitionCampaignStatusCommandHandler(_context, _currentUser, clock, syncService);

        var command = new TransitionCampaignStatusCommand(campaign.Id, CampaignStatus.Scheduled);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowExpired);

        var unaffected = await _context.Campaigns.FindAsync(campaign.Id);
        unaffected!.Status.Should().Be(CampaignStatus.UnderReview);

        var audits = await _context.CampaignAuditLogs.Where(a => a.CampaignId == campaign.Id).ToListAsync();
        audits.Should().BeEmpty();

        await syncService.DidNotReceive().SyncCampaignToOfferAsync(Arg.Any<Campaign>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TransitionToScheduled_AfterWindowEnd_IsRejected_AndStateUnaffected()
    {
        var reviewStarts = new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
        var reviewEnds = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

        var campaign = SeedCampaignUnderReview(reviewStarts, reviewEnds);
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(reviewEnds.AddMinutes(5));

        var syncService = Substitute.For<ICampaignOfferSyncService>();
        var handler = new TransitionCampaignStatusCommandHandler(_context, _currentUser, clock, syncService);

        var command = new TransitionCampaignStatusCommand(campaign.Id, CampaignStatus.Scheduled);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowExpired);

        var unaffected = await _context.Campaigns.FindAsync(campaign.Id);
        unaffected!.Status.Should().Be(CampaignStatus.UnderReview);

        var audits = await _context.CampaignAuditLogs.Where(a => a.CampaignId == campaign.Id).ToListAsync();
        audits.Should().BeEmpty();

        await syncService.DidNotReceive().SyncCampaignToOfferAsync(Arg.Any<Campaign>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TransitionToScheduled_UnderReviewWithoutReviewWindow_IsRejected_AndStateUnaffected()
    {
        var campaign = SeedCampaignUnderReview(null, null);
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 15, 11, 0, 0, DateTimeKind.Utc));

        var syncService = Substitute.For<ICampaignOfferSyncService>();
        var handler = new TransitionCampaignStatusCommandHandler(_context, _currentUser, clock, syncService);

        var command = new TransitionCampaignStatusCommand(campaign.Id, CampaignStatus.Scheduled);
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowMissing);

        var unaffected = await _context.Campaigns.FindAsync(campaign.Id);
        unaffected!.Status.Should().Be(CampaignStatus.UnderReview);

        var audits = await _context.CampaignAuditLogs.Where(a => a.CampaignId == campaign.Id).ToListAsync();
        audits.Should().BeEmpty();

        await syncService.DidNotReceive().SyncCampaignToOfferAsync(Arg.Any<Campaign>(), Arg.Any<CancellationToken>());
    }

    private static Campaign SeedCampaignUnderReview(DateTime? reviewStartsAtUtc, DateTime? reviewEndsAtUtc) => new()
    {
        NameAr = "حملة قيد المراجعة",
        NameEn = "Campaign Under Review",
        Status = CampaignStatus.UnderReview,
        StartsAtUtc = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc),
        EndsAtUtc = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
        TotalDiscountRate = Percentage.FromFraction(0.20m),
        MealMateShareRate = Percentage.FromFraction(0.10m),
        RestaurantShareRate = Percentage.FromFraction(0.10m),
        ReviewStartsAtUtc = reviewStartsAtUtc,
        ReviewEndsAtUtc = reviewEndsAtUtc
    };
}
