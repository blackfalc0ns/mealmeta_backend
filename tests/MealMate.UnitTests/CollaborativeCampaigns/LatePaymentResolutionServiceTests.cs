using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class LatePaymentResolutionServiceTests
{
    private readonly AppDbContext _context;
    private readonly ISubscriberCapManager _capManager = new SubscriberCapManager();
    private readonly IPaymentRefundGateway _refundGateway = Substitute.For<IPaymentRefundGateway>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly LatePaymentResolutionService _service;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public LatePaymentResolutionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _service = new LatePaymentResolutionService(
            _context,
            _capManager,
            _refundGateway,
            _clock);
    }

    private (Campaign Campaign, CampaignCheckoutSession Session, CampaignPaymentTransaction Tx, Guid RestA, DateOnly Date1)
        SeedLateSession(int currentReserved = 0, int maxBoxes = 5, int currentSubs = 0, int maxSubs = 10)
    {
        var campaign = new Campaign
        {
            NameAr = "حملة متأخرة",
            NameEn = "Late Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-2),
            EndsAtUtc = _now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            MaxSubscribers = maxSubs
        };
        _context.Campaigns.Add(campaign);

        var restA = Guid.NewGuid();
        var date1 = new DateOnly(2026, 10, 7);
        var capacity = new CampaignRestaurantCapacity
        {
            CampaignId = campaign.Id,
            RestaurantId = restA,
            ServiceDate = date1,
            MaxBoxes = maxBoxes,
            ReservedBoxes = currentReserved
        };
        _context.CampaignRestaurantCapacities.Add(capacity);

        var capState = new CampaignSubscriberCap
        {
            CampaignId = campaign.Id,
            Cap = maxSubs,
            CurrentCount = currentSubs
        };
        _context.CampaignSubscriberCaps.Add(capState);

        var session = new CampaignCheckoutSession
        {
            CampaignId = campaign.Id,
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.Expired,
            ExpiresAtUtc = _now.AddMinutes(-10)
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

        var tx = new CampaignPaymentTransaction
        {
            CheckoutSessionId = session.Id,
            CampaignId = campaign.Id,
            PaymentProvider = "Hesabe",
            ProviderTransactionId = "tx-late-123",
            Amount = 45.000m,
            Currency = "KWD",
            Status = PaymentTransactionStatus.Succeeded,
            InitiatedAtUtc = _now.AddMinutes(-5),
            CompletedAtUtc = _now
        };
        _context.CampaignPaymentTransactions.Add(tx);
        _context.SaveChanges();

        return (campaign, session, tx, restA, date1);
    }

    [Fact]
    public async Task ResolveLatePayment_WhenCapacityAndCapAvailable_ReacquiresCapacityAndMarksPaid()
    {
        var (campaign, session, tx, restA, date1) = SeedLateSession(currentReserved: 1, maxBoxes: 5);

        var result = await _service.ResolveLatePaymentAsync(session, tx, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.NewSessionStatus.Should().Be(CampaignCheckoutSessionStatus.Paid);
        result.RequiresRefund.Should().BeFalse();

        // Capacity re-reserved from 1 to 2
        var cap = await _context.CampaignRestaurantCapacities
            .SingleAsync(c => c.CampaignId == campaign.Id && c.RestaurantId == restA && c.ServiceDate == date1);
        cap.ReservedBoxes.Should().Be(2);

        // Session status updated
        var dbSession = await _context.CampaignCheckoutSessions.FindAsync(session.Id);
        dbSession!.Status.Should().Be(CampaignCheckoutSessionStatus.Paid);

        // No refund issued
        await _refundGateway.DidNotReceiveWithAnyArgs().IssueRefundAsync(default!, default!, default, default!, default!, default);
    }

    [Fact]
    public async Task ResolveLatePayment_WhenCapacityExhausted_TransitionsToRefundPendingAndTriggersRefund()
    {
        var (_, session, tx, _, _) = SeedLateSession(currentReserved: 5, maxBoxes: 5); // Capacity exhausted
        _refundGateway.IssueRefundAsync(
            tx.PaymentProvider,
            tx.ProviderTransactionId,
            tx.Amount,
            tx.Currency,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _service.ResolveLatePaymentAsync(session, tx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.RequiresRefund.Should().BeTrue();
        result.NewSessionStatus.Should().Be(CampaignCheckoutSessionStatus.Refunded);

        await _refundGateway.Received(1).IssueRefundAsync(
            tx.PaymentProvider,
            tx.ProviderTransactionId,
            tx.Amount,
            tx.Currency,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        var dbSession = await _context.CampaignCheckoutSessions.FindAsync(session.Id);
        dbSession!.Status.Should().Be(CampaignCheckoutSessionStatus.Refunded);

        var dbTx = await _context.CampaignPaymentTransactions.FindAsync(tx.Id);
        dbTx!.Status.Should().Be(PaymentTransactionStatus.Refunded);
    }

    [Fact]
    public async Task ResolveLatePayment_WhenSubscriberCapReached_TransitionsToRefundAndTriggersRefund()
    {
        var (_, session, tx, _, _) = SeedLateSession(currentReserved: 0, maxBoxes: 5, currentSubs: 10, maxSubs: 10); // Cap reached
        _refundGateway.IssueRefundAsync(
            tx.PaymentProvider,
            tx.ProviderTransactionId,
            tx.Amount,
            tx.Currency,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _service.ResolveLatePaymentAsync(session, tx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.RequiresRefund.Should().BeTrue();
        result.NewSessionStatus.Should().Be(CampaignCheckoutSessionStatus.Refunded);

        await _refundGateway.Received(1).IssueRefundAsync(
            tx.PaymentProvider,
            tx.ProviderTransactionId,
            tx.Amount,
            tx.Currency,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveLatePayment_WhenRefundGatewayReturnsFalse_LeavesStatusAsRefundPending()
    {
        var (_, session, tx, _, _) = SeedLateSession(currentReserved: 5, maxBoxes: 5);
        _refundGateway.IssueRefundAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<decimal>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(false); // Gateway refund failed or queued async

        var result = await _service.ResolveLatePaymentAsync(session, tx, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.RequiresRefund.Should().BeTrue();
        result.NewSessionStatus.Should().Be(CampaignCheckoutSessionStatus.RefundPending);

        var dbSession = await _context.CampaignCheckoutSessions.FindAsync(session.Id);
        dbSession!.Status.Should().Be(CampaignCheckoutSessionStatus.RefundPending);

        var dbTx = await _context.CampaignPaymentTransactions.FindAsync(tx.Id);
        dbTx!.Status.Should().Be(PaymentTransactionStatus.RefundPending);
    }
}
