using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Advertising.Admin;
using MealMate.Application.Features.Advertising.Customer;
using MealMate.Application.Features.Advertising.Restaurant;
using MealMate.Application.Features.Advertising.Services;
using MealMate.Domain.Advertising;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.Entities;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Advertising;

public class AdvertisingLifecycleTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    public AdvertisingLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.RestaurantId.Returns((Guid?)null);

        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(Now);
    }

    public void Dispose() => _context.Dispose();

    private AdCommercialPolicy SeedApprovedPolicy(decimal minBid = 20m, decimal step = 5m)
    {
        var policy = AdCommercialPolicy.Create(1, "KWD", minBid, step,
            AdCommercialPolicy.TiePolicyFirstAccepted, 1440,
            AdCommercialPolicy.NextBidderPolicyOfferWithConsent, "NoRefund").Value;
        policy.Approve(Guid.NewGuid(), Now);
        _context.AdCommercialPolicies.Add(policy);
        _context.SaveChanges();
        return policy;
    }

    private AdSpace SeedSpace(string key = "offers.hero") =>
        _context.AdSpaces.Add(new AdSpace
        {
            PlacementKey = key, NameAr = "بانر", NameEn = "Hero",
            WidthPx = 800, HeightPx = 300, MarketIdsJson = "[]"
        }).Entity;

    internal AdAuction SeedOpenAuction(AdSpace? space = null, AdCommercialPolicy? policy = null)
    {
        var auction = new AdAuction
        {
            AdSpaceId = (space ?? SeedSpace()).Id,
            AdSpace = space!,
            Status = AdAuctionStatus.Open,
            BidsOpenAtUtc = Now.AddHours(-1),
            BidsCloseAtUtc = Now.AddHours(1),
            DisplayStartsAtUtc = Now.AddDays(1),
            DisplayEndsAtUtc = Now.AddDays(8),
            PolicyId = policy?.Id ?? Guid.NewGuid(),
            PolicyVersion = policy?.Version ?? 1,
            CurrencyCode = policy?.CurrencyCode ?? "KWD"
        };
        _context.AdAuctions.Add(auction);
        _context.SaveChanges();
        return auction;
    }

    private void EnableAuctions()
    {
        _context.AdvertisingSettings.Add(new AdvertisingSettings
        {
            AuctionsEnabled = true, BusinessTimezone = "Asia/Kuwait", ExpectedVersion = 1
        });
        _context.SaveChanges();
    }

    [Fact]
    public async Task CreateAuction_WithoutApprovedPolicy_IsBlocked()
    {
        EnableAuctions();
        var space = SeedSpace();
        var handler = new CreateAdAuctionCommandHandler(_context, _clock);

        var result = await handler.Handle(new CreateAdAuctionCommand(
            space.Id, Now.AddMinutes(5), Now.AddHours(1), Now.AddDays(1), Now.AddDays(8)), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Advertising.CommercialPolicyNotApproved");
    }

    [Fact]
    public async Task CreateAuction_WithGlobalSwitchOff_IsBlocked()
    {
        SeedApprovedPolicy();
        var space = SeedSpace();
        var handler = new CreateAdAuctionCommandHandler(_context, _clock);

        var result = await handler.Handle(new CreateAdAuctionCommand(
            space.Id, Now.AddMinutes(5), Now.AddHours(1), Now.AddDays(1), Now.AddDays(8)), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Advertising.AuctionsDisabled");
    }

    [Fact]
    public async Task CreateAuction_OverlappingDisplayWindow_IsRejected()
    {
        EnableAuctions();
        var policy = SeedApprovedPolicy();
        var space = SeedSpace();
        SeedOpenAuction(space, policy);
        var handler = new CreateAdAuctionCommandHandler(_context, _clock);

        var result = await handler.Handle(new CreateAdAuctionCommand(
            space.Id, Now.AddHours(2), Now.AddHours(3), Now.AddDays(2), Now.AddDays(9)), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Advertising.DisplayWindowOverlap");
    }

    [Fact]
    public async Task PlaceBid_BelowMinimum_Or_OffStep_IsRejected()
    {
        EnableAuctions();
        var policy = SeedApprovedPolicy(minBid: 20m, step: 5m);
        var auction = SeedOpenAuction(policy: policy);
        _currentUser.RestaurantId.Returns(Guid.NewGuid());

        var handler = new PlaceAdBidCommandHandler(_context, _currentUser, _clock);

        var belowMin = await handler.Handle(new PlaceAdBidCommand(auction.Id, 10m, "k1"), CancellationToken.None);
        belowMin.Error!.Code.Should().Be("Advertising.BidBelowMinimum");

        var offStep = await handler.Handle(new PlaceAdBidCommand(auction.Id, 22m, "k2"), CancellationToken.None);
        offStep.Error!.Code.Should().Be("Advertising.BidStepInvalid");

        var onStep = await handler.Handle(new PlaceAdBidCommand(auction.Id, 25m, "k3"), CancellationToken.None);
        onStep.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PlaceBid_SameIdempotencyKey_ReturnsOriginalBidNotDuplicate()
    {
        EnableAuctions();
        var policy = SeedApprovedPolicy();
        var auction = SeedOpenAuction(policy: policy);
        _currentUser.RestaurantId.Returns(Guid.NewGuid());

        var handler = new PlaceAdBidCommandHandler(_context, _currentUser, _clock);
        var first = await handler.Handle(new PlaceAdBidCommand(auction.Id, 20m, "same-key"), CancellationToken.None);
        var second = await handler.Handle(new PlaceAdBidCommand(auction.Id, 30m, "same-key"), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second.Value!.BidId.Should().Be(first.Value.BidId);
        second.Value.Amount.Should().Be(20m, "replay returns the original accepted bid");
        _context.AdBids.Count(b => b.AdAuctionId == auction.Id).Should().Be(1);
    }

    [Fact]
    public async Task Sweep_ClosesAuctionWithTie_ByEarliestAccepted_AndIgnoresGlobalSwitchForPayment()
    {
        EnableAuctions();
        var policy = SeedApprovedPolicy();
        var auction = SeedOpenAuction(policy: policy);

        var restaurantA = Guid.NewGuid();
        var restaurantB = Guid.NewGuid();
        var earlier = Now.AddMinutes(-30);
        var later = Now.AddMinutes(-10);

        _context.AdBids.Add(new AdBid { AdAuctionId = auction.Id, RestaurantId = restaurantA, Amount = 50m, CurrencyCode = "KWD", IdempotencyKey = "a", AcceptedAtUtc = earlier });
        _context.AdBids.Add(new AdBid { AdAuctionId = auction.Id, RestaurantId = restaurantB, Amount = 50m, CurrencyCode = "KWD", IdempotencyKey = "b", AcceptedAtUtc = later });
        auction.BidsCloseAtUtc = Now.AddMinutes(-1); // bidding window has ended
        _context.SaveChanges();

        // Global switch off after bids were accepted: the sweep still closes and awards;
        // only NEW auctions/bids are blocked by the switch.
        var settings = await _context.AdvertisingSettings.FirstAsync();
        settings.AuctionsEnabled = false;
        await _context.SaveChangesAsync();

        var sweep = new AdAuctionSweepService(_context, _clock, NullLogger<AdAuctionSweepService>.Instance);
        var result = await sweep.SweepAsync();

        result.ClosedAuctions.Should().Be(1);
        var closed = await _context.AdAuctions.Include(a => a.Bids).SingleAsync(a => a.Id == auction.Id);
        closed.Status.Should().Be(AdAuctionStatus.AwaitingPayment);
        closed.WinningRestaurantId.Should().Be(restaurantA, "tie resolves to the earliest accepted bid");
        closed.ClosedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task Sweep_WhenNoBids_ClosesAsNoFill()
    {
        EnableAuctions();
        var policy = SeedApprovedPolicy();
        var auction = SeedOpenAuction(policy: policy);
        auction.BidsCloseAtUtc = Now.AddMinutes(-1);
        await _context.SaveChangesAsync();

        var sweep = new AdAuctionSweepService(_context, _clock, NullLogger<AdAuctionSweepService>.Instance);
        await sweep.SweepAsync();

        var closed = await _context.AdAuctions.SingleAsync(a => a.Id == auction.Id);
        closed.Status.Should().Be(AdAuctionStatus.NoFill);
    }

    [Fact]
    public async Task Sweep_WithMissingApprovedPolicy_DoesNotCloseAuction()
    {
        EnableAuctions();
        var auction = SeedOpenAuction();
        auction.BidsCloseAtUtc = Now.AddMinutes(-1);
        await _context.SaveChangesAsync();

        var sweep = new AdAuctionSweepService(_context, _clock, NullLogger<AdAuctionSweepService>.Instance);
        await sweep.SweepAsync();

        (await _context.AdAuctions.SingleAsync(a => a.Id == auction.Id)).Status
            .Should().Be(AdAuctionStatus.Open);
    }

    [Fact]
    public async Task Sweep_UsesAuctionPolicyVersionForPaymentDeadline()
    {
        EnableAuctions();
        var soldPolicy = SeedApprovedPolicy(); // 24-hour deadline captured by the auction
        var auction = SeedOpenAuction(policy: soldPolicy);
        auction.Status = AdAuctionStatus.AwaitingPayment;
        auction.ClosedAtUtc = Now.AddHours(-2);
        auction.WinningRestaurantId = Guid.NewGuid();
        auction.WinningBidId = Guid.NewGuid();
        auction.WinningAmount = 25m;
        var laterPolicy = AdCommercialPolicy.Create(2, "KWD", 20m, 5m,
            AdCommercialPolicy.TiePolicyFirstAccepted, 1,
            AdCommercialPolicy.NextBidderPolicyOfferWithConsent, "NoRefund").Value;
        laterPolicy.Approve(Guid.NewGuid(), Now);
        _context.AdCommercialPolicies.Add(laterPolicy);
        await _context.SaveChangesAsync();

        var sweep = new AdAuctionSweepService(_context, _clock, NullLogger<AdAuctionSweepService>.Instance);
        await sweep.SweepAsync();

        var pending = await _context.AdAuctions.SingleAsync(a => a.Id == auction.Id);
        pending.Status.Should().Be(AdAuctionStatus.AwaitingPayment,
            "later policy versions must not shorten a sold auction's payment window");
        pending.WinningRestaurantId.Should().NotBeNull();
    }

    [Fact]
    public async Task StopPlacement_UsesAuctionPolicyVersionAndCreatesReviewRequest()
    {
        var soldPolicy = AdCommercialPolicy.Create(1, "KWD", 20m, 5m,
            AdCommercialPolicy.TiePolicyFirstAccepted, 1440,
            AdCommercialPolicy.NextBidderPolicyOfferWithConsent, "FullRefundBeforeStart").Value;
        soldPolicy.Approve(Guid.NewGuid(), Now);
        _context.AdCommercialPolicies.Add(soldPolicy);
        var auction = SeedOpenAuction(policy: soldPolicy);
        auction.Status = AdAuctionStatus.Awarded;
        var payment = new AdPaymentTransaction
        {
            AdAuctionId = auction.Id, RestaurantId = Guid.NewGuid(), PaymentProvider = "test",
            ProviderTransactionId = "payment-1", Amount = 50m, CurrencyCode = "KWD",
            Status = "Succeeded", InitiatedAtUtc = Now.AddDays(-1), CompletedAtUtc = Now.AddDays(-1)
        };
        _context.AdPaymentTransactions.Add(payment);
        var placement = new AdPlacement
        {
            AdAuctionId = auction.Id, RestaurantId = payment.RestaurantId, AdSpaceId = auction.AdSpaceId,
            Status = AdPlacementStatus.Scheduled, DisplayStartsAtUtc = Now.AddDays(1), DisplayEndsAtUtc = Now.AddDays(8)
        };
        _context.AdPlacements.Add(placement);
        var laterPolicy = AdCommercialPolicy.Create(2, "KWD", 20m, 5m,
            AdCommercialPolicy.TiePolicyFirstAccepted, 1440,
            AdCommercialPolicy.NextBidderPolicyOfferWithConsent, "NoRefund").Value;
        laterPolicy.Approve(Guid.NewGuid(), Now);
        _context.AdCommercialPolicies.Add(laterPolicy);
        await _context.SaveChangesAsync();

        var core = new StopAdPlacementCore(_context, _clock);
        var result = await core.StopAsync(placement.Id, Guid.NewGuid(), null, "restaurant request", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be("RefundAwaitingReview",
            "stopping a paid ad must request review before any provider disbursement");
        var request = await _context.AdFinancialLedgerEntries.SingleAsync(e => e.AdAuctionId == auction.Id);
        request.Action.Should().Be("RefundRequest");
        request.Amount.Should().Be(50m);
        request.RefundReviewStatus.Should().Be("Pending");
        request.RefundApprovalPolicyVersion.Should().Be(soldPolicy.Version);
    }

    [Fact]
    public async Task RefundReconciliation_RequiresApprovalAndMatchingProviderConfirmation()
    {
        var policy = SeedApprovedPolicy();
        var auction = SeedOpenAuction(policy: policy);
        auction.Status = AdAuctionStatus.Awarded;
        var payment = new AdPaymentTransaction
        {
            AdAuctionId = auction.Id, RestaurantId = Guid.NewGuid(), PaymentProvider = "Payzah",
            ProviderTransactionId = "pz-track-2", Amount = 25m, CurrencyCode = "KWD",
            Status = "RefundAwaitingReview", InitiatedAtUtc = Now.AddDays(-1)
        };
        var attempt = new PaymentGatewayAttempt
        {
            Provider = "Payzah", TrackId = "track-2", PaymentId = "raw-payzah-2",
            CustomerReference = $"ad-auction:{auction.Id:N}:{payment.RestaurantId:N}",
            ExpectedAmount = 25m, ExpectedCurrency = "KWD", ProviderReference = "invoice-1",
            IsSandbox = true
        };
        var entry = new AdFinancialLedgerEntry
        {
            AdAuctionId = auction.Id, RestaurantId = payment.RestaurantId, Source = "AdStopSettlement",
            Action = "RefundRequest", Amount = 25m, CurrencyCode = "KWD",
            ProviderTransactionId = payment.ProviderTransactionId, RefundReviewStatus = "Pending",
            RefundReviewReason = "stopped before display", OccurredAtUtc = Now
        };
        _context.AdPaymentTransactions.Add(payment);
        _context.PaymentGatewayAttempts.Add(attempt);
        _context.AdFinancialLedgerEntries.Add(entry);
        await _context.SaveChangesAsync();

        var gateway = Substitute.For<IPaymentGatewayService>();
        var service = new AdPaymentReconciliationService(_context, gateway, _clock,
            NullLogger<AdPaymentReconciliationService>.Instance);
        var beforeApproval = await service.ProcessPendingRefundsAsync();
        beforeApproval.Attempted.Should().Be(0);
        await gateway.DidNotReceive().MakeRefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        var admin = Substitute.For<ICurrentUser>();
        admin.UserId.Returns(Guid.NewGuid());
        var review = new ReviewAdRefundRequestCommandHandler(_context, admin, _clock);
        (await review.Handle(new ReviewAdRefundRequestCommand(entry.Id, true, "approved per unused period"),
            CancellationToken.None)).IsSuccess.Should().BeTrue();

        var externalId = $"ad-refund-{entry.Id:N}";
        gateway.MakeRefundAsync(Arg.Any<string>(), 25m, "KWD", externalId,
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentGatewayRefundReceipt("provider-refund-1", "reference-1", externalId, 25m));
        gateway.GetRefundStatusAsync("provider-refund-1", "RefundId", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PaymentGatewayRefundStatus>>([]));

        var acceptedOnly = await service.ProcessPendingRefundsAsync();
        acceptedOnly.Refunded.Should().Be(0, "provider submission acceptance is not proof of completed refund");
        payment.Status.Should().Be("RefundSubmitted");

        gateway.GetRefundStatusAsync("provider-refund-1", "RefundId", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PaymentGatewayRefundStatus>>([
                new("provider-refund-1", "reference-1", externalId, "invoice-1", 25m,
                    25m, "KWD", "Refunded")
            ]));
        var confirmed = await service.ProcessPendingRefundsAsync();

        confirmed.Refunded.Should().Be(1);
        payment.Status.Should().Be("Refunded");
        await gateway.Received(1).MakeRefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordPayment_AmountMismatch_IsRejectedWithoutAward()
    {
        EnableAuctions();
        var policy = SeedApprovedPolicy();
        var auction = SeedOpenAuction(policy: policy);
        var winner = Guid.NewGuid();
        auction.Status = AdAuctionStatus.AwaitingPayment;
        auction.WinningRestaurantId = winner;
        auction.WinningAmount = 50m;
        auction.CurrencyCode = "KWD";
        var expectedReference = $"ad-auction:{auction.Id:N}:{winner:N}";
        _context.PaymentGatewayAttempts.Add(new PaymentGatewayAttempt
        {
            Provider = "Payzah", TrackId = "ad-track", PaymentId = "raw-ad-payment",
            CustomerReference = expectedReference, ExpectedAmount = 50m, ExpectedCurrency = "KWD", IsSandbox = true
        });
        _context.PaymentGatewayAttempts.Add(new PaymentGatewayAttempt
        {
            Provider = "Payzah", TrackId = "foreign-track", PaymentId = "foreign-payment",
            CustomerReference = $"ad-auction:{Guid.NewGuid():N}:{winner:N}", ExpectedAmount = 50m,
            ExpectedCurrency = "KWD", IsSandbox = true
        });
        await _context.SaveChangesAsync();

        _currentUser.RestaurantId.Returns(winner);
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentGatewayStatusResponse("invoice", "Paid", 50m, "", "", "", "TX-OK", "Payzah", Now,
                expectedReference, "KWD", "pz-ad-track"));
        var handler = new RecordAdPaymentCommandHandler(_context, _currentUser, _clock, gateway);

        var unrelated = await handler.Handle(new RecordAdPaymentCommand(
            auction.Id, "Payzah", "foreign-payment", 50m, "KWD"), CancellationToken.None);
        unrelated.IsFailure.Should().BeTrue();
        unrelated.Error!.Code.Should().Be("Advertising.PaymentAttemptMismatch");
        await gateway.DidNotReceive().GetPaymentStatusAsync("pz-foreign-track", Arg.Any<string>(), Arg.Any<CancellationToken>());

        var mismatch = await handler.Handle(new RecordAdPaymentCommand(
            auction.Id, "Payzah", "raw-ad-payment", 40m, "KWD"), CancellationToken.None);
        mismatch.IsFailure.Should().BeTrue();
        mismatch.Error!.Code.Should().Be("Advertising.PaymentAmountMismatch");

        var currencyMismatch = await handler.Handle(new RecordAdPaymentCommand(
            auction.Id, "Payzah", "raw-ad-payment", 50m, "AED"), CancellationToken.None);
        currencyMismatch.IsFailure.Should().BeTrue();

        var ok = await handler.Handle(new RecordAdPaymentCommand(
            auction.Id, "Payzah", "raw-ad-payment", 50m, "KWD"), CancellationToken.None);
        ok.IsSuccess.Should().BeTrue();

        var awarded = await _context.AdAuctions.SingleAsync(a => a.Id == auction.Id);
        awarded.Status.Should().Be(AdAuctionStatus.Awarded);
        var createdPlacement = await _context.AdPlacements.SingleAsync(p => p.AdAuctionId == auction.Id);
        createdPlacement.RestaurantId.Should().Be(winner);
        createdPlacement.AdSpaceId.Should().Be(auction.AdSpaceId);
        createdPlacement.Status.Should().Be(AdPlacementStatus.AwaitingCreative);
        _context.AdFinancialLedgerEntries.Count(e => e.AdAuctionId == auction.Id).Should().Be(1);

        // Repeated provider transaction: idempotent, no second charge entry.
        var repeat = await handler.Handle(new RecordAdPaymentCommand(
            auction.Id, "Payzah", "raw-ad-payment", 50m, "KWD"), CancellationToken.None);
        repeat.IsSuccess.Should().BeTrue();
        _context.AdFinancialLedgerEntries.Count(e => e.AdAuctionId == auction.Id).Should().Be(1);
    }

    [Fact]
    public async Task CustomerPlacements_RequirePaidApprovedActive_WindowAndSponsorship()
    {
        EnableAuctions();
        var policy = SeedApprovedPolicy();
        var space = SeedSpace();
        var auction = SeedOpenAuction(space, policy);
        auction.Status = AdAuctionStatus.Awarded;
        await _context.SaveChangesAsync();

        var placement = new AdPlacement
        {
            AdAuctionId = auction.Id,
            RestaurantId = Guid.NewGuid(),
            AdSpaceId = space.Id,
            Status = AdPlacementStatus.Scheduled,
            DisplayStartsAtUtc = Now.AddMinutes(-5),
            DisplayEndsAtUtc = Now.AddDays(7)
        };
        _context.AdPlacements.Add(placement);
        _currentUser.RestaurantId.Returns((Guid?)null);
        _context.CustomerAddresses.Add(new MealMate.Domain.IdentityAccess.CustomerAddress
        {
            UserId = _currentUser.UserId!.Value,
            Label = "Home", AddressLine = "test", Governorate = "Capital", RegionId = Guid.NewGuid(), IsDefault = true
        });
        await _context.SaveChangesAsync();

        var handler = new GetEligibleAdPlacementsQueryHandler(_context, _clock, currentUser: _currentUser);

        // Scheduled + window started: activates through the sweep, then serves.
        var sweep = new AdAuctionSweepService(_context, _clock, NullLogger<AdAuctionSweepService>.Instance);
        await sweep.SweepAsync();

        var result = await handler.Handle(new GetEligibleAdPlacementsQuery("offers.hero"), CancellationToken.None);
        result.Value!.Placements.Should().HaveCount(1);
        result.Value.Placements[0].IsSponsored.Should().BeTrue();
        result.Value.ServerNowUtc.Should().Be(Now);

        // Unknown placement key: honest empty result, never fabricated banners.
        var empty = await handler.Handle(new GetEligibleAdPlacementsQuery("unknown.slot"), CancellationToken.None);
        empty.Value!.Placements.Should().BeEmpty();

        space.IsActive = false;
        await _context.SaveChangesAsync();
        var disabled = await handler.Handle(new GetEligibleAdPlacementsQuery("offers.hero"), CancellationToken.None);
        disabled.Value!.Placements.Should().BeEmpty("a disabled space must not serve an otherwise active placement");
    }

    [Fact]
    public async Task CustomerPlacements_MalformedMarketDataFailsClosed()
    {
        var marketId = Guid.NewGuid();
        var space = SeedSpace();
        space.MarketIdsJson = "not-json";
        var auction = SeedOpenAuction(space);
        auction.Status = AdAuctionStatus.Awarded;
        var placement = new AdPlacement
        {
            AdAuctionId = auction.Id, RestaurantId = Guid.NewGuid(), AdSpaceId = space.Id,
            Status = AdPlacementStatus.Active, DisplayStartsAtUtc = Now.AddMinutes(-1), DisplayEndsAtUtc = Now.AddDays(1)
        };
        _context.AdPlacements.Add(placement);
        _context.CustomerAddresses.Add(new MealMate.Domain.IdentityAccess.CustomerAddress
        {
            UserId = _currentUser.UserId!.Value, Label = "Home", AddressLine = "test", RegionId = marketId, IsDefault = true
        });
        await _context.SaveChangesAsync();

        var handler = new GetEligibleAdPlacementsQueryHandler(_context, _clock, currentUser: _currentUser);
        var result = await handler.Handle(new GetEligibleAdPlacementsQuery("offers.hero"), CancellationToken.None);
        result.Value!.Placements.Should().BeEmpty();
    }

    [Fact]
    public async Task AdEvent_SameEventId_CountedOnce()
    {
        var auction = SeedOpenAuction();
        var placement = new AdPlacement
        {
            AdAuctionId = auction.Id, RestaurantId = Guid.NewGuid(),
            AdSpaceId = auction.AdSpaceId, Status = AdPlacementStatus.Active
        };
        _context.AdPlacements.Add(placement);
        await _context.SaveChangesAsync();

        var handler = new RecordAdEventCommandHandler(_context, _clock);

        var first = await handler.Handle(new RecordAdEventCommand(placement.Id, "evt-1", "impression"), CancellationToken.None);
        var second = await handler.Handle(new RecordAdEventCommand(placement.Id, "evt-1", "impression"), CancellationToken.None);
        var badType = await handler.Handle(new RecordAdEventCommand(placement.Id, "evt-2", "hover"), CancellationToken.None);
        var unknown = await handler.Handle(new RecordAdEventCommand(Guid.NewGuid(), "evt-3", "click"), CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue("duplicate event id is an idempotent no-op");
        badType.IsFailure.Should().BeTrue();
        unknown.IsFailure.Should().BeTrue("unknown placement is never logged as phantom traffic");
        _context.AdPlacementEvents.Count().Should().Be(1);
    }

    [Fact]
    public async Task Creative_DefaultRestaurantDestinationUsesPersistedWinner()
    {
        var restaurantId = Guid.NewGuid();
        _currentUser.RestaurantId.Returns(restaurantId);
        var auction = SeedOpenAuction();
        auction.Status = AdAuctionStatus.Awarded;
        var placement = new AdPlacement
        {
            AdAuctionId = auction.Id, RestaurantId = restaurantId, AdSpaceId = auction.AdSpaceId,
            Status = AdPlacementStatus.AwaitingCreative
        };
        _context.AdPlacements.Add(placement);
        _context.UploadFileRecords.Add(new UploadFileRecord
        {
            StorageKey = "owned-banner", UploadedByUserId = _currentUser.UserId!.Value,
            FileName = "banner.png", ContentType = "image/png", Scope = "advertising"
        });
        await _context.SaveChangesAsync();
        var handler = new SubmitAdCreativeCommandHandler(_context, _currentUser, _clock);

        var result = await handler.Handle(new SubmitAdCreativeCommand(auction.Id, "owned-banner", "restaurant", null,
            auction.DisplayStartsAtUtc, auction.DisplayEndsAtUtc), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        placement.TargetType.Should().Be("restaurant");
        placement.TargetId.Should().Be(restaurantId, "the existing restaurant UI does not supply a destination ID");
        placement.Status.Should().Be(AdPlacementStatus.UnderReview);
    }

    [Fact]
    public async Task AdEvent_NormalizedReplayDoesNotAddAnotherImpression()
    {
        var auction = SeedOpenAuction();
        var placement = new AdPlacement
        {
            AdAuctionId = auction.Id, RestaurantId = Guid.NewGuid(),
            AdSpaceId = auction.AdSpaceId, Status = AdPlacementStatus.Active
        };
        _context.AdPlacements.Add(placement);
        await _context.SaveChangesAsync();
        var handler = new RecordAdEventCommandHandler(_context, _clock);
        await handler.Handle(new RecordAdEventCommand(placement.Id, " impression-1 ", "impression"), CancellationToken.None);
        await handler.Handle(new RecordAdEventCommand(placement.Id, " impression-1 ", "impression"), CancellationToken.None);

        _context.AdPlacementEvents.Count().Should().Be(1);
    }

    [Fact]
    public async Task Placement_CannotServeWithoutPaidAuction()
    {
        var auction = SeedOpenAuction(); // still Open (unpaid)
        var placement = new AdPlacement
        {
            AdAuctionId = auction.Id, RestaurantId = Guid.NewGuid(),
            AdSpaceId = auction.AdSpaceId, Status = AdPlacementStatus.Scheduled,
            DisplayStartsAtUtc = Now.AddMinutes(-1), DisplayEndsAtUtc = Now.AddDays(1)
        };
        _context.AdPlacements.Add(placement);
        await _context.SaveChangesAsync();

        var sweep = new AdAuctionSweepService(_context, _clock, NullLogger<AdAuctionSweepService>.Instance);
        await sweep.SweepAsync();

        var persisted = await _context.AdPlacements.SingleAsync();
        persisted.Status.Should().NotBe(AdPlacementStatus.Active,
            "an unpaid auction can never produce a serving placement");
    }

    [Fact]
    public async Task AdPaymentReconciliation_FailedOrExpiredProviderStatus_TransitionsTransactionToFailedAndAllowsRetry()
    {
        var winner = Guid.NewGuid();
        var auction = SeedOpenAuction();
        auction.Status = AdAuctionStatus.AwaitingPayment;
        auction.WinningRestaurantId = winner;
        auction.WinningAmount = 50m;
        auction.CurrencyCode = "KWD";
        var payment = new AdPaymentTransaction
        {
            AdAuctionId = auction.Id,
            RestaurantId = winner,
            PaymentProvider = "Payzah",
            ProviderTransactionId = "pz-failed-123",
            Amount = 50m,
            CurrencyCode = "KWD",
            Status = "Pending",
            InitiatedAtUtc = Now.AddMinutes(-10)
        };
        _context.AdPaymentTransactions.Add(payment);
        await _context.SaveChangesAsync();

        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("pz-failed-123", cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new PaymentGatewayStatusResponse(
                "inv-failed-1", "Failed", 50m, "Restaurant", "r@example.com", "50000000",
                "TX-FAIL", "Payzah", Now, $"ad-auction:{auction.Id:N}:{winner:N}", "KWD", "pz-failed-123"));

        var reconciler = new AdPaymentReconciliationService(_context, gateway, _clock);
        var result = await reconciler.ProcessPendingPaymentsAsync();

        result.Attempted.Should().Be(1);
        result.Finalized.Should().Be(1);
        result.StillPending.Should().Be(0);

        var refreshed = await _context.AdPaymentTransactions.SingleAsync(x => x.Id == payment.Id);
        refreshed.Status.Should().Be("Failed");
        refreshed.CompletedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task RecordAdPayment_WhenProviderPaymentFailed_UpdatesPendingTransactionToFailed()
    {
        var winner = Guid.NewGuid();
        _currentUser.RestaurantId.Returns(winner);
        var auction = SeedOpenAuction();
        auction.Status = AdAuctionStatus.AwaitingPayment;
        auction.WinningRestaurantId = winner;
        auction.WinningAmount = 50m;
        auction.CurrencyCode = "KWD";

        _context.PaymentGatewayAttempts.Add(new PaymentGatewayAttempt
        {
            Provider = "Payzah",
            TrackId = "track-fail-1",
            CustomerReference = $"ad-auction:{auction.Id:N}:{winner:N}",
            ExpectedAmount = 50m,
            ExpectedCurrency = "KWD",
            CreatedAtUtc = Now.AddMinutes(-5)
        });

        var existingTx = new AdPaymentTransaction
        {
            AdAuctionId = auction.Id,
            RestaurantId = winner,
            PaymentProvider = "Payzah",
            ProviderTransactionId = "pz-track-fail-1",
            Amount = 50m,
            CurrencyCode = "KWD",
            Status = "Pending",
            InitiatedAtUtc = Now.AddMinutes(-5)
        };
        _context.AdPaymentTransactions.Add(existingTx);
        await _context.SaveChangesAsync();

        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("pz-track-fail-1", cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new PaymentGatewayStatusResponse(
                "inv-canceled", "Canceled", 50m, "Restaurant", "r@example.com", "50000000",
                "TX-CANC", "Payzah", Now, $"ad-auction:{auction.Id:N}:{winner:N}", "KWD", "pz-track-fail-1"));

        var handler = new RecordAdPaymentCommandHandler(_context, _currentUser, _clock, gateway);

        var response = await handler.Handle(new RecordAdPaymentCommand(
            auction.Id, "Payzah", "pz-track-fail-1", 50m, "KWD"), CancellationToken.None);

        response.IsFailure.Should().BeTrue();
        response.Error!.Code.Should().Be("Advertising.PaymentFailed");

        var refreshed = await _context.AdPaymentTransactions.SingleAsync(x => x.Id == existingTx.Id);
        refreshed.Status.Should().Be("Failed");
        refreshed.CompletedAtUtc.Should().Be(Now);
    }
}
