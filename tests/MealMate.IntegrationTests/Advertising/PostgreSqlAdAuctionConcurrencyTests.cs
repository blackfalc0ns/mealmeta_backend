using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Advertising.Restaurant;
using MealMate.Application.Features.Advertising.Services;
using MealMate.Domain.Advertising;
using MealMate.Domain.Common;
using MealMate.Domain.Entities;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;
using MealMate.Testing;
using Xunit;

namespace MealMate.IntegrationTests.Advertising;

/// <summary>
/// Real-PostgreSQL guarantees for the advertising auction:
/// unique bid idempotency under concurrent submits, and a single deterministic
/// winner when two workers race to close the same auction.
/// </summary>
public partial class PostgreSqlAdAuctionConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        try
        {
            await _database.StartAsync();
            _connectionString = _database.GetConnectionString();
            using var db = CreateContext();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException("Isolated PostgreSQL database failed to start for advertising tests.", ex);
        }
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static IDateTimeProvider FixedClock(DateTime utcNow)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(utcNow);
        return clock;
    }

    private async Task<(AdAuction Auction, Guid RestaurantId)> SeedOpenAuctionAsync(DateTime now)
    {
        var policy = AdCommercialPolicy.Create(1, "KWD", 20m, 5m,
            AdCommercialPolicy.TiePolicyFirstAccepted, 1440,
            AdCommercialPolicy.NextBidderPolicyOfferWithConsent, "NoRefund").Value;
        policy.Approve(Guid.NewGuid(), now);

        var space = new AdSpace
        {
            PlacementKey = "offers.hero", NameAr = "بانر", NameEn = "Hero",
            WidthPx = 800, HeightPx = 300, MarketIdsJson = "[]"
        };

        var auction = new AdAuction
        {
            AdSpace = space,
            Status = AdAuctionStatus.Open,
            BidsOpenAtUtc = now.AddHours(-1),
            BidsCloseAtUtc = now.AddMinutes(5),
            DisplayStartsAtUtc = now.AddDays(1),
            DisplayEndsAtUtc = now.AddDays(8),
            PolicyId = policy.Id,
            PolicyVersion = policy.Version,
            CurrencyCode = "KWD"
        };

        await using var db = CreateContext();
        db.AdCommercialPolicies.Add(policy);
        db.AdvertisingSettings.Add(new AdvertisingSettings
        {
            AuctionsEnabled = true, BusinessTimezone = "Asia/Kuwait", ExpectedVersion = 1
        });
        db.AdAuctions.Add(auction);
        await db.SaveChangesAsync();
        return (auction, Guid.NewGuid());
    }

    [Fact]
    public async Task ConcurrentBids_WithSameIdempotencyKey_PersistExactlyOneRow()
    {
        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var seeded = await SeedOpenAuctionAsync(now);
        var auctionId = seeded.Auction.Id;
        var restaurantId = seeded.RestaurantId;

        var gate = new TaskCompletionSource();
        const decimal amount = 25m;
        const string idempotencyKey = "race-key-1";

        var runBid = async () =>
        {
            await gate.Task;
            await using var db = CreateContext();
            var clock = FixedClock(now);
            var current = Substitute.For<ICurrentUser>();
            current.IsAuthenticated.Returns(true);
            current.UserId.Returns(Guid.NewGuid());
            current.RestaurantId.Returns(restaurantId);
            var handler = new PlaceAdBidCommandHandler(db, current, clock);
            return await handler.Handle(new PlaceAdBidCommand(auctionId, amount, idempotencyKey), CancellationToken.None);
        };

        var first = runBid();
        var second = runBid();
        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        // One bid is accepted for real; the loser of the unique-index race replays
        // the original accepted bid through the idempotency path (also success).
        results.Should().Contain(r => r.IsSuccess);
        results.Should().OnlyContain(r => r.IsSuccess || r.Error!.Code.Length > 0);

        await using var verify = CreateContext();
        (await verify.AdBids.CountAsync(b => b.AdAuctionId == auctionId && b.IdempotencyKey == idempotencyKey))
            .Should().Be(1, "the unique index makes duplicate idempotent bids impossible");
    }

    [Fact]
    public async Task TwoWorkersClosingTheSameAuction_ProduceOneWinner()
    {
        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var (auction, restaurantId) = await SeedOpenAuctionAsync(now);

        await using var seedDb = CreateContext();
        var tracked = await seedDb.AdAuctions.SingleAsync(x => x.Id == auction.Id);
        tracked.BidsCloseAtUtc = now.AddMinutes(-1); // bidding window ended
        seedDb.AdBids.Add(new AdBid
        {
            AdAuctionId = auction.Id, RestaurantId = restaurantId, Amount = 50m,
            CurrencyCode = "KWD", IdempotencyKey = "w1", AcceptedAtUtc = now.AddMinutes(-1)
        });
        await seedDb.SaveChangesAsync();

        var gate = new TaskCompletionSource();
        var sweep = async () =>
        {
            await gate.Task;
            await using var db = CreateContext();
            var service = new AdAuctionSweepService(db, FixedClock(now), NullLogger<AdAuctionSweepService>.Instance,
                new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db));
            return await service.SweepAsync();
        };

        var first = sweep();
        var second = sweep();
        gate.SetResult();
        await Task.WhenAll(first, second);

        await using var verify = CreateContext();
        var closed = await verify.AdAuctions.Include(a => a.Bids).SingleAsync(a => a.Id == auction.Id);
        closed.Status.Should().Be(AdAuctionStatus.AwaitingPayment);
        closed.WinningRestaurantId.Should().Be(restaurantId);
        closed.WinningAmount.Should().Be(50m);
        closed.Bids.Count(b => b.IsWinning).Should().Be(1);
    }

    [Fact]
    public async Task Bid_AtOrAfterCloseInstant_IsRejected()
    {
        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var (auction, restaurantId) = await SeedOpenAuctionAsync(now);

        await using var db = CreateContext();
        var clock = FixedClock(now.AddMinutes(5)); // exactly at close instant
        var current = Substitute.For<ICurrentUser>();
        current.IsAuthenticated.Returns(true);
        current.UserId.Returns(Guid.NewGuid());
        current.RestaurantId.Returns(restaurantId);

        var handler = new PlaceAdBidCommandHandler(db, current, clock);
        var result = await handler.Handle(new PlaceAdBidCommand(auction.Id, 25m, "late-bid"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("Advertising.BiddingClosed");
    }

    [Fact]
    public async Task Migration_RejectsOverlappingDisplayWindowsForTheSameSpace()
    {
        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var (first, _) = await SeedOpenAuctionAsync(now);
        await using var db = CreateContext();
        var spaceId = await db.AdAuctions.Where(x => x.Id == first.Id).Select(x => x.AdSpaceId).SingleAsync();
        var overlap = new AdAuction
        {
            AdSpaceId = spaceId,
            Status = AdAuctionStatus.Open,
            BidsOpenAtUtc = now,
            BidsCloseAtUtc = now.AddHours(1),
            DisplayStartsAtUtc = first.DisplayStartsAtUtc.AddDays(2),
            DisplayEndsAtUtc = first.DisplayEndsAtUtc.AddDays(2),
            PolicyId = first.PolicyId,
            PolicyVersion = first.PolicyVersion,
            CurrencyCode = "KWD"
        };
        db.AdAuctions.Add(overlap);
        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task PendingNextBidderOfferKeepsWindowReserved_AndExpiryOffersNextUnusedBid()
    {
        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var (auction, winnerRestaurantId) = await SeedOpenAuctionAsync(now);
        var losingOne = Guid.NewGuid();
        var losingTwo = Guid.NewGuid();
        await using (var seed = CreateContext())
        {
            var row = await seed.AdAuctions.Include(x => x.Bids).SingleAsync(x => x.Id == auction.Id);
            row.Status = AdAuctionStatus.AwaitingPayment;
            row.ClosedAtUtc = now.AddDays(-2);
            row.WinningRestaurantId = winnerRestaurantId;
            row.WinningAmount = 50m;
            row.CurrencyCode = "KWD";
            var winner = new AdBid { AdAuctionId = row.Id, RestaurantId = winnerRestaurantId, Amount = 50m, CurrencyCode = "KWD", IdempotencyKey = "winner", AcceptedAtUtc = now.AddMinutes(-3), IsWinning = true };
            var first = new AdBid { AdAuctionId = row.Id, RestaurantId = losingOne, Amount = 40m, CurrencyCode = "KWD", IdempotencyKey = "next-one", AcceptedAtUtc = now.AddMinutes(-2) };
            var second = new AdBid { AdAuctionId = row.Id, RestaurantId = losingTwo, Amount = 30m, CurrencyCode = "KWD", IdempotencyKey = "next-two", AcceptedAtUtc = now.AddMinutes(-1) };
            seed.AdBids.AddRange(winner, first, second);
            seed.AdAuctionNextBidderOffers.Add(new AdAuctionNextBidderOffer
            {
                AdAuctionId = row.Id, AdBidId = first.Id, RestaurantId = losingOne, Amount = first.Amount,
                CurrencyCode = first.CurrencyCode, Status = "Pending", ExpiresAtUtc = now.AddMinutes(10)
            });
            await seed.SaveChangesAsync();
        }

        await using (var pendingDb = CreateContext())
        {
            await new AdAuctionSweepService(pendingDb, FixedClock(now), NullLogger<AdAuctionSweepService>.Instance,
                new PostgresApplicationTransactionExecutor(pendingDb), new PostgresApplicationLockManager(pendingDb)).SweepAsync();
            (await pendingDb.AdAuctions.Where(x => x.Id == auction.Id).Select(x => x.Status).SingleAsync())
                .Should().Be(AdAuctionStatus.AwaitingPayment);
        }

        await using (var expireDb = CreateContext())
        {
            var offer = await expireDb.AdAuctionNextBidderOffers.SingleAsync(x => x.AdAuctionId == auction.Id);
            offer.ExpiresAtUtc = now.AddMinutes(-1);
            await expireDb.SaveChangesAsync();
        }
        await using (var nextDb = CreateContext())
        {
            await new AdAuctionSweepService(nextDb, FixedClock(now), NullLogger<AdAuctionSweepService>.Instance,
                new PostgresApplicationTransactionExecutor(nextDb), new PostgresApplicationLockManager(nextDb)).SweepAsync();
            var nextOffer = await nextDb.AdAuctionNextBidderOffers.AsNoTracking()
                .SingleAsync(x => x.AdAuctionId == auction.Id && x.Status == "Pending");
            nextOffer.RestaurantId.Should().Be(losingTwo);
            (await nextDb.AdAuctions.Where(x => x.Id == auction.Id).Select(x => x.Status).SingleAsync())
                .Should().Be(AdAuctionStatus.AwaitingPayment);
        }
    }

    [Fact]
    public async Task PaymentReconciliation_RequiresAuctionBoundSavedAttempt_AndFinalizesLedgerAndPlacementOnce()
    {
        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var (auction, restaurantId) = await SeedOpenAuctionAsync(now);
        const string handle = "pz-track-123";
        var reference = $"ad-auction:{auction.Id:N}:{restaurantId:N}";
        await using (var seed = CreateContext())
        {
            var row = await seed.AdAuctions.Include(x => x.Bids).SingleAsync(x => x.Id == auction.Id);
            row.Status = AdAuctionStatus.AwaitingPayment;
            row.WinningRestaurantId = restaurantId;
            row.WinningAmount = 50m;
            row.CurrencyCode = "KWD";
            var bid = new AdBid { AdAuctionId = row.Id, RestaurantId = restaurantId, Amount = 50m, CurrencyCode = "KWD", IdempotencyKey = "winner-payment", AcceptedAtUtc = now.AddMinutes(-1) };
            seed.AdBids.Add(bid);
            row.WinningBidId = bid.Id;
            seed.PaymentGatewayAttempts.Add(new PaymentGatewayAttempt
            {
                Provider = "Payzah", TrackId = "track-123", PaymentId = "raw-payzah-id",
                CustomerReference = reference, ExpectedAmount = 50m, ExpectedCurrency = "KWD", IsSandbox = true
            });
            seed.AdPaymentTransactions.Add(new AdPaymentTransaction
            {
                AdAuctionId = row.Id, RestaurantId = restaurantId, PaymentProvider = "Payzah",
                ProviderTransactionId = handle, Amount = 50m, CurrencyCode = "KWD", Status = "Pending", InitiatedAtUtc = now
            });
            await seed.SaveChangesAsync();
        }

        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync(handle, "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("provider-ref", "Paid", 50m, "", "", "", "provider-tx", "Payzah", now,
                reference, "KWD", handle));
        await using (var reconcileDb = CreateContext())
        {
            var service = new AdPaymentReconciliationService(reconcileDb, gateway, FixedClock(now),
                NullLogger<AdPaymentReconciliationService>.Instance,
                new PostgresApplicationTransactionExecutor(reconcileDb), new PostgresApplicationLockManager(reconcileDb));
            (await service.ProcessPendingPaymentsAsync()).Finalized.Should().Be(1);
        }
        await using (var verify = CreateContext())
        {
            (await verify.AdPaymentTransactions.SingleAsync(x => x.AdAuctionId == auction.Id)).Status.Should().Be("Succeeded");
            (await verify.AdAuctions.Where(x => x.Id == auction.Id).Select(x => x.Status).SingleAsync()).Should().Be(AdAuctionStatus.Awarded);
            (await verify.AdPlacements.CountAsync(x => x.AdAuctionId == auction.Id)).Should().Be(1);
            (await verify.AdFinancialLedgerEntries.CountAsync(x => x.AdAuctionId == auction.Id && x.Action == "Charge")).Should().Be(1);
        }

        await using (var replayDb = CreateContext())
        {
            var replay = new AdPaymentReconciliationService(replayDb, gateway, FixedClock(now),
                NullLogger<AdPaymentReconciliationService>.Instance,
                new PostgresApplicationTransactionExecutor(replayDb), new PostgresApplicationLockManager(replayDb));
            (await replay.ProcessPendingPaymentsAsync()).Attempted.Should().Be(0);
        }
    }

    [Fact]
    public async Task ConcurrentRefundReconcilers_OnlyOneSubmitsToProvider()
    {
        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var (auction, restaurantId) = await SeedOpenAuctionAsync(now);
        const string handle = "pz-refund-race";
        var policyId = auction.PolicyId;
        await using (var seed = CreateContext())
        {
            var row = await seed.AdAuctions.SingleAsync(x => x.Id == auction.Id);
            row.Status = AdAuctionStatus.Awarded;
            row.WinningRestaurantId = restaurantId;
            row.WinningAmount = 50m;
            row.CurrencyCode = "KWD";
            seed.AdPaymentTransactions.Add(new AdPaymentTransaction
            {
                AdAuctionId = row.Id, RestaurantId = restaurantId, PaymentProvider = "Payzah",
                ProviderTransactionId = handle, Amount = 50m, CurrencyCode = "KWD",
                Status = "RefundApproved", InitiatedAtUtc = now.AddDays(-1)
            });
            seed.PaymentGatewayAttempts.Add(new PaymentGatewayAttempt
            {
                Provider = "Payzah", TrackId = "refund-race", PaymentId = "raw-refund-race",
                CustomerReference = $"ad-auction:{row.Id:N}:{restaurantId:N}", ExpectedAmount = 50m,
                ExpectedCurrency = "KWD", ProviderReference = "invoice-refund-race", IsSandbox = true
            });
            seed.AdFinancialLedgerEntries.Add(new AdFinancialLedgerEntry
            {
                AdAuctionId = row.Id, RestaurantId = restaurantId, Source = "AdStopSettlement",
                Action = "RefundRequest", Amount = 50m, CurrencyCode = "KWD", ProviderTransactionId = handle,
                RefundReviewStatus = "Approved", RefundApprovedAmount = 50m,
                RefundApprovedPolicyId = policyId, RefundApprovedPolicyVersion = auction.PolicyVersion,
                ExternalRefundIdentifier = null, OccurredAtUtc = now
            });
            await seed.SaveChangesAsync();
        }

        var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var submissionCount = 0;
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.MakeRefundAsync(handle, 50m, "KWD", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                Interlocked.Increment(ref submissionCount);
                providerStarted.TrySetResult();
                await releaseProvider.Task;
                var externalId = call.ArgAt<string>(3);
                return new PaymentGatewayRefundReceipt("refund-race-id", "provider-ref", externalId, 50m);
            });
        gateway.GetRefundStatusAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PaymentGatewayRefundStatus>>([]));

        var firstTask = RunRefundReconcile(gateway, now);
        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var secondTask = RunRefundReconcile(gateway, now);
        await secondTask;
        releaseProvider.TrySetResult();
        await firstTask;

        submissionCount.Should().Be(1, "the status concurrency token lets only one worker claim the refund before provider I/O");
        await using var verify = CreateContext();
        var payment = await verify.AdPaymentTransactions.SingleAsync(x => x.AdAuctionId == auction.Id);
        payment.Status.Should().BeOneOf("RefundNeedsManualProviderReview", "RefundSubmitted");
        var refund = await verify.AdFinancialLedgerEntries.SingleAsync(x => x.AdAuctionId == auction.Id && x.Action == "RefundRequest");
        refund.ExternalRefundIdentifier.Should().NotBeNullOrWhiteSpace();

        async Task<AdRefundReconciliationResult> RunRefundReconcile(IPaymentGatewayService provider, DateTime clockNow)
        {
            await using var db = CreateContext();
            var service = new AdPaymentReconciliationService(db, provider, FixedClock(clockNow),
                NullLogger<AdPaymentReconciliationService>.Instance);
            return await service.ProcessPendingRefundsAsync();
        }
    }

    [Fact]
    public async Task AcceptingNextBidderOfferResetsPaymentWindowUnderAuctionLock()
    {
        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        var (auction, restaurantId) = await SeedOpenAuctionAsync(now);
        var bid = new AdBid { AdAuctionId = auction.Id, RestaurantId = restaurantId, Amount = 30m,
            CurrencyCode = "KWD", IdempotencyKey = "next-consent", AcceptedAtUtc = now.AddMinutes(-1) };
        var offer = new AdAuctionNextBidderOffer
        {
            AdAuctionId = auction.Id, AdBidId = bid.Id, RestaurantId = restaurantId, Amount = bid.Amount,
            CurrencyCode = bid.CurrencyCode, Status = "Pending", ExpiresAtUtc = now.AddMinutes(10)
        };
        await using (var seed = CreateContext())
        {
            var row = await seed.AdAuctions.SingleAsync(x => x.Id == auction.Id);
            row.Status = AdAuctionStatus.AwaitingPayment;
            row.ClosedAtUtc = now.AddDays(-3);
            row.WinningRestaurantId = null;
            row.WinningBidId = null;
            row.WinningAmount = null;
            seed.AdBids.Add(bid);
            seed.AdAuctionNextBidderOffers.Add(offer);
            await seed.SaveChangesAsync();
        }

        await using var responseDb = CreateContext();
        var current = Substitute.For<ICurrentUser>();
        current.IsAuthenticated.Returns(true);
        current.UserId.Returns(Guid.NewGuid());
        current.RestaurantId.Returns(restaurantId);
        var handler = new RespondToAdAuctionNextBidderOfferCommandHandler(responseDb, current, FixedClock(now),
            new PostgresApplicationTransactionExecutor(responseDb), new PostgresApplicationLockManager(responseDb));
        var result = await handler.Handle(new RespondToAdAuctionNextBidderOfferCommand(offer.Id, true, "explicit consent"), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        var updated = await responseDb.AdAuctions.AsNoTracking().SingleAsync(x => x.Id == auction.Id);
        updated.ClosedAtUtc.Should().Be(now);
        updated.WinningRestaurantId.Should().Be(restaurantId);
        updated.WinningBidId.Should().Be(bid.Id);
    }
}
