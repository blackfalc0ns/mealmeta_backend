using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignFinancialReportQueryTests
{
    private readonly AppDbContext _context;
    private readonly GetCampaignFinancialReportQueryHandler _handler;
    private readonly DateTime _now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    public CampaignFinancialReportQueryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);

        _handler = new GetCampaignFinancialReportQueryHandler(_context);
    }

    [Fact]
    public async Task Handle_WhenCampaignNotFound_ReturnsFailure()
    {
        var query = new GetCampaignFinancialReportQuery(Guid.NewGuid());

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.NotFound");
    }

    [Fact]
    public async Task Handle_WhenLedgerEntriesExist_CalculatesReconciliationAndAnalyticalSpreadWithoutDoubleCounting()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة التقارير المالية",
            NameEn = "Financial Report Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-10),
            EndsAtUtc = _now.AddDays(20),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        var campaignId = campaign.Id;

        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم الخير",
            TradeNameEn = "Good Restaurant",
            LegalNameAr = "شركة الخير",
            LegalNameEn = "Good Co"
        };
        _context.RestaurantProfiles.Add(restaurant);
        var restaurantId = restaurant.Id;

        var journal1 = Guid.NewGuid();
        var journal2 = Guid.NewGuid();

        // 1. Initial Purchase Journal
        var entryPayment = new CampaignFinancialLedger
        {
            JournalId = journal1,
            CampaignId = campaignId,
            EntryType = CampaignLedgerEntryType.CustomerPayment,
            Amount = 100.000m,
            Currency = "KWD",
            Description = "Customer payment for checkout",
            CreatedAtUtc = _now.AddDays(-5)
        };
        var entrySettlement = new CampaignFinancialLedger
        {
            JournalId = journal1,
            CampaignId = campaignId,
            RestaurantId = restaurantId,
            EntryType = CampaignLedgerEntryType.RestaurantSettlement,
            Amount = 75.000m,
            Currency = "KWD",
            Description = "Restaurant settlement payable",
            CreatedAtUtc = _now.AddDays(-5)
        };
        var entryFee = new CampaignFinancialLedger
        {
            JournalId = journal1,
            CampaignId = campaignId,
            EntryType = CampaignLedgerEntryType.PaymentProcessingFee,
            Amount = 2.500m,
            Currency = "KWD",
            Description = "Payment gateway commission",
            CreatedAtUtc = _now.AddDays(-5)
        };

        // 2. Cancellation / Partial Reversal Journal
        var entryRefund = new CampaignFinancialLedger
        {
            JournalId = journal2,
            CampaignId = campaignId,
            EntryType = CampaignLedgerEntryType.CustomerRefund,
            Amount = 10.000m,
            Currency = "KWD",
            Description = "Customer refund",
            CreatedAtUtc = _now.AddDays(-2)
        };
        var entrySettlementReversal = new CampaignFinancialLedger
        {
            JournalId = journal2,
            CampaignId = campaignId,
            RestaurantId = restaurantId,
            EntryType = CampaignLedgerEntryType.RestaurantSettlementReversal,
            Amount = 7.500m,
            Currency = "KWD",
            Description = "Restaurant settlement clawback",
            CreatedAtUtc = _now.AddDays(-2)
        };
        var entryFeeReversal = new CampaignFinancialLedger
        {
            JournalId = journal2,
            CampaignId = campaignId,
            EntryType = CampaignLedgerEntryType.PaymentFeeReversal,
            Amount = 0.250m,
            Currency = "KWD",
            Description = "Gateway fee adjustment",
            CreatedAtUtc = _now.AddDays(-2)
        };

        _context.CampaignFinancialLedgers.AddRange(
            entryPayment, entrySettlement, entryFee,
            entryRefund, entrySettlementReversal, entryFeeReversal);

        await _context.SaveChangesAsync();

        var query = new GetCampaignFinancialReportQuery(campaignId);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var report = result.Value;

        report.CampaignId.Should().Be(campaignId);
        report.CampaignNameEn.Should().Be("Financial Report Campaign");
        report.TotalCustomerCharged.Should().Be(100.000m);
        report.TotalCustomerRefunded.Should().Be(10.000m);
        report.NetCustomerRevenue.Should().Be(90.000m);

        report.TotalRestaurantSettlement.Should().Be(75.000m);
        report.TotalRestaurantSettlementReversals.Should().Be(7.500m);
        report.NetRestaurantSettlement.Should().Be(67.500m);

        report.TotalPaymentProcessingFees.Should().Be(2.250m); // 2.500 - 0.250
        report.NetPlatformRetainedGrossProfit.Should().Be(20.250m); // 90.000 - 67.500 - 2.250

        // Strict Balance Check: NetCustomerRevenue == NetRestaurantSettlement + TotalPaymentProcessingFees + NetPlatformRetainedGrossProfit
        report.IsReconciled.Should().BeTrue();

        // Analytical wholesale spread should be positive analytical metric without modifying ledger balance
        report.AnalyticalWholesaleSpread.Should().BeGreaterThan(0);

        report.LedgerEntries.Should().HaveCount(6);
        report.LedgerEntries.First(e => e.RestaurantId == restaurantId).RestaurantName.Should().Be("Good Restaurant");
    }

    [Fact]
    public async Task Handle_WithDateFilters_ReturnsFilteredLedgerEntries()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة التصفية",
            NameEn = "Filtered Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-30),
            EndsAtUtc = _now.AddDays(30),
            TotalDiscountRate = Percentage.FromFraction(0.15m),
            MealMateShareRate = Percentage.FromFraction(0.08m),
            RestaurantShareRate = Percentage.FromFraction(0.07m)
        };
        _context.Campaigns.Add(campaign);
        var campaignId = campaign.Id;

        var entryOld = new CampaignFinancialLedger
        {
            JournalId = Guid.NewGuid(),
            CampaignId = campaignId,
            EntryType = CampaignLedgerEntryType.CustomerPayment,
            Amount = 50.0m,
            Currency = "KWD",
            Description = "Old payment",
            CreatedAtUtc = _now.AddDays(-20)
        };
        var entryNew = new CampaignFinancialLedger
        {
            JournalId = Guid.NewGuid(),
            CampaignId = campaignId,
            EntryType = CampaignLedgerEntryType.CustomerPayment,
            Amount = 80.0m,
            Currency = "KWD",
            Description = "Recent payment",
            CreatedAtUtc = _now.AddDays(-2)
        };

        _context.CampaignFinancialLedgers.AddRange(entryOld, entryNew);
        await _context.SaveChangesAsync();

        var query = new GetCampaignFinancialReportQuery(campaignId, FromUtc: _now.AddDays(-5), ToUtc: _now);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.LedgerEntries.Should().HaveCount(1);
        result.Value.LedgerEntries[0].Description.Should().Be("Recent payment");
        result.Value.TotalCustomerCharged.Should().Be(80.0m);
    }
}
