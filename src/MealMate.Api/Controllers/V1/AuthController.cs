using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController : ApiControllerBase
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

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new RefreshTokenCommand(
            body.RefreshToken,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new LogoutCommand(body?.RefreshToken), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("verify-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new VerifyOtpCommand(body.Destination, body.Purpose, body.Code), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("resend-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendOtp([FromBody] SendOtpRequest body, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new SendOtpCommand(body.Destination, body.Channel, body.Purpose), cancellationToken);
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

    [HttpPost("register/restaurant/start")]
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

    [HttpPost("register/restaurant/verify")]
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

    [HttpPost("register/restaurant/resend")]
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

public sealed record LoginRequest(string? Email, string? Phone, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string? RefreshToken);
public sealed record VerifyOtpRequest(string Destination, string Purpose, string Code);
public sealed record SendOtpRequest(string Destination, string Channel, string Purpose);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string OtpCode, string NewPassword);
public sealed record TotpChallengeRequest(string LoginChallengeToken);
public sealed record TotpConfirmSetupRequest(string LoginChallengeToken, string Code);
public sealed record TotpVerifyLoginRequest(string LoginChallengeToken, string? Code, string? RecoveryCode);
public sealed record RegisterRestaurantStartRequest(string Email, string Phone, string Password, string FullName);
public sealed record RegisterRestaurantVerifyRequest(string RegistrationToken, string Code);
public sealed record RegisterRestaurantResendRequest(string RegistrationToken);
