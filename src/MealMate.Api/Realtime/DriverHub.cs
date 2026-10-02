using System.Security.Claims;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Realtime;

/// <summary>
/// Real-time SignalR hub dedicated to Driver Mobile applications.
/// Emits instant notifications for newly assigned boxes, kitchen prep completions,
/// shift updates, and dispatcher emergency instructions. Also accepts live GPS
/// location streams from the driver app via the <see cref="UpdateLocation"/> method.
/// </summary>
[AllowAnonymous]
public sealed class DriverHub : Hub
{
    private readonly ISender _sender;
    private readonly DriverLocationBroadcaster _locationBroadcaster;

    public DriverHub(ISender sender, DriverLocationBroadcaster locationBroadcaster)
    {
        _sender = sender;
        _locationBroadcaster = locationBroadcaster;
    }

    public const string HubPath = "/hubs/driver-hub";
    public const string HubAlternativePath = "/hubs/driver";

    public const string UniversalDriverGroup = "drivers:all";

    public const string EventConnectionEstablished = "connection-established";
    public const string EventBoxAssigned = "box-assigned";
    public const string EventBoxReassigned = "box-reassigned";
    public const string EventKitchenReady = "kitchen-ready";
    public const string EventShiftStatusConfirmed = "shift-status-confirmed";
    public const string EventDispatcherMessage = "dispatcher-message";
    public const string EventDeliveryCompleted = "delivery-completed";

    public static string GroupForDriver(Guid driverId) => $"driver:{driverId:D}";
    public static string GroupForDriver(string driverId) => $"driver:{driverId}";
    public static string GroupForRestaurant(Guid restaurantId) => $"restaurant:{restaurantId:D}";
    public static string GroupForRestaurant(string restaurantId) => $"restaurant:{restaurantId}";

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();

        var driverIdClaim = Context.User?.FindFirstValue("DriverId")
                            ?? Context.User?.FindFirstValue("driver_id")
                            ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? httpContext?.Request.Query["driverId"].FirstOrDefault()
                            ?? httpContext?.Request.Query["driver_id"].FirstOrDefault()
                            ?? httpContext?.Request.Headers["X-Driver-Id"].FirstOrDefault();

        var restaurantIdClaim = Context.User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.RestaurantId)
                                ?? Context.User?.FindFirstValue("restaurant_id")
                                ?? httpContext?.Request.Query["restaurantId"].FirstOrDefault()
                                ?? httpContext?.Request.Headers["X-Restaurant-Id"].FirstOrDefault();

        if (!string.IsNullOrEmpty(driverIdClaim))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForDriver(driverIdClaim));
        }

        if (!string.IsNullOrEmpty(restaurantIdClaim))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForRestaurant(restaurantIdClaim));
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UniversalDriverGroup);

        await Clients.Caller.SendAsync(EventConnectionEstablished, new
        {
            connectionId = Context.ConnectionId,
            driverId = driverIdClaim,
            connectedAtUtc = DateTime.UtcNow,
            status = "Connected",
            message = "Connected to MealMate Driver Hub."
        });

        await base.OnConnectedAsync();
    }

    public async Task JoinDriverGroup(string driverId)
    {
        if (!string.IsNullOrWhiteSpace(driverId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForDriver(driverId));
        }
    }

    public async Task LeaveDriverGroup(string driverId)
    {
        if (!string.IsNullOrWhiteSpace(driverId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupForDriver(driverId));
        }
    }

    /// <summary>
    /// Live GPS streaming method: the driver app invokes this every few seconds while
    /// tracking is active. Persists a tracking point and pushes the update to dispatcher
    /// dashboards in real time (identical event to the REST endpoint). The driver identity
    /// is always resolved from the authenticated JWT, never from arguments. The connection
    /// must carry the driver token via the <c>access_token</c> query parameter.
    /// </summary>
    public async Task<object> UpdateLocation(
        decimal latitude,
        decimal longitude,
        double? heading = null,
        double? speedKmh = null)
    {
        if (Context.User?.Identity?.IsAuthenticated != true)
        {
            throw new HubException("unauthorized: connect with ?access_token=<driver JWT>");
        }

        var result = await _sender.Send(new UpdateDriverLocationCommand(
            DriverId: null,
            TripId: null,
            Latitude: latitude,
            Longitude: longitude,
            Heading: heading,
            SpeedKmh: speedKmh));

        if (!result.IsSuccess)
        {
            throw new HubException($"{result.Error.Code}: {result.Error.Description}");
        }

        var httpContext = Context.GetHttpContext();
        _ = Guid.TryParse(httpContext?.Request.Query["tripId"].FirstOrDefault(), out var requestTripId);

        try
        {
            await _locationBroadcaster.BroadcastAsync(
                result.Value,
                requestDriverId: null,
                requestTripId: requestTripId == Guid.Empty ? null : requestTripId,
                requestLatitude: latitude,
                requestLongitude: longitude,
                requestHeading: heading,
                requestSpeedKmh: speedKmh,
                Context.ConnectionAborted);
        }
        catch
        {
            // Non-fatal SignalR broadcast
        }

        return new
        {
            success = true,
            trackingPointId = result.Value.TrackingPointId,
            driverId = result.Value.DriverId,
            tripId = result.Value.TripId,
            latitude = result.Value.Latitude ?? latitude,
            longitude = result.Value.Longitude ?? longitude,
            heading = result.Value.Heading ?? heading,
            speedKmh = result.Value.SpeedKmh ?? speedKmh,
            locationZone = result.Value.LocationZone,
            remainingDistanceKm = result.Value.RemainingDistanceKm,
            remainingDistanceText = result.Value.RemainingDistanceText,
            recordedAtUtc = result.Value.RecordedAtUtc,
            message = result.Value.Message
        };
    }
}
