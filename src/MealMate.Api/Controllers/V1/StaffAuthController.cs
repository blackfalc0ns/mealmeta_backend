using System.Threading;
using System.Threading.Tasks;
using MealMate.Api.Common;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.IdentityAccess.StaffAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/auth/staff")]
[EnableRateLimiting("auth")]
public sealed class StaffAuthController : ApiControllerBase
{
    [HttpGet("roles")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(System.Collections.Generic.IReadOnlyList<StaffRoleDto>), Microsoft.AspNetCore.Http.StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetStaffRolesQuery(), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("lookup-phone")]
    [AllowAnonymous]
    public async Task<IActionResult> LookupPhone(
        [FromBody] StaffLookupPhoneRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new StaffPhoneLookupQuery(body.Phone, body.Role), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("verify-first-time-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyFirstTimeOtp(
        [FromBody] StaffVerifyFirstTimeOtpRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new StaffVerifyFirstTimeOtpCommand(
            body.Phone,
            body.Role,
            body.OtpCode), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("set-password")]
    [AllowAnonymous]
    public async Task<IActionResult> SetPassword(
        [FromBody] StaffSetPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new StaffSetPasswordCommand(
            body.Phone,
            body.Role,
            body.VerificationToken,
            body.NewPassword,
            body.ConfirmPassword,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] StaffLoginApiRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new StaffLoginCommand(
            body.Phone,
            body.Role,
            body.Password,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("resend-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendOtp(
        [FromBody] StaffResendOtpRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new StaffResendOtpCommand(body.Phone, body.Role), cancellationToken);
        if (result.IsFailure) return HandleResult(result);
        return Ok(new { success = true, message = Messages.GetMessage(MessageCodes.Auth.OtpResent) });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] StaffForgotPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new StaffForgotPasswordCommand(body.Phone, body.Role), cancellationToken);
        if (result.IsFailure) return HandleResult(result);
        return Ok(new { success = true, message = Messages.GetMessage(MessageCodes.Auth.ForgotPasswordSent) });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        [FromBody] StaffResetPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new StaffResetPasswordCommand(
            body.Phone,
            body.Role,
            body.OtpCode,
            body.NewPassword,
            body.ConfirmPassword), cancellationToken);
        if (result.IsFailure) return HandleResult(result);
        return Ok(new { success = true, message = Messages.GetMessage(MessageCodes.Auth.PasswordResetSuccess) });
    }
}

public sealed record StaffLookupPhoneRequest(string Phone, string Role);
public sealed record StaffVerifyFirstTimeOtpRequest(string Phone, string Role, string OtpCode);
public sealed record StaffSetPasswordRequest(string Phone, string Role, string VerificationToken, string NewPassword, string ConfirmPassword);
public sealed record StaffLoginApiRequest(string Phone, string Role, string Password);
public sealed record StaffResendOtpRequest(string Phone, string Role);
public sealed record StaffForgotPasswordRequest(string Phone, string Role);
public sealed record StaffResetPasswordRequest(string Phone, string Role, string OtpCode, string NewPassword, string ConfirmPassword);
