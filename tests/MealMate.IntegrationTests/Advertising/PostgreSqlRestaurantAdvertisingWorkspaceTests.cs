using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Advertising.Admin;
using MealMate.Application.Features.Advertising.Restaurant;
using MealMate.Application.Features.Advertising.Services;
using MealMate.Domain.Advertising;
using MealMate.Domain.Common;
using MealMate.Domain.Entities;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.IntegrationTests.Advertising;

public partial class PostgreSqlAdAuctionConcurrencyTests
{
    [Fact]
    public async Task DeclinedNextOffer_CannotBeAcceptedFromAnOlderTrackedSnapshot()
    {
        var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var (auction, restaurantId) = await SeedOpenAuctionAsync(now);
        var bid = new AdBid { AdAuctionId = auction.Id, RestaurantId = restaurantId, Amount = 30m,
            CurrencyCode = "KWD", IdempotencyKey = "stale-offer", AcceptedAtUtc = now };
        var offer = new AdAuctionNextBidderOffer { AdAuctionId = auction.Id, AdBidId = bid.Id,
            RestaurantId = restaurantId, Amount = 30m, CurrencyCode = "KWD", ExpiresAtUtc = now.AddHours(1) };
        await using (var seed = CreateContext())
        {
            var row = await seed.AdAuctions.SingleAsync(a => a.Id == auction.Id);
            row.Status = AdAuctionStatus.AwaitingPayment;
            row.ClosedAtUtc = now;
            seed.AdBids.Add(bid);
            seed.AdAuctionNextBidderOffers.Add(offer);
            await seed.SaveChangesAsync();
        }
        var actor = Substitute.For<ICurrentUser>();
        actor.RestaurantId.Returns(restaurantId);
        actor.UserId.Returns(Guid.NewGuid());
        await using var staleDb = CreateContext();
        await staleDb.AdAuctionNextBidderOffers.SingleAsync(o => o.Id == offer.Id);
        await using (var declineDb = CreateContext())
        {
            (await new RespondToAdAuctionNextBidderOfferCommandHandler(declineDb, actor, FixedClock(now),
                new PostgresApplicationTransactionExecutor(declineDb), new PostgresApplicationLockManager(declineDb))
                .Handle(new(offer.Id, false, "Owner declined"), default)).IsSuccess.Should().BeTrue();
        }
        var staleAccept = await new RespondToAdAuctionNextBidderOfferCommandHandler(staleDb, actor, FixedClock(now),
            new PostgresApplicationTransactionExecutor(staleDb), new PostgresApplicationLockManager(staleDb))
            .Handle(new(offer.Id, true, "Stale accept"), default);
        staleAccept.IsFailure.Should().BeTrue();
        await using var verify = CreateContext();
        (await verify.AdAuctionNextBidderOffers.SingleAsync(o => o.Id == offer.Id)).Status.Should().Be("Declined");
        (await verify.AdAuctions.SingleAsync(a => a.Id == auction.Id)).WinningRestaurantId.Should().BeNull();
    }

