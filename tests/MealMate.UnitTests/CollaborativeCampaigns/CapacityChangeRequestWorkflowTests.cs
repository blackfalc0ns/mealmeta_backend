using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CapacityChangeRequestWorkflowTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly SubmitCapacityChangeRequestCommandHandler _submitHandler;
    private readonly ReviewCapacityChangeRequestCommandHandler _reviewHandler;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public CapacityChangeRequestWorkflowTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _submitHandler = new SubmitCapacityChangeRequestCommandHandler(_context, _clock);
        _reviewHandler = new ReviewCapacityChangeRequestCommandHandler(_context, _clock);
    }

    private (Campaign Campaign, Guid RestA, DateOnly Date1, CampaignRestaurantCapacity Capacity) SeedCampaignCapacity(
        int maxBoxes = 10, int reservedBoxes = 3)
    {
        var campaign = new Campaign
        {
            NameAr = "حملة السعة",
            NameEn = "Capacity Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-2),
            EndsAtUtc = _now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            MaxSubscribers = 50
        };
        _context.Campaigns.Add(campaign);

        var restA = Guid.NewGuid();
        var date1 = new DateOnly(2026, 10, 8);
        var capacity = new CampaignRestaurantCapacity
        {
            CampaignId = campaign.Id,
            RestaurantId = restA,
            ServiceDate = date1,
            MaxBoxes = maxBoxes,
            ReservedBoxes = reservedBoxes
        };
        _context.CampaignRestaurantCapacities.Add(capacity);
        _context.SaveChanges();

        return (campaign, restA, date1, capacity);
    }

    [Fact]
    public async Task SubmitRequest_CreatesReviewInUnderReviewStatus()
    {
        var (campaign, restA, date1, _) = SeedCampaignCapacity();

        var command = new SubmitCapacityChangeRequestCommand(
            CampaignId: campaign.Id,
            RestaurantId: restA,
            ServiceDate: date1,
            NewMaxBoxes: 15,
            Reason: "Extra kitchen staff available");

        var result = await _submitHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(nameof(VerificationStatus.UnderReview));
        result.Value.RequestedMaxBoxes.Should().Be(15);

        var review = await _context.CampaignCapacityReviews.FindAsync(result.Value.ReviewId);
        review.Should().NotBeNull();
        review!.Decision.Should().Be(VerificationStatus.UnderReview);
    }

    [Fact]
    public async Task ApproveRequest_WhenValid_UpdatesMaxBoxesAndMarksApproved()
    {
        var (campaign, restA, date1, capacity) = SeedCampaignCapacity(maxBoxes: 10, reservedBoxes: 3);

        var submitCommand = new SubmitCapacityChangeRequestCommand(
            CampaignId: campaign.Id,
            RestaurantId: restA,
            ServiceDate: date1,
            NewMaxBoxes: 15,
            Reason: "Scale up");
        var submitResult = await _submitHandler.Handle(submitCommand, CancellationToken.None);

        var reviewCommand = new ReviewCapacityChangeRequestCommand(
            ReviewId: submitResult.Value.ReviewId,
            Approve: true,
            AdminNotes: "Approved by Operations");

        var reviewResult = await _reviewHandler.Handle(reviewCommand, CancellationToken.None);

        reviewResult.IsSuccess.Should().BeTrue();
        reviewResult.Value.Status.Should().Be(nameof(VerificationStatus.Approved));

        var updatedCap = await _context.CampaignRestaurantCapacities.FindAsync(capacity.Id);
        updatedCap!.MaxBoxes.Should().Be(15);
    }

    [Fact]
    public async Task ApproveRequest_WhenNewCapacityBelowReserved_FailsWithConflict()
    {
        var (campaign, restA, date1, capacity) = SeedCampaignCapacity(maxBoxes: 10, reservedBoxes: 5);

        // Requesting 4 boxes when 5 are reserved
        var submitCommand = new SubmitCapacityChangeRequestCommand(
            CampaignId: campaign.Id,
            RestaurantId: restA,
            ServiceDate: date1,
            NewMaxBoxes: 4,
            Reason: "Reduce prep");
        var submitResult = await _submitHandler.Handle(submitCommand, CancellationToken.None);

        var reviewCommand = new ReviewCapacityChangeRequestCommand(
            ReviewId: submitResult.Value.ReviewId,
            Approve: true,
            AdminNotes: "Try approving reduction");

        var reviewResult = await _reviewHandler.Handle(reviewCommand, CancellationToken.None);

        reviewResult.IsFailure.Should().BeTrue();
        reviewResult.Error.Code.Should().Be("Campaign.CapacityReductionBreach");

        var unchangedCap = await _context.CampaignRestaurantCapacities.FindAsync(capacity.Id);
        unchangedCap!.MaxBoxes.Should().Be(10); // Unchanged!
    }

    [Fact]
    public async Task RejectRequest_LeavesMaxBoxesUnchangedAndMarksRejected()
    {
        var (campaign, restA, date1, capacity) = SeedCampaignCapacity(maxBoxes: 10, reservedBoxes: 2);

        var submitCommand = new SubmitCapacityChangeRequestCommand(
            CampaignId: campaign.Id,
            RestaurantId: restA,
            ServiceDate: date1,
            NewMaxBoxes: 20,
            Reason: "Excess request");
        var submitResult = await _submitHandler.Handle(submitCommand, CancellationToken.None);

        var reviewCommand = new ReviewCapacityChangeRequestCommand(
            ReviewId: submitResult.Value.ReviewId,
            Approve: false,
            AdminNotes: "Denied due to driver shortage");

        var reviewResult = await _reviewHandler.Handle(reviewCommand, CancellationToken.None);

        reviewResult.IsSuccess.Should().BeTrue();
        reviewResult.Value.Status.Should().Be(nameof(VerificationStatus.Rejected));

        var unchangedCap = await _context.CampaignRestaurantCapacities.FindAsync(capacity.Id);
        unchangedCap!.MaxBoxes.Should().Be(10);
    }
}
