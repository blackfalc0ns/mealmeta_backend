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

public class OverrideCampaignProfitabilityTests
{
    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly OverrideCampaignProfitabilityCommandHandler _handler;

    public OverrideCampaignProfitabilityTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());

        _handler = new OverrideCampaignProfitabilityCommandHandler(_context, _currentUser);
    }

    [Fact]
    public async Task Handle_WithValidJustification_OverridesProfitabilitySuccessfully()
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

        var snapshot = new CampaignProfitabilitySnapshot
        {
            Campaign = campaign,
            OverallExpectedMarginPct = 0.05m,
            IsViable = true,
            RequiresOverride = true,
            DetailsJson = "[]"
        };
        _context.Set<CampaignProfitabilitySnapshot>().Add(snapshot);
        await _context.SaveChangesAsync();

        var justification = "Strategic promotional investment approved by VP of Growth for customer acquisition in Q3.";
        var command = new OverrideCampaignProfitabilityCommand(campaign.Id, justification);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var updated = await _context.Set<CampaignProfitabilitySnapshot>()
            .FirstOrDefaultAsync(s => s.CampaignId == campaign.Id);
        updated.Should().NotBeNull();
        updated!.RequiresOverride.Should().BeFalse();
        updated.OverrideReason.Should().Be(justification);
        updated.OverrideApprovedByUserId.Should().Be(_currentUser.UserId);

        var audit = await _context.CampaignAuditLogs
            .FirstOrDefaultAsync(a => a.CampaignId == campaign.Id && a.Action.Contains("ProfitabilityOverridden"));
        audit.Should().NotBeNull();
        audit!.JustificationReason.Should().Be(justification);
    }

    [Fact]
    public async Task Handle_WithShortJustificationUnder20Chars_ReturnsValidationFailure()
    {
        var campaignId = Guid.NewGuid();
        var command = new OverrideCampaignProfitabilityCommand(campaignId, "Approved by VP"); // 14 chars < 20

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Description.Should().Contain("at least 20 characters");
    }

    [Fact]
    public async Task Handle_WhenUnauthenticated_ReturnsUnauthorized()
    {
        _currentUser.IsAuthenticated.Returns(false);

        var command = new OverrideCampaignProfitabilityCommand(
            Guid.NewGuid(),
            "Strategic promotional investment approved by VP of Growth for customer acquisition."
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(MealMate.Application.Common.Models.ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Handle_WhenNoSnapshotFound_ReturnsNotFound()
    {
        var command = new OverrideCampaignProfitabilityCommand(
            Guid.NewGuid(),
            "Strategic promotional investment approved by VP of Growth for customer acquisition."
        );

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(MealMate.Application.Common.Models.ErrorType.NotFound);
    }
}
