using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
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
}
