using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class ReduceCampaignCapacityCommandTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ReduceCampaignCapacityCommandHandler _handler;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public ReduceCampaignCapacityCommandTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _handler = new ReduceCampaignCapacityCommandHandler(_context, _clock);
    }

    private (Campaign Campaign, Guid RestA, DateOnly Date1, CampaignRestaurantCapacity Capacity) SeedCapacity(
        int maxBoxes = 10, int reservedBoxes = 4)
    {
        var campaign = new Campaign
        {
            NameAr = "حملة تقليص السعة",
            NameEn = "Reduce Capacity Campaign",
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
        var date1 = new DateOnly(2026, 10, 9);
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
    public async Task Handle_WhenNewCapacityGreaterThanOrEqualToReserved_UpdatesMaxBoxesAndWritesAuditLog()
    {
        var (campaign, restA, date1, capacity) = SeedCapacity(maxBoxes: 10, reservedBoxes: 4);

        var command = new ReduceCampaignCapacityCommand(
            CampaignId: campaign.Id,
            RestaurantId: restA,
            ServiceDate: date1,
            NewMaxBoxes: 6,
            Reason: "Staff reduced");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.MaxBoxes.Should().Be(6);
        result.Value.ReservedBoxes.Should().Be(4);

        var updated = await _context.CampaignRestaurantCapacities.FindAsync(capacity.Id);
        updated!.MaxBoxes.Should().Be(6);

        // Verify Audit Log
        var audit = await _context.CampaignAuditLogs
            .FirstOrDefaultAsync(a => a.CampaignId == campaign.Id && a.Action == "CapacityReduced");
        audit.Should().NotBeNull();
        audit!.JustificationReason.Should().Be("Staff reduced");
    }

    [Fact]
    public async Task Handle_WhenNewCapacityLessThanReserved_FailsWithCapacityReductionBreach()
    {
        var (campaign, restA, date1, capacity) = SeedCapacity(maxBoxes: 10, reservedBoxes: 4);

        // Try reducing to 3 when 4 are already reserved!
        var command = new ReduceCampaignCapacityCommand(
            CampaignId: campaign.Id,
            RestaurantId: restA,
            ServiceDate: date1,
            NewMaxBoxes: 3,
            Reason: "Emergency downscale");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.CapacityReductionBreach");

        var unchanged = await _context.CampaignRestaurantCapacities.FindAsync(capacity.Id);
        unchanged!.MaxBoxes.Should().Be(10); // Protected!
    }

    [Fact]
    public async Task Handle_WhenNewCapacityNegative_FailsWithValidationError()
    {
        var (campaign, restA, date1, _) = SeedCapacity();

        var command = new ReduceCampaignCapacityCommand(
            CampaignId: campaign.Id,
            RestaurantId: restA,
            ServiceDate: date1,
            NewMaxBoxes: -1,
            Reason: "Negative number");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.InvalidCapacity");
    }

    [Fact]
    public async Task Handle_WhenCapacityRecordNotFound_ReturnsNotFound()
    {
        var command = new ReduceCampaignCapacityCommand(
            CampaignId: Guid.NewGuid(),
            RestaurantId: Guid.NewGuid(),
            ServiceDate: new DateOnly(2026, 10, 9),
            NewMaxBoxes: 5,
            Reason: "Unknown");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.CapacityNotFound");
    }
}
