using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignReservationExpirationWorkerTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly CampaignReservationExpirationService _service;
    private readonly DateTime _now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    public CampaignReservationExpirationWorkerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);

        _clock.UtcNow.Returns(_now);
        _service = new CampaignReservationExpirationService(_context, _clock);
    }

    [Fact]
    public async Task ExpireStaleReservations_WhenPendingSessionExpired_MarksExpiredAndReleasesCapacity()
    {
        var campaignId = Guid.NewGuid();
        var restId = Guid.NewGuid();
        var date = new DateOnly(2026, 10, 2);

        var cap = new CampaignRestaurantCapacity
        {
            CampaignId = campaignId,
            RestaurantId = restId,
            ServiceDate = date,
            MaxBoxes = 10,
            ReservedBoxes = 2
        };
        _context.CampaignRestaurantCapacities.Add(cap);

        var session = new CampaignCheckoutSession
        {
            CampaignId = campaignId,
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.Pending,
            ExpiresAtUtc = _now.AddMinutes(-5), // Expired 5 mins ago
            Allocations = new List<CampaignReservationAllocation>
            {
                new()
                {
                    CampaignId = campaignId,
                    RestaurantId = restId,
                    ServiceDate = date,
                    Units = 1
                }
            }
        };
        _context.CampaignCheckoutSessions.Add(session);
        await _context.SaveChangesAsync();

        var expiredCount = await _service.ExpireStaleReservationsAsync();

        expiredCount.Should().Be(1);
        session.Status.Should().Be(CampaignCheckoutSessionStatus.Expired);
        cap.ReservedBoxes.Should().Be(1); // Decremented from 2 to 1
    }

    [Fact]
    public async Task ExpireStaleReservations_WhenPendingSessionNotExpired_DoesNotExpire()
    {
        var session = new CampaignCheckoutSession
        {
            CampaignId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.Pending,
            ExpiresAtUtc = _now.AddMinutes(5) // Still valid for 5 mins
        };
        _context.CampaignCheckoutSessions.Add(session);
        await _context.SaveChangesAsync();

        var expiredCount = await _service.ExpireStaleReservationsAsync();

        expiredCount.Should().Be(0);
        session.Status.Should().Be(CampaignCheckoutSessionStatus.Pending);
    }

    [Fact]
    public async Task ExpireStaleReservations_WhenPaymentPendingWithActiveLease_DoesNotExpire()
    {
        var session = new CampaignCheckoutSession
        {
            CampaignId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.PaymentPending,
            ExpiresAtUtc = _now.AddMinutes(-2), // Session TTL expired
            ProcessingLeaseExpiresAtUtc = _now.AddMinutes(3) // But payment lease active!
        };
        _context.CampaignCheckoutSessions.Add(session);
        await _context.SaveChangesAsync();

        var expiredCount = await _service.ExpireStaleReservationsAsync();

        expiredCount.Should().Be(0);
        session.Status.Should().Be(CampaignCheckoutSessionStatus.PaymentPending);
    }

    [Fact]
    public async Task ExpireStaleReservations_WhenPaymentPendingWithExpiredLeaseAndNoSuccess_MarksExpired()
    {
        var session = new CampaignCheckoutSession
        {
            CampaignId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.PaymentPending,
            ExpiresAtUtc = _now.AddMinutes(-5),
            ProcessingLeaseExpiresAtUtc = _now.AddMinutes(-1) // Lease also expired
        };
        _context.CampaignCheckoutSessions.Add(session);
        await _context.SaveChangesAsync();

        var expiredCount = await _service.ExpireStaleReservationsAsync();

        expiredCount.Should().Be(1);
        session.Status.Should().Be(CampaignCheckoutSessionStatus.Expired);
    }

    [Fact]
    public async Task ExpireStaleReservations_WhenPaymentPendingHasSucceededTransaction_NeverExpires()
    {
        var session = new CampaignCheckoutSession
        {
            CampaignId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.PaymentPending,
            ExpiresAtUtc = _now.AddMinutes(-5),
            ProcessingLeaseExpiresAtUtc = _now.AddMinutes(-1),
            PaymentTransactions = new List<CampaignPaymentTransaction>
            {
                new()
                {
                    PaymentProvider = "MyFatoorah",
                    ProviderTransactionId = "TXN_123",
                    Status = PaymentTransactionStatus.Succeeded
                }
            }
        };
        _context.CampaignCheckoutSessions.Add(session);
        await _context.SaveChangesAsync();

        var expiredCount = await _service.ExpireStaleReservationsAsync();

        expiredCount.Should().Be(0);
        session.Status.Should().Be(CampaignCheckoutSessionStatus.PaymentPending);
    }

    [Fact]
    public async Task ExpireStaleReservations_WhenSessionIsPaidOrCommitting_NeverExpires()
    {
        var paidSession = new CampaignCheckoutSession
        {
            CampaignId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.Paid,
            ExpiresAtUtc = _now.AddMinutes(-10)
        };
        var committingSession = new CampaignCheckoutSession
        {
            CampaignId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.Committing,
            ExpiresAtUtc = _now.AddMinutes(-10)
        };
        _context.CampaignCheckoutSessions.AddRange(paidSession, committingSession);
        await _context.SaveChangesAsync();

        var expiredCount = await _service.ExpireStaleReservationsAsync();

        expiredCount.Should().Be(0);
        paidSession.Status.Should().Be(CampaignCheckoutSessionStatus.Paid);
        committingSession.Status.Should().Be(CampaignCheckoutSessionStatus.Committing);
    }
}
