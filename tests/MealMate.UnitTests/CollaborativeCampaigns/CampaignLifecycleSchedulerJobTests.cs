using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignLifecycleSchedulerJobTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly CampaignLifecycleSchedulerService _service;
    private readonly DateTime _now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    public CampaignLifecycleSchedulerJobTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _service = new CampaignLifecycleSchedulerService(_context, _clock);
    }

    private Campaign CreateCampaign(CampaignStatus status, DateTime startsAt, DateTime endsAt)
    {
        var campaign = new Campaign
        {
            NameAr = "حملة اختبار الجدولة",
            NameEn = "Scheduler Test Campaign",
            Status = status,
            StartsAtUtc = startsAt,
            EndsAtUtc = endsAt,
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        return campaign;
    }

    [Fact]
    public async Task ProcessLifecycleTransitionsAsync_WhenCampaignScheduledAndStartTimeReached_TransitionsToActive()
    {
        // Arrange
        var campaign = CreateCampaign(CampaignStatus.Scheduled, _now.AddMinutes(-5), _now.AddDays(7));
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.ProcessLifecycleTransitionsAsync();

        // Assert
        result.ActivatedCount.Should().Be(1);
        result.EndedCount.Should().Be(0);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.Active);

        var auditLog = await _context.CampaignAuditLogs.FirstOrDefaultAsync(l => l.CampaignId == campaign.Id);
        auditLog.Should().NotBeNull();
        auditLog!.Action.Should().Be("CampaignActivatedByScheduler");
        auditLog.ActorId.Should().Be("SYSTEM_LIFECYCLE_SCHEDULER");
    }

    [Fact]
    public async Task ProcessLifecycleTransitionsAsync_WhenCampaignScheduledButStartTimeNotReached_RemainsScheduled()
    {
        // Arrange
        var campaign = CreateCampaign(CampaignStatus.Scheduled, _now.AddHours(2), _now.AddDays(7));
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.ProcessLifecycleTransitionsAsync();

        // Assert
        result.ActivatedCount.Should().Be(0);
        result.EndedCount.Should().Be(0);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.Scheduled);
    }

    [Fact]
    public async Task ProcessLifecycleTransitionsAsync_WhenCampaignActiveAndEndTimeReached_TransitionsToEnded()
    {
        // Arrange
        var campaign = CreateCampaign(CampaignStatus.Active, _now.AddDays(-7), _now.AddMinutes(-10));
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.ProcessLifecycleTransitionsAsync();

        // Assert
        result.ActivatedCount.Should().Be(0);
        result.EndedCount.Should().Be(1);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.Ended);

        var auditLog = await _context.CampaignAuditLogs.FirstOrDefaultAsync(l => l.CampaignId == campaign.Id);
        auditLog.Should().NotBeNull();
        auditLog!.Action.Should().Be("CampaignEndedByScheduler");
        auditLog.ActorId.Should().Be("SYSTEM_LIFECYCLE_SCHEDULER");
    }

    [Fact]
    public async Task ProcessLifecycleTransitionsAsync_WhenCampaignStoppedAndEndTimeReached_TransitionsToEnded()
    {
        // Arrange
        var campaign = CreateCampaign(CampaignStatus.Stopped, _now.AddDays(-7), _now.AddMinutes(-1));
        await _context.SaveChangesAsync();

        // Act
        var result = await _service.ProcessLifecycleTransitionsAsync();

        // Assert
        result.ActivatedCount.Should().Be(0);
        result.EndedCount.Should().Be(1);

        var updated = await _context.Campaigns.FindAsync(campaign.Id);
        updated!.Status.Should().Be(CampaignStatus.Ended);

        var auditLog = await _context.CampaignAuditLogs.FirstOrDefaultAsync(l => l.CampaignId == campaign.Id);
        auditLog.Should().NotBeNull();
        auditLog!.Action.Should().Be("CampaignEndedByScheduler");
    }

    [Fact]
    public async Task ProcessLifecycleTransitionsAsync_IsIdempotentAndReconciliationSafe()
    {
        // Arrange
        var c1 = CreateCampaign(CampaignStatus.Scheduled, _now.AddMinutes(-30), _now.AddDays(5)); // Should activate
        var c2 = CreateCampaign(CampaignStatus.Active, _now.AddDays(-10), _now.AddMinutes(-5));   // Should end
        var c3 = CreateCampaign(CampaignStatus.Draft, _now.AddMinutes(-10), _now.AddDays(5));     // Untouched (Draft)
        var c4 = CreateCampaign(CampaignStatus.Active, _now.AddDays(-2), _now.AddDays(3));        // Untouched (Still active)
        await _context.SaveChangesAsync();

        // Act 1
        var result1 = await _service.ProcessLifecycleTransitionsAsync();

        // Assert 1
        result1.ActivatedCount.Should().Be(1);
        result1.EndedCount.Should().Be(1);

        (await _context.Campaigns.FindAsync(c1.Id))!.Status.Should().Be(CampaignStatus.Active);
        (await _context.Campaigns.FindAsync(c2.Id))!.Status.Should().Be(CampaignStatus.Ended);
        (await _context.Campaigns.FindAsync(c3.Id))!.Status.Should().Be(CampaignStatus.Draft);
        (await _context.Campaigns.FindAsync(c4.Id))!.Status.Should().Be(CampaignStatus.Active);

        // Act 2 (Re-run immediately)
        var result2 = await _service.ProcessLifecycleTransitionsAsync();

        // Assert 2 (Idempotent - 0 transitions)
        result2.ActivatedCount.Should().Be(0);
        result2.EndedCount.Should().Be(0);
    }
}
