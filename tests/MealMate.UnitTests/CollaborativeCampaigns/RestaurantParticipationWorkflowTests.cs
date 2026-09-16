using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class RestaurantParticipationWorkflowTests
{
    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly InviteRestaurantToCampaignCommandHandler _inviteHandler;
    private readonly RespondToCampaignInvitationCommandHandler _respondHandler;

    public RestaurantParticipationWorkflowTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());

        _inviteHandler = new InviteRestaurantToCampaignCommandHandler(_context, _currentUser);
        _respondHandler = new RespondToCampaignInvitationCommandHandler(_context, _currentUser);
    }

    [Fact]
    public async Task Invite_WhenCampaignOpenForJoining_CreatesEnrollmentSuccessfully()
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
        await _context.SaveChangesAsync();

        var restaurantId = Guid.NewGuid();
        var command = new InviteRestaurantToCampaignCommand(campaign.Id, restaurantId);

        var result = await _inviteHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.ParticipationStatus.Should().Be(CampaignRestaurantParticipationStatus.Invited);
        result.Value.RestaurantId.Should().Be(restaurantId);

        var enrollment = await _context.CampaignRestaurantEnrollments.FindAsync(result.Value.Id);
        enrollment.Should().NotBeNull();
        enrollment!.ParticipationStatus.Should().Be(CampaignRestaurantParticipationStatus.Invited);
    }

    [Fact]
    public async Task Invite_WhenCampaignNotInDraftOrOpenForJoining_ReturnsFailure()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(25),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        var command = new InviteRestaurantToCampaignCommand(campaign.Id, Guid.NewGuid());

        var result = await _inviteHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("Cannot invite restaurants");
    }

    [Fact]
    public async Task Invite_WhenAlreadyInvited_ReturnsFailure()
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

        var restaurantId = Guid.NewGuid();
        var existing = new CampaignRestaurantEnrollment
        {
            Campaign = campaign,
            RestaurantId = restaurantId,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Invited,
            RequestedAtUtc = DateTime.UtcNow
        };
        _context.CampaignRestaurantEnrollments.Add(existing);
        await _context.SaveChangesAsync();

        var command = new InviteRestaurantToCampaignCommand(campaign.Id, restaurantId);

        var result = await _inviteHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("already invited");
    }

    [Fact]
    public async Task Respond_WhenAccepted_AndCampaignOpenForJoining_UpdatesToAccepted()
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

        var command = new RespondToCampaignInvitationCommand(
            EnrollmentId: enrollment.Id,
            Accept: true,
            DedicatedDailyCapacity: 50
        );

        var result = await _respondHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParticipationStatus.Should().Be(CampaignRestaurantParticipationStatus.Accepted);
        result.Value.DedicatedDailyCapacity.Should().Be(50);

        var updated = await _context.CampaignRestaurantEnrollments.FindAsync(enrollment.Id);
        updated!.ParticipationStatus.Should().Be(CampaignRestaurantParticipationStatus.Accepted);
        updated.DedicatedDailyCapacity.Should().Be(50);
    }

    [Fact]
    public async Task Respond_WhenAcceptedWithoutCapacity_ReturnsValidationFailure()
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

        var command = new RespondToCampaignInvitationCommand(
            EnrollmentId: enrollment.Id,
            Accept: true,
            DedicatedDailyCapacity: null
        );

        var result = await _respondHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("DedicatedDailyCapacity is required");
    }

    [Fact]
    public async Task Respond_WhenDeclinedWithReason_UpdatesToDeclined()
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

        var command = new RespondToCampaignInvitationCommand(
            EnrollmentId: enrollment.Id,
            Accept: false,
            DeclineReason: "Kitchen undergoing scheduled renovation"
        );

        var result = await _respondHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParticipationStatus.Should().Be(CampaignRestaurantParticipationStatus.Declined);
        result.Value.DeclineReason.Should().Be("Kitchen undergoing scheduled renovation");
    }

    [Fact]
    public async Task Respond_WhenDeclinedWithoutReason_ReturnsValidationFailure()
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

        var command = new RespondToCampaignInvitationCommand(
            EnrollmentId: enrollment.Id,
            Accept: false,
            DeclineReason: " "
        );

        var result = await _respondHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("DeclineReason is required");
    }

    [Fact]
    public async Task Respond_WhenJoiningClosed_ReturnsFailure()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.JoiningClosed,
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

        var command = new RespondToCampaignInvitationCommand(
            EnrollmentId: enrollment.Id,
            Accept: true,
            DedicatedDailyCapacity: 20
        );

        var result = await _respondHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("joining is closed");
    }
}
