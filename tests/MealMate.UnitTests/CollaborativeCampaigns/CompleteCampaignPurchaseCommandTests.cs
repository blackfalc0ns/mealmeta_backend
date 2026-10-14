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

public class CompleteCampaignPurchaseCommandTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly CompleteCampaignPurchaseCommandHandler _handler;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public CompleteCampaignPurchaseCommandTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _handler = new CompleteCampaignPurchaseCommandHandler(_context, _clock);
    }

    private (Campaign Campaign, CampaignCheckoutSession Session, CampaignPaymentTransaction Tx, Guid BundleId, Guid RestA, DateOnly Date1)
        SeedPaidSession()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة الشراء",
            NameEn = "Purchase Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-1),
            EndsAtUtc = _now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            MaxSubscribers = 50
        };
        _context.Campaigns.Add(campaign);

        var bundle = new Bundle
        {
            Code = "BUNDLE-01",
            NameAr = "باقة التجربة",
            NameEn = "Trial Bundle",
            ProgramId = Guid.NewGuid()
        };
        _context.Bundles.Add(bundle);

        var restA = Guid.NewGuid();
        var date1 = new DateOnly(2026, 10, 7);

        var session = new CampaignCheckoutSession
        {
            CampaignId = campaign.Id,
            BundleId = bundle.Id,
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.Paid,
            ExpiresAtUtc = _now.AddMinutes(15)
        };
        session.Allocations.Add(new CampaignReservationAllocation
        {
            CheckoutSessionId = session.Id,
            CampaignId = campaign.Id,
            BundleId = bundle.Id,
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
            ProviderTransactionId = "tx-paid-12345",
            Amount = 45.000m,
            Currency = "KWD",
            Status = PaymentTransactionStatus.Succeeded,
            InitiatedAtUtc = _now,
            CompletedAtUtc = _now
        };
        _context.CampaignPaymentTransactions.Add(tx);
        _context.SaveChanges();

        return (campaign, session, tx, bundle.Id, restA, date1);
    }

    [Fact]
    public async Task Handle_WhenSessionPaid_CreatesSubscriptionAndSubscriptionDaysAndCompletesSession()
    {
        var (campaign, session, tx, bundleId, restA, date1) = SeedPaidSession();

        var command = new CompleteCampaignPurchaseCommand(
            CheckoutSessionId: session.Id,
            DeliveryAddress: "Kuwait City, Block 1, St 2");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(nameof(SubscriptionStatus.Active));
        result.Value.TotalAmount.Should().Be(45.000m);
        result.Value.StartDate.Should().Be(date1);
        result.Value.EndDate.Should().Be(date1);

        // Verify Subscription was created in DB
        var sub = await _context.Subscriptions.FindAsync(result.Value.SubscriptionId);
        sub.Should().NotBeNull();
        sub!.CampaignId.Should().Be(campaign.Id);
        sub.CustomerUserId.Should().Be(session.CustomerId);
        sub.BundleId.Should().Be(bundleId);
        sub.Status.Should().Be(SubscriptionStatus.Active);
        sub.PaidAmount.Amount.Should().Be(45.000m);
        sub.DeliveryAddress.Should().Be("Kuwait City, Block 1, St 2");

        // Verify SubscriptionDay was created
        var subDays = await _context.SubscriptionDays
            .Where(d => d.SubscriptionId == sub.Id)
            .ToListAsync();
        subDays.Should().HaveCount(1);
        subDays[0].RestaurantId.Should().Be(restA);
        subDays[0].ServiceDate.Should().Be(date1);

        // Verify Session is marked Completed
        var dbSession = await _context.CampaignCheckoutSessions.FindAsync(session.Id);
        dbSession!.Status.Should().Be(CampaignCheckoutSessionStatus.Completed);
    }

    [Fact]
    public async Task Handle_WhenSessionNotPaid_ReturnsConflict()
    {
        var (_, session, _, _, _, _) = SeedPaidSession();
        session.Status = CampaignCheckoutSessionStatus.Pending; // Not yet paid!
        await _context.SaveChangesAsync();

        var command = new CompleteCampaignPurchaseCommand(session.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.InvalidSessionState");
    }

    [Fact]
    public async Task Handle_WhenSessionNotFound_ReturnsNotFound()
    {
        var command = new CompleteCampaignPurchaseCommand(Guid.NewGuid());

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.SessionNotFound");
    }

    [Fact]
    public async Task Handle_WhenPaymentMissing_ReturnsConflict()
    {
        var (_, session, tx, _, _, _) = SeedPaidSession();
        _context.CampaignPaymentTransactions.Remove(tx); // Remove transaction
        await _context.SaveChangesAsync();

        var command = new CompleteCampaignPurchaseCommand(session.Id);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.PaymentMissing");
    }

    [Fact]
    public async Task Handle_WhenAlreadyCompleted_ReturnsIdempotentSuccess()
    {
        var (_, session, _, _, _, _) = SeedPaidSession();

        var command = new CompleteCampaignPurchaseCommand(session.Id);

        var first = await _handler.Handle(command, CancellationToken.None);
        var second = await _handler.Handle(command, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value.SubscriptionId.Should().Be(first.Value.SubscriptionId);

        // Subscriptions count must be 1, not 2
        var subsCount = await _context.Subscriptions
            .CountAsync(s => s.CampaignId == session.CampaignId && s.CustomerUserId == session.CustomerId);
        subsCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SeparatePaidSessionsForSameCustomerAndBundle_CreateDistinctSubscriptions()
    {
        var (campaign, first, _, bundleId, restaurantId, date) = SeedPaidSession();
        var second = new CampaignCheckoutSession
        {
            CampaignId = campaign.Id, BundleId = bundleId, CustomerId = first.CustomerId,
            Status = CampaignCheckoutSessionStatus.Paid, ExpiresAtUtc = _now.AddMinutes(15)
        };
        second.Allocations.Add(new CampaignReservationAllocation
        {
            CheckoutSessionId = second.Id, CampaignId = campaign.Id, BundleId = bundleId,
            RestaurantId = restaurantId, ServiceDate = date, Units = 1
        });
        _context.CampaignCheckoutSessions.Add(second);
        _context.CampaignPaymentTransactions.Add(new CampaignPaymentTransaction
        {
            CheckoutSessionId = second.Id, CampaignId = campaign.Id, PaymentProvider = "Hesabe",
            ProviderTransactionId = "tx-paid-second", Amount = 45m, Currency = "KWD",
            Status = PaymentTransactionStatus.Succeeded, InitiatedAtUtc = _now, CompletedAtUtc = _now
        });
        await _context.SaveChangesAsync();

        var firstResult = await _handler.Handle(new CompleteCampaignPurchaseCommand(first.Id), CancellationToken.None);
        var secondResult = await _handler.Handle(new CompleteCampaignPurchaseCommand(second.Id), CancellationToken.None);

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value.SubscriptionId.Should().NotBe(firstResult.Value.SubscriptionId);
        var subscriptions = await _context.Subscriptions.Where(s => s.CampaignId == campaign.Id && s.CustomerUserId == first.CustomerId).ToListAsync();
        subscriptions.Should().HaveCount(2);
        subscriptions.Select(x => x.CorrelationId).Should().Contain(new[] { first.Id.ToString("N"), second.Id.ToString("N") });
    }
}