    [Fact]
    public async Task RestaurantWorkspace_Postgres_PolicyConsentPaymentAndCreativeLifecycle()
    {
        var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var (seeded, firstRestaurant) = await SeedOpenAuctionAsync(now);
        var secondRestaurant = Guid.NewGuid();
        var actor = Substitute.For<ICurrentUser>();
        actor.UserId.Returns(Guid.NewGuid());
        actor.IsAuthenticated.Returns(true);
        actor.RestaurantId.Returns(firstRestaurant);
        var clock = FixedClock(now);
        await using var db = CreateContext();
        var auction = await db.AdAuctions.SingleAsync(a => a.Id == seeded.Id);
        auction.DisplayStartsAtUtc = now.AddDays(4);
        auction.DisplayEndsAtUtc = now.AddDays(11);
        var newer = AdCommercialPolicy.Create(2, "KWD", 100m, 10m,
            AdCommercialPolicy.TiePolicyFirstAccepted, 1440,
            AdCommercialPolicy.NextBidderPolicyOfferWithConsent, "NoRefund").Value;
        newer.Approve(Guid.NewGuid(), now);
        db.AdCommercialPolicies.Add(newer);
        await db.SaveChangesAsync();
        var opportunities = await new GetAdOpportunitiesQueryHandler(db, actor, clock).Handle(new(), default);
        opportunities.Value.Should().ContainSingle().Which.MinimumBid.Should().Be(20m);
        var bidder = new PlaceAdBidCommandHandler(db, actor, clock);
        (await bidder.Handle(new(auction.Id, 50m, "first"), default)).IsSuccess.Should().BeTrue();
        actor.RestaurantId.Returns(secondRestaurant);
        (await bidder.Handle(new(auction.Id, 45m, "second"), default)).IsSuccess.Should().BeTrue();
        var transactions = new PostgresApplicationTransactionExecutor(db);
        var locks = new PostgresApplicationLockManager(db);
        var sweep = new AdAuctionSweepService(db, clock, transactions: transactions, locks: locks);
        clock.UtcNow.Returns(now.AddHours(1));
        await sweep.SweepAsync();
        clock.UtcNow.Returns(now.AddHours(26));
        await sweep.SweepAsync();
        var offers = await new GetPendingAdAuctionOffersQueryHandler(db, actor, clock).Handle(new(), default);
        var offer = offers.Value.Should().ContainSingle().Subject;
        offer.SpaceNameEn.Should().Be("Hero");
        (await new RespondToAdAuctionNextBidderOfferCommandHandler(db, actor, clock, transactions, locks)
            .Handle(new(offer.Id, true, "Explicit consent"), default)).IsSuccess.Should().BeTrue();
        var workspace = new GetMyAdBidPageQueryHandler(db, actor);
        (await workspace.Handle(new(), default)).Value.AwaitingPaymentCount.Should().Be(1);
        var reference = $"ad-auction:{auction.Id:N}:{secondRestaurant:N}";
        db.PaymentGatewayAttempts.Add(new PaymentGatewayAttempt { Provider = "Payzah", TrackId = "pg-next-track",
            PaymentId = "pg-next-payment", CustomerReference = reference, ExpectedAmount = 45m,
            ExpectedCurrency = "KWD", IsSandbox = true });
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("pz-pg-next-track", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentGatewayStatusResponse("invoice", "Paid", 45m, "", "", "", "pg-tx", "Payzah", now,
                reference, "KWD", "pz-pg-next-track"));
        var payment = new RecordAdPaymentCommandHandler(db, actor, clock, gateway, transactions, locks);
        var pay = new RecordAdPaymentCommand(auction.Id, "Payzah", "pz-pg-next-track", 45m, "KWD");
        (await payment.Handle(pay, default)).IsSuccess.Should().BeTrue();
        (await payment.Handle(pay, default)).IsSuccess.Should().BeTrue();
        (await db.AdFinancialLedgerEntries.CountAsync()).Should().Be(1);
        db.UploadFileRecords.Add(new UploadFileRecord { StorageKey = "pg-owned-image", FileName = "creative.png",
            ContentType = "image/png", UploadedByUserId = actor.UserId!.Value, Scope = "advertising" });
        await db.SaveChangesAsync();
        var placement = await db.AdPlacements.SingleAsync();
        var creative = new SubmitAdCreativeCommandHandler(db, actor, clock);
        var submit = new SubmitAdCreativeCommand(auction.Id, "pg-owned-image", "restaurant", null,
            auction.DisplayStartsAtUtc, auction.DisplayEndsAtUtc);
        (await creative.Handle(submit, default)).IsSuccess.Should().BeTrue();
        var review = new ReviewAdCreativeCommandHandler(db, actor, clock);
        (await review.Handle(new(placement.Id, false, "Adjust image crop"), default)).IsSuccess.Should().BeTrue();
        var revision = (await workspace.Handle(new(), default)).Value;
        revision.CreativeDueCount.Should().Be(1);
        revision.Items.Single().PlacementStatus.Should().Be("NeedsChanges");
        revision.Items.Single().ReviewNotes.Should().Be("Adjust image crop");
        (await creative.Handle(submit, default)).IsSuccess.Should().BeTrue();
        (await review.Handle(new(placement.Id, true, null), default)).IsSuccess.Should().BeTrue();
        clock.UtcNow.Returns(auction.DisplayStartsAtUtc);
        await sweep.SweepAsync();
        placement.Status.Should().Be(AdPlacementStatus.Active);
        (await new RestaurantStopAdPlacementCommandHandler(db, actor, clock, new StopAdPlacementCore(db, clock))
            .Handle(new(placement.Id, "Campaign completed"), default)).IsSuccess.Should().BeTrue();
        (await workspace.Handle(new(), default)).Value.Items.Single().PlacementStatus.Should().Be("Stopped");
    }
}
