namespace MealMate.Application.Common.Abstractions;

public interface IMyFatoorahPaymentService
{
    Task<bool> TestConnectionAsync(string apiToken, bool isSandbox, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MyFatoorahPaymentMethod>> GetPaymentMethodsAsync(decimal invoiceValue, CancellationToken cancellationToken = default);
    Task<string> ExecutePaymentAsync(decimal invoiceValue, string customerName, string customerEmail, string customerMobile, string callbackUrl, string errorUrl, string? customerReference = null, int paymentMethodId = 1, CancellationToken cancellationToken = default);
    Task<MyFatoorahPaymentStatusResponse?> GetPaymentStatusAsync(string key, string keyType = "PaymentId", CancellationToken cancellationToken = default);
    bool VerifyWebhookSignature(string payloadJson, string signatureHeader, string secretKey);
    Task<MyFatoorahRefundReceipt?> MakeRefundAsync(string paymentId, decimal amount, string currency,
        string externalIdentifier, string reason, CancellationToken cancellationToken = default)
        => Task.FromResult<MyFatoorahRefundReceipt?>(null);
    Task<IReadOnlyList<MyFatoorahRefundStatus>> GetRefundStatusAsync(string key, string keyType = "RefundId", CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MyFatoorahRefundStatus>>([]);
}

public sealed record MyFatoorahRefundReceipt(string RefundId, string RefundReference, string ExternalIdentifier, decimal Amount);
public sealed record MyFatoorahRefundStatus(string RefundId, string RefundReference, string ExternalIdentifier,
    string InvoiceId, decimal Amount, decimal RefundAmount, string BaseCurrency, string Status);

public sealed record MyFatoorahPaymentMethod(
    int PaymentMethodId,
    string Code,
    string NameAr,
    string NameEn,
    decimal ServiceCharge,
    decimal TotalAmount,
    string CurrencyCode,
    bool IsDirectPayment,
    string? ImageUrl = null);

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
