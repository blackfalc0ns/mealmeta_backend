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

        // Find the stop to determine destination coordinates or zone
        var stop = await _db.TripStops
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        var zone = stop?.ZoneNameAr ?? "السالمية";
        var destLat = 29.3333m;
        var destLng = 48.0667m;

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
