using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.IdentityAccess;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.AdminDashboard.Email;

// ──────────────────────────────────────────────────────────────────────────────
// Constants
// ──────────────────────────────────────────────────────────────────────────────

public static class EmailSettingKeys
{
    public const string Root = "communication.email.resend";
}

// ──────────────────────────────────────────────────────────────────────────────
// Settings model (persisted as JSON in SystemSetting.ValueJson)
// ──────────────────────────────────────────────────────────────────────────────

public sealed class ResendEmailSettings
{
    public bool Enabled { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string? ReplyTo { get; set; }
    public bool WebhookEnabled { get; set; }
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMinutes { get; set; } = 10;
    public string? TestRecipient { get; set; }

    /// <summary>Encrypted API key (AES-256-GCM base64 blob). Write-only at the API layer.</summary>
    public string? EncryptedApiKey { get; set; }

    /// <summary>Encrypted webhook signing secret. Write-only at the API layer.</summary>
    public string? EncryptedWebhookSecret { get; set; }

    public ResendEmailTemplates Templates { get; set; } = new();
}

public sealed class ResendEmailTemplates
{
    public BilingualTemplate VerificationOtp { get; set; } = new();
    public BilingualTemplate PasswordReset { get; set; } = new();
    public BilingualTemplate RestaurantSubmitted { get; set; } = new();
    public BilingualTemplate RestaurantApproved { get; set; } = new();
    public BilingualTemplate RestaurantChangesRequested { get; set; } = new();
    public BilingualTemplate RestaurantRejected { get; set; } = new();
    public BilingualTemplate AdminInvite { get; set; } = new();
}

public sealed class BilingualTemplate
{
    public string SubjectAr { get; set; } = string.Empty;
    public string SubjectEn { get; set; } = string.Empty;
    public string HtmlBodyAr { get; set; } = string.Empty;
    public string HtmlBodyEn { get; set; } = string.Empty;
}

// ──────────────────────────────────────────────────────────────────────────────
// DTOs
// ──────────────────────────────────────────────────────────────────────────────

public sealed record EmailSettingsDto(
    bool Enabled,
    string SenderName,
    string SenderEmail,
    string? ReplyTo,
    bool WebhookEnabled,
    int MaxRetries,
    int RetryDelayMinutes,
    string? TestRecipient,
    bool HasApiKey,
    bool HasWebhookSecret,
    string? ApiKeyLastFour,
    string? WebhookSecretLastFour,
    ResendEmailTemplates Templates);

public sealed record EmailLogEntryDto(
    Guid Id,
    string Action,
    string? MaskedRecipient,
    string? TemplateOrEvent,
    string? Status,
    string? ResendId,
    string? SafeError,
    DateTime OccurredAtUtc);

// ──────────────────────────────────────────────────────────────────────────────
// Commands / Queries
// ──────────────────────────────────────────────────────────────────────────────

public sealed record GetEmailSettingsQuery : IRequest<Result<EmailSettingsDto>>;

public sealed record UpdateEmailSettingsCommand(
    bool Enabled,
    string SenderName,
    string SenderEmail,
    string? ReplyTo,
    bool WebhookEnabled,
    int MaxRetries,
    int RetryDelayMinutes,
    string? TestRecipient,
    string? NewApiKey,
    string? NewWebhookSecret,
    ResendEmailTemplates? Templates) : IRequest<Result>;

public sealed record SendTestEmailCommand(string? OverrideRecipient) : IRequest<Result<string>>;

public sealed record GetEmailLogsQuery(int Page = 1, int PageSize = 20) : IRequest<Result<PagedResult<EmailLogEntryDto>>>;

// ──────────────────────────────────────────────────────────────────────────────
// Handlers
// ──────────────────────────────────────────────────────────────────────────────

public sealed class GetEmailSettingsHandler
    : IRequestHandler<GetEmailSettingsQuery, Result<EmailSettingsDto>>
{
    private readonly IAppDbContext _db;
    private readonly IEmailSecretProtector _protector;

    public GetEmailSettingsHandler(IAppDbContext db, IEmailSecretProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<Result<EmailSettingsDto>> Handle(
        GetEmailSettingsQuery request, CancellationToken cancellationToken)
    {
        var setting = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == EmailSettingKeys.Root && s.IsActive, cancellationToken);

        if (setting is null)
        {
            return Result.Success(new EmailSettingsDto(
                false, string.Empty, string.Empty, null, false, 3, 10, null,
                false, false, null, null, new ResendEmailTemplates()));
        }

        var s = DeserializeOrNew(setting.ValueJson);

        string? apiKeyLastFour = null;
        string? webhookLastFour = null;

        if (!string.IsNullOrEmpty(s.EncryptedApiKey))
        {
            try
            {
                var plain = _protector.Decrypt(s.EncryptedApiKey);
                apiKeyLastFour = _protector.MaskLastFour(plain);
            }
            catch { /* corrupted cipher text – treat as absent */ }
        }

        if (!string.IsNullOrEmpty(s.EncryptedWebhookSecret))
        {
            try
            {
                var plain = _protector.Decrypt(s.EncryptedWebhookSecret);
                webhookLastFour = _protector.MaskLastFour(plain);
            }
            catch { /* corrupted cipher text – treat as absent */ }
        }

        return Result.Success(new EmailSettingsDto(
            s.Enabled,
            s.SenderName,
            s.SenderEmail,
            s.ReplyTo,
            s.WebhookEnabled,
            s.MaxRetries,
            s.RetryDelayMinutes,
            s.TestRecipient,
            !string.IsNullOrEmpty(s.EncryptedApiKey),
            !string.IsNullOrEmpty(s.EncryptedWebhookSecret),
            apiKeyLastFour,
            webhookLastFour,
            s.Templates));
    }

    internal static ResendEmailSettings DeserializeOrNew(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ResendEmailSettings();
        try { return JsonSerializer.Deserialize<ResendEmailSettings>(json, JsonOpts) ?? new ResendEmailSettings(); }
        catch { return new ResendEmailSettings(); }
    }

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}

public sealed class UpdateEmailSettingsHandler
    : IRequestHandler<UpdateEmailSettingsCommand, Result>
{
    private readonly IAppDbContext _db;
    private readonly IEmailSecretProtector _protector;
    private readonly IAuditLogger _audit;
    private readonly ILogger<UpdateEmailSettingsHandler> _logger;

    public UpdateEmailSettingsHandler(
        IAppDbContext db,
        IEmailSecretProtector protector,
        IAuditLogger audit,
        ILogger<UpdateEmailSettingsHandler> logger)
    {
        _db = db;
        _protector = protector;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result> Handle(
        UpdateEmailSettingsCommand cmd, CancellationToken cancellationToken)
    {
        // ── Validate ──────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(cmd.SenderName))
            return Result.Failure(Error.Validation("email_settings.sender_name_required", "Sender name is required."));

        if (string.IsNullOrWhiteSpace(cmd.SenderEmail) || !cmd.SenderEmail.Contains('@'))
            return Result.Failure(Error.Validation("email_settings.sender_email_invalid", "A valid sender email is required."));

        if (cmd.MaxRetries is < 0 or > 10)
            return Result.Failure(Error.Validation("email_settings.max_retries_range", "Max retries must be between 0 and 10."));

        if (cmd.RetryDelayMinutes is < 1 or > 1440)
            return Result.Failure(Error.Validation("email_settings.retry_delay_range", "Retry delay must be between 1 and 1440 minutes."));

        if (!string.IsNullOrEmpty(cmd.ReplyTo) && !cmd.ReplyTo.Contains('@'))
            return Result.Failure(Error.Validation("email_settings.reply_to_invalid", "Reply-to must be a valid email address."));

        if (!string.IsNullOrEmpty(cmd.TestRecipient) && !cmd.TestRecipient.Contains('@'))
            return Result.Failure(Error.Validation("email_settings.test_recipient_invalid", "Test recipient must be a valid email address."));

        // ── Load or create ────────────────────────────────────────────────
        var setting = await _db.SystemSettings
            .FirstOrDefaultAsync(s => s.Key == EmailSettingKeys.Root, cancellationToken);

        var current = setting is not null
            ? GetEmailSettingsHandler.DeserializeOrNew(setting.ValueJson)
            : new ResendEmailSettings();

        // ── Apply changes ─────────────────────────────────────────────────
        current.Enabled = cmd.Enabled;
        current.SenderName = cmd.SenderName.Trim();
        current.SenderEmail = cmd.SenderEmail.Trim();
        current.ReplyTo = string.IsNullOrWhiteSpace(cmd.ReplyTo) ? null : cmd.ReplyTo.Trim();
        current.WebhookEnabled = cmd.WebhookEnabled;
        current.MaxRetries = cmd.MaxRetries;
        current.RetryDelayMinutes = cmd.RetryDelayMinutes;
        current.TestRecipient = string.IsNullOrWhiteSpace(cmd.TestRecipient) ? null : cmd.TestRecipient.Trim();

        // Write-only secrets: encrypt if provided, keep existing if null/empty
        if (!string.IsNullOrEmpty(cmd.NewApiKey))
            current.EncryptedApiKey = _protector.Encrypt(cmd.NewApiKey);

        if (!string.IsNullOrEmpty(cmd.NewWebhookSecret))
            current.EncryptedWebhookSecret = _protector.Encrypt(cmd.NewWebhookSecret);

        if (cmd.Templates is not null)
            current.Templates = cmd.Templates;

        // ── Persist ───────────────────────────────────────────────────────
        var json = JsonSerializer.Serialize(current, GetEmailSettingsHandler.JsonOpts);

        if (setting is null)
        {
            setting = new SystemSetting
            {
                Key = EmailSettingKeys.Root,
                ValueJson = json,
                Description = "Resend email provider configuration",
                IsActive = true,
            };
            _db.SystemSettings.Add(setting);
        }
        else
        {
            setting.ValueJson = json;
            setting.ModifiedAtUtc = DateTime.UtcNow;
        }

        await _audit.LogAsync(new AuditEntry(
            Action: "email_settings.updated",
            EntityType: "SystemSetting",
            Reason: "Admin updated Resend email settings",
            SourceChannel: "admin-api"), cancellationToken);

        _logger.LogInformation("Resend email settings updated by admin");

        return Result.Success();
    }
}

public sealed class SendTestEmailHandler
    : IRequestHandler<SendTestEmailCommand, Result<string>>
{
    private readonly IAppDbContext _db;
    private readonly IEmailSecretProtector _protector;
    private readonly ITransactionalEmailSender _sender;
    private readonly IAuditLogger _audit;
    private readonly ILogger<SendTestEmailHandler> _logger;

    public SendTestEmailHandler(
        IAppDbContext db,
        IEmailSecretProtector protector,
        ITransactionalEmailSender sender,
        IAuditLogger audit,
        ILogger<SendTestEmailHandler> logger)
    {
        _db = db;
        _protector = protector;
        _sender = sender;
        _audit = audit;
        _logger = logger;
    }

    public async Task<Result<string>> Handle(
        SendTestEmailCommand cmd, CancellationToken cancellationToken)
    {
        var setting = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == EmailSettingKeys.Root && s.IsActive, cancellationToken);

        if (setting is null)
            return Result.Failure<string>(Error.Validation("email_settings.not_configured", "Email settings are not configured."));

        var s = GetEmailSettingsHandler.DeserializeOrNew(setting.ValueJson);

        if (!s.Enabled)
            return Result.Failure<string>(Error.Validation("email_settings.disabled", "Email sending is disabled."));

        if (string.IsNullOrEmpty(s.EncryptedApiKey))
            return Result.Failure<string>(Error.Validation("email_settings.no_api_key", "No API key configured."));

        var recipient = !string.IsNullOrEmpty(cmd.OverrideRecipient)
            ? cmd.OverrideRecipient
            : s.TestRecipient;

        if (string.IsNullOrEmpty(recipient) || !recipient.Contains('@'))
            return Result.Failure<string>(Error.Validation("email_settings.no_test_recipient", "No valid test recipient configured."));

        var result = await _sender.SendAsync(new TransactionalEmailRequest(
            To: recipient,
            Subject: "MealMate Test Email",
            Html: "<h1>MealMate Test</h1><p>This is a test email from MealMate admin dashboard.</p>",
            Text: "MealMate Test - This is a test email from MealMate admin dashboard.",
            ReplyTo: s.ReplyTo,
            IdempotencyKey: $"test-{Guid.NewGuid():N}"), cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            Action: "email.test_sent",
            EntityType: "SystemSetting",
            Reason: $"Test email sent to masked recipient",
            SourceChannel: "admin-api"), cancellationToken);

        return result;
    }
}

