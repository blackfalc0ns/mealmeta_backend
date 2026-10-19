using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Offers.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Offers.Queries;

public sealed record GetOfferEligibleRestaurantsQuery(Guid OfferId) : IQuery<OfferEligibleRestaurantsResponse>;

public sealed record OfferRestaurantDto(Guid RestaurantId, string NameAr, string NameEn, string? LogoUrl);

public sealed record OfferEligibleRestaurantsResponse(
    Guid OfferId,
    string DisplayNameAr,
    string DisplayNameEn,
    bool AllRestaurants,
    int TotalCount,
    List<OfferRestaurantDto> Restaurants);

public sealed class GetOfferEligibleRestaurantsQueryHandler : IQueryHandler<GetOfferEligibleRestaurantsQuery, OfferEligibleRestaurantsResponse>
{
    private const int MaxRestaurants = 500;

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public GetOfferEligibleRestaurantsQueryHandler(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<OfferEligibleRestaurantsResponse>> Handle(GetOfferEligibleRestaurantsQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var offer = await _db.Offers.AsNoTracking()
            .Include(o => o.EligibleRestaurants)
            .FirstOrDefaultAsync(o => o.Id == request.OfferId
                && o.Status == OfferStatus.Active
                && o.StartsAtUtc <= now
                && o.EndsAtUtc >= now, cancellationToken);
        if (offer is null)
        {
            return Error.NotFound("offers.customer.unavailable", string.Empty);
        }

        var explicitRestaurantIds = offer.EligibleRestaurants.Select(x => x.RestaurantId).Distinct().ToList();
        var allRestaurants = explicitRestaurantIds.Count == 0;

        var rows = allRestaurants
            ? await _db.RestaurantProfiles.AsNoTracking()
                .Where(x => x.VerificationStatus == VerificationStatus.Approved && x.IsOperationallyActive)
                .Select(x => new RestaurantNameRow(x.Id, x.TradeNameAr, x.TradeNameEn))
                .ToListAsync(cancellationToken)
            : await _db.RestaurantProfiles.AsNoTracking()
                .Where(x => explicitRestaurantIds.Contains(x.Id))
                .Select(x => new RestaurantNameRow(x.Id, x.TradeNameAr, x.TradeNameEn))
                .ToListAsync(cancellationToken);

        if (allRestaurants)
        {
            rows = rows.Take(MaxRestaurants).ToList();
        }

        rows = rows
            .OrderBy(x => x.TradeNameEn, StringComparer.Ordinal)
            .ThenBy(x => x.TradeNameAr, StringComparer.Ordinal)
            .ThenBy(x => x.Id)
            .ToList();

        var logos = await SubscriptionRestaurantSummaryImages.LoadAsync(_db, rows.Select(x => x.Id).ToList(), cancellationToken);

        var restaurants = rows
            .Select(x => new OfferRestaurantDto(x.Id, x.TradeNameAr, x.TradeNameEn, logos.GetValueOrDefault(x.Id)))
            .ToList();

        return new OfferEligibleRestaurantsResponse(offer.Id, offer.NameAr, offer.NameEn, allRestaurants, restaurants.Count, restaurants);
    }

    private sealed record RestaurantNameRow(Guid Id, string TradeNameAr, string TradeNameEn);
}
