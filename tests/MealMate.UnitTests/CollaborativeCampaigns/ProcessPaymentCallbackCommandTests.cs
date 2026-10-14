using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.DTOs;
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

public class ProcessPaymentCallbackCommandTests
{
    private readonly AppDbContext _context;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly ILatePaymentResolutionService _latePaymentService = Substitute.For<ILatePaymentResolutionService>();
    private readonly ProcessPaymentCallbackCommandHandler _handler;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public ProcessPaymentCallbackCommandTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        _context = new AppDbContext(options, dispatcher);
        _clock.UtcNow.Returns(_now);

        _handler = new ProcessPaymentCallbackCommandHandler(
            _context,
            _clock,
            _latePaymentService);
    }

    private (Campaign Campaign, CampaignCheckoutSession Session, Guid RestA, DateOnly Date1) SeedSession(
        CampaignCheckoutSessionStatus initialStatus = CampaignCheckoutSessionStatus.Pending,
        bool isExpired = false)
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-1),
            EndsAtUtc = _now.AddDays(5),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            MaxSubscribers = 100
        };
        _context.Campaigns.Add(campaign);

        var restA = Guid.NewGuid();
        var date1 = new DateOnly(2026, 10, 6);
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
            Status = initialStatus,
            ExpiresAtUtc = isExpired ? _now.AddMinutes(-5) : _now.AddMinutes(10),
            FinalAmount = 50.000m,
            CurrencyCode = "KWD"
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
    public async Task Handle_WhenPaymentSucceedsAndNotExpired_MarksSessionPaidAndRecordsTransaction()
    {
        var (_, session, _, _) = SeedSession();

        var command = new ProcessPaymentCallbackCommand(
            CheckoutSessionId: session.Id,
            PaymentProvider: "Hesabe",
            ProviderTransactionId: "tx-hesabe-001",
            ProviderPaymentReference: "ref-001",
            Amount: 50.000m,
            Currency: "KWD",
            IsSuccess: true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SessionStatus.Should().Be(nameof(CampaignCheckoutSessionStatus.Paid));
        result.Value.IsLatePayment.Should().BeFalse();

        // Verify session in db
        var dbSession = await _context.CampaignCheckoutSessions.FindAsync(session.Id);
        dbSession!.Status.Should().Be(CampaignCheckoutSessionStatus.Paid);

        // Verify transaction in db
        var tx = await _context.CampaignPaymentTransactions
            .FirstOrDefaultAsync(t => t.CheckoutSessionId == session.Id);
        tx.Should().NotBeNull();
        tx!.Status.Should().Be(PaymentTransactionStatus.Succeeded);
        tx.ProviderTransactionId.Should().Be("tx-hesabe-001");
    }

    [Fact]
    public async Task Handle_WhenDuplicateCallbackReceived_ReturnsExistingTransactionWithoutDuplicateProcessing()
    {
        var (_, session, _, _) = SeedSession();

        var command = new ProcessPaymentCallbackCommand(
            CheckoutSessionId: session.Id,
            PaymentProvider: "Hesabe",
            ProviderTransactionId: "tx-duplicate-123",
            ProviderPaymentReference: "ref-123",
            Amount: 50.000m,
            Currency: "KWD",
            IsSuccess: true);

        var firstResult = await _handler.Handle(command, CancellationToken.None);
        var secondResult = await _handler.Handle(command, CancellationToken.None);

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        secondResult.Value.PaymentTransactionId.Should().Be(firstResult.Value.PaymentTransactionId);

        // Exactly 1 transaction in DB
        var count = await _context.CampaignPaymentTransactions
            .CountAsync(t => t.ProviderTransactionId == "tx-duplicate-123");
        count.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenPaymentFails_MarksSessionPaymentFailedAndReleasesReservedCapacity()
    {
        var (campaign, session, restA, date1) = SeedSession();

        var command = new ProcessPaymentCallbackCommand(
            CheckoutSessionId: session.Id,
            PaymentProvider: "Hesabe",
            ProviderTransactionId: "tx-fail-555",
            ProviderPaymentReference: null,
            Amount: 50.000m,
            Currency: "KWD",
            IsSuccess: false,
            FailureReason: "Card declined");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SessionStatus.Should().Be(nameof(CampaignCheckoutSessionStatus.PaymentFailed));

        var dbSession = await _context.CampaignCheckoutSessions.FindAsync(session.Id);
        dbSession!.Status.Should().Be(CampaignCheckoutSessionStatus.PaymentFailed);

        // ReservedBoxes must be decremented from 1 back to 0
        var cap = await _context.CampaignRestaurantCapacities
            .SingleAsync(c => c.CampaignId == campaign.Id && c.RestaurantId == restA && c.ServiceDate == date1);
        cap.ReservedBoxes.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenSessionNotFound_ReturnsNotFound()
    {
        var command = new ProcessPaymentCallbackCommand(
            CheckoutSessionId: Guid.NewGuid(),
            PaymentProvider: "Hesabe",
            ProviderTransactionId: "tx-notfound",
            ProviderPaymentReference: null,
            Amount: 50.000m,
            Currency: "KWD",
            IsSuccess: true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Campaign.SessionNotFound");
    }

    [Fact]
    public async Task Handle_WhenAmountDiffersFromQuote_RejectsCallbackWithoutMarkingPaid()
    {
        var (_, session, _, _) = SeedSession();

        var command = new ProcessPaymentCallbackCommand(
            CheckoutSessionId: session.Id,
            PaymentProvider: "Hesabe",
            ProviderTransactionId: "tx-amount-mismatch",
            ProviderPaymentReference: null,
            Amount: 99.999m, // != server quote 50.000
            Currency: "KWD",
            IsSuccess: true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(MessageCodes.Payment.WebhookAmountInvalid);

        var dbSession = await _context.CampaignCheckoutSessions.FindAsync(session.Id);
        dbSession!.Status.Should().Be(CampaignCheckoutSessionStatus.Pending);
        (await _context.CampaignPaymentTransactions.CountAsync(t => t.CheckoutSessionId == session.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenCurrencyDiffersFromQuote_RejectsCallback()
    {
        var (_, session, _, _) = SeedSession();

        var command = new ProcessPaymentCallbackCommand(
            CheckoutSessionId: session.Id,
            PaymentProvider: "Hesabe",
            ProviderTransactionId: "tx-currency-mismatch",
            ProviderPaymentReference: null,
            Amount: 50.000m,
            Currency: "AED",
            IsSuccess: true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(MessageCodes.Payment.WebhookAmountInvalid);
    }

    [Fact]
    public async Task Handle_WhenPaymentSucceedsOnExpiredSession_DelegatesToLatePaymentResolution()
    {
        var (_, session, _, _) = SeedSession(CampaignCheckoutSessionStatus.Expired, isExpired: true);

        _latePaymentService.ResolveLatePaymentAsync(
            Arg.Any<CampaignCheckoutSession>(),
            Arg.Any<CampaignPaymentTransaction>(),
            Arg.Any<CancellationToken>())
            .Returns(new LatePaymentResolutionResult(
                Success: true,
                NewSessionStatus: CampaignCheckoutSessionStatus.Paid,
                RequiresRefund: false,
                Message: "Capacity reacquired"));

        var command = new ProcessPaymentCallbackCommand(
            CheckoutSessionId: session.Id,
            PaymentProvider: "Hesabe",
            ProviderTransactionId: "tx-late-777",
            ProviderPaymentReference: null,
            Amount: 50.000m,
            Currency: "KWD",
            IsSuccess: true);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsLatePayment.Should().BeTrue();

        await _latePaymentService.Received(1).ResolveLatePaymentAsync(
            Arg.Is<CampaignCheckoutSession>(s => s.Id == session.Id),
            Arg.Any<CampaignPaymentTransaction>(),
            Arg.Any<CancellationToken>());
    }
}
