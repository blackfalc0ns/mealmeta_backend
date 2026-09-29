using MealMate.Api.Common;
using MealMate.Application.Features.AdminPush;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/device-tokens")]
[Route("api/v1/admin/me/device-tokens")]
[Authorize]
public sealed class AdminDeviceTokensController : ApiControllerBase
{
    [HttpPut]
    public async Task<IActionResult> Register(
        [FromBody] RegisterAdminDeviceTokenRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RegisterAdminDeviceTokenCommand(
            body.Token,
            body.Platform,
            body.DeviceId), cancellationToken));

    [HttpDelete]
    public async Task<IActionResult> Unregister(
        [FromBody] UnregisterAdminDeviceTokenRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UnregisterAdminDeviceTokenCommand(body.Token), cancellationToken));

    [HttpPost("test")]
    public async Task<IActionResult> SendTestPush(
        [FromBody] TestAdminPushRequest? body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SendAdminTestPushCommand(body?.Token), cancellationToken));
}

public sealed record RegisterAdminDeviceTokenRequest(
    string Token,
    MobilePlatform Platform = MobilePlatform.Web,
    string? DeviceId = null);

public sealed record UnregisterAdminDeviceTokenRequest(string Token);

public sealed record TestAdminPushRequest(string? Token = null);
