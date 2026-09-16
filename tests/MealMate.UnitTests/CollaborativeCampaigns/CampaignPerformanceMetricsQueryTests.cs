using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CampaignPerformanceMetricsQueryTests
{
    private readonly AppDbContext _context;
    private readonly GetCampaignPerformanceMetricsQueryHandler _handler;
    private readonly DateTime _now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    public CampaignPerformanceMetricsQueryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);

        _handler = new GetCampaignPerformanceMetricsQueryHandler(_context);
    }

    [Fact]
    public async Task Handle_WhenCampaignNotFound_ReturnsFailure()
    {
        var query = new GetCampaignPerformanceMetricsQuery(Guid.NewGuid());

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.NotFound");
    }

    [Fact]
    public async Task Handle_WhenCampaignHasParticipantsAndSessions_ComputesAccurateMetrics()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة الصيف",
            NameEn = "Summer Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-5),
            EndsAtUtc = _now.AddDays(15),
            MaxSubscribers = 100,
            CurrentSubscribers = 20,
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m)
        };
        _context.Campaigns.Add(campaign);
        var campaignId = campaign.Id;

        // Add 2 enrollments
        var rest1 = Guid.NewGuid();
        var rest2 = Guid.NewGuid();
        var enrollment1 = new CampaignRestaurantEnrollment
        {
            CampaignId = campaignId,
            RestaurantId = rest1,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
            DedicatedDailyCapacity = 30
        };
        var enrollment2 = new CampaignRestaurantEnrollment
        {
            CampaignId = campaignId,
            RestaurantId = rest2,
            ParticipationStatus = CampaignRestaurantParticipationStatus.Accepted,
            DedicatedDailyCapacity = 20
        };
        _context.CampaignRestaurantEnrollments.AddRange(enrollment1, enrollment2);

        // Add 3 checkout sessions: 2 completed, 1 expired
        var session1 = new CampaignCheckoutSession
        {
            CampaignId = campaignId,
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.Completed,
            ExpiresAtUtc = _now.AddMinutes(15)
        };
        var session2 = new CampaignCheckoutSession
        {
            CampaignId = campaignId,
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.Completed,
            ExpiresAtUtc = _now.AddMinutes(15)
        };
        var session3 = new CampaignCheckoutSession
        {
            CampaignId = campaignId,
            BundleId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = CampaignCheckoutSessionStatus.Expired,
            ExpiresAtUtc = _now.AddMinutes(-5)
        };
        _context.CampaignCheckoutSessions.AddRange(session1, session2, session3);

        // Add Ledger Entries for campaignId
        var journalId = Guid.NewGuid();
        var entry1 = new CampaignFinancialLedger
        {
            JournalId = journalId,
            CampaignId = campaignId,
            CheckoutSessionId = session1.Id,
            EntryType = CampaignLedgerEntryType.CustomerPayment,
            Amount = 100.0m,
            Currency = "KWD",
            Description = "Customer payment"
        };
        var entry2 = new CampaignFinancialLedger
        {
            JournalId = journalId,
            CampaignId = campaignId,
            CheckoutSessionId = session2.Id,
            EntryType = CampaignLedgerEntryType.CustomerPayment,
            Amount = 200.0m,
            Currency = "KWD",
            Description = "Customer payment"
        };
        var entry3 = new CampaignFinancialLedger
        {
            JournalId = journalId,
            CampaignId = campaignId,
            RestaurantId = rest1,
            EntryType = CampaignLedgerEntryType.RestaurantSettlement,
            Amount = 80.0m,
            Currency = "KWD",
            Description = "Restaurant settlement"
        };
        var entry4 = new CampaignFinancialLedger
        {
            JournalId = journalId,
            CampaignId = campaignId,
            RestaurantId = rest2,
            EntryType = CampaignLedgerEntryType.RestaurantSettlement,
            Amount = 160.0m,
            Currency = "KWD",
            Description = "Restaurant settlement"
        };
        var entry5 = new CampaignFinancialLedger
        {
            JournalId = journalId,
            CampaignId = campaignId,
            EntryType = CampaignLedgerEntryType.CustomerRefund,
            Amount = 10.0m,
            Currency = "KWD",
            Description = "Customer refund"
        };

        _context.CampaignFinancialLedgers.AddRange(entry1, entry2, entry3, entry4, entry5);
        await _context.SaveChangesAsync();

        var query = new GetCampaignPerformanceMetricsQuery(campaignId);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var metrics = result.Value;
        metrics.CampaignId.Should().Be(campaignId);
        metrics.CampaignNameEn.Should().Be("Summer Campaign");
        metrics.Status.Should().Be(CampaignStatus.Active);
        metrics.TotalParticipatingRestaurants.Should().Be(2);
        metrics.TotalDedicatedDailyCapacity.Should().Be(50);
        metrics.MaxSubscribers.Should().Be(100);
        metrics.CurrentSubscribers.Should().Be(20);
        metrics.CapacityUtilizationRate.Should().Be(0.20m); // 20 / 100

        metrics.TotalCheckoutSessions.Should().Be(3);
        metrics.CompletedCheckoutSessions.Should().Be(2);
        metrics.ExpiredCheckoutSessions.Should().Be(1);
        metrics.CheckoutConversionRate.Should().BeApproximately(0.6667m, 0.001m); // 2 / 3

        metrics.TotalCustomerPayments.Should().Be(300.0m);
        metrics.TotalRestaurantPayouts.Should().Be(240.0m);
        metrics.TotalCustomerRefunds.Should().Be(10.0m);
        metrics.NetPlatformMargin.Should().Be(50.0m); // 300 - 240 - 10 = 50
    }

    [Fact]
    public async Task Handle_WhenZeroSessionsAndZeroCap_AvoidsDivisionByZero()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة تجريبية",
            NameEn = "Zero Cap Campaign",
            Status = CampaignStatus.Draft,
            StartsAtUtc = _now,
            EndsAtUtc = _now.AddDays(7),
            MaxSubscribers = 0,
            CurrentSubscribers = 0,
            TotalDiscountRate = Percentage.FromFraction(0.10m),
            MealMateShareRate = Percentage.FromFraction(0.05m),
            RestaurantShareRate = Percentage.FromFraction(0.05m)
        };
        _context.Campaigns.Add(campaign);
        var campaignId = campaign.Id;
        await _context.SaveChangesAsync();

        var query = new GetCampaignPerformanceMetricsQuery(campaignId);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CapacityUtilizationRate.Should().Be(0.0m);
        result.Value.CheckoutConversionRate.Should().Be(0.0m);
        result.Value.TotalCheckoutSessions.Should().Be(0);
        result.Value.CompletedCheckoutSessions.Should().Be(0);
    }
}
