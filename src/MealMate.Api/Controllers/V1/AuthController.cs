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
    [HttpPost("register/customer")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterCustomer(
        [FromBody] RegisterCustomerRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new RegisterCustomerCommand(
            body.FullName,
            body.Email,
            body.Phone,
            body.Password,
            body.PreferredLanguage,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleCreated(result, "/api/v1/auth/login");
    }

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
    [Authorize]
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
}

public sealed record RegisterCustomerRequest(
    string FullName,
    string? Email,
    string? Phone,
    string Password,
    string? PreferredLanguage);

public sealed record LoginRequest(string? Email, string? Phone, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string? RefreshToken);
public sealed record VerifyOtpRequest(string Destination, string Purpose, string Code);
public sealed record SendOtpRequest(string Destination, string Channel, string Purpose);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string OtpCode, string NewPassword);
