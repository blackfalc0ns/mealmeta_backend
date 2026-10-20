using MealMate.Api.Realtime;
using MealMate.Application.Common.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Realtime;

public sealed class SignalRDriverSupportHandoverNotifier(
    IHubContext<DispatcherHub> dispatcherHub,
    IHubContext<DriverHub> driverHub)
{
    public async Task NotifyAsync(DriverSupportHandoverEvent message, CancellationToken cancellationToken)
    {
        var payload = new
        {
            eventId = message.EventId,
            occurredAtUtc = message.OccurredAtUtc,
            handoverId = message.HandoverId,
            tripId = message.TripId,
            replacementTripId = message.ReplacementTripId,
            restaurantId = message.RestaurantId,
            originalDriverId = message.OriginalDriverProfileId,
            replacementDriverId = message.ReplacementDriverProfileId,
            status = message.Status,
            eventName = message.Event
        };
        await dispatcherHub.Clients.Group(DispatcherHub.GroupForRestaurant(message.RestaurantId))
            .SendAsync(DispatcherHub.EventDriverSupportHandoverUpdated, payload, cancellationToken);
        await driverHub.Clients.Group(DriverHub.GroupForDriver(message.OriginalDriverProfileId))
            .SendAsync(DriverHub.EventSupportHandoverUpdated, payload, cancellationToken);
        await driverHub.Clients.Group(DriverHub.GroupForDriver(message.ReplacementDriverProfileId))
            .SendAsync(DriverHub.EventSupportHandoverUpdated, payload, cancellationToken);
    }

    public async Task NotifyIssueAsync(DriverSupportIssueEvent message, CancellationToken cancellationToken)
    {
        var payload = new
        {
            eventId = message.EventId,
            occurredAtUtc = message.OccurredAtUtc,
            issueId = message.IssueId,
            restaurantId = message.RestaurantId,
            driverId = message.DriverProfileId,
            category = message.Category,
            eventName = message.Event
        };
        await dispatcherHub.Clients.Group(DispatcherHub.GroupForRestaurant(message.RestaurantId))
            .SendAsync(DispatcherHub.EventDriverIssueUpdated, payload, cancellationToken);
        if (message.DriverProfileId.HasValue)
            await driverHub.Clients.Group(DriverHub.GroupForDriver(message.DriverProfileId.Value))
                .SendAsync(DriverHub.EventDriverIssueUpdated, payload, cancellationToken);
    }
}
