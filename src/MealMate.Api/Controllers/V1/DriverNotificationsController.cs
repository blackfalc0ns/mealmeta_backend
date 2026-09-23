using MealMate.Api.Common;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/driver")]
[AllowAnonymous]
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
    /// Enables push notifications even when the app is closed.
    /// </summary>
    [HttpPost("device-token")]
    public async Task<IActionResult> RegisterDeviceToken(
        [FromBody] RegisterDeviceTokenRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new RegisterDriverDeviceTokenCommand(
            DriverId: driverId,
            Token: request.Token,
            Platform: request.Platform,
            DeviceId: request.DeviceId), cancellationToken);

        return HandleResult(result);
    }
}
