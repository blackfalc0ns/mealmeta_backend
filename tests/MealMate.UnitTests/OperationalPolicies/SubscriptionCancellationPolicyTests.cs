using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.OperationalPolicies;

public class SubscriptionCancellationPolicyTests
{
    private readonly Persistence.AppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditLogger _audit;
    private readonly IOperationalPolicyService _policyService;
    private readonly DateTime _utcNow = new(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);
    private readonly DateOnly _today = new(2026, 8, 20);

    public SubscriptionCancellationPolicyTests()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(_utcNow);
        _clock.TodayUtc.Returns(_today);
        _audit = Substitute.For<IAuditLogger>();
        _policyService = Substitute.For<IOperationalPolicyService>();
    }

    private SubscriptionCancellationService CreateSut()
    {
        return new SubscriptionCancellationService(_db, _clock, _audit, _policyService);
    }

    private (Subscription Sub, List<SubscriptionDay> Days) SetupSubscription(
        Guid customerUserId,
        decimal paidAmount = 90.000m,
        int durationDays = 30,
        int deliveredDays = 0,
        int daysOffset = 0)
    {
        var sub = new Subscription
        {
            CustomerUserId = customerUserId,
            Status = SubscriptionStatus.Active,
            DurationDays = durationDays,
            PaidAmount = new Money(paidAmount, "KWD"),
            StartDate = _today.AddDays(daysOffset),
            EndDate = _today.AddDays(daysOffset + durationDays - 1),
            CreatedAtUtc = _utcNow
        };
        _db.Subscriptions.Add(sub);

        var days = new List<SubscriptionDay>();
        for (var i = 0; i < durationDays; i++)
        {
            var date = sub.StartDate.AddDays(i);
            var status = i < deliveredDays ? SubscriptionDayStatus.Delivered : SubscriptionDayStatus.Open;
            var day = new SubscriptionDay
            {
                SubscriptionId = sub.Id,
                ServiceDate = date,
                Status = status,
                CreatedAtUtc = _utcNow
            };
            days.Add(day);
            _db.SubscriptionDays.Add(day);
        }

        _db.SaveChanges();
        return (sub, days);
    }

    [Fact]
    public async Task GetQuoteAsync_WithPercentageFee_DeductsPercentageAndReturnsNetRefund()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        // Starts 10 days in future so all 30 days are outside lead window
        var (sub, _) = SetupSubscription(customerId, paidAmount: 90.000m, durationDays: 30, deliveredDays: 0, daysOffset: 10);

        var policy = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(CutoffLeadHours: 72),
            Cancellation: new CancellationPolicySettings(FeeType: CancellationFeeType.Percentage, FeeValue: 10.0m, RefundToCashWallet: true),
            Freeze: new FreezePolicySettings(LeadDaysNotice: 3)
        );
        _policyService.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(policy);

        var sut = CreateSut();

        // Act
        var result = await sut.GetQuoteAsync(sub.Id, customerId, UserType.Customer);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var quote = result.Value;
        quote.RefundableDays.Should().Be(30);
        quote.RefundAmount.Should().Be(90.000m); // Gross
        quote.CancellationFeeAmount.Should().Be(9.000m); // 10% of 90
        quote.NetRefundAmount.Should().Be(81.000m); // 90 - 9
    }

    [Fact]
    public async Task GetQuoteAsync_WithFixedAmountFee_DeductsFixedFeeAndReturnsNetRefund()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var (sub, _) = SetupSubscription(customerId, paidAmount: 90.000m, durationDays: 30, deliveredDays: 0, daysOffset: 10);

        var policy = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(CutoffLeadHours: 72),
            Cancellation: new CancellationPolicySettings(FeeType: CancellationFeeType.FixedAmount, FeeValue: 5.0m, RefundToCashWallet: true),
            Freeze: new FreezePolicySettings(LeadDaysNotice: 3)
        );
        _policyService.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(policy);

        var sut = CreateSut();

        // Act
        var result = await sut.GetQuoteAsync(sub.Id, customerId, UserType.Customer);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var quote = result.Value;
        quote.RefundAmount.Should().Be(90.000m);
        quote.CancellationFeeAmount.Should().Be(5.000m);
        quote.NetRefundAmount.Should().Be(85.000m);
    }

    [Fact]
    public async Task RequestCancellationAsync_WhenRefundToWalletIsTrue_CreditsCashWalletAndRecordsTransaction()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var (sub, _) = SetupSubscription(customerId, paidAmount: 90.000m, durationDays: 30, deliveredDays: 0, daysOffset: 10);

        var policy = new OperationalPolicySettings(
            Cutoff: new CutoffPolicySettings(CutoffLeadHours: 72),
            Cancellation: new CancellationPolicySettings(FeeType: CancellationFeeType.Percentage, FeeValue: 10.0m, RefundToCashWallet: true),
            Freeze: new FreezePolicySettings(LeadDaysNotice: 3)
        );
        _policyService.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(policy);

        var sut = CreateSut();

        // Act
        var result = await sut.RequestCancellationAsync(sub.Id, customerId, UserType.Customer, reason: "Travel");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CancellationFeeAmount.Should().Be(9.000m);
        result.Value.NetRefundAmount.Should().Be(81.000m);
        result.Value.RefundedToWallet.Should().BeTrue();

        // Verify CustomerCashWallet in DB
        var wallet = await _db.CustomerCashWallets.FirstOrDefaultAsync(w => w.CustomerUserId == customerId);
        wallet.Should().NotBeNull();
        wallet!.Balance.Amount.Should().Be(81.000m);
        wallet.Balance.CurrencyCode.Should().Be("KWD");

        // Verify WalletTransaction in DB
        var tx = await _db.WalletTransactions.FirstOrDefaultAsync(t => t.CustomerUserId == customerId);
        tx.Should().NotBeNull();
        tx!.WalletType.Should().Be(WalletType.Cash);
        tx.Type.Should().Be(WalletTransactionType.Refund);
        tx.Amount.Amount.Should().Be(81.000m);
        tx.ReferenceType.Should().Be("SubscriptionCancellation");
        tx.ReferenceId.Should().Be(sub.Id);
    }
}
