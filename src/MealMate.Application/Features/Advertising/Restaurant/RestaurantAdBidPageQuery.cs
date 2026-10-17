using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Advertising;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Advertising.Restaurant;

public sealed record GetMyAdBidPageQuery(int Page = 1, int PageSize = 20) : IQuery<MyAdBidPageDto>;
public sealed record MyAdBidPageDto(IReadOnlyList<MyAdBidDto> Items, int TotalCount, int Page, int PageSize,
    int AwaitingPaymentCount, int CreativeDueCount, IReadOnlyList<MyAdBidDto> ActionableBids);

public sealed class GetMyAdBidPageQueryHandler(IAppDbContext context, ICurrentUser currentUser)
    : IQueryHandler<GetMyAdBidPageQuery, MyAdBidPageDto>
{
    public async Task<Result<MyAdBidPageDto>> Handle(GetMyAdBidPageQuery request, CancellationToken ct)
    {
        if (!currentUser.RestaurantId.HasValue)
            return Error.Forbidden("Advertising.NotRestaurantActor", "Only restaurant actors can view their bids.");
        var restaurantId = currentUser.RestaurantId.Value;
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = context.AdBids.AsNoTracking().Where(b => b.RestaurantId == restaurantId);
        var total = await query.CountAsync(ct);
        var page = Math.Clamp(request.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var bids = await query.Include(b => b.AdAuction).ThenInclude(a => a.AdSpace)
            .OrderByDescending(b => b.AcceptedAtUtc).ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        // Actions are independent of the visible history page, so older wins remain payable.
        var actionable = await query.Include(b => b.AdAuction).ThenInclude(a => a.AdSpace)
            .Where(b => b.AdAuction.WinningBidId == b.Id && b.AdAuction.WinningRestaurantId == restaurantId &&
                (b.AdAuction.Status == AdAuctionStatus.AwaitingPayment ||
                 (b.AdAuction.Status == AdAuctionStatus.Awarded && context.AdPlacements.Any(p =>
                     p.AdAuctionId == b.AdAuctionId && p.RestaurantId == restaurantId &&
                     (p.Status == AdPlacementStatus.AwaitingCreative || p.Status == AdPlacementStatus.NeedsChanges)))))
            .OrderBy(b => b.AdAuction.DisplayStartsAtUtc).ThenBy(b => b.Id).ToListAsync(ct);
        var mappedActions = await RestaurantAdBidProjection.MapAsync(context, actionable, restaurantId, ct);
        return new MyAdBidPageDto(await RestaurantAdBidProjection.MapAsync(context, bids, restaurantId, ct),
            total, page, pageSize,
            mappedActions.Count(b => b.AuctionStatus == nameof(AdAuctionStatus.AwaitingPayment)),
            mappedActions.Count(b => b.AuctionStatus == nameof(AdAuctionStatus.Awarded)), mappedActions);
    }
}

internal static class RestaurantAdBidProjection
{
    internal static async Task<IReadOnlyList<MyAdBidDto>> MapAsync(IAppDbContext context,
        IReadOnlyList<AdBid> bids, Guid restaurantId, CancellationToken ct)
    {
        var auctionIds = bids.Select(b => b.AdAuctionId).Distinct().ToArray();
        var placements = await context.AdPlacements.AsNoTracking()
            .Where(p => auctionIds.Contains(p.AdAuctionId) && p.RestaurantId == restaurantId)
            .ToDictionaryAsync(p => p.AdAuctionId, ct);
        return bids.Select(b =>
        {
            placements.TryGetValue(b.AdAuctionId, out var placement);
            return new MyAdBidDto(b.Id, b.AdAuctionId, b.AdAuction.AdSpace.PlacementKey,
                b.AdAuction.Status.ToString(), b.Amount, b.CurrencyCode,
                b.AdAuction.WinningBidId == b.Id && b.AdAuction.WinningRestaurantId == restaurantId,
                b.AcceptedAtUtc, b.AdAuction.DisplayStartsAtUtc, b.AdAuction.DisplayEndsAtUtc,
                placement?.Id, placement?.Status.ToString(), placement?.ReviewNotes, placement?.ReviewedAtUtc,
                b.AdAuction.AdSpace.NameAr, b.AdAuction.AdSpace.NameEn);
        }).ToList();
    }
}
