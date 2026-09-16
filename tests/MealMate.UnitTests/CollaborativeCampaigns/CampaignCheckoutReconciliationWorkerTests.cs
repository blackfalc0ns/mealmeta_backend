using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignCheckoutReconciliationWorkerTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly CampaignCheckoutReconciliationService _service;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public CampaignCheckoutReconciliationWorkerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _service = new CampaignCheckoutReconciliationService(_context, _clock, _mediator);
    }

    private (Campaign Campaign, CampaignCheckoutSession Session, Guid RestA, DateOnly Date1) SeedSession(
        CampaignCheckoutSessionStatus status,
        DateTime expiresAt,
        DateTime? leaseExpiresAt = null)
    {
        var campaign = new Campaign
        {
            NameAr = "حملة التسوية",
            NameEn = "Reconciliation Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-2),
            EndsAtUtc = _now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            MaxSubscribers = 100
        };
        _context.Campaigns.Add(campaign);

        var restA = Guid.NewGuid();
        var date1 = new DateOnly(2026, 10, 8);
        var capacity = new CampaignRestaurantCapacity
        {
            CampaignId = campaign.Id,
            RestaurantId = restA,
            ServiceDate = date1,
            MaxBoxes = 10,
            ReservedBoxes = 1
        };
        _context.CampaignRestaurantCapacities.Add(capacity);

        var session = new CampaignCheckoutSession
        {
            CampaignId = campaign.Id,
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = status,
            ExpiresAtUtc = expiresAt,
            ProcessingLeaseExpiresAtUtc = leaseExpiresAt
        };
        session.Allocations.Add(new CampaignReservationAllocation
        {
            CheckoutSessionId = session.Id,
            CampaignId = campaign.Id,
            BundleId = session.BundleId,
            RestaurantId = restA,
            ServiceDate = date1,
            Units = 1
        });
        _context.CampaignCheckoutSessions.Add(session);
        _context.SaveChanges();

        return (campaign, session, restA, date1);
    }

    [Fact]
    public async Task Reconcile_WhenSessionStuckInCommittingAndSubscriptionExists_MarksSessionCompleted()
    {
        var (campaign, session, _, _) = SeedSession(
            CampaignCheckoutSessionStatus.Committing,
            expiresAt: _now.AddMinutes(-20),
            leaseExpiresAt: _now.AddMinutes(-5)); // Expired lease

        // Seed existing subscription
        var sub = new Subscription
        {
            CampaignId = campaign.Id,
            CustomerUserId = session.CustomerId,
            BundleId = session.BundleId,
            Status = SubscriptionStatus.Active,
            DurationDays = 1,
            StartDate = new DateOnly(2026, 10, 8),
            EndDate = new DateOnly(2026, 10, 8),
            PaidAmount = new Money(50m, "KWD")
        };
        _context.Subscriptions.Add(sub);
        await _context.SaveChangesAsync();

        var reconciledCount = await _service.ReconcileStuckSessionsAsync(CancellationToken.None);

        reconciledCount.Should().Be(1);
        var dbSession = await _context.CampaignCheckoutSessions.FindAsync(session.Id);
        dbSession!.Status.Should().Be(CampaignCheckoutSessionStatus.Completed);
    }

    [Fact]
    public async Task Reconcile_WhenSessionStuckInCommittingAndNoSubscription_DispatchesCompletePurchaseCommand()
    {
        var (_, session, _, _) = SeedSession(
            CampaignCheckoutSessionStatus.Committing,
            expiresAt: _now.AddMinutes(-20),
            leaseExpiresAt: _now.AddMinutes(-5));

        await _service.ReconcileStuckSessionsAsync(CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<CompleteCampaignPurchaseCommand>(c => c.CheckoutSessionId == session.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_WhenSessionInPaymentPendingWithSucceededTx_MarksPaidAndDispatchesCompletePurchase()
    {
        var (campaign, session, _, _) = SeedSession(
            CampaignCheckoutSessionStatus.PaymentPending,
            expiresAt: _now.AddMinutes(-20),
            leaseExpiresAt: _now.AddMinutes(-5));

        var tx = new CampaignPaymentTransaction
        {
            CheckoutSessionId = session.Id,
            CampaignId = campaign.Id,
            PaymentProvider = "Hesabe",
            ProviderTransactionId = "tx-succeeded",
            Amount = 45m,
            Currency = "KWD",
            Status = PaymentTransactionStatus.Succeeded,
            InitiatedAtUtc = _now.AddMinutes(-25),
            CompletedAtUtc = _now.AddMinutes(-20)
        };
        _context.CampaignPaymentTransactions.Add(tx);
        await _context.SaveChangesAsync();

        var reconciledCount = await _service.ReconcileStuckSessionsAsync(CancellationToken.None);

        reconciledCount.Should().Be(1);
        var dbSession = await _context.CampaignCheckoutSessions.FindAsync(session.Id);
        dbSession!.Status.Should().Be(CampaignCheckoutSessionStatus.Paid);

        await _mediator.Received(1).Send(
            Arg.Is<CompleteCampaignPurchaseCommand>(c => c.CheckoutSessionId == session.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reconcile_WhenSessionInPaymentPendingPastExpiryWithNoTx_ExpiresSessionAndReleasesCapacity()
    {
        var (campaign, session, restA, date1) = SeedSession(
            CampaignCheckoutSessionStatus.PaymentPending,
            expiresAt: _now.AddMinutes(-20),
            leaseExpiresAt: _now.AddMinutes(-5));

        var reconciledCount = await _service.ReconcileStuckSessionsAsync(CancellationToken.None);

        reconciledCount.Should().Be(1);
        var dbSession = await _context.CampaignCheckoutSessions.FindAsync(session.Id);
        dbSession!.Status.Should().Be(CampaignCheckoutSessionStatus.Expired);

        // Verify capacity decremented back to 0
        var cap = await _context.CampaignRestaurantCapacities
            .SingleAsync(c => c.CampaignId == campaign.Id && c.RestaurantId == restA && c.ServiceDate == date1);
        cap.ReservedBoxes.Should().Be(0);
    }

    [Fact]
    public async Task Reconcile_WhenSessionHasActiveLease_SkipsSession()
    {
        var (_, session, _, _) = SeedSession(
            CampaignCheckoutSessionStatus.Committing,
            expiresAt: _now.AddMinutes(-20),
            leaseExpiresAt: _now.AddMinutes(5)); // Active lease!

        var reconciledCount = await _service.ReconcileStuckSessionsAsync(CancellationToken.None);

        reconciledCount.Should().Be(0);
        var dbSession = await _context.CampaignCheckoutSessions.FindAsync(session.Id);
        dbSession!.Status.Should().Be(CampaignCheckoutSessionStatus.Committing);

        await _mediator.DidNotReceiveWithAnyArgs().Send(default!, default);
    }
}
