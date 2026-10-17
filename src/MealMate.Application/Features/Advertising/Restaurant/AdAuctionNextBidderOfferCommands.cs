using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Advertising;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Advertising.Restaurant;

public sealed record RespondToAdAuctionNextBidderOfferCommand(Guid OfferId, bool Accept, string Reason) : ICommand;
public sealed record GetPendingAdAuctionOffersQuery : IQuery<IReadOnlyList<AdAuctionNextBidderOfferDto>>;
public sealed record AdAuctionNextBidderOfferDto(Guid Id, Guid AuctionId, decimal Amount, string CurrencyCode,
    DateTime ExpiresAtUtc, string Status, string SpaceNameAr, string SpaceNameEn,
    DateTime DisplayStartsAtUtc, DateTime DisplayEndsAtUtc);

public sealed class GetPendingAdAuctionOffersQueryHandler(IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock)
    : IQueryHandler<GetPendingAdAuctionOffersQuery, IReadOnlyList<AdAuctionNextBidderOfferDto>>
{
    public async Task<Result<IReadOnlyList<AdAuctionNextBidderOfferDto>>> Handle(GetPendingAdAuctionOffersQuery request, CancellationToken ct)
    {
        if (!currentUser.RestaurantId.HasValue)
            return Error.Forbidden("Advertising.NotRestaurantActor", "Only a restaurant can view its offers.");
        var offers = await (from offer in context.AdAuctionNextBidderOffers.AsNoTracking()
            join auction in context.AdAuctions.AsNoTracking() on offer.AdAuctionId equals auction.Id
            join bid in context.AdBids.AsNoTracking() on offer.AdBidId equals bid.Id
            where offer.RestaurantId == currentUser.RestaurantId.Value && offer.Status == "Pending" &&
                  offer.ExpiresAtUtc > clock.UtcNow && auction.Status == AdAuctionStatus.AwaitingPayment &&
                  auction.WinningRestaurantId == null && bid.RestaurantId == offer.RestaurantId &&
                  bid.AdAuctionId == auction.Id && !context.AdPaymentTransactions.Any(t =>
                      t.AdAuctionId == auction.Id && t.Status == "Succeeded")
            orderby offer.ExpiresAtUtc, offer.Id
            select new AdAuctionNextBidderOfferDto(offer.Id, auction.Id, offer.Amount, offer.CurrencyCode,
                offer.ExpiresAtUtc, offer.Status, auction.AdSpace.NameAr, auction.AdSpace.NameEn,
                auction.DisplayStartsAtUtc, auction.DisplayEndsAtUtc)).ToListAsync(ct);
        return offers;
    }
}

public sealed class RespondToAdAuctionNextBidderOfferCommandHandler(
    IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock,
    IApplicationTransactionExecutor? transactions = null, IApplicationLockManager? locks = null)
    : ICommandHandler<RespondToAdAuctionNextBidderOfferCommand>
{
    public Task<Result> Handle(RespondToAdAuctionNextBidderOfferCommand request, CancellationToken ct)
        => transactions is null ? HandleCore(request, ct) : transactions.ExecuteAsync(token => HandleCore(request, token), ct);

    private async Task<Result> HandleCore(RespondToAdAuctionNextBidderOfferCommand request, CancellationToken ct)
    {
        if (!currentUser.RestaurantId.HasValue)
            return Error.Forbidden("Advertising.NotRestaurantActor", "Only a restaurant can respond to an offer.");
        if (!currentUser.UserId.HasValue)
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Error.Validation("Advertising.ReasonRequired", "A reason is required.");
        if (request.Reason.Trim().Length > 500)
            return Error.Validation("Advertising.ReasonTooLong", "The response reason must not exceed 500 characters.");
        var offer = await context.AdAuctionNextBidderOffers.FirstOrDefaultAsync(x => x.Id == request.OfferId, ct);
        if (offer is null) return Error.NotFound("Advertising.OfferNotFound", "Offer was not found.");
        if (offer.RestaurantId != currentUser.RestaurantId.Value)
            return Error.Forbidden("Advertising.OfferOwnershipMismatch", "This offer belongs to another restaurant.");
        if (offer.Status != "Pending" || offer.ExpiresAtUtc <= clock.UtcNow)
            return Error.Conflict("Advertising.OfferExpired", "This offer is no longer pending.");
        if (locks is not null)
        {
            await locks.LockAdAuctionAsync(offer.AdAuctionId, ct);
            // Read persisted consent after the lock, bypassing any pre-lock tracked snapshot.
            var freshOffer = await context.AdAuctionNextBidderOffers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.OfferId, ct);
            if (freshOffer is null || freshOffer.Status != "Pending" || freshOffer.ExpiresAtUtc <= clock.UtcNow)
                return Error.Conflict("Advertising.OfferExpired", "This offer is no longer pending.");
        }
        var auction = await context.AdAuctions.Include(x => x.Bids).FirstOrDefaultAsync(x => x.Id == offer.AdAuctionId, ct);
        if (auction is null) return Error.NotFound("Advertising.AuctionNotFound", "Auction was not found.");
        if (auction.Status != AdAuctionStatus.AwaitingPayment || auction.WinningRestaurantId.HasValue ||
            await context.AdPaymentTransactions.AnyAsync(x => x.AdAuctionId == auction.Id && x.Status == "Succeeded", ct))
            return Error.Conflict("Advertising.OfferNoLongerAvailable", "This auction offer is no longer available.");
        var offeredBid = auction.Bids.FirstOrDefault(x => x.Id == offer.AdBidId && x.RestaurantId == offer.RestaurantId);
        if (offeredBid is null || offeredBid.Amount != offer.Amount ||
            !string.Equals(offeredBid.CurrencyCode, offer.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            return Error.Conflict("Advertising.OfferNoLongerAvailable", "The offered bid is no longer available.");
        offer.Status = request.Accept ? "Accepted" : "Declined";
        offer.RespondedByUserId = currentUser.UserId;
        offer.RespondedAtUtc = clock.UtcNow;
        offer.ResponseReason = request.Reason.Trim();
        if (request.Accept)
        {
            auction.Status = AdAuctionStatus.AwaitingPayment;
            auction.ClosedAtUtc = clock.UtcNow; // restart the saved policy payment window on explicit acceptance
            auction.WinningRestaurantId = offer.RestaurantId;
            auction.WinningBidId = offer.AdBidId;
            auction.WinningAmount = offer.Amount;
            auction.CurrencyCode = offer.CurrencyCode;
            foreach (var bid in auction.Bids) bid.IsWinning = bid.Id == offeredBid.Id;
        }
        await context.SaveChangesAsync(ct);
        return Result.Success();
    }
}
