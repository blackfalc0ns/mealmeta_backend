using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/auth/admin")]
[EnableRateLimiting("auth")]
public sealed class AdminAuthController : ApiControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new LoginCommand(
            body.Email,
            body.Phone,
            body.Password,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("totp/setup")]
    [AllowAnonymous]
    public async Task<IActionResult> TotpSetup([FromBody] TotpChallengeRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new TotpSetupCommand(body.LoginChallengeToken), cancellationToken));

    [HttpPost("totp/confirm-setup")]
    [AllowAnonymous]
    public async Task<IActionResult> TotpConfirmSetup([FromBody] TotpConfirmSetupRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new TotpConfirmSetupCommand(body.LoginChallengeToken, body.Code), cancellationToken));

    [HttpPost("totp/verify-login")]
    [AllowAnonymous]
    public async Task<IActionResult> TotpVerifyLogin([FromBody] TotpVerifyLoginRequest body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new TotpVerifyLoginCommand(
            body.LoginChallengeToken,
            body.Code,
            body.RecoveryCode,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }
}
