using MealMate.Api.Common;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/driver")]
[Authorize]
public sealed class DriverNotificationsController : ApiControllerBase
{
    /// <summary>
    /// Returns the driver notifications list with read/unread statuses and deep links.
    /// Powers the notification bell on Screen 05.02 and 05.03.
    /// </summary>
    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDriverNotificationsQuery(driverId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Marks a driver notification as read.
    /// </summary>
    [HttpPut("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new MarkDriverNotificationReadCommand(id), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Registers or updates the FCM Push Device Token for the driver's mobile device (Android / iOS).
    /// Supports pre-login registration via registrationId or post-login via authenticated session / driverId.
    /// Enables push notifications even when the app is closed.
    /// </summary>
    [HttpPost("device-token")]
    public async Task<IActionResult> RegisterDeviceToken(
        [FromBody] RegisterDeviceTokenRequest request,
        [FromQuery] Guid? driverId = null,
        [FromQuery] Guid? registrationId = null,
        CancellationToken cancellationToken = default)
    {
        var targetRegistrationId = registrationId ?? request.RegistrationId;
        var result = await Sender.Send(new RegisterDriverDeviceTokenCommand(
            DriverId: driverId,
            Token: request.Token,
            Platform: request.Platform,
            DeviceId: request.DeviceId,
            RegistrationId: targetRegistrationId), cancellationToken);

        return HandleResult(result);
    }

    /// <summary>
    /// Unregisters / deactivates the driver's FCM Push Device Token (upon logout or token refresh).
    /// Accepts token in query parameter (?token=...) or in JSON body.
    /// </summary>
    [HttpDelete("device-token")]
    public async Task<IActionResult> UnregisterDeviceToken(
        [FromQuery] string? token = null,
        [FromBody] UnregisterDriverDeviceTokenRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        var targetToken = token ?? body?.Token;
        if (string.IsNullOrWhiteSpace(targetToken))
        {
            return HandleResult(MealMate.Application.Common.Models.Result.Success());
        }

        var result = await Sender.Send(new UnregisterDriverDeviceTokenCommand(targetToken), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Alternative endpoint to deactivate driver's device token for HTTP clients where DELETE with body is restricted.
    /// </summary>
    [HttpPost("device-token/deactivate")]
    public async Task<IActionResult> DeactivateDeviceToken(
        [FromBody] UnregisterDriverDeviceTokenRequest body,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new UnregisterDriverDeviceTokenCommand(body.Token), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record RegisterDeviceTokenRequest(
    string Token,
    string Platform = "Android",
    string? DeviceId = null,
    Guid? RegistrationId = null);

public sealed record UnregisterDriverDeviceTokenRequest(
    string Token);

