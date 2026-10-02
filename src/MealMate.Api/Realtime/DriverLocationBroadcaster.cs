using MealMate.Application.Features.DriverDelivery.Dispatcher;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Realtime;

/// <summary>
/// Pushes a driver location update to dispatcher dashboard clients over SignalR.
/// Shared by the REST endpoint and the DriverHub streaming method so both paths
/// emit the identical <c>driver-location-updated</c> payload.
/// </summary>
public sealed class DriverLocationBroadcaster(IHubContext<DispatcherHub> dispatcherHub)
{
    public async Task BroadcastAsync(
        UpdateDriverLocationResponse result,
        Guid? requestDriverId,
        Guid? requestTripId,
        decimal requestLatitude,
        decimal requestLongitude,
        double? requestHeading,
        double? requestSpeedKmh,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            driverId = result.DriverId ?? requestDriverId,
            tripId = result.TripId ?? requestTripId,
            latitude = result.Latitude ?? requestLatitude,
            longitude = result.Longitude ?? requestLongitude,
            heading = result.Heading ?? requestHeading ?? 0.0,
            speedKmh = result.SpeedKmh ?? requestSpeedKmh ?? 0.0,
            locationZone = result.LocationZone,
            remainingDistanceKm = result.RemainingDistanceKm,
            remainingDistanceText = result.RemainingDistanceText,
            updatedAtUtc = result.RecordedAtUtc
        };

        if (result.RestaurantId.HasValue)
        {
            var groupName = DispatcherHub.GroupForRestaurant(result.RestaurantId.Value);
            await dispatcherHub.Clients.Group(groupName)
                .SendAsync(DispatcherHub.EventDriverLocationUpdated, payload, cancellationToken);
            await dispatcherHub.Clients.Group(DispatcherHub.UniversalDispatcherGroup)
                .SendAsync(DispatcherHub.EventDriverLocationUpdated, payload, cancellationToken);
        }
        else
        {
            await dispatcherHub.Clients.All
                .SendAsync(DispatcherHub.EventDriverLocationUpdated, payload, cancellationToken);
        }
    }
}
