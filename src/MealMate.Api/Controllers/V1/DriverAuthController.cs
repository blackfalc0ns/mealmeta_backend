using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/auth/drivers")]
[EnableRateLimiting("auth")]
public sealed class DriverAuthController : ApiControllerBase
{
    [HttpGet("restaurants-options")]
    [AllowAnonymous]
    public async Task<IActionResult> GetRestaurantOptions(CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetPublicActiveRestaurantsQuery(), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("lookup-phone")]
    [AllowAnonymous]
    public async Task<IActionResult> LookupPhone(
        [FromBody] DriverLookupPhoneRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new DriverPhoneLookupQuery(body.Phone), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("first-time-setup")]
    [AllowAnonymous]
    public async Task<IActionResult> FirstTimeSetup(
        [FromBody] DriverFirstTimeSetupRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new DriverFirstTimeSetupCommand(
            body.Phone,
            body.OtpCode,
            body.Password,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(
        [FromBody] CreateDriverRegistrationCommand body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(body, cancellationToken);
        return HandleCreated(result, "/api/v1/drivers/registrations");
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new DriverLoginCommand(
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
        [FromBody] DriverForgotPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new DriverForgotPasswordCommand(body.Phone), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        [FromBody] DriverResetPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new DriverResetPasswordCommand(body.Phone, body.OtpCode, body.NewPassword), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("registrations/{id:guid}/documents/attach")]
    [Authorize]
    public async Task<IActionResult> AttachDocument(
        Guid id,
        [FromBody] DriverAttachDocumentRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new MealMate.Application.Features.IdentityAccess.Documents.AttachDocumentCommand(
            MealMate.Domain.Enums.DocumentOwnerType.Registration,
            id,
            body.DocumentType,
            body.StorageKey,
            body.FileName,
            body.ContentType,
            body.SizeBytes), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record DriverLookupPhoneRequest(string Phone);
public sealed record DriverFirstTimeSetupRequest(string Phone, string OtpCode, string Password);
public sealed record DriverForgotPasswordRequest(string Phone);
public sealed record DriverResetPasswordRequest(string Phone, string OtpCode, string NewPassword);
public sealed record DriverAttachDocumentRequest(
    MealMate.Domain.Enums.DocumentType DocumentType,
    string StorageKey,
    string? FileName = null,
    string? ContentType = null,
    long? SizeBytes = null);
