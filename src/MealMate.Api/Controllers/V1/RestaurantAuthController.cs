using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/auth/restaurants")]
[EnableRateLimiting("auth")]
public sealed class RestaurantAuthController : ApiControllerBase
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

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new ForgotPasswordCommand(body.Email), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new ResetPasswordCommand(body.Email, body.OtpCode, body.NewPassword), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("register/start")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterRestaurantStart(
        [FromBody] RegisterRestaurantStartRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new StartRestaurantRegistrationCommand(body.Email, body.Phone, body.Password, body.FullName),
            cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("register/verify")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterRestaurantVerify(
        [FromBody] RegisterRestaurantVerifyRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new VerifyRestaurantRegistrationCommand(body.RegistrationToken, body.Code),
            cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("register/resend")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterRestaurantResend(
        [FromBody] RegisterRestaurantResendRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new ResendRestaurantRegistrationOtpCommand(body.RegistrationToken),
            cancellationToken);
        return HandleResult(result);
    }
}
