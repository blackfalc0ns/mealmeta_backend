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
            Phone: CustomerPhoneInput.Normalize(body.CountryCode, body.Phone) ?? string.Empty,
            body.Password), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] CustomerLoginRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new CustomerLoginCommand(
            Email: null,
            Phone: CustomerPhoneInput.Normalize(body.CountryCode, body.Phone),
            body.Password,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("family-first-login/verify-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyFamilyMemberFirstLoginOtp(
        [FromBody] FamilyMemberFirstLoginOtpRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new VerifyFamilyMemberFirstLoginOtpCommand(
            body.LoginChallengeToken,
            body.OtpCode,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken));

    [HttpPost("family-first-login/set-password")]
    [AllowAnonymous]
    public async Task<IActionResult> SetFamilyMemberFirstLoginPassword(
        [FromBody] FamilyMemberFirstLoginSetPasswordRequest body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SetFamilyMemberFirstLoginPasswordCommand(
            body.LoginChallengeToken,
            body.NewPassword,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()), cancellationToken));

    [HttpPost("verify-phone")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyPhone(
        [FromBody] CustomerVerifyPhoneRequest body,
        CancellationToken cancellationToken)
    {
        var code = string.IsNullOrWhiteSpace(body.Code) ? body.OtpCode : body.Code;
        var result = await Sender.Send(new CustomerVerifyPhoneCommand(
            CustomerPhoneInput.Normalize(body.CountryCode, body.Phone) ?? string.Empty,
            code ?? string.Empty,
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
        var result = await Sender.Send(new CustomerResendPhoneOtpCommand(
            CustomerPhoneInput.Normalize(body.CountryCode, body.Phone) ?? string.Empty), cancellationToken);
        return result.IsSuccess
            ? Ok(new OtpActionResponse(true, true, "verify_phone_otp"))
            : HandleResult(result);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] CustomerForgotPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new CustomerForgotPasswordCommand(
                CustomerPhoneInput.Normalize(body.CountryCode, body.Phone) ?? string.Empty), cancellationToken);
        return result.IsSuccess
            ? Ok(new OtpActionResponse(true, true, "reset_password_otp"))
            : HandleResult(result);
    }

    [HttpPost("resend-password-otp")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendPasswordOtp(
        [FromBody] CustomerForgotPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new CustomerForgotPasswordCommand(
                CustomerPhoneInput.Normalize(body.CountryCode, body.Phone) ?? string.Empty), cancellationToken);
        return result.IsSuccess
            ? Ok(new OtpActionResponse(true, true, "reset_password_otp"))
            : HandleResult(result);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(
        [FromBody] CustomerResetPasswordRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new CustomerResetPasswordCommand(
                CustomerPhoneInput.Normalize(body.CountryCode, body.Phone) ?? string.Empty,
                body.OtpCode,
                body.NewPassword), cancellationToken);
        return result.IsSuccess
            ? Ok(new ResetPasswordResponse(true, true, "sign_in"))
            : HandleResult(result);
    }
}

public sealed record CustomerRegisterRequest(
    string FullName,
    string Phone,
    string Password,
    string CountryCode);

public sealed record CustomerLoginRequest(string Phone, string Password, string? CountryCode);
public sealed record CustomerVerifyPhoneRequest(string Phone, string? Code, string? CountryCode, string? OtpCode = null);
public sealed record CustomerResendPhoneOtpRequest(string Phone, string? CountryCode);
public sealed record CustomerForgotPasswordRequest(string Phone, string? CountryCode);
public sealed record CustomerResetPasswordRequest(string Phone, string OtpCode, string NewPassword, string? CountryCode);
public sealed record FamilyMemberFirstLoginOtpRequest(string LoginChallengeToken, string OtpCode);
public sealed record FamilyMemberFirstLoginSetPasswordRequest(string LoginChallengeToken, string NewPassword);
public sealed record OtpActionResponse(bool Success, bool OtpSent, string NextStep);
public sealed record ResetPasswordResponse(bool Success, bool PasswordReset, string NextStep);

internal static class CustomerPhoneInput
{
    public static string? Normalize(string? countryCode, string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return phone;
        var value = phone.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
        if (value.StartsWith("+", StringComparison.Ordinal)) return value;
        var code = countryCode?.Trim();
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;

        var dialCode = code.StartsWith('+') || code.All(char.IsDigit)
            ? $"+{code.TrimStart('+')}"
            : ArabicDialCodes.GetValueOrDefault(code.ToUpperInvariant());

        return string.IsNullOrWhiteSpace(dialCode) ? string.Empty : $"{dialCode}{value.TrimStart('0')}";
    }

    private static readonly IReadOnlyDictionary<string, string> ArabicDialCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KW"] = "+965",
            ["SA"] = "+966",
            ["AE"] = "+971",
            ["QA"] = "+974",
            ["BH"] = "+973",
            ["OM"] = "+968",
            ["EG"] = "+20",
            ["JO"] = "+962",
            ["LB"] = "+961",
            ["SY"] = "+963",
            ["IQ"] = "+964",
            ["PS"] = "+970",
            ["YE"] = "+967",
            ["MA"] = "+212",
            ["DZ"] = "+213",
            ["TN"] = "+216",
            ["LY"] = "+218",
            ["SD"] = "+249",
        };
}

