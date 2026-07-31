using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/auth/customers")]
[EnableRateLimiting("auth")]
public sealed class CustomerAuthController : ApiControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(
        [FromBody] CustomerRegisterRequest body,
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
        return HandleResult(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new CustomerLoginCommand(
            body.Email,
            body.Phone,
            body.Password,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("verify-phone")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyPhone(
        [FromBody] CustomerVerifyPhoneRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new CustomerVerifyPhoneCommand(
            body.Phone,
            body.Code,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("resend-phone-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendPhoneOtp(
        [FromBody] CustomerResendPhoneOtpRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new CustomerResendPhoneOtpCommand(body.Phone), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] CustomerForgotPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new CustomerForgotPasswordCommand(body.Phone), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        [FromBody] CustomerResetPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new CustomerResetPasswordCommand(body.Phone, body.OtpCode, body.NewPassword), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record CustomerRegisterRequest(
    string FullName,
    string? Email,
    string Phone,
    string Password,
    string? PreferredLanguage);

public sealed record CustomerVerifyPhoneRequest(string Phone, string Code);
public sealed record CustomerResendPhoneOtpRequest(string Phone);
public sealed record CustomerForgotPasswordRequest(string Phone);
public sealed record CustomerResetPasswordRequest(string Phone, string OtpCode, string NewPassword);

