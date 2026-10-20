using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MealMate.Domain.Enums;

namespace MealMate.Api.Realtime;

/// <summary>
/// Real-time SignalR hub for delivery dispatcher operations.
/// Supports both JWT authentication and development fallback.
/// Emits live driver locations, trip state changes, and urgent operational alerts.
/// </summary>
[Authorize(Policy = "DispatcherPolicy")]
public sealed class DispatcherHub : Hub
{
    public const string HubPath = "/hubs/dispatcher-hub";
    public const string HubAlternativePath = "/hubs/dispatcher";

    public const string UniversalDispatcherGroup = "dispatchers:all";

    public const string EventConnectionEstablished = "connection-established";
    public const string EventDriverLocationUpdated = "driver-location-updated";
    public const string EventDriverStatusUpdated = "driver-status-updated";
    public const string EventDriverAvailabilityUpdated = "driver-availability-updated";
    public const string EventDriverIssueUpdated = "driver-issue-updated";
    public const string EventDriverSupportHandoverUpdated = "driver-support-handover-updated";
    public const string EventDriverMonitoringUpdated = "driver-monitoring-updated";
    public const string EventBoxAssigned = "box-assigned";

    public override async Task OnConnectedAsync()
    {
        // Restaurant scope must come from the authenticated principal. Client supplied
        // query strings and headers are never authorization sources.
        var restaurantClaim = Context.User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.RestaurantId)
                              ?? Context.User?.FindFirstValue("restaurant_id")
                              ?? Context.User?.FindFirstValue("RestaurantId");

        if (!string.IsNullOrEmpty(restaurantClaim))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForRestaurant(restaurantClaim));
        }
        else
        {
            var userType = Context.User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.UserType);
            if (string.Equals(userType, nameof(UserType.Admin), StringComparison.OrdinalIgnoreCase))
                await Groups.AddToGroupAsync(Context.ConnectionId, UniversalDispatcherGroup);
            else
            {
                Context.Abort();
                return;
            }
        }

        // Send an immediate handshake/confirmation event so the client knows connection succeeded
        await Clients.Caller.SendAsync(EventConnectionEstablished, new
        {
            connectionId = Context.ConnectionId,
            restaurantId = restaurantClaim,
            connectedAtUtc = DateTime.UtcNow,
            status = "Connected",
            message = "Connected to MealMate Dispatcher Hub."
        });

        await base.OnConnectedAsync();
    }

    public async Task JoinRestaurantDispatcherGroup(string restaurantId)
    {
        var scope = Context.User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.RestaurantId)
                    ?? Context.User?.FindFirstValue("restaurant_id")
                    ?? Context.User?.FindFirstValue("RestaurantId");
        var isAdmin = string.Equals(Context.User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.UserType), nameof(UserType.Admin), StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(restaurantId) && (isAdmin || string.Equals(scope, restaurantId, StringComparison.OrdinalIgnoreCase)))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForRestaurant(restaurantId));
        }
    }

    public async Task JoinRestaurant(string restaurantId)
    {
        await JoinRestaurantDispatcherGroup(restaurantId);
    }

    public async Task LeaveRestaurantDispatcherGroup(string restaurantId)
    {
        var scope = Context.User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.RestaurantId)
                    ?? Context.User?.FindFirstValue("restaurant_id")
                    ?? Context.User?.FindFirstValue("RestaurantId");
        var isAdmin = string.Equals(Context.User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.UserType), nameof(UserType.Admin), StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(restaurantId) && (isAdmin || string.Equals(scope, restaurantId, StringComparison.OrdinalIgnoreCase)))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupForRestaurant(restaurantId));
        }
    }

    /// <summary>
    /// Optional streaming method for clients utilizing SignalR stream APIs.
    /// Emits a periodic heartbeat/status for active monitoring.
    /// </summary>
    public async IAsyncEnumerable<object> StreamDriverLocations(
        string? restaurantId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return new
            {
                restaurantId,
                timestampUtc = DateTime.UtcNow,
                status = "StreamingActive"
            };

            await Task.Delay(5000, cancellationToken);
        }
    }

    public static string GroupForRestaurant(string restaurantId) => $"restaurant:{restaurantId}:dispatcher";
    public static string GroupForRestaurant(Guid restaurantId) => $"restaurant:{restaurantId}:dispatcher";
}
