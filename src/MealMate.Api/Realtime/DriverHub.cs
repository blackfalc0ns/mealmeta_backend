using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Realtime;

/// <summary>
/// Real-time SignalR hub dedicated to Driver Mobile applications.
/// Emits instant notifications for newly assigned boxes, kitchen prep completions,
/// shift updates, and dispatcher emergency instructions.
/// </summary>
[AllowAnonymous]
public sealed class DriverHub : Hub
{
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
}
