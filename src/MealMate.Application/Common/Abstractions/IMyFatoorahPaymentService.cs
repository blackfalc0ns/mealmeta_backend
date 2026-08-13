namespace MealMate.Application.Common.Abstractions;

public interface IMyFatoorahPaymentService
{
    Task<bool> TestConnectionAsync(string apiToken, bool isSandbox, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MyFatoorahPaymentMethod>> GetPaymentMethodsAsync(decimal invoiceValue, CancellationToken cancellationToken = default);
    Task<string> ExecutePaymentAsync(decimal invoiceValue, string customerName, string customerEmail, string customerMobile, string callbackUrl, string errorUrl, string? customerReference = null, int paymentMethodId = 1, CancellationToken cancellationToken = default);
    Task<MyFatoorahPaymentStatusResponse?> GetPaymentStatusAsync(string key, string keyType = "PaymentId", CancellationToken cancellationToken = default);
    bool VerifyWebhookSignature(string payloadJson, string signatureHeader, string secretKey);
}

public sealed record MyFatoorahPaymentMethod(
    int PaymentMethodId,
    string Code,
    string NameAr,
    string NameEn,
    decimal ServiceCharge,
    decimal TotalAmount,
    string CurrencyCode,
    bool IsDirectPayment);

public sealed record MyFatoorahPaymentStatusResponse(
    string InvoiceId,
    string InvoiceStatus,
    decimal InvoiceValue,
    string CustomerName,
    string CustomerEmail,
    string CustomerMobile,
    string TransactionId,
    string PaymentMethod,
    DateTime CreatedDateUtc,
    string? CustomerReference = null,
    string CurrencyCode = "KWD",
    string? PaymentId = null
);
