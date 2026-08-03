using System.Text.Json;
using System.Text.Json.Serialization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.AdminDashboard.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Email;

/// <summary>
/// Delivers email OTP codes via the Resend transactional email sender.
/// Renders templates from <see cref="EmailTemplateCatalog"/> using persisted
/// <see cref="ResendEmailSettings"/> when available, otherwise catalog defaults.
/// Never logs the OTP code value or full email addresses.
/// </summary>
public sealed class ResendOtpDeliveryService : IOtpDeliveryService
{
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IAppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ResendOtpDeliveryService> _logger;

    public ResendOtpDeliveryService(
        ITransactionalEmailSender emailSender,
        IAppDbContext db,
        IConfiguration configuration,
        ILogger<ResendOtpDeliveryService> logger)
    {
        _emailSender = emailSender;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task DeliverAsync(
        string channel,
        string destination,
        string code,
        string purpose,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? templateVariables = null,
        string? language = null)
    {
        if (!string.Equals(channel, "Email", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("ResendOtpDeliveryService skipping non-email channel={Channel}", channel);
            return;
        }

        // Load persisted email settings for template overrides
        var setting = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.Key == EmailSettingKeys.Root && s.IsActive,
                cancellationToken);

        var emailSettings = setting is not null
            ? DeserializeSettingsOrDefault(setting.ValueJson)
            : null;

        var templates = emailSettings?.Templates;
        var templateKey = PurposeToTemplateId(purpose);
        var lang = EmailLanguage.Resolve(language);

        // Build dashboard URL for invitation links
        var adminDashboardUrl = _configuration["ClientUrls:AdminDashboard"]
            ?? "http://localhost:4200";

        // Build template variables
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["otp_code"] = code,
            ["expires_minutes"] = "10",
            ["dashboard_url"] = adminDashboardUrl,
            ["support_email"] = emailSettings?.ReplyTo ?? "support@mealmate.app",
            ["current_year"] = DateTime.UtcNow.Year.ToString(),
        };

        // For admin invite, build the accept-invite link with encoded email
        if (string.Equals(purpose, "AdminInvite", StringComparison.OrdinalIgnoreCase))
        {
            var encodedEmail = Uri.EscapeDataString(destination);
            variables["dashboard_url"] =
                $"{adminDashboardUrl.TrimEnd('/')}/login/accept-invite?email={encodedEmail}";
        }

        // Merge caller-provided variables (recipient_name, etc.) without overriding otp_code
        if (templateVariables is not null)
        {
            foreach (var kv in templateVariables)
            {
                if (!string.Equals(kv.Key, "otp_code", StringComparison.OrdinalIgnoreCase))
                {
                    variables[kv.Key] = kv.Value;
                }
            }
        }

        // Ensure recipient_name has a fallback
        variables.TryAdd("recipient_name", "");

        // Render template
        var rendered = EmailTemplateCatalog.Render(templateKey, lang, variables, templates);
        if (rendered.IsFailure)
        {
            _logger.LogWarning(
                "OTP email template rendering failed for purpose={Purpose} template={Template}: {Error}",
                purpose, templateKey, rendered.Error.Code);
            throw new InvalidOperationException(
                $"OTP email template rendering failed for template '{templateKey}'.");
        }

        var result = await _emailSender.SendAsync(new TransactionalEmailRequest(
            To: destination,
            Subject: rendered.Value.Subject,
            Html: rendered.Value.Html,
            Text: rendered.Value.Text,
            IdempotencyKey: $"otp-{purpose}-{Guid.NewGuid():N}",
            TemplateId: templateKey),
            cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogWarning("OTP email delivery failed for purpose={Purpose} destination={MaskedDest}: {Error}",
                purpose, MaskEmail(destination), result.Error.Code);
            throw new InvalidOperationException(
                $"OTP email delivery failed for purpose '{purpose}'.");
        }
        else
        {
            _logger.LogInformation("OTP email delivered for purpose={Purpose} destination={MaskedDest}",
                purpose, MaskEmail(destination));
        }
    }

    private static string PurposeToTemplateId(string purpose) => purpose switch
    {
        "PasswordReset" => EmailTemplateCatalog.PasswordReset,
        "AdminInvite" => EmailTemplateCatalog.AdminInvite,
        _ => EmailTemplateCatalog.VerificationOtp,
    };

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 2) return "***@***";
        return string.Concat(email.AsSpan(0, 2), "***", email.AsSpan(at));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static ResendEmailSettings DeserializeSettingsOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ResendEmailSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<ResendEmailSettings>(json, JsonOpts)
                ?? new ResendEmailSettings();
            settings.Templates ??= EmailTemplateCatalog.CreateDefaults();
            return settings;
        }
        catch
        {
            return new ResendEmailSettings();
        }
    }
}
