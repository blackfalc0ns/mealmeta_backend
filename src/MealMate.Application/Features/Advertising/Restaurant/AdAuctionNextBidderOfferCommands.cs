using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Advertising;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Advertising.Restaurant;

public sealed record RespondToAdAuctionNextBidderOfferCommand(Guid OfferId, bool Accept, string Reason) : ICommand;
public sealed record GetPendingAdAuctionOffersQuery : IQuery<IReadOnlyList<AdAuctionNextBidderOfferDto>>;
public sealed record AdAuctionNextBidderOfferDto(Guid Id, Guid AuctionId, decimal Amount, string CurrencyCode, DateTime ExpiresAtUtc, string Status);

public sealed class GetPendingAdAuctionOffersQueryHandler(IAppDbContext context, ICurrentUser currentUser, IDateTimeProvider clock)
    : IQueryHandler<GetPendingAdAuctionOffersQuery, IReadOnlyList<AdAuctionNextBidderOfferDto>>
{
    public async Task<Result<IReadOnlyList<AdAuctionNextBidderOfferDto>>> Handle(GetPendingAdAuctionOffersQuery request, CancellationToken ct)
    {
        if (!currentUser.RestaurantId.HasValue)
            return Error.Forbidden("Advertising.NotRestaurantActor", "Only a restaurant can view its offers.");
        var offers = await context.AdAuctionNextBidderOffers.AsNoTracking()
            .Where(x => x.RestaurantId == currentUser.RestaurantId.Value && x.Status == "Pending" && x.ExpiresAtUtc > clock.UtcNow)
            .OrderBy(x => x.ExpiresAtUtc)
            .Select(x => new AdAuctionNextBidderOfferDto(x.Id, x.AdAuctionId, x.Amount, x.CurrencyCode, x.ExpiresAtUtc, x.Status))
            .ToListAsync(ct);
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
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Error.Validation("Advertising.ReasonRequired", "A reason is required.");
        var offer = await context.AdAuctionNextBidderOffers.FirstOrDefaultAsync(x => x.Id == request.OfferId, ct);
        if (offer is null) return Error.NotFound("Advertising.OfferNotFound", "Offer was not found.");
        if (offer.RestaurantId != currentUser.RestaurantId.Value)
            return Error.Forbidden("Advertising.OfferOwnershipMismatch", "This offer belongs to another restaurant.");
        if (offer.Status != "Pending" || offer.ExpiresAtUtc <= clock.UtcNow)
            return Error.Conflict("Advertising.OfferExpired", "This offer is no longer pending.");
        if (locks is not null)
        {
            await locks.LockAdAuctionAsync(offer.AdAuctionId, ct);
            var freshOffer = await context.AdAuctionNextBidderOffers.FirstOrDefaultAsync(x => x.Id == request.OfferId, ct);
            if (freshOffer is null || freshOffer.Status != "Pending" || freshOffer.ExpiresAtUtc <= clock.UtcNow)
                return Error.Conflict("Advertising.OfferExpired", "This offer is no longer pending.");
            offer = freshOffer;
        }
        var auction = await context.AdAuctions.Include(x => x.Bids).FirstOrDefaultAsync(x => x.Id == offer.AdAuctionId, ct);
        if (auction is null) return Error.NotFound("Advertising.AuctionNotFound", "Auction was not found.");
        if (auction.Status != AdAuctionStatus.AwaitingPayment || auction.WinningRestaurantId.HasValue ||
            await context.AdPaymentTransactions.AnyAsync(x => x.AdAuctionId == auction.Id && x.Status == "Succeeded", ct))
            return Error.Conflict("Advertising.OfferNoLongerAvailable", "This auction offer is no longer available.");
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
            var acceptedBid = auction.Bids.FirstOrDefault(x => x.Id == offer.AdBidId);
            if (acceptedBid is null) return Error.Conflict("Advertising.OfferNoLongerAvailable", "The offered bid is no longer available.");
            acceptedBid.IsWinning = true;
        }
        await context.SaveChangesAsync(ct);
        return Result.Success();
    }
}
