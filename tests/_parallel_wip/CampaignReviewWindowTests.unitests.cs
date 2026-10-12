using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
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
}
