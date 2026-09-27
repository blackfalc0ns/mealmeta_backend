using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Services;

public sealed class MyFatoorahPaymentService : IMyFatoorahPaymentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MyFatoorahPaymentService> _logger;
    private readonly IAppDbContext _db;
    private readonly IEmailSecretProtector _protector;
    private readonly IConfiguration _configuration;

    public MyFatoorahPaymentService(
        HttpClient httpClient,
        ILogger<MyFatoorahPaymentService> logger,
        IAppDbContext db,
        IEmailSecretProtector protector,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _db = db;
        _protector = protector;
        _configuration = configuration;
    }

    public async Task<bool> TestConnectionAsync(string apiToken, bool isSandbox, CancellationToken cancellationToken = default)
    {
        try
        {
            var baseUrl = isSandbox ? "https://apitest.myfatoorah.com" : "https://api.myfatoorah.com";
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/InitiatePayment");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            request.Content = JsonContent.Create(new { InvoiceAmount = 10.0, CurrencyIso = "KWD" });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MyFatoorah TestConnection failed");
            return false;
        }
    }

    public async Task<string> ExecutePaymentAsync(
        decimal invoiceValue,
        string customerName,
        string customerEmail,
        string customerMobile,
        string callbackUrl,
        string errorUrl,
        string? customerReference = null,
        int paymentMethodId = 1,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetRuntimeSettingsAsync(cancellationToken);
        if (settings is null)
        {
            throw new InvalidOperationException("MyFatoorah is not configured. Save and test the gateway settings first.");
        }

        var publicBaseUrl = MyFatoorahSettings.ResolvePublicBaseUrl(_configuration);
        var payload = new
        {
            PaymentMethodId = paymentMethodId,
            InvoiceValue = invoiceValue,
            CustomerName = customerName,
            CustomerEmail = NormalizeCustomerEmail(customerEmail),
            CustomerMobile = NormalizeCustomerMobile(customerMobile),
            CallBackUrl = ToMyFatoorahBrowserUrl(callbackUrl, publicBaseUrl),
            ErrorUrl = ToMyFatoorahBrowserUrl(errorUrl, publicBaseUrl),
            CustomerReference = customerReference,
            UserDefinedField = customerReference,
            WebhookUrl = MyFatoorahSettings.WebhookUrl(_configuration),
            Language = "ar"
        };

        using var request = AuthorizedRequest(HttpMethod.Post, $"{settings.BaseUrl}/v2/ExecutePayment", settings.ApiToken);
        request.Content = JsonContent.Create(payload);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var jsonDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var json = jsonDoc.RootElement;
        if (!response.IsSuccessStatusCode || json.TryGetProperty("IsSuccess", out var success) && success.ValueKind == JsonValueKind.False)
        {
            var message = json.TryGetProperty("Message", out var msg) ? msg.GetString() : response.ReasonPhrase;
            _logger.LogWarning("MyFatoorah ExecutePayment failed: {Status} {Body}", (int)response.StatusCode, raw);
            throw new InvalidOperationException($"MyFatoorah ExecutePayment failed: {message}");
        }

        var paymentUrl = json.GetProperty("Data").GetProperty("PaymentURL").GetString();
        return (paymentUrl ?? string.Empty).Trim();
    }

    public async Task<IReadOnlyList<MyFatoorahPaymentMethod>> GetPaymentMethodsAsync(decimal invoiceValue, CancellationToken cancellationToken = default)
    {
        var settings = await GetRuntimeSettingsAsync(cancellationToken);
        if (settings is null)
        {
            throw new InvalidOperationException("MyFatoorah is not configured. Save and test the gateway settings first.");
        }

        using var request = AuthorizedRequest(HttpMethod.Post, $"{settings.BaseUrl}/v2/InitiatePayment", settings.ApiToken);
        request.Content = JsonContent.Create(new { InvoiceAmount = invoiceValue, CurrencyIso = "KWD" });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        if (!json.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("PaymentMethods", out var methods) || methods.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = methods.EnumerateArray().Select(method => new MyFatoorahPaymentMethod(
            method.GetProperty("PaymentMethodId").GetInt32(),
            method.TryGetProperty("PaymentMethodCode", out var code) ? code.GetString() ?? string.Empty : string.Empty,
            method.TryGetProperty("PaymentMethodAr", out var ar) ? ar.GetString() ?? string.Empty : string.Empty,
            method.TryGetProperty("PaymentMethodEn", out var en) ? en.GetString() ?? string.Empty : string.Empty,
            method.TryGetProperty("ServiceCharge", out var charge) ? charge.GetDecimal() : 0m,
            method.TryGetProperty("TotalAmount", out var total) ? total.GetDecimal() : invoiceValue,
            method.TryGetProperty("CurrencyIso", out var currency) ? currency.GetString() ?? "KWD" : "KWD",
            method.TryGetProperty("IsDirectPayment", out var direct) && direct.GetBoolean(),
            method.TryGetProperty("ImageUrl", out var img) ? img.GetString() : null)).ToList();

        return list;
    }

    internal static string NormalizeCustomerMobile(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length <= 11 ? digits : digits[^11..];
    }

    internal static string NormalizeCustomerEmail(string? value)
        => !string.IsNullOrWhiteSpace(value) && System.Net.Mail.MailAddress.TryCreate(value.Trim(), out _)
            ? value.Trim()
            : "payments@mealmate.app";

    internal static string ToMyFatoorahBrowserUrl(string url, string publicBaseUrl)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return url;
        var root = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? MyFatoorahSettings.DefaultPublicBaseUrl
            : publicBaseUrl.Trim().TrimEnd('/');
        return $"{root}/api/v1/payments/myfatoorah/return?to={Uri.EscapeDataString(url)}";
    }

    public async Task<MyFatoorahPaymentStatusResponse?> GetPaymentStatusAsync(string key, string keyType = "PaymentId", CancellationToken cancellationToken = default)
    {
        var settings = await GetRuntimeSettingsAsync(cancellationToken);
        if (settings is null)
        {
            _logger.LogWarning("Cannot get MyFatoorah payment status because runtime settings are not configured.");
            return null;
        }

        try
        {
            var payload = new { Key = key, KeyType = keyType };
            using var request = AuthorizedRequest(HttpMethod.Post, $"{settings.BaseUrl}/v2/GetPaymentStatus", settings.ApiToken);
            request.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("MyFatoorah GetPaymentStatus failed with status {StatusCode}", (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var data = json.GetProperty("Data");

            var invoiceId = data.GetProperty("InvoiceId").GetInt64().ToString();
            var status = data.GetProperty("InvoiceStatus").GetString() ?? "Paid";
            var value = data.GetProperty("InvoiceValue").GetDecimal();
            var name = data.GetProperty("CustomerName").GetString() ?? "Customer";
            var email = data.GetProperty("CustomerEmail").GetString() ?? "";
            var mobile = data.GetProperty("CustomerMobile").GetString() ?? "";
            var customerReference = data.TryGetProperty("CustomerReference", out var referenceElement)
                ? referenceElement.GetString()
                : null;
            var currencyCode = data.TryGetProperty("InvoiceDisplayValue", out var displayValueElement)
                ? ExtractCurrencyCode(displayValueElement.GetString())
                : data.TryGetProperty("InvoiceCurrency", out var currencyElement)
                    ? currencyElement.GetString() ?? "KWD"
                    : "KWD";

            var transId = "MF-" + invoiceId;
            var paymentMethod = "KNET";
            string? paymentId = keyType.Equals("PaymentId", StringComparison.OrdinalIgnoreCase) ? key : null;

            if (data.TryGetProperty("InvoiceTransactions", out var transArray) && transArray.GetArrayLength() > 0)
            {
                var firstTrans = transArray[0];
                if (firstTrans.TryGetProperty("TransactionId", out var tId)) transId = tId.GetString() ?? transId;
                if (firstTrans.TryGetProperty("PaymentGateway", out var pG)) paymentMethod = pG.GetString() ?? paymentMethod;
                if (firstTrans.TryGetProperty("PaymentId", out var pId) && pId.ValueKind == JsonValueKind.String)
                    paymentId ??= pId.GetString();
            }

            return new MyFatoorahPaymentStatusResponse(
                invoiceId,
                status,
                value,
                name,
                email,
                mobile,
                transId,
                paymentMethod,
                DateTime.UtcNow,
                customerReference,
                currencyCode,
                keyType.Equals("PaymentId", StringComparison.OrdinalIgnoreCase) ? key : paymentId
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get MyFatoorah payment status for key {Key}", key);
            return null;
        }
    }

    private static string ExtractCurrencyCode(string? displayValue)
    {
        if (string.IsNullOrWhiteSpace(displayValue)) return "KWD";
        var token = displayValue.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(x => x.Length == 3 && x.All(char.IsLetter));
        return token?.ToUpperInvariant() ?? "KWD";
    }

    public bool VerifyWebhookSignature(string payloadJson, string signatureHeader, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secretKey))
            return false;

        try
        {
            var keyBytes = Encoding.UTF8.GetBytes(secretKey);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            using var hmac = new HMACSHA256(keyBytes);
            var hashBytes = hmac.ComputeHash(payloadBytes);
            var received = Convert.FromBase64String(signatureHeader.Trim());
            return received.Length == hashBytes.Length
                && CryptographicOperations.FixedTimeEquals(received, hashBytes);
        }
        catch
        {
            return false;
        }
    }

    private async Task<RuntimeSettings?> GetRuntimeSettingsAsync(CancellationToken cancellationToken)
    {
        var config = await _db.MyFatoorahGatewayConfigs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.GatewayName == "MyFatoorah", cancellationToken);
        if (config is null || string.IsNullOrWhiteSpace(config.EncryptedApiToken)) return null;
        try
        {
            var token = _protector.Decrypt(config.EncryptedApiToken);
            var baseUrl = config.IsSandbox ? "https://apitest.myfatoorah.com" : "https://api.myfatoorah.com";
            return new(baseUrl, token);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Using a legacy MyFatoorah API token; resave gateway settings to encrypt it.");
            var baseUrl = config.IsSandbox ? "https://apitest.myfatoorah.com" : "https://api.myfatoorah.com";
            return new(baseUrl, config.EncryptedApiToken);
        }
    }

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed record RuntimeSettings(string BaseUrl, string ApiToken);
}
