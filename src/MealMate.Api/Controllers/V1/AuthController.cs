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
