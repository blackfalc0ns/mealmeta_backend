using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/auth/delivery-managers")]
[EnableRateLimiting("auth")]
public sealed class DeliveryManagerAuthController : ApiControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new DeliveryManagerLoginCommand(
            body.Email,
            body.Phone,
            body.Password,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("verify-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyOtp(
        [FromBody] VerifyOtpRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new VerifyOtpCommand(body.Destination, body.Purpose, body.Code), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("resend-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendOtp(
        [FromBody] SendOtpRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new SendOtpCommand(body.Destination, body.Channel, body.Purpose), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] DeliveryManagerForgotPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new DeliveryManagerForgotPasswordCommand(body.Email), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        [FromBody] DeliveryManagerResetPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new DeliveryManagerResetPasswordCommand(body.Email, body.OtpCode, body.NewPassword), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        [FromBody] DeliveryManagerChangePasswordRequest body,
        CancellationToken cancellationToken)
    {
        var subStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(subStr, out var userId))
        {
            return Unauthorized();
        }

        var result = await Sender.Send(new DeliveryManagerChangePasswordCommand(userId, body.CurrentPassword, body.NewPassword), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("lookup-phone")]
    [AllowAnonymous]
    public async Task<IActionResult> LookupPhone(
        [FromBody] DeliveryManagerLookupPhoneRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration.DeliveryManagerPhoneLookupQuery(body.Phone), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("first-time-setup")]
    [AllowAnonymous]
    public async Task<IActionResult> FirstTimeSetup(
        [FromBody] DeliveryManagerFirstTimeSetupRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration.DeliveryManagerFirstTimeSetupCommand(
            body.Phone,
            body.Otp,
            body.NewPassword,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record DeliveryManagerLookupPhoneRequest(string Phone);
public sealed record DeliveryManagerFirstTimeSetupRequest(string Phone, string Otp, string NewPassword, string ConfirmPassword);
public sealed record DeliveryManagerForgotPasswordRequest(string Email);
public sealed record DeliveryManagerResetPasswordRequest(string Email, string OtpCode, string NewPassword);
public sealed record DeliveryManagerChangePasswordRequest(string CurrentPassword, string NewPassword);
