using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Realtime;

/// <summary>
/// Authenticated SignalR hub for real-time delivery dispatcher operations.
/// Emits live driver locations, trip state changes, and urgent operational alerts.
/// </summary>
[Authorize]
public sealed class DispatcherHub : Hub
{
    public const string HubPath = "/hubs/dispatcher-hub";
    public const string HubAlternativePath = "/hubs/dispatcher";

    public const string EventDriverLocationUpdated = "driver-location-updated";
    public const string EventDriverStatusUpdated = "driver-status-updated";
    public const string EventDriverIssueUpdated = "driver-issue-updated";
    public const string EventDriverMonitoringUpdated = "driver-monitoring-updated";
    public const string EventBoxAssigned = "box-assigned";

    public override async Task OnConnectedAsync()
    {
        var restaurantClaim = Context.User?.FindFirstValue("restaurant_id")
                              ?? Context.User?.FindFirstValue("RestaurantId");

        if (!string.IsNullOrEmpty(restaurantClaim))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForRestaurant(restaurantClaim));
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinRestaurantDispatcherGroup(string restaurantId)
    {
        if (!string.IsNullOrWhiteSpace(restaurantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForRestaurant(restaurantId));
        }
    }

    public async Task LeaveRestaurantDispatcherGroup(string restaurantId)
    {
        if (!string.IsNullOrWhiteSpace(restaurantId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupForRestaurant(restaurantId));
        }
    }

    public static string GroupForRestaurant(string restaurantId) => $"restaurant:{restaurantId}:dispatcher";
    public static string GroupForRestaurant(Guid restaurantId) => $"restaurant:{restaurantId}:dispatcher";
}
