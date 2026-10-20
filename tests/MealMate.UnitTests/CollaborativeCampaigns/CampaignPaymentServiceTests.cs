using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.Foundation;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CollaborativeCampaigns;

public sealed class CampaignPaymentServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly IUserAccountService _users = Substitute.For<IUserAccountService>();
    private readonly IPaymentGatewayService _gateway = Substitute.For<IPaymentGatewayService>();
    private readonly IApplicationLockManager _locks = Substitute.For<IApplicationLockManager>();
    private readonly ILatePaymentResolutionService _latePayments = Substitute.For<ILatePaymentResolutionService>();
    private readonly IConfiguration _configuration;
    private readonly DateTime _now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();

    public CampaignPaymentServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        _clock.UtcNow.Returns(_now);
        _locks.LockCampaignPaymentKeyAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _locks.LockCampaignCheckoutSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Checkout:DefaultCallbackUrl"] = "https://mealmate.example/pay/callback",
            ["Checkout:DefaultErrorUrl"] = "https://mealmate.example/pay/error",
            ["Checkout:AllowedRedirectSchemes:0"] = "https",
            ["Checkout:AllowedRedirectHosts:0"] = "mealmate.example"
        }).Build();
        SeedSession();
        _users.FindByIdAsync(_customerId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            _customerId, "customer@mealmate.example", "+96550000000", "Customer", UserType.Customer,
            AccountStatus.Active, null, true, true, _now, null));
    }

    [Fact]
    public async Task StartAsync_PersistsBeforeGatewayAndReplaysOriginalReceiptForSameKey()
    {
        var receipt = new PaymentCreationReceipt("provider-id", "https://payzah.example/checkout", "track-123");
        _gateway.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _db.IdempotencyRecords.Count(x => x.Scope == $"CampaignPayment:{_customerId:N}").Should().Be(1);
                return receipt;
            });
        var service = CreateService();
        var request = new StartCampaignPaymentRequest("payment-key", PaymentMethodId: 3);

        var first = await service.StartAsync(_sessionId, _customerId, request);
        var replay = await service.StartAsync(_sessionId, _customerId, request);

        first.IsSuccess.Should().BeTrue();
        first.Value.PaymentId.Should().Be("pz-track-123");
        first.Value.PaymentUrl.Should().Be(receipt.PaymentUrl);
        replay.Value.Should().Be(first.Value);
        await _gateway.Received(1).ExecutePaymentAsync(50.000m, "Customer", "customer@mealmate.example", "+96550000000",
            "https://mealmate.example/pay/callback", "https://mealmate.example/pay/error",
            $"campaign-checkout:{_sessionId:N}", 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_RejectsReusedKeyWithDifferentPayloadAndDoesNotCallGatewayAgain()
    {
        SetGatewayReceipt();
        var service = CreateService();
        await service.StartAsync(_sessionId, _customerId, new StartCampaignPaymentRequest("same-key"));

        var result = await service.StartAsync(_sessionId, _customerId,
            new StartCampaignPaymentRequest("same-key", ErrorUrl: "https://mealmate.example/other"));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        await _gateway.Received(1).ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_HidesSessionOwnedByAnotherCustomer()
    {
        var service = CreateService();

        var result = await service.StartAsync(_sessionId, Guid.NewGuid(), new StartCampaignPaymentRequest("owner-key"));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        await _gateway.DidNotReceiveWithAnyArgs().ExecutePaymentAsync(default, default!, default!, default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task StartAsync_RejectsRedirectOutsideConfiguredHost()
    {
        var service = CreateService();

        var result = await service.StartAsync(_sessionId, _customerId,
            new StartCampaignPaymentRequest("url-key", CallbackUrl: "https://attacker.example/callback"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("campaign.payment.return_url_not_allowed");
        await _gateway.DidNotReceiveWithAnyArgs().ExecutePaymentAsync(default, default!, default!, default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task StartAsync_RejectsSessionWithoutSealedQuote()
    {
        var session = await _db.CampaignCheckoutSessions.SingleAsync();
        session.FinalAmount = null;
        await _db.SaveChangesAsync();
        var service = CreateService();

        var result = await service.StartAsync(_sessionId, _customerId, new StartCampaignPaymentRequest("no-quote"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("campaign.payment.quote_missing");
        await _gateway.DidNotReceiveWithAnyArgs().ExecutePaymentAsync(default, default!, default!, default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task StartAsync_DoesNotInventMissingConfiguredReturnUrls()
    {
        var configuration = new ConfigurationBuilder().Build();
        var service = new CampaignPaymentService(_db, new PostgresApplicationTransactionExecutor(_db), _locks,
            _gateway, _users, _latePayments, _clock, configuration);

        var result = await service.StartAsync(_sessionId, _customerId, new StartCampaignPaymentRequest("no-default-url"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("campaign.payment.return_url_missing");
        await _gateway.DidNotReceiveWithAnyArgs().ExecutePaymentAsync(default, default!, default!, default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task StartAsync_RejectsCancelledAttemptWithoutCreatingAnotherPayment()
    {
        var session = await _db.CampaignCheckoutSessions.SingleAsync();
        session.PaymentWasCancelled = true;
        session.Status = CampaignCheckoutSessionStatus.PaymentFailed;
        await _db.SaveChangesAsync();
        var service = CreateService();

        var result = await service.StartAsync(_sessionId, _customerId, new StartCampaignPaymentRequest("new-key-after-cancel"));

        result.IsFailure.Should().BeTrue();
        await _gateway.DidNotReceiveWithAnyArgs().ExecutePaymentAsync(default, default!, default!, default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task StartAsync_RejectsExpiredHoldWithoutCallingProvider()
    {
        var session = await _db.CampaignCheckoutSessions.SingleAsync();
        session.ExpiresAtUtc = _now.AddSeconds(-1);
        session.Status = CampaignCheckoutSessionStatus.Expired;
        await _db.SaveChangesAsync();
        var service = CreateService();

        var result = await service.StartAsync(_sessionId, _customerId, new StartCampaignPaymentRequest("expired-key"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("campaign.payment.session_expired");
        result.Error.Type.Should().Be(ErrorType.Gone);
        result.Error.Metadata.Should().ContainKey("refreshSummary").WhoseValue.Should().Be(true);
        result.Error.Metadata.Should().ContainKey("refreshAvailability").WhoseValue.Should().Be(true);
        await _gateway.DidNotReceiveWithAnyArgs().ExecutePaymentAsync(default, default!, default!, default!, default!, default!, default, default, default);
    }

    [Fact]
    public async Task StartAsync_RecordsAmbiguousGatewayFailureAndNeverSubmitsAgainForThatKey()
    {
        _gateway.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<PaymentCreationReceipt>>(_ => throw new HttpRequestException("connection lost after request"));
        var service = CreateService();

        var first = await service.StartAsync(_sessionId, _customerId, new StartCampaignPaymentRequest("uncertain-key"));
        var retry = await service.StartAsync(_sessionId, _customerId, new StartCampaignPaymentRequest("uncertain-key"));

        first.IsFailure.Should().BeTrue();
        first.Error.Code.Should().Be("campaign.payment.initialization_unknown");
        retry.IsFailure.Should().BeTrue();
        await _gateway.Received(1).ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        (await _db.CampaignCheckoutSessions.SingleAsync()).PaymentInitializationErrorCode.Should().Be("campaign.payment.initialization_unknown");
    }

    [Fact]
    public async Task GetStatusAsync_RequiresActualCustomerOwnership()
    {
        var service = CreateService();

        var result = await service.GetStatusAsync(_sessionId, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task ProcessCallbackAsync_VerifiesPaidPaymentAndCreatesOnlyOneSubscription()
    {
        var (campaign, capacity) = PrepareInitiatedPayment();
        var catalogProgram = new Program { Code = "campaign-program", NameAr = "برنامج", NameEn = "Program" };
        var catalogBundle = new Bundle { Code = "campaign-bundle", NameAr = "باقة", NameEn = "Bundle", ProgramId = catalogProgram.Id };
        _db.Programs.Add(catalogProgram);
        _db.Bundles.Add(catalogBundle);
        var scope = new CampaignBundle { CampaignId = campaign.Id, BundleId = catalogBundle.Id };
        _db.CampaignBundles.Add(scope);
        var session = await _db.CampaignCheckoutSessions.SingleAsync();
        session.BundleId = scope.Id;
        foreach (var allocation in session.Allocations) allocation.BundleId = scope.Id;
        _db.ChangeTracker.DetectChanges();
        foreach (var entry in _db.ChangeTracker.Entries()
                     .Where(x => x.State == EntityState.Modified && !ReferenceEquals(x.Entity, session)))
            entry.State = EntityState.Unchanged;
        await _db.SaveChangesAsync();
        var response = VerifiedStatus("Paid");
        _gateway.GetPaymentStatusAsync("pz-track-123", "PaymentId", Arg.Any<CancellationToken>()).Returns(response);
        var service = CreateService();
        var callback = new ProcessPaymentCallbackCommand(_sessionId, "Payzah", response.TransactionId, null,
            50.000m, "KWD", IsSuccess: true);

        var first = await service.ProcessCallbackAsync(callback);
        var duplicate = await service.ProcessCallbackAsync(callback with { ProviderTransactionId = "provider-id" });

        first.IsSuccess.Should().BeTrue();
        first.Value.SessionStatus.Should().Be(nameof(CampaignCheckoutSessionStatus.Completed));
        duplicate.IsSuccess.Should().BeTrue();
        (await _db.CampaignPaymentTransactions.CountAsync(x => x.CheckoutSessionId == _sessionId)).Should().Be(1);
        (await _db.Subscriptions.CountAsync(x => x.CorrelationId == _sessionId.ToString("N"))).Should().Be(1);
        capacity.ReservedBoxes.Should().Be(1);
    }

    [Fact]
    public async Task ProcessCallbackAsync_CancelledProviderStatusReleasesCapacityOnlyOnce()
    {
        var (_, capacity) = PrepareInitiatedPayment();
        var response = VerifiedStatus("Cancelled");
        _gateway.GetPaymentStatusAsync("pz-track-123", "PaymentId", Arg.Any<CancellationToken>()).Returns(response);
        var service = CreateService();
        var callback = new ProcessPaymentCallbackCommand(_sessionId, "Payzah", "pz-track-123", null,
            50.000m, "KWD", IsSuccess: true);

        var first = await service.ProcessCallbackAsync(callback);
        var duplicate = await service.ProcessCallbackAsync(callback);

        first.IsSuccess.Should().BeTrue();
        duplicate.IsSuccess.Should().BeTrue();
        (await _db.CampaignCheckoutSessions.SingleAsync()).PaymentWasCancelled.Should().BeTrue();
        (await _db.CampaignCheckoutSessions.SingleAsync()).ReservationsReleasedAtUtc.Should().Be(_now);
        capacity.ReservedBoxes.Should().Be(0);
        (await _db.CampaignPaymentTransactions.CountAsync(x => x.CheckoutSessionId == _sessionId)).Should().Be(1);
    }

    private CampaignPaymentService CreateService() => new(
        _db, new PostgresApplicationTransactionExecutor(_db), _locks, _gateway, _users, _latePayments, _clock, _configuration);

    private void SetGatewayReceipt() => _gateway.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
        Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
        .Returns(new PaymentCreationReceipt("provider-id", "https://payzah.example/checkout", "track-123"));

    [Fact]
    public async Task ProcessCallbackAsync_LateSuccessAfterReservationRelease_RecoversSubscriptionWithoutDoubleRelease()
    {
        var (campaign, capacity) = PrepareInitiatedPayment();

        // The hold expired and the expiration worker released the boxes before the provider
        // capture arrived late.
        var session = _db.CampaignCheckoutSessions.Single();
        session.Status = CampaignCheckoutSessionStatus.Expired;
        session.ReservationsReleasedAtUtc = _now.AddMinutes(-1);
        capacity.ReservedBoxes = 0;
        _db.SaveChanges();

        _gateway.GetPaymentStatusAsync("pz-track-123", "PaymentId", Arg.Any<CancellationToken>())
            .Returns(VerifiedStatus("CAPTURED"));
        _latePayments.ResolveLatePaymentAsync(Arg.Do<CampaignCheckoutSession>(s => s.MarkPaid()), Arg.Any<CampaignPaymentTransaction>(), Arg.Any<CancellationToken>())
            .Returns(new LatePaymentResolutionResult(true, CampaignCheckoutSessionStatus.Paid, false, "late capture honored"));
        var service = CreateService();

        var result = await service.ProcessCallbackAsync(new ProcessPaymentCallbackCommand(
            _sessionId, "Payzah", "pz-track-123", null, 50.000m, "KWD", true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var freshCapacity = await _db.CampaignRestaurantCapacities.SingleAsync();
        freshCapacity.ReservedBoxes.Should().Be(0); // released exactly once, never negative
        (await _db.Subscriptions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_StoppedCampaignWithLiveHold_StillPayableWithinHoldValidity()
    {
        SetGatewayReceipt();
        var session = _db.CampaignCheckoutSessions.Single();
        var campaign = _db.Campaigns.Single(c => c.Id == session.CampaignId);
        campaign.Status = CampaignStatus.Stopped; // merchant stopped the campaign mid-hold
        _db.SaveChanges();

        var service = CreateService();
        var result = await service.StartAsync(_sessionId, _customerId, new StartCampaignPaymentRequest("stop-hold-key"));

        // Policy: no NEW reservations after stop, but an existing live hold stays payable
        // until its own expiry; a completed subscription is never auto-cancelled.
        result.IsSuccess.Should().BeTrue();
        await _gateway.Received(1).ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private void SeedSession()
    {
        var campaign = new Campaign
        {
            NameAr = "حملة",
            NameEn = "Campaign",
            Status = CampaignStatus.Active,
            StartsAtUtc = _now.AddDays(-1),
            EndsAtUtc = _now.AddDays(4),
            TotalDiscountRate = Percentage.FromFraction(0.20m),
            MealMateShareRate = Percentage.FromFraction(0.10m),
            RestaurantShareRate = Percentage.FromFraction(0.10m),
            MaxSubscribers = 10
        };
        _db.Campaigns.Add(campaign);
                var bundle = new Bundle
        {
            Code = "BUNDLE-PAY-01",
            NameAr = "باقة",
            NameEn = "Bundle",
            ProgramId = Guid.NewGuid()
        };
        _db.Bundles.Add(bundle);
        var bundleId = bundle.Id;
        _db.CampaignBundleDurationPrices.Add(CampaignBundleDurationPrice.Create(bundleId, 1,
            new Money(50.000m, "KWD"), "System", 1).Value);
        _db.CampaignCheckoutSessions.Add(new CampaignCheckoutSession(_sessionId)
        {
            CampaignId = campaign.Id,
            BundleId = bundleId,
            CustomerId = _customerId,
            Status = CampaignCheckoutSessionStatus.Pending,
            ExpiresAtUtc = _now.AddMinutes(15),
            FinalAmount = 50.000m,
            CurrencyCode = "KWD",
            Allocations = []
        });
        _db.SaveChanges();
    }

    private (Campaign Campaign, CampaignRestaurantCapacity Capacity) PrepareInitiatedPayment()
    {
        var session = _db.CampaignCheckoutSessions.Single();
        var campaign = _db.Campaigns.Single(x => x.Id == session.CampaignId);
        var restaurantId = Guid.NewGuid();
        var serviceDate = DateOnly.FromDateTime(_now.AddDays(1));
        var capacity = new CampaignRestaurantCapacity
        {
            CampaignId = campaign.Id,
            RestaurantId = restaurantId,
            ServiceDate = serviceDate,
            MaxBoxes = 5,
            ReservedBoxes = 1
        };
        _db.CampaignRestaurantCapacities.Add(capacity);
        session.Status = CampaignCheckoutSessionStatus.PaymentPending;
        session.PaymentIdempotencyKey = "payment-key";
        session.PaymentRequestHash = "request-hash";
        session.PaymentId = "pz-track-123";
        session.PaymentUrl = "https://payzah.example/checkout";
        session.PaymentInitializationStartedAtUtc = _now;
        session.Allocations.Add(new CampaignReservationAllocation
        {
            CheckoutSessionId = session.Id,
            CampaignId = campaign.Id,
            BundleId = session.BundleId,
            RestaurantId = restaurantId,
            ServiceDate = serviceDate,
            Units = 1
        });
        _db.PaymentGatewayAttempts.Add(new PaymentGatewayAttempt
        {
            Provider = "Payzah",
            TrackId = "track-123",
            PaymentId = "provider-id",
            CustomerReference = $"campaign-checkout:{session.Id:N}",
            ExpectedAmount = 50.000m,
            ExpectedCurrency = "KWD"
        });
        _db.ChangeTracker.DetectChanges();
        foreach (var entry in _db.ChangeTracker.Entries()
                     .Where(x => x.State == EntityState.Modified && !ReferenceEquals(x.Entity, session)))
            entry.State = EntityState.Unchanged;
        _db.SaveChanges();
        return (campaign, capacity);
    }

    private PaymentGatewayStatusResponse VerifiedStatus(string status) => new(
        "provider-reference", status, 50.000m, "Customer", "customer@mealmate.example", "+96550000000",
        "provider-transaction", "Payzah", _now, $"campaign-checkout:{_sessionId:N}", "KWD", "pz-track-123");

    public void Dispose() => _db.Dispose();
}
