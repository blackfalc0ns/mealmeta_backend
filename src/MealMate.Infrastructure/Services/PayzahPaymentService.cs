using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Settings;
using MealMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace MealMate.Infrastructure.Services;

public sealed class PayzahPaymentService(HttpClient httpClient, ILogger<PayzahPaymentService> logger,
    IAppDbContext db, IEmailSecretProtector protector, IConfiguration configuration) : IPaymentGatewayService
{
    public Task<bool> TestConnectionAsync(string apiToken, bool isSandbox, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Payzah provides no documented read-only credential test. Save the private key and validate with a merchant-authorized sandbox checkout.");

    public async Task<IReadOnlyList<PaymentGatewayMethod>> GetPaymentMethodsAsync(decimal invoiceValue, CancellationToken cancellationToken = default)
    {
        // Hosted checkout obtains available methods directly from the merchant's Payzah account.
        var settings = await SettingsAsync(cancellationToken);
        if (!string.Equals(settings.Config.DefaultCurrency, "KWD", StringComparison.Ordinal))
            return [];
        return [new(3, "PAYZAH", "الدفع عبر Payzah", "Payzah hosted checkout", null, null, settings.Config.DefaultCurrency, false)];
    }

    public async Task<PaymentCreationReceipt> ExecutePaymentAsync(decimal invoiceValue, string customerName,
        string customerEmail, string customerMobile, string callbackUrl, string errorUrl,
        string? customerReference = null, int paymentMethodId = 3, CancellationToken cancellationToken = default)
    {
        if (invoiceValue <= 0 || decimal.Round(invoiceValue, 3) != invoiceValue || string.IsNullOrWhiteSpace(customerReference))
            throw new ArgumentException("A positive KWD amount with at most three decimals and an application reference are required.");
        if (paymentMethodId != 3) throw new ArgumentException("Payzah supports hosted checkout method 3; refresh the available payment methods.");
        if (customerReference.Length > 255 || customerName.Length > 255 || customerEmail.Length > 255 || customerMobile.Length > 255)
            throw new ArgumentException("Payzah customer fields and reference must be at most 255 characters.");
        var settings = await SettingsAsync(cancellationToken);
        if (settings.Config.DefaultCurrency != "KWD") throw new InvalidOperationException("Payzah checkout currently supports the documented KWD currency contract only.");
        var attempt = new PaymentGatewayAttempt { TrackId = Guid.NewGuid().ToString("N"), CustomerReference = customerReference,
            ExpectedAmount = invoiceValue, ExpectedCurrency = settings.Config.DefaultCurrency, IsSandbox = settings.Config.IsSandbox };
        db.PaymentGatewayAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
        using var request = Request(settings.BaseUrl + "/index", settings.Key);
        request.Content = JsonContent.Create(new { trackid = attempt.TrackId, amount = invoiceValue.ToString("F3", CultureInfo.InvariantCulture),
            currency = "414", success_url = BrowserUrl(callbackUrl, attempt.TrackId), error_url = BrowserUrl(errorUrl, attempt.TrackId),
            language = "ARA", udf1 = customerReference, payment_type = "3", customer_name = customerName,
            customer_email = customerEmail, customer_phone = ToPayzahPhone(customerMobile) });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var data = RequireData(response, json.RootElement);
        var paymentId = Text(data, "PaymentID");
        var paymentUrl = Text(data, "transit_url");
        if (string.IsNullOrWhiteSpace(paymentUrl))
        {
            paymentUrl = Text(data, "PaymentUrl");
            if (!string.IsNullOrWhiteSpace(paymentUrl)) paymentUrl += (paymentUrl.Contains('?') ? "&" : "?") + "PaymentID=" + Uri.EscapeDataString(paymentId);
        }
        if (string.IsNullOrWhiteSpace(paymentId) || !Uri.TryCreate(paymentUrl, UriKind.Absolute, out var redirect)
            || redirect.Scheme != "https" || !string.IsNullOrEmpty(redirect.UserInfo) || !string.IsNullOrEmpty(redirect.Fragment) || !(redirect.Host == "devgateway.trytajer.app" || redirect.Host == "payzah.net" || redirect.Host.EndsWith(".payzah.net", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Payzah returned no valid payment ID and secure hosted redirect.");
        attempt.PaymentId = paymentId;
        await db.SaveChangesAsync(cancellationToken);
        return new(paymentId, paymentUrl, attempt.TrackId);
    }

    public async Task<PaymentGatewayStatusResponse?> GetPaymentStatusAsync(string key, string keyType = "PaymentId", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key) || keyType is not ("PaymentId" or "TrackId")) return null;
        if (keyType == "PaymentId" && !key.StartsWith("pz-", StringComparison.Ordinal)) return null;
        var settings = await SettingsAsync(cancellationToken);
        var attempt = keyType == "TrackId"
            ? await db.PaymentGatewayAttempts.SingleOrDefaultAsync(x => x.Provider == "Payzah" && x.TrackId == key, cancellationToken)
            : await db.PaymentGatewayAttempts.SingleOrDefaultAsync(x => x.Provider == "Payzah" && x.TrackId == key.Substring(3), cancellationToken);
        if (attempt is null || string.IsNullOrWhiteSpace(attempt.PaymentId)) return null;
        if (attempt.IsSandbox != settings.Config.IsSandbox) return null;
        using var request = Request(settings.BaseUrl + "/get-payment-details", settings.Key);
        request.Content = JsonContent.Create(new { trackid = attempt.TrackId, payment_id = attempt.PaymentId });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var data = RequireData(response, json.RootElement);
        if (Text(data, "trackId") != attempt.TrackId || Text(data, "UDF1") != attempt.CustomerReference) return null;
        if (data.TryGetProperty("amount", out var amount) && (!decimal.TryParse(amount.ToString(), CultureInfo.InvariantCulture, out var value) || value != attempt.ExpectedAmount)) return null;
        if (data.TryGetProperty("currency", out var currency) && currency.ToString() is not ("414" or "KWD")) return null;
        var reference = Text(data, "payzahRefrenceCode");
        var transaction = Text(data, "transactionNumber");
        var state = Text(data, "paymentStatus");
        if (state == "CAPTURED" && (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(transaction))) return null;
        if (attempt.ProviderReference is not null && attempt.ProviderReference != reference) return null;
        if (!string.IsNullOrWhiteSpace(reference)) { attempt.ProviderReference = reference; await db.SaveChangesAsync(cancellationToken); }
        var status = state == "CAPTURED" ? "Paid" : state is "VOIDED" or "NOT CAPTURED" or "CANCELED" or "DENIED BY RISK" ? "Failed" : "Pending";
        // InvoiceValue/CurrencyCode carry immutable expected creation values: status API does not document monetary fields.
        return new(reference, status, attempt.ExpectedAmount, "", "", "", transaction, "Payzah", attempt.CreatedAtUtc,
            attempt.CustomerReference, attempt.ExpectedCurrency, "pz-" + attempt.TrackId);
    }

    public async Task<PaymentGatewayRefundReceipt?> MakeRefundAsync(string paymentId, decimal amount, string currency, string externalIdentifier, string reason, CancellationToken cancellationToken = default)
    {
        if (!paymentId.StartsWith("pz-", StringComparison.Ordinal)) throw new InvalidOperationException("Refund requires an immutable Payzah attempt handle.");
        var settings = await SettingsAsync(cancellationToken);
        var attempt = await db.PaymentGatewayAttempts.SingleOrDefaultAsync(x => x.Provider == "Payzah" && x.TrackId == paymentId.Substring(3), cancellationToken);
        if (attempt is null || amount <= 0 || amount > attempt.ExpectedAmount || decimal.Round(amount, 3) != amount || currency != attempt.ExpectedCurrency)
            throw new InvalidOperationException("Refund is not bound to a Payzah payment or amount/currency is invalid.");
        var verified = await GetPaymentStatusAsync(paymentId, cancellationToken: cancellationToken);
        if (verified?.InvoiceStatus != "Paid" || string.IsNullOrWhiteSpace(attempt.ProviderReference)) throw new InvalidOperationException("Original Payzah capture could not be verified.");
        using var request = Request(settings.BaseUrl + "/refund", settings.Key);
        request.Content = JsonContent.Create(new { trackid = attempt.TrackId, refrence_code = attempt.ProviderReference,
            amount = amount.ToString("F3", CultureInfo.InvariantCulture), refund_type = amount == attempt.ExpectedAmount ? 1 : 2, message = reason });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!response.IsSuccessStatusCode || !json.RootElement.TryGetProperty("status", out var status) || (status.ValueKind != JsonValueKind.True && status.ToString() != "1"))
            throw new InvalidOperationException("Payzah did not confirm refund acceptance; merchant reconciliation is required before any retry.");
        // The vendor documents acceptance only: persist NeedsReview and never automatically resubmit.
        return null;
    }
    public Task<IReadOnlyList<PaymentGatewayRefundStatus>> GetRefundStatusAsync(string key, string keyType = "RefundId", CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Payzah refund reconciliation requires merchant review.");

    private async Task<(PayzahGatewayConfig Config, string Key, string BaseUrl)> SettingsAsync(CancellationToken ct)
    {
        var config = await db.PayzahGatewayConfigs.AsNoTracking().SingleOrDefaultAsync(x => x.GatewayName == "Payzah", ct);
        if (config is null || string.IsNullOrWhiteSpace(config.EncryptedPrivateKey)) throw new InvalidOperationException("Payzah is not configured. Save the private key first.");
        return (config, protector.Decrypt(config.EncryptedPrivateKey), config.IsSandbox ? PayzahSettings.SandboxBaseUrl : PayzahSettings.LiveBaseUrl);
    }
    private string BrowserUrl(string destination, string trackId)
    {
        var root = PayzahSettings.ResolvePublicBaseUrl(configuration);
        if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidOperationException("Payments:PublicBaseUrl must be absolute HTTPS.");
        var url = $"{root}/api/v1/payments/payzah/return?trackId={Uri.EscapeDataString(trackId)}";
        if (url.Length > 255) throw new InvalidOperationException("Payzah return URL exceeds the documented 255 character limit.");
        return url;
    }
    private static HttpRequestMessage Request(string url, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Authorization", Convert.ToBase64String(Encoding.UTF8.GetBytes(key)));
        return request;
    }
    // Payzah validates customer_phone as a local Kuwaiti number; stored values are E.164 or
    // 00-prefixed with the country code, so the code is stripped before sending.
    internal static string ToPayzahPhone(string customerMobile)
    {
        var digits = new string((customerMobile ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.Length > 8 && digits.StartsWith("965", StringComparison.Ordinal)) digits = digits[3..];
        return digits.Length > 0 ? digits : customerMobile ?? string.Empty;
    }
    private static JsonElement RequireData(HttpResponseMessage response, JsonElement root)
    {
        if (!response.IsSuccessStatusCode || !root.TryGetProperty("status", out var status) || (status.ValueKind != JsonValueKind.True && status.ToString() != "1")
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Payzah rejected the request or returned an invalid response.");
        return data;
    }
    private static string Text(JsonElement data, string name) => data.TryGetProperty(name, out var value) ? value.ToString() : "";
}
