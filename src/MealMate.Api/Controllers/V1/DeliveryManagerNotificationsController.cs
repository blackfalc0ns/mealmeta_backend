using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.DeliveryManagerApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// Delivery-manager (مسئول التوصيل) mobile-app notification bell and FCM device
/// token registration — mirrors the driver notification endpoints.
/// </summary>
[Route("api/v1/delivery-manager")]
[Authorize]
public sealed class DeliveryManagerNotificationsController : ApiControllerBase
{
    /// <summary>
    /// Returns the delivery manager's notifications list with read/unread
    /// statuses and deep links (delay escalations and other delivery alerts).
    /// </summary>
    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications(CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDeliveryManagerNotificationsQuery(), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Marks a delivery manager notification as read.
    /// </summary>
    [HttpPut("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new MarkDeliveryManagerNotificationReadCommand(id), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Registers or updates the FCM Push Device Token for the delivery manager's
    /// mobile device (Android / iOS), enabling delay-escalation pushes even when
    /// the app is closed.
    /// </summary>
    [HttpPost("device-token")]
    public async Task<IActionResult> RegisterDeviceToken(
        [FromBody] RegisterDeliveryManagerDeviceTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new RegisterDeliveryManagerDeviceTokenCommand(
            RestaurantId: request.RestaurantId,
            Token: request.Token,
            Platform: request.Platform,
            DeviceId: request.DeviceId), cancellationToken);

        return HandleResult(result);
    }

    /// <summary>
    /// Unregisters / deactivates the delivery manager's FCM Push Device Token
    /// (upon logout or token refresh). Accepts token in query (?token=...) or body.
    /// </summary>
    [HttpDelete("device-token")]
    public async Task<IActionResult> UnregisterDeviceToken(
        [FromQuery] string? token = null,
        [FromBody] UnregisterDeliveryManagerDeviceTokenRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        var targetToken = token ?? body?.Token;
        if (string.IsNullOrWhiteSpace(targetToken))
        {
            return HandleResult(MealMate.Application.Common.Models.Result.Success());
        }

        var result = await Sender.Send(new UnregisterDeliveryManagerDeviceTokenCommand(targetToken), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Alternative endpoint to deactivate the device token for HTTP clients
    /// where DELETE with body is restricted.
    /// </summary>
    [HttpPost("device-token/deactivate")]
    public async Task<IActionResult> DeactivateDeviceToken(
        [FromBody] UnregisterDeliveryManagerDeviceTokenRequest body,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new UnregisterDeliveryManagerDeviceTokenCommand(body.Token), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record RegisterDeliveryManagerDeviceTokenRequest(
    string Token,
    string Platform = "Android",
    string? DeviceId = null,
    Guid? RestaurantId = null);

public sealed record UnregisterDeliveryManagerDeviceTokenRequest(
    string Token);
