using MealMate.Api.Common;
using MealMate.Application.Features.RestaurantPush;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/device-tokens")]
[Route("api/v1/restaurants/me/device-tokens")]
[Authorize]
public sealed class RestaurantDeviceTokensController : ApiControllerBase
{
    [HttpPut]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRestaurantDeviceTokenRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RegisterRestaurantDeviceTokenCommand(
            body.Token,
            body.Platform,
            body.DeviceId), cancellationToken));

    [HttpDelete]
    public async Task<IActionResult> Unregister(
        [FromQuery] string? token = null,
        [FromBody] UnregisterRestaurantDeviceTokenRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        var targetToken = token ?? body?.Token;
        if (string.IsNullOrWhiteSpace(targetToken))
            return HandleResult(MealMate.Application.Common.Models.Result.Success());

        return HandleResult(await Sender.Send(new UnregisterRestaurantDeviceTokenCommand(targetToken), cancellationToken));
    }

    [HttpPost("deactivate")]
    public async Task<IActionResult> Deactivate(
        [FromBody] UnregisterRestaurantDeviceTokenRequest body,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new UnregisterRestaurantDeviceTokenCommand(body.Token), cancellationToken));

    [AllowAnonymous]
    [HttpPost("test")]
    public async Task<IActionResult> SendTestPush(
        [FromBody] TestRestaurantPushRequest? body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SendRestaurantTestPushCommand(body?.Token), cancellationToken));

    [HttpPost("test-push/dispatcher")]
    public async Task<IActionResult> SendDispatcherTestPush(
        [FromBody] MealMate.Application.Features.RestaurantPush.SendFleetTestPushApiRequest? body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new MealMate.Application.Features.RestaurantPush.SendDispatcherTestPushCommand(
            RestaurantId: body?.RestaurantId,
            DeliveryManagerId: body?.TargetId,
            CustomMessage: body?.Message), cancellationToken));

    [HttpPost("test-push/driver")]
    public async Task<IActionResult> SendDriverTestPush(
        [FromBody] MealMate.Application.Features.RestaurantPush.SendFleetTestPushApiRequest? body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new MealMate.Application.Features.RestaurantPush.SendDriverTestPushCommand(
            RestaurantId: body?.RestaurantId,
            DriverId: body?.TargetId,
            CustomMessage: body?.Message), cancellationToken));
}

public sealed record RegisterRestaurantDeviceTokenRequest(
    string Token,
    MobilePlatform Platform = MobilePlatform.Web,
    string? DeviceId = null);

public sealed record UnregisterRestaurantDeviceTokenRequest(string Token);

public sealed record TestRestaurantPushRequest(string? Token = null);
