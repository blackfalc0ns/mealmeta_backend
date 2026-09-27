using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetBoxCandidateDriversQuery(
    Guid BoxId,
    Guid? RestaurantId = null) : IQuery<IReadOnlyList<CandidateDriverDto>>;

public sealed class GetBoxCandidateDriversQueryHandler : IQueryHandler<GetBoxCandidateDriversQuery, IReadOnlyList<CandidateDriverDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDispatchRecommendationEngine _recommendationEngine;

    public GetBoxCandidateDriversQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDispatchRecommendationEngine recommendationEngine)
    {
        _db = db;
        _currentUser = currentUser;
        _recommendationEngine = recommendationEngine;
    }

    public async Task<Result<IReadOnlyList<CandidateDriverDto>>> Handle(
        GetBoxCandidateDriversQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(request.RestaurantId, cancellationToken);

        // Find the stop or subscription day to determine destination coordinates and zone
        var stop = await _db.TripStops
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        string zone;
        decimal destLat;
        decimal destLng;

        if (stop != null)
        {
            zone = stop.ZoneNameAr ?? "السالمية";
            destLat = stop.Latitude ?? 29.3375m;
            destLng = stop.Longitude ?? 48.0280m;
        }
        else
        {
            var day = await _db.SubscriptionDays
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == request.BoxId, cancellationToken);

            zone = !string.IsNullOrWhiteSpace(day?.DeliveryAddressLine) ? day.DeliveryAddressLine : "السالمية";
            MealMate.Domain.IdentityAccess.CustomerAddress? customerAddr = null;
            if (day?.DeliveryAddressId.HasValue == true)
            {
                customerAddr = await _db.CustomerAddresses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == day.DeliveryAddressId.Value, cancellationToken);
            }
            destLat = customerAddr?.Latitude ?? 29.3375m;
            destLng = customerAddr?.Longitude ?? 48.0280m;
        }

        var candidates = await _recommendationEngine.GetCandidateDriversForBoxAsync(
            restaurantId, zone, destLat, destLng, cancellationToken);

        return Result.Success(candidates);
    }

    private async Task<Guid> ResolveRestaurantIdAsync(Guid? requestedRestaurantId, CancellationToken cancellationToken)
    {
        if (requestedRestaurantId.HasValue && requestedRestaurantId.Value != Guid.Empty)
        {
            return requestedRestaurantId.Value;
        }

        if (_currentUser.RestaurantId.HasValue && _currentUser.RestaurantId.Value != Guid.Empty)
        {
            return _currentUser.RestaurantId.Value;
        }

        var firstRestaurantId = await _db.RestaurantProfiles
            .AsNoTracking()
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return firstRestaurantId != Guid.Empty ? firstRestaurantId : Guid.NewGuid();
    }
}
