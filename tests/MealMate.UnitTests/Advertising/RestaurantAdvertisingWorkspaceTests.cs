using FluentAssertions;
using MealMate.Application.Features.Advertising.Restaurant;
using MealMate.Application.Features.Advertising.Admin;
using MealMate.Application.Features.Advertising.Services;
using MealMate.Application.Features.Advertising.Customer;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Advertising;
using MealMate.Domain.Common;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace MealMate.UnitTests.Advertising;

public partial class AdvertisingLifecycleTests
{
    [Fact]
    public async Task Opportunities_UseEachAuctionsSavedPolicy_NotLatestPolicy()
    {
        EnableAuctions();
        _currentUser.RestaurantId.Returns(Guid.NewGuid());
        var policy = SeedApprovedPolicy(20m, 5m);
        var auction = SeedOpenAuction(policy: policy);
        var newer = AdCommercialPolicy.Create(2, "KWD", 100m, 10m,
            AdCommercialPolicy.TiePolicyFirstAccepted, 1440,
            AdCommercialPolicy.NextBidderPolicyOfferWithConsent, "NoRefund").Value;
        newer.Approve(Guid.NewGuid(), Now);
        _context.AdCommercialPolicies.Add(newer);
        await _context.SaveChangesAsync();

        var result = await new GetAdOpportunitiesQueryHandler(_context, _currentUser, _clock)
            .Handle(new GetAdOpportunitiesQuery(), CancellationToken.None);

        result.Value.Should().ContainSingle().Which.MinimumBid.Should().Be(20m);
        result.Value.Single().BidStep.Should().Be(5m);
        var bid = await new PlaceAdBidCommandHandler(_context, _currentUser, _clock)
            .Handle(new PlaceAdBidCommand(auction.Id, 25m, "saved-policy"), CancellationToken.None);
        bid.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Opportunities_ExcludeFutureClosedAndUnapprovedAuctions_AndDisabledSpaces()
    {
        EnableAuctions();
        _currentUser.RestaurantId.Returns(Guid.NewGuid());
        var policy = SeedApprovedPolicy();
        var open = SeedOpenAuction(policy: policy);
        var future = SeedOpenAuction(policy: policy);
        future.BidsOpenAtUtc = Now.AddMinutes(1);
        var closed = SeedOpenAuction(policy: policy);
        closed.BidsCloseAtUtc = Now;
        SeedOpenAuction(); // No approved saved policy.
        var disabledSpace = SeedSpace("disabled");
        disabledSpace.IsActive = false;
        SeedOpenAuction(disabledSpace, policy);
        await _context.SaveChangesAsync();

        var result = await new GetAdOpportunitiesQueryHandler(_context, _currentUser, _clock)
            .Handle(new GetAdOpportunitiesQuery(), CancellationToken.None);
        result.Value.Select(x => x.AuctionId).Should().Equal(open.Id);
    }

    [Fact]
    public async Task Opportunities_GlobalSwitchOff_ReturnsEmpty()
    {
        _currentUser.RestaurantId.Returns(Guid.NewGuid());
        SeedOpenAuction(policy: SeedApprovedPolicy());
        var result = await new GetAdOpportunitiesQueryHandler(_context, _currentUser, _clock)
            .Handle(new GetAdOpportunitiesQuery(), CancellationToken.None);
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task BidPage_ReportsTotalsBeyond100_AndActionsOutsideVisiblePage_AndOwnedReviewState()
    {
        var restaurant = Guid.NewGuid();
        _currentUser.RestaurantId.Returns(restaurant);
        var auction = SeedOpenAuction();
        var bids = Enumerable.Range(0, 105).Select(i => new AdBid
        {
            AdAuctionId = auction.Id, RestaurantId = restaurant, Amount = 20m + i,
            CurrencyCode = "KWD", IdempotencyKey = "page-" + i, AcceptedAtUtc = Now.AddMinutes(i)
        }).ToArray();
        _context.AdBids.AddRange(bids);
        auction.Status = AdAuctionStatus.Awarded;
        auction.WinningBidId = bids[0].Id;
        auction.WinningRestaurantId = restaurant;
        bids[0].IsWinning = true;
        var placement = new AdPlacement { AdAuctionId = auction.Id, AdSpaceId = auction.AdSpaceId,
            RestaurantId = restaurant, Status = AdPlacementStatus.NeedsChanges, ReviewNotes = "Crop the banner",
            ReviewedAtUtc = Now };
        _context.AdPlacements.Add(placement);
        var foreignAuction = SeedOpenAuction();
        _context.AdBids.Add(new AdBid { AdAuctionId = foreignAuction.Id, RestaurantId = Guid.NewGuid(),
            CurrencyCode = "KWD", Amount = 500m, IdempotencyKey = "foreign", AcceptedAtUtc = Now });
        await _context.SaveChangesAsync();

        var handler = new GetMyAdBidPageQueryHandler(_context, _currentUser);
        var first = (await handler.Handle(new GetMyAdBidPageQuery(), CancellationToken.None)).Value;
        first.TotalCount.Should().Be(105);
        first.Items.Should().HaveCount(20);
        first.CreativeDueCount.Should().Be(1);
        first.ActionableBids.Should().ContainSingle().Which.BidId.Should().Be(bids[0].Id);
        first.ActionableBids.Single().PlacementStatus.Should().Be("NeedsChanges");
        first.ActionableBids.Single().ReviewNotes.Should().Be("Crop the banner");
        first.Items.Select(b => b.BidId).Should().NotContain(bids[0].Id);
        var last = (await handler.Handle(new GetMyAdBidPageQuery(999), CancellationToken.None)).Value;
        last.Page.Should().Be(6);
        last.Items.Should().HaveCount(5);
        var invalid = (await handler.Handle(new GetMyAdBidPageQuery(-1, 1000), CancellationToken.None)).Value;
        invalid.Page.Should().Be(1);
        invalid.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task NextBidder_DeclineThenExpiry_DoesNotReofferOriginalWinner_AndEnforcesOwnership()
    {
        EnableAuctions();
        var auction = SeedOpenAuction(policy: SeedApprovedPolicy());
        var restaurants = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var bidHandler = new PlaceAdBidCommandHandler(_context, _currentUser, _clock);
        for (var i = 0; i < restaurants.Length; i++)
        {
            _currentUser.RestaurantId.Returns(restaurants[i]);
            (await bidHandler.Handle(new PlaceAdBidCommand(auction.Id, 50m - 5m * i, "bid-" + i), CancellationToken.None)).IsSuccess.Should().BeTrue();
        }
        _clock.UtcNow.Returns(Now.AddHours(2));
        var sweep = new AdAuctionSweepService(_context, _clock);
        await sweep.SweepAsync();
        _clock.UtcNow.Returns(Now.AddHours(27));
        await sweep.SweepAsync();
        var offer = await _context.AdAuctionNextBidderOffers.SingleAsync(o => o.Status == "Pending");
        offer.RestaurantId.Should().Be(restaurants[1]);
        var responseHandler = new RespondToAdAuctionNextBidderOfferCommandHandler(_context, _currentUser, _clock);
        _currentUser.RestaurantId.Returns(restaurants[0]);
        (await responseHandler.Handle(new(offer.Id, true, "Accept"), CancellationToken.None)).Error.Code.Should().Be("Advertising.OfferOwnershipMismatch");
        var oldWinner = (await new GetMyAdBidPageQueryHandler(_context, _currentUser).Handle(new(), CancellationToken.None)).Value;
        oldWinner.AwaitingPaymentCount.Should().Be(0);
        oldWinner.Items.Should().OnlyContain(x => !x.IsWinning);
        _currentUser.RestaurantId.Returns(restaurants[1]);
        (await responseHandler.Handle(new(offer.Id, false, " "), CancellationToken.None)).Error.Code.Should().Be("Advertising.ReasonRequired");
        (await responseHandler.Handle(new(offer.Id, false, new string('x', 501)), CancellationToken.None)).Error.Code.Should().Be("Advertising.ReasonTooLong");
        offer.Status.Should().Be("Pending");
        (await responseHandler.Handle(new(offer.Id, false, "No longer needed"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        await sweep.SweepAsync();
        var finalOffer = await _context.AdAuctionNextBidderOffers.SingleAsync(o => o.Status == "Pending");
        finalOffer.RestaurantId.Should().Be(restaurants[2]);
        _clock.UtcNow.Returns(finalOffer.ExpiresAtUtc);
        _currentUser.RestaurantId.Returns(restaurants[2]);
        (await responseHandler.Handle(new(finalOffer.Id, true, "Accept"), CancellationToken.None)).Error.Code.Should().Be("Advertising.OfferExpired");
        (await new GetPendingAdAuctionOffersQueryHandler(_context, _currentUser, _clock).Handle(new(), CancellationToken.None)).Value.Should().BeEmpty();
        await sweep.SweepAsync();
        auction.Status.Should().Be(AdAuctionStatus.NoFill);
        _context.AdAuctionNextBidderOffers.Count(o => o.Status == "Pending").Should().Be(0);
    }

    [Fact]
    public async Task NextBidder_FullLifecycle_ConsentPaymentCreativeReviewActivationAndStop()
    {
        EnableAuctions();
        var auction = SeedOpenAuction(policy: SeedApprovedPolicy());
        // Keep the display window beyond both the initial and replacement payment windows.
        auction.DisplayStartsAtUtc = Now.AddDays(4);
        auction.DisplayEndsAtUtc = Now.AddDays(11);
        var originalWinner = Guid.NewGuid();
        var nextRestaurant = Guid.NewGuid();
        var bidHandler = new PlaceAdBidCommandHandler(_context, _currentUser, _clock);
        _currentUser.RestaurantId.Returns(originalWinner);
        (await bidHandler.Handle(new(auction.Id, 50m, "winner"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        _currentUser.RestaurantId.Returns(nextRestaurant);
        (await bidHandler.Handle(new(auction.Id, 45m, "next"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        _clock.UtcNow.Returns(Now.AddHours(2));
        var sweep = new AdAuctionSweepService(_context, _clock);
        await sweep.SweepAsync();
        _clock.UtcNow.Returns(Now.AddHours(27));
        await sweep.SweepAsync();
        var offer = (await new GetPendingAdAuctionOffersQueryHandler(_context, _currentUser, _clock)
            .Handle(new(), CancellationToken.None)).Value.Should().ContainSingle().Subject;
        offer.Amount.Should().Be(45m);
        offer.SpaceNameEn.Should().NotBeEmpty();
        _context.AdPaymentTransactions.Should().BeEmpty("consent never charges automatically");
        var respond = new RespondToAdAuctionNextBidderOfferCommandHandler(_context, _currentUser, _clock);
        (await respond.Handle(new(offer.Id, true, "Accept the displayed amount"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        auction.WinningRestaurantId.Should().Be(nextRestaurant);
        auction.Bids.Count(b => b.IsWinning).Should().Be(1);
        (await respond.Handle(new(offer.Id, true, "Replay"), CancellationToken.None)).IsFailure.Should().BeTrue();
        var workspace = new GetMyAdBidPageQueryHandler(_context, _currentUser);
        (await workspace.Handle(new(), CancellationToken.None)).Value.AwaitingPaymentCount.Should().Be(1);

        var accounts = Substitute.For<IUserAccountService>();
        var ownerUserId = _currentUser.UserId!.Value;
        var account = new UserAccountDto(
            ownerUserId, "restaurant@example.test", "+96555555555", "Restaurant owner", UserType.Restaurant,
            AccountStatus.Active, nextRestaurant, true, true, Now, null);
        accounts.FindByIdAsync(ownerUserId, Arg.Any<CancellationToken>()).Returns(account);
        var gateway = Substitute.For<IPaymentGatewayService>();
        var reference = $"ad-auction:{auction.Id:N}:{nextRestaurant:N}";
        gateway.ExecutePaymentAsync(45m, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), reference, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(async _ =>
            {
                _context.PaymentGatewayAttempts.Add(new PaymentGatewayAttempt { Provider = "Payzah", TrackId = "flow-next-track",
                    PaymentId = "flow-next-payment", CustomerReference = reference, ExpectedAmount = 45m,
                    ExpectedCurrency = "KWD", IsSandbox = true });
                await _context.SaveChangesAsync();
                return new PaymentCreationReceipt("flow-next-payment", "https://payment.example.test/checkout", "flow-next-track");
            });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Checkout:DefaultCallbackUrl"] = "https://restaurant.example.test/return",
            ["Checkout:DefaultErrorUrl"] = "https://restaurant.example.test/error",
            ["Checkout:AllowedRedirectSchemes:0"] = "https", ["Checkout:AllowedRedirectHosts:0"] = "restaurant.example.test"
        }).Build();
        var session = await new CreateAdPaymentSessionCommandHandler(_context, _currentUser, accounts, gateway,
            configuration, _clock, new InlineAdTestTransaction(), Substitute.For<IApplicationLockManager>())
            .Handle(new(auction.Id), CancellationToken.None);
        session.IsSuccess.Should().BeTrue();
        session.Value.Amount.Should().Be(45m);
        gateway.GetPaymentStatusAsync("pz-flow-next-track", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentGatewayStatusResponse("invoice", "Paid", 45m, "", "", "", "flow-transaction", "Payzah", Now,
                reference, "KWD", "pz-flow-next-track"));
        var payment = new RecordAdPaymentCommandHandler(_context, _currentUser, _clock, gateway);
        var command = new RecordAdPaymentCommand(auction.Id, "Payzah", session.Value.PaymentHandle, 45m, "KWD");
        (await payment.Handle(command, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await payment.Handle(command, CancellationToken.None)).IsSuccess.Should().BeTrue();
        _context.AdFinancialLedgerEntries.Count().Should().Be(1);
        var placement = await _context.AdPlacements.SingleAsync();
        var upload = new UploadFileRecord { StorageKey = "flow-owned-banner", UploadedByUserId = _currentUser.UserId.Value,
            FileName = "banner.png", ContentType = "image/png", Scope = "advertising" };
        _context.UploadFileRecords.Add(upload);
        await _context.SaveChangesAsync();
        var creative = new SubmitAdCreativeCommandHandler(_context, _currentUser, _clock);
        var creativeCommand = new SubmitAdCreativeCommand(auction.Id, upload.StorageKey, "restaurant", null,
            auction.DisplayStartsAtUtc, auction.DisplayEndsAtUtc);
        (await creative.Handle(creativeCommand, CancellationToken.None)).IsSuccess.Should().BeTrue();
        var reviewer = new ReviewAdCreativeCommandHandler(_context, _currentUser, _clock);
        (await reviewer.Handle(new(placement.Id, false, "Resize the banner"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        var revision = (await workspace.Handle(new(), CancellationToken.None)).Value;
        revision.CreativeDueCount.Should().Be(1);
        revision.ActionableBids.Single().ReviewNotes.Should().Be("Resize the banner");
        (await creative.Handle(creativeCommand, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await reviewer.Handle(new(placement.Id, true, null), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await workspace.Handle(new(), CancellationToken.None)).Value.CreativeDueCount.Should().Be(0);
        _clock.UtcNow.Returns(auction.DisplayStartsAtUtc);
        await sweep.SweepAsync();
        placement.Status.Should().Be(AdPlacementStatus.Active);
        _context.CustomerAddresses.Add(new MealMate.Domain.IdentityAccess.CustomerAddress { UserId = _currentUser.UserId.Value,
            Label = "Home", AddressLine = "test", Governorate = "Capital", RegionId = Guid.NewGuid(), IsDefault = true });
        await _context.SaveChangesAsync();
        var served = await new GetEligibleAdPlacementsQueryHandler(_context, _clock, currentUser: _currentUser)
            .Handle(new("offers.hero"), CancellationToken.None);
        served.Value.Placements.Should().ContainSingle();
        (await new RestaurantStopAdPlacementCommandHandler(_context, _currentUser, _clock,
            new StopAdPlacementCore(_context, _clock)).Handle(new(placement.Id, "Campaign complete"), CancellationToken.None)).IsSuccess.Should().BeTrue();
        placement.Status.Should().Be(AdPlacementStatus.Stopped);
        (await new GetEligibleAdPlacementsQueryHandler(_context, _clock, currentUser: _currentUser)
            .Handle(new("offers.hero"), CancellationToken.None)).Value.Placements.Should().BeEmpty();
    }

    private sealed class InlineAdTestTransaction : IApplicationTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
