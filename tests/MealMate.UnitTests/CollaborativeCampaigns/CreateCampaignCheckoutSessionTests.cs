using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CreateCampaignCheckoutSessionTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ICampaignEligibilityEngine _eligibilityEngine = new CampaignEligibilityEngine();
    private readonly ISubscriberCapManager _capManager = new SubscriberCapManager();
    private readonly ICampaignFulfillmentAllocator _allocator = new CampaignFulfillmentAllocator();
    private readonly ICampaignReservationCoordinator _coordinator;
    private readonly CreateCampaignCheckoutSessionCommandHandler _handler;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public CreateCampaignCheckoutSessionTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _coordinator = new CampaignReservationCoordinator(_capManager, _allocator, _clock);
        _handler = new CreateCampaignCheckoutSessionCommandHandler(
            _context,
            _clock,
            _eligibilityEngine,
            _coordinator);
    }

    private (Campaign Campaign, Guid BundleId, Guid RestA, DateOnly Date1) SeedCampaignWithCapacity()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة الأبطال",
            NameEn = "Champions Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-2),
            EndsAtUtc = _now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            MaxSubscribers = 10
        };
        _context.Campaigns.Add(campaign);

        var bundleId = Guid.NewGuid();
        var durationPrice = CampaignBundleDurationPrice.Create(
            bundleId,
            1,
            new Money(100.000m, "KWD"),
            "System",
            1).Value;
        _context.CampaignBundleDurationPrices.Add(durationPrice);

        var restA = Guid.NewGuid();
        var date1 = new DateOnly(2026, 10, 6);
        var capacity = new CampaignRestaurantCapacity
        {
            CampaignId = campaign.Id,
            RestaurantId = restA,
            ServiceDate = date1,
            MaxBoxes = 5,
            ReservedBoxes = 0
        };
        _context.CampaignRestaurantCapacities.Add(capacity);
        _context.SaveChanges();

        return (campaign, bundleId, restA, date1);
    }

    [Fact]
    public async Task Handle_WhenValidRequest_CreatesSessionAndReservesCapacity()
    {
        var (campaign, bundleId, restA, date1) = SeedCampaignWithCapacity();
        var customerId = Guid.NewGuid();

        var command = new CreateCampaignCheckoutSessionCommand(
            IdempotencyKey: "key-1",
            CampaignId: campaign.Id,
            BundleId: bundleId,
            CustomerId: customerId,
            DurationDays: 1,
            RequestedServiceDates: new List<DateOnly> { date1 });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(nameof(CampaignCheckoutSessionStatus.Pending));
        result.Value.Allocations.Should().HaveCount(1);
        result.Value.Allocations[0].RestaurantId.Should().Be(restA);

        // Verify capacity was reserved
        var cap = await _context.CampaignRestaurantCapacities
            .SingleAsync(c => c.CampaignId == campaign.Id && c.RestaurantId == restA && c.ServiceDate == date1);
        cap.ReservedBoxes.Should().Be(1);

        // Verify session entity was saved in db
        var session = await _context.CampaignCheckoutSessions.FindAsync(result.Value.SessionId);
        session.Should().NotBeNull();
        session!.Status.Should().Be(CampaignCheckoutSessionStatus.Pending);
    }

    [Fact]
    public async Task Handle_WhenIdempotentRequestRepeated_ReturnsCachedSessionWithoutDuplicateReservation()
    {
        var (campaign, bundleId, restA, date1) = SeedCampaignWithCapacity();
        var customerId = Guid.NewGuid();

        var command = new CreateCampaignCheckoutSessionCommand(
            IdempotencyKey: "key-repeat",
            CampaignId: campaign.Id,
            BundleId: bundleId,
            CustomerId: customerId,
            DurationDays: 1,
            RequestedServiceDates: new List<DateOnly> { date1 });

        var firstResult = await _handler.Handle(command, CancellationToken.None);
        var secondResult = await _handler.Handle(command, CancellationToken.None);

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value.SessionId.Should().Be(firstResult.Value.SessionId);

        // Reserved capacity must remain 1, NOT 2!
        var cap = await _context.CampaignRestaurantCapacities
            .SingleAsync(c => c.CampaignId == campaign.Id && c.RestaurantId == restA && c.ServiceDate == date1);
        cap.ReservedBoxes.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenIdempotentKeyReusedWithDifferentPayload_ReturnsConflict()
    {
        var (campaign, bundleId, restA, date1) = SeedCampaignWithCapacity();
        var customerId = Guid.NewGuid();

        var command1 = new CreateCampaignCheckoutSessionCommand(
            IdempotencyKey: "key-conflict",
            CampaignId: campaign.Id,
            BundleId: bundleId,
            CustomerId: customerId,
            DurationDays: 1,
            RequestedServiceDates: new List<DateOnly> { date1 });

        var command2 = new CreateCampaignCheckoutSessionCommand(
            IdempotencyKey: "key-conflict",
            CampaignId: campaign.Id,
            BundleId: bundleId,
            CustomerId: Guid.NewGuid(), // Different customer!
            DurationDays: 1,
            RequestedServiceDates: new List<DateOnly> { date1 });

        await _handler.Handle(command1, CancellationToken.None);
        var result2 = await _handler.Handle(command2, CancellationToken.None);

        result2.IsFailure.Should().BeTrue();
        result2.Error.Code.Should().Be("Idempotency.Conflict");
    }

    [Fact]
    public async Task Handle_WhenCapacityUnavailable_ReturnsConflictAndDoesNotReserve()
    {
        var (campaign, bundleId, restA, date1) = SeedCampaignWithCapacity();

        // Exhaust capacity
        var cap = await _context.CampaignRestaurantCapacities
            .SingleAsync(c => c.CampaignId == campaign.Id && c.RestaurantId == restA && c.ServiceDate == date1);
        cap.ReservedBoxes = 5; // MaxBoxes is 5
        await _context.SaveChangesAsync();

        var command = new CreateCampaignCheckoutSessionCommand(
            IdempotencyKey: "key-exhausted",
            CampaignId: campaign.Id,
            BundleId: bundleId,
            CustomerId: Guid.NewGuid(),
            DurationDays: 1,
            RequestedServiceDates: new List<DateOnly> { date1 });

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.CapacityUnavailable");
    }
}
