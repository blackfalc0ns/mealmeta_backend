using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CloseCampaignJoiningCommandTests
{
    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly CloseCampaignJoiningCommandHandler _handler;

    public CloseCampaignJoiningCommandTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());

        _handler = new CloseCampaignJoiningCommandHandler(_context, _currentUser);
    }

    [Fact]
    public async Task Handle_WhenValidWithAcceptedOffers_TransitionsAndReturnsSnapshot()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.OpenForJoining,
            StartsAtUtc = DateTime.UtcNow.AddDays(5),
            EndsAtUtc = DateTime.UtcNow.AddDays(25),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);

        var rId1 = Guid.NewGuid();
        var rId2 = Guid.NewGuid();

        var enrollment1 = new CampaignRestaurantEnrollment
        {
            Campaign = campaign,
            RestaurantId = rId1,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
            DedicatedDailyCapacity = 40,
            RequestedAtUtc = DateTime.UtcNow
        };
        var enrollment2 = new CampaignRestaurantEnrollment
        {
            Campaign = campaign,
            RestaurantId = rId2,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Declined,
            DeclineReason = "No capacity",
            RequestedAtUtc = DateTime.UtcNow
        };

        _context.CampaignRestaurantEnrollments.AddRange(enrollment1, enrollment2);
        await _context.SaveChangesAsync();

        var command = new CloseCampaignJoiningCommand(campaign.Id, "Closing joining window");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.CampaignId.Should().Be(campaign.Id);
        result.Value.AcceptedOffers.Should().HaveCount(1);
        result.Value.AcceptedOffers[0].RestaurantId.Should().Be(rId1);
        result.Value.AcceptedOffers[0].DedicatedDailyCapacity.Should().Be(40);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.JoiningClosed);

        var updatedEnrollment1 = await _context.CampaignRestaurantEnrollments.FindAsync(enrollment1.Id);
        updatedEnrollment1!.ParticipationStatus.Should().Be(CampaignRestaurantParticipationStatus.Locked);

        var audit = await _context.CampaignAuditLogs
            .FirstOrDefaultAsync(a => a.CampaignId == campaign.Id && a.Action.Contains("JoiningClosed"));
        audit.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_WhenNoAcceptedOffers_ReturnsValidationFailure()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.OpenForJoining,
            StartsAtUtc = DateTime.UtcNow.AddDays(5),
            EndsAtUtc = DateTime.UtcNow.AddDays(25),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);

        var enrollment = new CampaignRestaurantEnrollment
        {
            Campaign = campaign,
            RestaurantId = Guid.NewGuid(),
            ParticipationStatus = CampaignRestaurantParticipationStatus.Invited,
            RequestedAtUtc = DateTime.UtcNow
        };
        _context.CampaignRestaurantEnrollments.Add(enrollment);
        await _context.SaveChangesAsync();

        var command = new CloseCampaignJoiningCommand(campaign.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("at least one accepted restaurant offer");
    }

    [Fact]
    public async Task Handle_WhenCampaignNotInOpenForJoining_ReturnsFailure()
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

        var command = new CloseCampaignJoiningCommand(campaign.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("Cannot close joining");
    }

    [Fact]
    public async Task Handle_WhenCampaignNotFound_ReturnsNotFound()
    {
        var command = new CloseCampaignJoiningCommand(Guid.NewGuid());

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(MealMate.Application.Common.Models.ErrorType.NotFound);
    }
}
