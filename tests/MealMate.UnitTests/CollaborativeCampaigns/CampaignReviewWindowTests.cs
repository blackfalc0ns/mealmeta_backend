using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Application.Features.Offers.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.ValueObjects;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignReviewWindowTests
{
    private static readonly DateTime JoiningClosesAt = new(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReviewStartsAt = new(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReviewEndsAt = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CampaignStartsAt = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CampaignEndsAt = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _context;
    private readonly CreateCampaignDraftCommandHandler _createHandler;
    private readonly UpdateCampaignDraftCommandHandler _updateHandler;

    public CampaignReviewWindowTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        _createHandler = new CreateCampaignDraftCommandHandler(_context);
        _updateHandler = new UpdateCampaignDraftCommandHandler(_context);
    }

    [Fact]
    public void DeadlineIsExclusive()
    {
        var start = new DateTime(2026, 10, 10, 10, 0, 0, DateTimeKind.Utc);
        var window = new ReviewWindow(start, start.AddHours(2));
        Assert.True(window.IsOpen(start));
        Assert.False(window.IsOpen(start.AddHours(2)));
    }

    [Fact]
    public void IsOpen_BeforeWindowStarts_ReturnsFalse()
    {
        var window = new ReviewWindow(ReviewStartsAt, ReviewEndsAt);
        window.IsOpen(ReviewStartsAt.AddTicks(-1)).Should().BeFalse();
    }

    [Fact]
    public void IsOpen_WithinWindow_ReturnsTrue()
    {
        var window = new ReviewWindow(ReviewStartsAt, ReviewEndsAt);
        window.IsOpen(ReviewStartsAt).Should().BeTrue();
        window.IsOpen(ReviewEndsAt.AddTicks(-1)).Should().BeTrue();
    }

    [Fact]
    public void Approval_BeforeWindowStarts_IsRejected()
    {
        var campaign = CampaignUnderReview(ReviewStartsAt, ReviewEndsAt);

        var result = campaign.CanApproveAt(ReviewStartsAt.AddTicks(-1));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Approval_AtWindowEnd_IsRejected()
    {
        var campaign = CampaignUnderReview(ReviewStartsAt, ReviewEndsAt);

        var result = campaign.CanApproveAt(ReviewEndsAt);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Approval_WithinWindow_IsAllowed()
    {
        var campaign = CampaignUnderReview(ReviewStartsAt, ReviewEndsAt);

        campaign.CanApproveAt(ReviewStartsAt).IsSuccess.Should().BeTrue();
        campaign.CanApproveAt(ReviewEndsAt.AddTicks(-1)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Approval_ForLegacyCampaignWithoutReviewWindow_IsRejected()
    {
        var campaign = CampaignUnderReview(null, null);

        var result = campaign.CanApproveAt(DateTime.UtcNow);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Create_WithValidReviewWindow_PersistsReviewDates()
    {
        var command = ValidCreateCommand(ReviewStartsAt, ReviewEndsAt);

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ReviewStartsAtUtc.Should().Be(ReviewStartsAt);
        result.Value.ReviewEndsAtUtc.Should().Be(ReviewEndsAt);

        var entity = await _context.Campaigns.FindAsync(result.Value.Id);
        entity!.ReviewStartsAtUtc.Should().Be(ReviewStartsAt);
        entity.ReviewEndsAtUtc.Should().Be(ReviewEndsAt);
        entity.CurrentSnapshotVersion.Should().Be(1);
        entity.ApprovedSnapshotVersion.Should().BeNull();
    }

    [Fact]
    public async Task Create_WhenReviewEndsAfterCampaignStarts_IsRejected()
    {
        var command = ValidCreateCommand(ReviewStartsAt, CampaignStartsAt.AddHours(1));

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowOrderingInvalid);
    }

    [Fact]
    public async Task Create_WhenReviewStartsEqualsReviewEnds_IsRejected()
    {
        var command = ValidCreateCommand(ReviewStartsAt, ReviewStartsAt);

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowOrderingInvalid);
    }

    [Fact]
    public async Task Create_WhenReviewStartsBeforeEnrollmentCloses_IsRejected()
    {
        var command = ValidCreateCommand(ReviewStartsAt.AddHours(-1), ReviewEndsAt);

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowOrderingInvalid);
    }

    [Fact]
    public async Task Create_WhenOnlyOneReviewDateProvided_IsRejected()
    {
        var command = ValidCreateCommand(ReviewStartsAt, null);

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowIncomplete);
    }

    [Fact]
    public async Task Update_WhenReviewEndsAfterCampaignStarts_IsRejected()
    {
        var campaign = await SeedDraftAsync();

        var command = ValidUpdateCommand(campaign.Id, ReviewStartsAt, CampaignStartsAt.AddHours(1));

        var result = await _updateHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowOrderingInvalid);
    }

    [Fact]
    public async Task Update_WithValidReviewWindow_PersistsReviewDates()
    {
        var campaign = await SeedDraftAsync();

        var command = ValidUpdateCommand(campaign.Id, ReviewStartsAt, ReviewEndsAt);

        var result = await _updateHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ReviewStartsAtUtc.Should().Be(ReviewStartsAt);
        result.Value.ReviewEndsAtUtc.Should().Be(ReviewEndsAt);

        var entity = await _context.Campaigns.FindAsync(campaign.Id);
        entity!.ReviewStartsAtUtc.Should().Be(ReviewStartsAt);
        entity.ReviewEndsAtUtc.Should().Be(ReviewEndsAt);
    }

    [Fact]
    public async Task FullLifecycle_ApprovalBeforeWindowStart_IsRejected_AndStateUnaffected()
    {
        var syncService = Substitute.For<ICampaignOfferSyncService>();
        var clock = new TestDateTimeProvider();
        var transitionHandler = CreateTransitionHandler(clock, syncService);
        var listHandler = new GetCampaignsListQueryHandler(_context);
        var byIdHandler = new GetCampaignByIdQueryHandler(_context);

        var (campaignId, reviewStarts, reviewEnds) = await ExecuteFullLifecycleSetupAsync(listHandler, byIdHandler, transitionHandler);
        syncService.ClearReceivedCalls();

        // Approval before window start
        clock.UtcNow = reviewStarts.AddMinutes(-5);
        var approveCommand = new TransitionCampaignStatusCommand(campaignId, CampaignStatus.Scheduled, "Approving early");
        var approveResult = await transitionHandler.Handle(approveCommand, CancellationToken.None);

        approveResult.IsSuccess.Should().BeFalse();
        approveResult.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowNotStarted);

        var entity = await _context.Campaigns.FindAsync(campaignId);
        entity!.Status.Should().Be(CampaignStatus.UnderReview);

        var scheduledAudits = await _context.CampaignAuditLogs
            .Where(a => a.CampaignId == campaignId && a.Action.Contains("Scheduled"))
            .ToListAsync();
        scheduledAudits.Should().BeEmpty();

        await syncService.DidNotReceive().SyncCampaignToOfferAsync(Arg.Any<Campaign>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FullLifecycle_ApprovalAtWindowStart_Succeeds_AndPersistsAuditAndSyncs()
    {
        var syncService = Substitute.For<ICampaignOfferSyncService>();
        var clock = new TestDateTimeProvider();
        var transitionHandler = CreateTransitionHandler(clock, syncService);
        var listHandler = new GetCampaignsListQueryHandler(_context);
        var byIdHandler = new GetCampaignByIdQueryHandler(_context);

        var (campaignId, reviewStarts, reviewEnds) = await ExecuteFullLifecycleSetupAsync(listHandler, byIdHandler, transitionHandler);
        syncService.ClearReceivedCalls();

        // Approval at exact window start
        clock.UtcNow = reviewStarts;
        var approveCommand = new TransitionCampaignStatusCommand(campaignId, CampaignStatus.Scheduled, "Approving at window start");
        var approveResult = await transitionHandler.Handle(approveCommand, CancellationToken.None);

        approveResult.IsSuccess.Should().BeTrue();

        var entity = await _context.Campaigns.FindAsync(campaignId);
        entity!.Status.Should().Be(CampaignStatus.Scheduled);

        var audit = await _context.CampaignAuditLogs
            .FirstOrDefaultAsync(a => a.CampaignId == campaignId && a.Action.Contains("Scheduled"));
        audit.Should().NotBeNull();
        audit!.Action.Should().Contain("UnderReview->Scheduled");

        await syncService.Received(1).SyncCampaignToOfferAsync(Arg.Is<Campaign>(c => c.Id == campaignId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FullLifecycle_ApprovalAtWindowEnd_IsRejected_AndStateUnaffected()
    {
        var syncService = Substitute.For<ICampaignOfferSyncService>();
        var clock = new TestDateTimeProvider();
        var transitionHandler = CreateTransitionHandler(clock, syncService);
        var listHandler = new GetCampaignsListQueryHandler(_context);
        var byIdHandler = new GetCampaignByIdQueryHandler(_context);

        var (campaignId, reviewStarts, reviewEnds) = await ExecuteFullLifecycleSetupAsync(listHandler, byIdHandler, transitionHandler);
        syncService.ClearReceivedCalls();

        // Approval at exact window end (deadline is exclusive)
        clock.UtcNow = reviewEnds;
        var approveCommand = new TransitionCampaignStatusCommand(campaignId, CampaignStatus.Scheduled, "Approving at window end");
        var approveResult = await transitionHandler.Handle(approveCommand, CancellationToken.None);

        approveResult.IsSuccess.Should().BeFalse();
        approveResult.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowExpired);

        var entity = await _context.Campaigns.FindAsync(campaignId);
        entity!.Status.Should().Be(CampaignStatus.UnderReview);

        var scheduledAudits = await _context.CampaignAuditLogs
            .Where(a => a.CampaignId == campaignId && a.Action.Contains("Scheduled"))
            .ToListAsync();
        scheduledAudits.Should().BeEmpty();

        await syncService.DidNotReceive().SyncCampaignToOfferAsync(Arg.Any<Campaign>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FullLifecycle_ApprovalAfterWindowEnd_IsRejected_AndStateUnaffected()
    {
        var syncService = Substitute.For<ICampaignOfferSyncService>();
        var clock = new TestDateTimeProvider();
        var transitionHandler = CreateTransitionHandler(clock, syncService);
        var listHandler = new GetCampaignsListQueryHandler(_context);
        var byIdHandler = new GetCampaignByIdQueryHandler(_context);

        var (campaignId, reviewStarts, reviewEnds) = await ExecuteFullLifecycleSetupAsync(listHandler, byIdHandler, transitionHandler);
        syncService.ClearReceivedCalls();

        // Approval after window end
        clock.UtcNow = reviewEnds.AddMinutes(30);
        var approveCommand = new TransitionCampaignStatusCommand(campaignId, CampaignStatus.Scheduled, "Approving after window end");
        var approveResult = await transitionHandler.Handle(approveCommand, CancellationToken.None);

        approveResult.IsSuccess.Should().BeFalse();
        approveResult.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowExpired);

        var entity = await _context.Campaigns.FindAsync(campaignId);
        entity!.Status.Should().Be(CampaignStatus.UnderReview);

        var scheduledAudits = await _context.CampaignAuditLogs
            .Where(a => a.CampaignId == campaignId && a.Action.Contains("Scheduled"))
            .ToListAsync();
        scheduledAudits.Should().BeEmpty();

        await syncService.DidNotReceive().SyncCampaignToOfferAsync(Arg.Any<Campaign>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_WhenReviewStartsBeforeEnrollmentCloses_IsRejected()
    {
        var campaign = await SeedDraftAsync();

        var command = ValidUpdateCommand(campaign.Id, ReviewStartsAt.AddHours(-1), ReviewEndsAt);

        var result = await _updateHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowOrderingInvalid);
    }

    [Fact]
    public async Task Create_WhenReviewStartsAfterReviewEnds_IsRejected()
    {
        var command = ValidCreateCommand(ReviewEndsAt.AddHours(1), ReviewEndsAt);

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowOrderingInvalid);
    }

    [Fact]
    public async Task Update_WhenReviewStartsEqualsOrAfterReviewEnds_IsRejected()
    {
        var campaign = await SeedDraftAsync();

        var command = ValidUpdateCommand(campaign.Id, ReviewStartsAt, ReviewStartsAt);

        var result = await _updateHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowOrderingInvalid);
    }

    [Fact]
    public async Task Create_WhenOnlyEndReviewDateProvided_IsRejected()
    {
        var command = ValidCreateCommand(null, ReviewEndsAt);

        var result = await _createHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowIncomplete);
    }

    [Fact]
    public async Task Update_WhenOnlyOneReviewDateProvided_IsRejected()
    {
        var campaign = await SeedDraftAsync();

        var command = ValidUpdateCommand(campaign.Id, ReviewStartsAt, null);

        var result = await _updateHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowIncomplete);
    }

    [Fact]
    public async Task DraftWithoutReviewWindow_CanBeSaved_ButCannotBeOpenedForJoiningOrApproved()
    {
        var syncService = Substitute.For<ICampaignOfferSyncService>();
        var clock = new TestDateTimeProvider();
        var transitionHandler = CreateTransitionHandler(clock, syncService);

        var createCommand = ValidCreateCommand(null, null);
        var createResult = await _createHandler.Handle(createCommand, CancellationToken.None);

        createResult.IsSuccess.Should().BeTrue();
        createResult.Value.ReviewStartsAtUtc.Should().BeNull();
        createResult.Value.ReviewEndsAtUtc.Should().BeNull();

        var campaignId = createResult.Value.Id;

        // Attempting to open joining without review window fails
        var openCommand = new TransitionCampaignStatusCommand(campaignId, CampaignStatus.OpenForJoining);
        var openResult = await transitionHandler.Handle(openCommand, CancellationToken.None);

        openResult.IsSuccess.Should().BeFalse();
        openResult.Error.Code.Should().Be(MessageCodes.Campaign.ReviewWindowMissing);

        var entity = await _context.Campaigns.FindAsync(campaignId);
        entity!.Status.Should().Be(CampaignStatus.Draft);

        var audits = await _context.CampaignAuditLogs.Where(a => a.CampaignId == campaignId).ToListAsync();
        audits.Should().BeEmpty();
        await syncService.DidNotReceive().SyncCampaignToOfferAsync(Arg.Any<Campaign>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DraftWithReviewWindow_WithoutEnrollmentDates_CanBeSaved_ButCannotBeOpenedForJoining()
    {
        var syncService = Substitute.For<ICampaignOfferSyncService>();
        var clock = new TestDateTimeProvider();
        var transitionHandler = CreateTransitionHandler(clock, syncService);

        // Draft with review window, but missing enrollment dates
        var createCommand = new CreateCampaignDraftCommand(
            NameAr: "حملة مسودة بدون مواعيد انضمام",
            NameEn: "Draft Without Enrollment Dates",
            StartsAtUtc: CampaignStartsAt,
            EndsAtUtc: CampaignEndsAt,
            TotalDiscountRate: 0.20m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m,
            FundingMode: CampaignFundingMode.Shared,
            MaxSubscribers: 100,
            EnrollmentOpensAtUtc: null,
            EnrollmentClosesAtUtc: null,
            ProgramIds: null,
            BundleIds: null,
            ReviewStartsAtUtc: ReviewStartsAt,
            ReviewEndsAtUtc: ReviewEndsAt);

        var createResult = await _createHandler.Handle(createCommand, CancellationToken.None);
        createResult.IsSuccess.Should().BeTrue();

        var campaignId = createResult.Value.Id;

        // Opening joining requires complete enrollment dates
        var openCommand = new TransitionCampaignStatusCommand(campaignId, CampaignStatus.OpenForJoining);
        var openResult = await transitionHandler.Handle(openCommand, CancellationToken.None);

        openResult.IsSuccess.Should().BeFalse();
        openResult.Error.Code.Should().Be(MessageCodes.Campaign.EnrollmentDatesRequired);

        var entity = await _context.Campaigns.FindAsync(campaignId);
        entity!.Status.Should().Be(CampaignStatus.Draft);

        var audits = await _context.CampaignAuditLogs.Where(a => a.CampaignId == campaignId).ToListAsync();
        audits.Should().BeEmpty();
        await syncService.DidNotReceive().SyncCampaignToOfferAsync(Arg.Any<Campaign>(), Arg.Any<CancellationToken>());
    }

    private async Task<(Guid CampaignId, DateTime ReviewStarts, DateTime ReviewEnds)> ExecuteFullLifecycleSetupAsync(
        GetCampaignsListQueryHandler listHandler,
        GetCampaignByIdQueryHandler byIdHandler,
        TransitionCampaignStatusCommandHandler transitionHandler)
    {
        var enrollmentOpens = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);
        var enrollmentCloses = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        var initialReviewStarts = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
        var initialReviewEnds = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var campaignStarts = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        var campaignEnds = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

        // 1. Create draft from CreateCampaignDraftCommandHandler
        var createCommand = new CreateCampaignDraftCommand(
            NameAr: "حملة المسار الكامل",
            NameEn: "Full Lifecycle Campaign",
            StartsAtUtc: campaignStarts,
            EndsAtUtc: campaignEnds,
            TotalDiscountRate: 0.20m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m,
            EnrollmentOpensAtUtc: enrollmentOpens,
            EnrollmentClosesAtUtc: enrollmentCloses,
            ReviewStartsAtUtc: initialReviewStarts,
            ReviewEndsAtUtc: initialReviewEnds);

        var createResult = await _createHandler.Handle(createCommand, CancellationToken.None);
        createResult.IsSuccess.Should().BeTrue();
        var campaignId = createResult.Value.Id;
        createResult.Value.ReviewStartsAtUtc.Should().Be(initialReviewStarts);
        createResult.Value.ReviewEndsAtUtc.Should().Be(initialReviewEnds);

        // 2. Read via list and byId
        var listResult = await listHandler.Handle(new GetCampaignsListQuery(), CancellationToken.None);
        listResult.IsSuccess.Should().BeTrue();
        var listItem = listResult.Value!.Items.First(x => x.Id == campaignId);
        listItem.ReviewStartsAtUtc.Should().Be(initialReviewStarts);
        listItem.ReviewEndsAtUtc.Should().Be(initialReviewEnds);

        var detailsResult = await byIdHandler.Handle(new GetCampaignByIdQuery(campaignId), CancellationToken.None);
        detailsResult.IsSuccess.Should().BeTrue();
        detailsResult.Value!.ReviewStartsAtUtc.Should().Be(initialReviewStarts);
        detailsResult.Value.ReviewEndsAtUtc.Should().Be(initialReviewEnds);

        // 3. Update review dates via UpdateCampaignDraftCommandHandler
        var updatedReviewStarts = initialReviewStarts.AddHours(2);
        var updatedReviewEnds = initialReviewEnds.AddHours(2);
        var updateCommand = new UpdateCampaignDraftCommand(
            CampaignId: campaignId,
            NameAr: "حملة المسار الكامل المحدثة",
            NameEn: "Full Lifecycle Campaign Updated",
            StartsAtUtc: campaignStarts,
            EndsAtUtc: campaignEnds,
            TotalDiscountRate: 0.20m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m,
            EnrollmentOpensAtUtc: enrollmentOpens,
            EnrollmentClosesAtUtc: enrollmentCloses,
            ReviewStartsAtUtc: updatedReviewStarts,
            ReviewEndsAtUtc: updatedReviewEnds);

        var updateResult = await _updateHandler.Handle(updateCommand, CancellationToken.None);
        updateResult.IsSuccess.Should().BeTrue();
        updateResult.Value.ReviewStartsAtUtc.Should().Be(updatedReviewStarts);
        updateResult.Value.ReviewEndsAtUtc.Should().Be(updatedReviewEnds);

        // Read again to verify persistence of updated dates
        var updatedDetails = await byIdHandler.Handle(new GetCampaignByIdQuery(campaignId), CancellationToken.None);
        updatedDetails.IsSuccess.Should().BeTrue();
        updatedDetails.Value!.ReviewStartsAtUtc.Should().Be(updatedReviewStarts);
        updatedDetails.Value.ReviewEndsAtUtc.Should().Be(updatedReviewEnds);

        // 4. Reach UnderReview via legitimate path
        var openResult = await transitionHandler.Handle(
            new TransitionCampaignStatusCommand(campaignId, CampaignStatus.OpenForJoining, "Opening joining"),
            CancellationToken.None);
        openResult.IsSuccess.Should().BeTrue();

        var closeResult = await transitionHandler.Handle(
            new TransitionCampaignStatusCommand(campaignId, CampaignStatus.JoiningClosed, "Closing joining"),
            CancellationToken.None);
        closeResult.IsSuccess.Should().BeTrue();

        var underReviewResult = await transitionHandler.Handle(
            new TransitionCampaignStatusCommand(campaignId, CampaignStatus.UnderReview, "Submitting for admin review"),
            CancellationToken.None);
        underReviewResult.IsSuccess.Should().BeTrue();

        return (campaignId, updatedReviewStarts, updatedReviewEnds);
    }

    private TransitionCampaignStatusCommandHandler CreateTransitionHandler(
        IDateTimeProvider clock,
        ICampaignOfferSyncService? syncService = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());
        return new TransitionCampaignStatusCommandHandler(_context, currentUser, clock, syncService);
    }

    private static CreateCampaignDraftCommand ValidCreateCommand(
        DateTime? reviewStartsAtUtc,
        DateTime? reviewEndsAtUtc) => new(
            NameAr: "حملة",
            NameEn: "Campaign",
            StartsAtUtc: CampaignStartsAt,
            EndsAtUtc: CampaignEndsAt,
            TotalDiscountRate: 0.20m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m,
            FundingMode: CampaignFundingMode.Shared,
            MaxSubscribers: 100,
            EnrollmentOpensAtUtc: JoiningClosesAt.AddDays(-5),
            EnrollmentClosesAtUtc: JoiningClosesAt,
            ProgramIds: null,
            BundleIds: null,
            ReviewStartsAtUtc: reviewStartsAtUtc,
            ReviewEndsAtUtc: reviewEndsAtUtc);

    private static UpdateCampaignDraftCommand ValidUpdateCommand(
        Guid campaignId,
        DateTime? reviewStartsAtUtc,
        DateTime? reviewEndsAtUtc) => new(
            CampaignId: campaignId,
            NameAr: "حملة",
            NameEn: "Campaign",
            StartsAtUtc: CampaignStartsAt,
            EndsAtUtc: CampaignEndsAt,
            TotalDiscountRate: 0.20m,
            MealMateShareRate: 0.10m,
            RestaurantShareRate: 0.10m,
            FundingMode: CampaignFundingMode.Shared,
            MaxSubscribers: 100,
            EnrollmentOpensAtUtc: JoiningClosesAt.AddDays(-5),
            EnrollmentClosesAtUtc: JoiningClosesAt,
            ProgramIds: null,
            BundleIds: null,
            ReviewStartsAtUtc: reviewStartsAtUtc,
            ReviewEndsAtUtc: reviewEndsAtUtc);

    private async Task<Campaign> SeedDraftAsync()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.Draft,
            StartsAtUtc = CampaignStartsAt,
            EndsAtUtc = CampaignEndsAt,
            EnrollmentClosesAtUtc = JoiningClosesAt,
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();
        return campaign;
    }

    private static Campaign CampaignUnderReview(DateTime? reviewStartsAtUtc, DateTime? reviewEndsAtUtc) => new()
    {
        NameAr = "حملة",
        NameEn = "Campaign",
        Status = CampaignStatus.UnderReview,
        StartsAtUtc = CampaignStartsAt,
        EndsAtUtc = CampaignEndsAt,
        EnrollmentClosesAtUtc = JoiningClosesAt,
        TotalDiscountRate = Percentage.FromFraction(0.20m),
        MealMateShareRate = Percentage.FromFraction(0.10m),
        RestaurantShareRate = Percentage.FromFraction(0.10m),
        ReviewStartsAtUtc = reviewStartsAtUtc,
        ReviewEndsAtUtc = reviewEndsAtUtc
    };

    private sealed class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
        public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);
    }
}
