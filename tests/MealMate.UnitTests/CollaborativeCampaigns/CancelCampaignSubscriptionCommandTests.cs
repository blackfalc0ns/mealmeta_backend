using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Common;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public class CancelCampaignSubscriptionCommandTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IPaymentRefundGateway _refundGateway = Substitute.For<IPaymentRefundGateway>();
    private readonly CancelCampaignSubscriptionCommandHandler _handler;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public CancelCampaignSubscriptionCommandTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _handler = new CancelCampaignSubscriptionCommandHandler(
            _context,
            _clock,
            _refundGateway);
    }

    private (Campaign Campaign, Subscription Subscription, Guid RestA, DateOnly Date1, CampaignRestaurantCapacity Cap)
        SeedCampaignSubscription(SubscriptionStatus status = SubscriptionStatus.Active)
    {
        var campaign = new Campaign
        {
            NameAr = "حملة الإلغاء",
            NameEn = "Cancel Campaign",
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
        var date1 = new DateOnly(2026, 10, 10);
        var capacity = new CampaignRestaurantCapacity
        {
            CampaignId = campaign.Id,
            RestaurantId = restA,
            ServiceDate = date1,
            MaxBoxes = 10,
            ReservedBoxes = 2
        };
        _context.CampaignRestaurantCapacities.Add(capacity);

        var capState = new CampaignSubscriberCap
        {
            CampaignId = campaign.Id,
            Cap = 50,
            CurrentCount = 5
        };
        _context.CampaignSubscriberCaps.Add(capState);

        var sub = new Subscription
        {
            CampaignId = campaign.Id,
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = status,
            DurationDays = 1,
            StartDate = date1,
            EndDate = date1,
            PaidAmount = new Money(40.000m, "KWD"),
            PaymentId = "pay-12345"
        };
        _context.Subscriptions.Add(sub);

        var day = new SubscriptionDay
        {
            SubscriptionId = sub.Id,
            RestaurantId = restA,
            ServiceDate = date1,
            Status = SubscriptionDayStatus.AutoSelected
        };
        _context.SubscriptionDays.Add(day);
        _context.SaveChanges();

        return (campaign, sub, restA, date1, capacity);
    }

    [Fact]
    public async Task Handle_WhenSubscriptionActive_ReleasesCapacityAndPostsLedgerOnlyAfterRefundReconciliation()
    {
        var (campaign, sub, restA, date1, capacity) = SeedCampaignSubscription();
        var payment = new CampaignPaymentTransaction
        {
            CampaignId = campaign.Id,
            CheckoutSessionId = Guid.NewGuid(),
            PaymentProvider = "MyFatoorah",
            ProviderTransactionId = sub.PaymentId!,
            Amount = 40.000m,
            Currency = "KWD",
            Status = PaymentTransactionStatus.Succeeded
        };
        _context.CampaignPaymentTransactions.Add(payment);
        await _context.SaveChangesAsync();
        _refundGateway.RequestRefundAsync(
            payment.PaymentProvider,
            sub.PaymentId!,
            40.000m,
            "KWD",
            "Customer relocation",
            "campaign-cancel-" + sub.Id,
            Arg.Any<CancellationToken>())
            .Returns(new RefundGatewayResult(RefundGatewayState.Pending, "refund-reference"));

        var command = new CancelCampaignSubscriptionCommand(
            SubscriptionId: sub.Id,
            Reason: "Customer relocation",
            IssueRefund: true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(nameof(SubscriptionStatus.Cancelled));
        result.Value.RefundAmount.Should().Be(40.000m);
        result.Value.RefundStatus.Should().Be(nameof(RefundGatewayState.Pending));
        result.Value.RefundReference.Should().Be("refund-reference");
        await _refundGateway.Received(1).RequestRefundAsync(payment.PaymentProvider, sub.PaymentId!,
            40.000m, "KWD", command.Reason, "campaign-cancel-" + sub.Id, Arg.Any<CancellationToken>());

        // Verify Subscription marked Cancelled
        var updatedSub = await _context.Subscriptions.FindAsync(sub.Id);
        updatedSub!.Status.Should().Be(SubscriptionStatus.Cancelled);

        // Verify Capacity released: 2 -> 1
        var updatedCap = await _context.CampaignRestaurantCapacities.FindAsync(capacity.Id);
        updatedCap!.ReservedBoxes.Should().Be(1);

        // Verify SubscriberCap decremented: 5 -> 4
        var capState = await _context.CampaignSubscriberCaps.SingleAsync(c => c.CampaignId == campaign.Id);
        capState.CurrentCount.Should().Be(4);

        // Verify SubscriptionDay marked Cancelled
        var day = await _context.SubscriptionDays.SingleAsync(d => d.SubscriptionId == sub.Id);
        day.Status.Should().Be(SubscriptionDayStatus.Cancelled);

        // Requesting a refund must not post financial entries before provider confirmation.
        (await _context.CampaignFinancialLedgers.ToListAsync()).Should().BeEmpty();
        var operation = new RefundOperation
        {
            OperationReference = "campaign-cancel-" + sub.Id,
            PaymentId = sub.PaymentId!, InvoiceId = "invoice-12345",
            Amount = 40.000m, Currency = "KWD", State = RefundOperationState.Confirmed
        };
        _context.RefundOperations.Add(operation);
        await _context.SaveChangesAsync();
        _refundGateway.ReconcileAsync(operation.Id, Arg.Any<CancellationToken>())
            .Returns(new RefundGatewayResult(RefundGatewayState.Confirmed));
        var transactions = Substitute.For<IApplicationTransactionExecutor>();
        transactions.ExecuteAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task>>()(call.Arg<CancellationToken>()));
        var reconciliation = new ProviderRefundReconciliationService(_context, _refundGateway,
            transactions, Substitute.For<IApplicationLockManager>(), _clock);
        await reconciliation.ReconcileAsync(operation.Id, CancellationToken.None);
        await reconciliation.ReconcileAsync(operation.Id, CancellationToken.None);

        // Confirmed reconciliation posts each entry once, including on repeat checks.
        var ledgerEntries = await _context.CampaignFinancialLedgers
            .Where(l => l.CampaignId == campaign.Id)
            .ToListAsync();
        ledgerEntries.Should().Contain(e => e.EntryType == CampaignLedgerEntryType.CustomerRefund && e.Amount == 40.000m);
        ledgerEntries.Should().Contain(e => e.EntryType == CampaignLedgerEntryType.RestaurantSettlementReversal);
        ledgerEntries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WhenSubscriptionNotFound_ReturnsNotFound()
    {
        var command = new CancelCampaignSubscriptionCommand(
            SubscriptionId: Guid.NewGuid(),
            Reason: "Missing",
            IssueRefund: false);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.SubscriptionNotFound");
    }

    [Fact]
    public async Task Handle_WhenSubscriptionNotLinkedToCampaign_ReturnsValidationError()
    {
        var sub = new Subscription
        {
            CampaignId = null, // Not a campaign subscription!
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 1,
            StartDate = new DateOnly(2026, 10, 10),
            EndDate = new DateOnly(2026, 10, 10),
            PaidAmount = new Money(50m, "KWD")
        };
        _context.Subscriptions.Add(sub);
        await _context.SaveChangesAsync();

        var command = new CancelCampaignSubscriptionCommand(
            SubscriptionId: sub.Id,
            Reason: "Wrong type");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.NotCampaignSubscription");
    }

    [Fact]
    public async Task Handle_WhenSubscriptionAlreadyCancelled_ReturnsConflict()
    {
        var (_, sub, _, _, _) = SeedCampaignSubscription(SubscriptionStatus.Cancelled);

        var command = new CancelCampaignSubscriptionCommand(
            SubscriptionId: sub.Id,
            Reason: "Already done");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.AlreadyCancelled");
    }
}
