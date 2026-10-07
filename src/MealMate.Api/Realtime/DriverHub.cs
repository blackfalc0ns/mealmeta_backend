using System.Security.Claims;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Realtime;

/// <summary>
/// Real-time SignalR hub dedicated to Driver Mobile applications.
/// Emits instant notifications for newly assigned boxes, kitchen prep completions,
/// supervisor-driven shift status updates, and dispatcher emergency instructions.
/// Also accepts live GPS location streams from the driver app via the
/// <see cref="UpdateLocation"/> method.
/// Connections require a driver JWT; group membership is always derived from the
/// authenticated identity, never from query strings, headers, or client arguments.
/// </summary>
[Authorize]
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
    public const string EventDriverStatusUpdated = "driver-status-updated";
    public const string EventDispatcherMessage = "dispatcher-message";
    public const string EventDeliveryCompleted = "delivery-completed";

    public static string GroupForDriver(Guid driverId) => $"driver:{driverId:D}";
    public static string GroupForDriver(string driverId) => $"driver:{driverId}";
    public static string GroupForRestaurant(Guid restaurantId) => $"restaurant:{restaurantId:D}";
    public static string GroupForRestaurant(string restaurantId) => $"restaurant:{restaurantId}";

    public override async Task OnConnectedAsync()
    {
        // The hub is driver-only: an authenticated connection that cannot be
        // resolved to a driver profile is aborted instead of lingering unnamed.
        Guid driverProfileId;
        try
        {
            driverProfileId = await ResolveDriverProfileIdOrThrowAsync();
        }
        catch (HubException)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupForDriver(driverProfileId));

        // Restaurant group comes from the JWT claim only — no query/header identity.
        var restaurantIdClaim = Context.User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.RestaurantId);
        if (!string.IsNullOrEmpty(restaurantIdClaim) && Guid.TryParse(restaurantIdClaim, out var restaurantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForRestaurant(restaurantId));
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UniversalDriverGroup);

        await Clients.Caller.SendAsync(EventConnectionEstablished, new
        {
            connectionId = Context.ConnectionId,
            driverId = driverProfileId,
            connectedAtUtc = DateTime.UtcNow,
            status = "Connected",
            message = "Connected to MealMate Driver Hub."
        });

        await base.OnConnectedAsync();
    }

    public async Task JoinDriverGroup(string driverId)
    {
        var ownDriverId = await ResolveDriverProfileIdOrThrowAsync();

        if (!Guid.TryParse(driverId, out var requested) || requested != ownDriverId)
        {
            throw new HubException("forbidden: cannot join another driver's group");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupForDriver(ownDriverId));
    }

    public async Task LeaveDriverGroup(string driverId)
    {
        var ownDriverId = await ResolveDriverProfileIdOrThrowAsync();

        if (!Guid.TryParse(driverId, out var requested) || requested != ownDriverId)
        {
            throw new HubException("forbidden: cannot leave another driver's group");
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupForDriver(ownDriverId));
    }

    /// <summary>
    /// Resolves the connection's driver profile id from the authenticated JWT only.
    /// Throws a <see cref="HubException"/> when the caller is unauthenticated or
    /// does not map to a driver profile, so private per-driver events can never be
    /// routed by caller-supplied identifiers.
    /// </summary>
    private async Task<Guid> ResolveDriverProfileIdOrThrowAsync()
    {
        if (Context.User?.Identity?.IsAuthenticated != true)
        {
            throw new HubException("unauthorized: connect with ?access_token=<driver JWT>");
        }

        var identity = await _sender.Send(new GetDriverIdentityForCurrentUserQuery());
        if (!identity.IsSuccess)
        {
            throw new HubException("forbidden: connection does not belong to a driver account");
        }

        return identity.Value.DriverProfileId;
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

        // G03: the legacy invocation cannot carry measured accuracy. Clients must
        // move to UpdateLocationV2 - an explicit upgrade requirement, not a silent
        // accuracy fabrication.
        throw new HubException("upgrade_required: location streaming now requires measured accuracy; invoke UpdateLocationV2(latitude, longitude, accuracyMeters, capturedAtUtc, heading, speedKmh)");
    }

    /// <summary>
    /// v2 GPS streaming (G03): the client sends its MEASURED accuracy and the real fix
    /// capture time. The command validates both; the server stamps receive time.
    /// </summary>
    public async Task<object> UpdateLocationV2(
        decimal latitude,
        decimal longitude,
        double accuracyMeters,
        DateTime capturedAtUtc,
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
            SpeedKmh: speedKmh,
            AccuracyMeters: accuracyMeters,
            CapturedAtUtc: capturedAtUtc));

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
