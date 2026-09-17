using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Queries;

public sealed record OfferRedemptionItemDto(
    Guid RedemptionId,
    Guid CustomerId,
    Guid SubscriptionId,
    Guid? PromoCodeId,
    decimal DiscountAppliedAmount,
    decimal PlatformFundedAmount,
    decimal RestaurantFundedAmount,
    DateTime RedeemedAtUtc
);

public sealed record OfferRedemptionSummaryDto(
    Guid OfferId,
    string OfferNameAr,
    int TotalRedemptionsCount,
    decimal TotalDiscountAppliedKwd,
    decimal TotalPlatformFundedKwd,
    decimal TotalRestaurantFundedKwd,
    PagedResult<OfferRedemptionItemDto> Redemptions
);

public sealed record GetOfferRedemptionsQuery(
    Guid OfferId,
    PageRequest? Page = null
) : IQuery<OfferRedemptionSummaryDto>;

public sealed class GetOfferRedemptionsQueryHandler : IQueryHandler<GetOfferRedemptionsQuery, OfferRedemptionSummaryDto>
{
    private readonly IAppDbContext _context;

    public GetOfferRedemptionsQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<OfferRedemptionSummaryDto>> Handle(GetOfferRedemptionsQuery request, CancellationToken cancellationToken)
    {
        var offer = await _context.Offers
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OfferId, cancellationToken);

        if (offer == null)
        {
            return Error.NotFound("Offer.NotFound", $"Offer with ID '{request.OfferId}' was not found.");
        }

        var baseQuery = _context.OfferRedemptions
            .AsNoTracking()
            .Where(r => r.OfferId == request.OfferId);

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var allItems = await baseQuery.ToListAsync(cancellationToken);
        var totalDiscount = allItems.Sum(r => r.DiscountAppliedAmount.Amount);
        var totalPlatform = allItems.Sum(r => r.PlatformFundedAmount.Amount);
        var totalRestaurant = allItems.Sum(r => r.RestaurantFundedAmount.Amount);

        var pageReq = request.Page ?? new PageRequest();
        var pagedItems = allItems
            .OrderByDescending(r => r.RedeemedAtUtc)
            .Skip(pageReq.Skip)
            .Take(pageReq.NormalizedPageSize)
            .Select(r => new OfferRedemptionItemDto(
                r.Id,
                r.CustomerId,
                r.SubscriptionId,
                r.PromoCodeId,
                r.DiscountAppliedAmount.Amount,
                r.PlatformFundedAmount.Amount,
                r.RestaurantFundedAmount.Amount,
                r.RedeemedAtUtc
            ))
            .ToList();

        var pagedResult = new PagedResult<OfferRedemptionItemDto>(
            pagedItems,
            totalCount,
            pageReq.NormalizedPage,
            pageReq.NormalizedPageSize);

        return new OfferRedemptionSummaryDto(
            offer.Id,
            offer.NameAr,
            totalCount,
            totalDiscount,
            totalPlatform,
            totalRestaurant,
            pagedResult);
    }
}
