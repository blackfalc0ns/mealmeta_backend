using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Realtime;

/// <summary>
/// Authenticated SignalR hub for restaurant registration review updates.
/// Clients join a per-user group automatically on connect. The admin never
/// chooses a group or user id — the hub derives it from the authenticated claims.
/// </summary>
[Authorize]
public sealed class RestaurantReviewHub : Hub
{
    internal const string HubPath = "/hubs/restaurant-review";

    /// <summary>
    /// Derives the current user id from the JWT claims and joins the
    /// <c>user:{userId}</c> group. No client-supplied group id is accepted.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? Context.User?.FindFirstValue("sub");

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForUser(userId));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? Context.User?.FindFirstValue("sub");

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupForUser(userId));
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Returns the SignalR group name for a given user id.
    /// </summary>
    internal static string GroupForUser(string userId) => $"user:{userId}";
}