public sealed class GetEmailLogsHandler
    : IRequestHandler<GetEmailLogsQuery, Result<PagedResult<EmailLogEntryDto>>>
{
    private readonly IAppDbContext _db;

    public GetEmailLogsHandler(IAppDbContext db) => _db = db;

    public async Task<Result<PagedResult<EmailLogEntryDto>>> Handle(
        GetEmailLogsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.Action.StartsWith("email.") || a.Action.StartsWith("resend."))
            .OrderByDescending(a => a.OccurredAtUtc);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new EmailLogEntryDto(
                a.Id,
                a.Action,
                MaskRecipient(a.Reason),
                a.EntityType,
                ExtractStatus(a.AfterJson),
                ExtractResendId(a.AfterJson),
                ExtractSafeError(a.AfterJson),
                a.OccurredAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<EmailLogEntryDto>(items, total, request.Page, request.PageSize));
    }

    private static string? MaskRecipient(string? reason)
    {
        if (string.IsNullOrEmpty(reason)) return null;
        // Extract email-like patterns and mask
        var at = reason.IndexOf('@');
        if (at < 0) return null;
        var start = Math.Max(0, at - 2);
        var end = Math.Min(reason.Length, at + 5);
        return string.Concat(reason.AsSpan(0, start), "***", reason.AsSpan(end));
    }

    private static string? ExtractStatus(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
        }
        catch { return null; }
    }

    private static string? ExtractResendId(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("resendId", out var s) ? s.GetString() : null;
        }
        catch { return null; }
    }

    private static string? ExtractSafeError(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var s) ? s.GetString() : null;
        }
        catch { return null; }
    }
}
