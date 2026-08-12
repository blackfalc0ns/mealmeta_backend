using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Services;

public sealed class MyFatoorahPaymentService : IMyFatoorahPaymentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MyFatoorahPaymentService> _logger;

    public MyFatoorahPaymentService(HttpClient httpClient, ILogger<MyFatoorahPaymentService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync(string apiToken, bool isSandbox, CancellationToken cancellationToken = default)
    {
        try {
            var baseUrl = isSandbox ? "https://apitest.myfatoorah.com" : "https://api.myfatoorah.com";
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/InitiatePayment");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            request.Content = JsonContent.Create(new { InvoiceAmount = 10.0, CurrencyIso = "KWD" });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) {
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
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            PaymentMethodId = 1, // KNET
            InvoiceValue = invoiceValue,
            CustomerName = customerName,
            CustomerEmail = customerEmail,
            CustomerMobile = customerMobile,
            CallBackUrl = callbackUrl,
            ErrorUrl = errorUrl,
            CustomerReference = customerReference,
            Language = "ar"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v2/ExecutePayment");
        request.Content = JsonContent.Create(payload);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var paymentUrl = json.GetProperty("Data").GetProperty("PaymentURL").GetString();
        return paymentUrl ?? string.Empty;
    }

    public async Task<MyFatoorahPaymentStatusResponse?> GetPaymentStatusAsync(string key, string keyType = "PaymentId", CancellationToken cancellationToken = default)
    {
        try {
            var payload = new { Key = key, KeyType = keyType };
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v2/GetPaymentStatus");
            request.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var data = json.GetProperty("Data");

            var invoiceId = data.GetProperty("InvoiceId").GetInt64().ToString();
            var status = data.GetProperty("InvoiceStatus").GetString() ?? "Paid";
            var value = data.GetProperty("InvoiceValue").GetDecimal();
            var name = data.GetProperty("CustomerName").GetString() ?? "Customer";
            var email = data.GetProperty("CustomerEmail").GetString() ?? "";
            var mobile = data.GetProperty("CustomerMobile").GetString() ?? "";

            var transId = "MF-" + invoiceId;
            var paymentMethod = "KNET";

            if (data.TryGetProperty("InvoiceTransactions", out var transArray) && transArray.GetArrayLength() > 0)
            {
                var firstTrans = transArray[0];
                if (firstTrans.TryGetProperty("TransactionId", out var tId)) transId = tId.GetString() ?? transId;
                if (firstTrans.TryGetProperty("PaymentGateway", out var pG)) paymentMethod = pG.GetString() ?? paymentMethod;
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
                DateTime.UtcNow
            );
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to get MyFatoorah payment status for key {Key}", key);
            return null;
        }
    }

    public bool VerifyWebhookSignature(string payloadJson, string signatureHeader, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secretKey))
            return false;

        try {
            var keyBytes = Encoding.UTF8.GetBytes(secretKey);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            using var hmac = new HMACSHA256(keyBytes);
            var hashBytes = hmac.ComputeHash(payloadBytes);
            var computedSignature = Convert.ToBase64String(hashBytes);

            return string.Equals(computedSignature, signatureHeader, StringComparison.Ordinal);
        }
        catch {
            return false;
        }
    }
}
