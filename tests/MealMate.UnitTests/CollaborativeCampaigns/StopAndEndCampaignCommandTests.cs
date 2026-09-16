using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class StopAndEndCampaignCommandTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly StopCampaignCommandHandler _stopHandler;
    private readonly EndCampaignCommandHandler _endHandler;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public StopAndEndCampaignCommandTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _stopHandler = new StopCampaignCommandHandler(_context, _clock);
        _endHandler = new EndCampaignCommandHandler(_context, _clock);
    }

    private (Campaign Campaign, Subscription Sub) SeedCampaignWithActiveSubscription(CampaignStatus status = CampaignStatus.Active)
    {
        var campaign = new Campaign
        {
            NameAr = "حملة الصيف",
            NameEn = "Summer Campaign",
            Status = status,
            StartsAtUtc = _now.AddDays(-5),
            EndsAtUtc = _now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            MaxSubscribers = 100
        };
        _context.Campaigns.Add(campaign);

        var sub = new Subscription
        {
            CampaignId = campaign.Id,
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 14,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 15),
            PaidAmount = new Money(60m, "KWD")
        };
        _context.Subscriptions.Add(sub);
        _context.SaveChanges();

        return (campaign, sub);
    }

    [Fact]
    public async Task StopCampaign_WhenActive_TransitionsToStoppedAndRecordsAuditLog()
    {
        var (campaign, _) = SeedCampaignWithActiveSubscription();

        var command = new StopCampaignCommand(
            CampaignId: campaign.Id,
            Reason: "Operational pause requested by management",
            StoppedByUserId: "admin-1");

        var result = await _stopHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NewStatus.Should().Be(nameof(CampaignStatus.Stopped));

        var dbCampaign = await _context.Campaigns.FindAsync(campaign.Id);
        dbCampaign!.Status.Should().Be(CampaignStatus.Stopped);

        var audit = await _context.CampaignAuditLogs
            .FirstOrDefaultAsync(a => a.CampaignId == campaign.Id && a.Action == "CampaignStopped");
        audit.Should().NotBeNull();
        audit!.JustificationReason.Should().Be("Operational pause requested by management");
    }

    [Fact]
    public async Task StopCampaign_WhenAlreadyStoppedOrDraft_FailsWithInvalidTransition()
    {
        var (campaign, _) = SeedCampaignWithActiveSubscription(CampaignStatus.Draft);

        var command = new StopCampaignCommand(
            CampaignId: campaign.Id,
            Reason: "Attempt stop on draft",
            StoppedByUserId: "admin-1");

        var result = await _stopHandler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.InvalidStateTransition");
    }

    [Fact]
    public async Task CustomerProtection_ExistingActiveSubscriptionsRemainActiveWhenCampaignStopped()
    {
        var (campaign, sub) = SeedCampaignWithActiveSubscription();

        var command = new StopCampaignCommand(
            CampaignId: campaign.Id,
            Reason: "Stop new sales only",
            StoppedByUserId: "admin-1");

        await _stopHandler.Handle(command, CancellationToken.None);

        // Existing customer subscription MUST remain Active
        var dbSub = await _context.Subscriptions.FindAsync(sub.Id);
        dbSub!.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task EndCampaign_WhenActiveOrStopped_TransitionsToCompleted()
    {
        var (campaign, _) = SeedCampaignWithActiveSubscription(CampaignStatus.Stopped);

        var command = new EndCampaignCommand(
            CampaignId: campaign.Id,
            Reason: "Campaign duration completed",
            EndedByUserId: "admin-1");

        var result = await _endHandler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NewStatus.Should().Be(nameof(CampaignStatus.Ended));

        var dbCampaign = await _context.Campaigns.FindAsync(campaign.Id);
        dbCampaign!.Status.Should().Be(CampaignStatus.Ended);

        var audit = await _context.CampaignAuditLogs
            .FirstOrDefaultAsync(a => a.CampaignId == campaign.Id && a.Action == "CampaignEnded");
        audit.Should().NotBeNull();
    }
}
