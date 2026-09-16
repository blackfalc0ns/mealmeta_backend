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

public class CampaignLedgerPostingServiceTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly CampaignLedgerPostingService _service;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public CampaignLedgerPostingServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _service = new CampaignLedgerPostingService(_context, _clock);
    }

    private (Campaign Campaign, CampaignCheckoutSession Session, CampaignPaymentTransaction Tx, Guid RestA, Guid RestB)
        SeedSessionWithTwoRestaurants()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة المالية",
            NameEn = "Financial Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-1),
            EndsAtUtc = _now.AddDays(10),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            MaxSubscribers = 50
        };
        _context.Campaigns.Add(campaign);

        var restA = Guid.NewGuid();
        var restB = Guid.NewGuid();

        var session = new CampaignCheckoutSession
        {
            CampaignId = campaign.Id,
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.Paid,
            ExpiresAtUtc = _now.AddMinutes(15)
        };
        session.Allocations.Add(new CampaignReservationAllocation
        {
            CheckoutSessionId = session.Id,
            CampaignId = campaign.Id,
            BundleId = session.BundleId,
            RestaurantId = restA,
            ServiceDate = new DateOnly(2026, 10, 6),
            Units = 1
        });
        session.Allocations.Add(new CampaignReservationAllocation
        {
            CheckoutSessionId = session.Id,
            CampaignId = campaign.Id,
            BundleId = session.BundleId,
            RestaurantId = restB,
            ServiceDate = new DateOnly(2026, 10, 7),
            Units = 1
        });
        _context.CampaignCheckoutSessions.Add(session);

        var tx = new CampaignPaymentTransaction
        {
            CheckoutSessionId = session.Id,
            CampaignId = campaign.Id,
            PaymentProvider = "Hesabe",
            ProviderTransactionId = "tx-fin-100",
            Amount = 50.000m,
            Currency = "KWD",
            Status = PaymentTransactionStatus.Succeeded,
            InitiatedAtUtc = _now,
            CompletedAtUtc = _now
        };
        _context.CampaignPaymentTransactions.Add(tx);
        _context.SaveChanges();

        return (campaign, session, tx, restA, restB);
    }

    [Fact]
    public async Task PostPurchaseEntries_CreatesCustomerPaymentAndRestaurantSettlementsAndFeeUnderSameJournalId()
    {
        var (campaign, session, tx, restA, restB) = SeedSessionWithTwoRestaurants();

        var result = await _service.PostPurchaseEntriesAsync(session, tx, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var journalId = result.Value;
        journalId.Should().NotBeEmpty();

        var entries = await _context.CampaignFinancialLedgers
            .Where(e => e.JournalId == journalId)
            .ToListAsync();

        entries.Should().HaveCount(4); // 1 CustomerPayment + 2 RestaurantSettlements + 1 Fee

        // 1. Customer Payment
        var paymentEntry = entries.Single(e => e.EntryType == CampaignLedgerEntryType.CustomerPayment);
        paymentEntry.Amount.Should().Be(50.000m);
        paymentEntry.Currency.Should().Be("KWD");
        paymentEntry.CampaignId.Should().Be(campaign.Id);
        paymentEntry.CheckoutSessionId.Should().Be(session.Id);

        // 2. Restaurant Settlements
        var settlements = entries.Where(e => e.EntryType == CampaignLedgerEntryType.RestaurantSettlement).ToList();
        settlements.Should().HaveCount(2);
        settlements.Select(s => s.RestaurantId).Should().Contain(restA);
        settlements.Select(s => s.RestaurantId).Should().Contain(restB);

        // 3. Payment Processing Fee
        var feeEntry = entries.Single(e => e.EntryType == CampaignLedgerEntryType.PaymentProcessingFee);
        feeEntry.Amount.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task PostRefundEntries_CreatesRefundEntryWithSameJournalId()
    {
        var (campaign, session, tx, _, _) = SeedSessionWithTwoRestaurants();

        var result = await _service.PostRefundEntriesAsync(session, tx, "Capacity exhausted late payment", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var journalId = result.Value;
        journalId.Should().NotBeEmpty();

        var entries = await _context.CampaignFinancialLedgers
            .Where(e => e.JournalId == journalId)
            .ToListAsync();

        entries.Should().HaveCount(1);
        var refund = entries[0];
        refund.EntryType.Should().Be(CampaignLedgerEntryType.CustomerRefund);
        refund.Amount.Should().Be(50.000m);
        refund.CampaignId.Should().Be(campaign.Id);
        refund.Description.Should().Contain("Capacity exhausted");
    }
}
