using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminDashboard.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Email;

/// <summary>
/// Sends transactional emails via the Resend REST API (https://api.resend.com).
/// Bearer token is resolved per-request from the encrypted settings store.
/// Never logs API keys, full recipient addresses, or message bodies.
/// </summary>
public sealed class ResendTransactionalEmailSender : ITransactionalEmailSender
{
    private const string ResendBaseUrl = "https://api.resend.com";
    private const string UserAgent = "MealMate/1.0";

    private readonly HttpClient _http;
    private readonly IAppDbContext _db;
    private readonly IEmailSecretProtector _protector;
    private readonly ILogger<ResendTransactionalEmailSender> _logger;

    public ResendTransactionalEmailSender(
        HttpClient http,
        IAppDbContext db,
        IEmailSecretProtector protector,
        ILogger<ResendTransactionalEmailSender> logger)
    {
        _http = http;
        _db = db;
        _protector = protector;
        _logger = logger;
    }

    public async Task<Result<string>> SendAsync(
        TransactionalEmailRequest request, CancellationToken cancellationToken = default)
    {
        // Resolve API key from settings
        var setting = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == "communication.email.resend" && s.IsActive, cancellationToken);

        if (setting is null)
            return Result.Failure<string>(Error.Failure("email.provider_not_configured", "Email provider is not configured."));

        var settings = JsonSerializer.Deserialize<ResendEmailSettings>(setting.ValueJson,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (string.IsNullOrEmpty(settings?.EncryptedApiKey))
            return Result.Failure<string>(Error.Failure("email.no_api_key", "Email API key is not configured."));

        string apiKey;
        try
        {
            apiKey = _protector.Decrypt(settings.EncryptedApiKey);
        }
        catch
        {
            return Result.Failure<string>(Error.Failure("email.api_key_decrypt_failed", "Failed to decrypt email API key."));
        }

        var template = !string.IsNullOrWhiteSpace(request.TemplateId) && settings.Templates is not null
            ? EmailTemplateCatalog.ResolveTemplate(settings.Templates, request.TemplateId)
            : null;

        var fromName = string.IsNullOrWhiteSpace(request.FromName)
            ? template?.SenderName ?? settings.SenderName
            : request.FromName.Trim();
        var fromEmail = string.IsNullOrWhiteSpace(request.FromEmail)
            ? template?.SenderEmail ?? settings.SenderEmail
            : request.FromEmail.Trim();

        // Build Resend payload
        var payload = new ResendSendPayload
        {
            From = $"{fromName} <{fromEmail}>",
            To = new[] { request.To },
            Subject = request.Subject,
            Html = request.Html,
            Text = request.Text,
            ReplyTo = request.ReplyTo ?? template?.ReplyTo ?? settings.ReplyTo,
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{ResendBaseUrl}/emails");
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

        if (!string.IsNullOrEmpty(request.IdempotencyKey))
            httpRequest.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);

        try
        {
            using var response = await _http.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var safeError = ExtractSafeError(body);
                _logger.LogWarning("Resend API returned {StatusCode}: {Error}",
                    (int)response.StatusCode, safeError);

                if ((int)response.StatusCode == 403
                    && safeError?.Contains(
                        "domain is not verified",
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    return Result.Failure<string>(Error.Failure(
                        "email.domain_not_verified",
                        "The sender domain is not verified."));
                }

                return Result.Failure<string>(Error.Failure("email.send_failed",
                    $"Email provider returned status {(int)response.StatusCode}."));
            }

            var messageId = ExtractMessageId(body);
            _logger.LogInformation("Resend email sent successfully, id={MessageId}", messageId);
            return Result.Success(messageId ?? "unknown");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Resend API request failed");
            return Result.Failure<string>(Error.Failure("email.network_error", "Failed to reach email provider."));
        }
    }

    private static string? ExtractMessageId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch { return null; }
    }

    private static string? ExtractSafeError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var msg))
                return msg.GetString();
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.GetString();
            return null;
        }
        catch { return null; }
    }

    private sealed class ResendSendPayload
    {
        public string From { get; set; } = string.Empty;
        public string[] To { get; set; } = Array.Empty<string>();
        public string Subject { get; set; } = string.Empty;
        public string? Html { get; set; }
        public string? Text { get; set; }
        public string? ReplyTo { get; set; }
    }
}
