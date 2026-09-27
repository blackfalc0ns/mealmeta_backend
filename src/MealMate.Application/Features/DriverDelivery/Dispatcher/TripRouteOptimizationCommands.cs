using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record OptimizeTripRouteCommand(
    Guid TripId) : ICommand<OptimizeTripRouteResponseDto>;

public sealed record OptimizeTripRouteResponseDto(
    Guid TripId,
    string TripCode,
    int StopsCount,
    IReadOnlyList<OptimizedStopSequenceDto> OptimizedStops,
    string Message,
    DateTime OptimizedAtUtc);

public sealed record OptimizedStopSequenceDto(
    Guid StopId,
    int NewSequenceNumber,
    string OrderCode,
    string CustomerName,
    string ZoneName);

public sealed class OptimizeTripRouteCommandHandler
    : ICommandHandler<OptimizeTripRouteCommand, OptimizeTripRouteResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public OptimizeTripRouteCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<OptimizeTripRouteResponseDto>> Handle(
        OptimizeTripRouteCommand request,
        CancellationToken cancellationToken)
    {
        var trip = await _db.Trips
            .Include(t => t.Stops)
            .FirstOrDefaultAsync(t => t.Id == request.TripId, cancellationToken);

        if (trip is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على الرحلة المحددة.");
        }

        if (trip.Stops.Count == 0)
        {
            return Error.Validation("Trips.NoStops", "لا توجد محطات في هذه الرحلة لتحسين مسارها.");
        }

        // Nearest Neighbor TSP optimization starting from restaurant location
        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(r => r.Id == trip.RestaurantId)
            .Select(r => new { r.Latitude, r.Longitude })
            .FirstOrDefaultAsync(cancellationToken);

        var currentLat = restaurant?.Latitude ?? 29.3375m;
        var currentLng = restaurant?.Longitude ?? 48.0280m;

        var unvisited = trip.Stops.ToList();
        var sortedStops = new List<TripStop>();

        while (unvisited.Count > 0)
        {
            var nextStop = unvisited
                .OrderBy(s => {
                    var sLat = s.Latitude ?? currentLat;
                    var sLng = s.Longitude ?? currentLng;
                    return MealMate.Application.Common.Geography.KuwaitGeoBounds.HaversineKm(currentLat, currentLng, sLat, sLng);
                })
                .First();

            sortedStops.Add(nextStop);
            unvisited.Remove(nextStop);
            currentLat = nextStop.Latitude ?? currentLat;
            currentLng = nextStop.Longitude ?? currentLng;
        }

        var optimizedList = new List<OptimizedStopSequenceDto>();
        for (var i = 0; i < sortedStops.Count; i++)
        {
            var stop = sortedStops[i];
            stop.SequenceNumber = i + 1;

            optimizedList.Add(new OptimizedStopSequenceDto(
                StopId: stop.Id,
                NewSequenceNumber: stop.SequenceNumber,
                OrderCode: stop.OrderCode,
                CustomerName: stop.CustomerName ?? "عميل ميل ميت",
                ZoneName: stop.ZoneNameAr
            ));
        }

        _db.AuditLogs.Add(new MealMate.Domain.IdentityAccess.AuditLog
        {
            Action = "TripRouteOptimized",
            EntityType = "Trip",
            EntityId = trip.Id,
            Reason = $"Optimized sequence for {trip.Stops.Count} stops",
            ActorUserId = _currentUser.UserId
        });

        await _db.SaveChangesAsync(cancellationToken);

        var isAr = _messages.IsArabic();
        var msg = isAr
            ? $"تم تحسين مسار الرحلة {trip.Code} وترتيب {trip.Stops.Count} محطات جغرافياً بنجاح."
            : $"Trip {trip.Code} route optimized for {trip.Stops.Count} stops successfully.";

        return Result.Success(new OptimizeTripRouteResponseDto(
            TripId: trip.Id,
            TripCode: trip.Code,
            StopsCount: trip.Stops.Count,
            OptimizedStops: optimizedList,
            Message: msg,
            OptimizedAtUtc: DateTime.UtcNow
        ));
    }
}
