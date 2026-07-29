using MealMate.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Email;

/// <summary>
/// Delivers email OTP codes via the Resend transactional email sender.
/// Never logs the OTP code value. Falls back gracefully if email is not configured.
/// </summary>
public sealed class ResendOtpDeliveryService : IOtpDeliveryService
{
    private readonly ITransactionalEmailSender _emailSender;
    private readonly ILogger<ResendOtpDeliveryService> _logger;

    public ResendOtpDeliveryService(
        ITransactionalEmailSender emailSender,
        ILogger<ResendOtpDeliveryService> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task DeliverAsync(
        string channel,
        string destination,
        string code,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(channel, "Email", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("ResendOtpDeliveryService skipping non-email channel={Channel}", channel);
            return;
        }

        var subject = PurposeToSubject(purpose);
        var html = BuildOtpHtml(purpose, code);

        var result = await _emailSender.SendAsync(new TransactionalEmailRequest(
            To: destination,
            Subject: subject,
            Html: html,
            Text: $"Your MealMate verification code is: {code}",
            IdempotencyKey: $"otp-{purpose}-{destination}-{DateTime.UtcNow:yyyyMMddHH}"),
            cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogWarning("OTP email delivery failed for purpose={Purpose} destination={MaskedDest}: {Error}",
                purpose, MaskEmail(destination), result.Error.Code);
        }
        else
        {
            _logger.LogInformation("OTP email delivered for purpose={Purpose} destination={MaskedDest}",
                purpose, MaskEmail(destination));
        }
    }

    private static string PurposeToSubject(string purpose) => purpose switch
    {
        "VerifyEmail" => "MealMate - Email Verification Code",
        "VerifyPhone" => "MealMate - Phone Verification Code",
        "PasswordReset" => "MealMate - Password Reset Code",
        "AdminInvite" => "MealMate - Admin Invitation Code",
        _ => "MealMate - Verification Code",
    };

    private static string BuildOtpHtml(string purpose, string code) =>
        $"""
        <div style="font-family:system-ui,sans-serif;max-width:480px;margin:0 auto;padding:2rem;">
          <h1 style="color:#059669;font-size:1.5rem;">MealMate</h1>
          <p style="color:#334155;font-size:1rem;">Your verification code:</p>
          <div style="background:#f0fdf4;border:2px solid #059669;border-radius:12px;padding:1.5rem;text-align:center;margin:1.5rem 0;">
            <span style="font-size:2rem;font-weight:800;letter-spacing:0.3em;color:#059669;">{code}</span>
          </div>
          <p style="color:#64748b;font-size:0.875rem;">This code expires in 10 minutes. Do not share it with anyone.</p>
          <p style="color:#94a3b8;font-size:0.75rem;margin-top:2rem;">Purpose: {purpose}</p>
        </div>
        """;

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 2) return "***@***";
        return string.Concat(email.AsSpan(0, 2), "***", email.AsSpan(at));
    }
}
