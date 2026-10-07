namespace MealMate.Application.Common.Abstractions;

public interface IPaymentGatewayService
{
    Task<bool> TestConnectionAsync(string apiToken, bool isSandbox, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentGatewayMethod>> GetPaymentMethodsAsync(decimal invoiceValue, CancellationToken cancellationToken = default);
    Task<PaymentCreationReceipt> ExecutePaymentAsync(decimal invoiceValue, string customerName, string customerEmail, string customerMobile, string callbackUrl, string errorUrl, string? customerReference = null, int paymentMethodId = 3, CancellationToken cancellationToken = default);
    Task<PaymentGatewayStatusResponse?> GetPaymentStatusAsync(string key, string keyType = "PaymentId", CancellationToken cancellationToken = default);
    Task<PaymentGatewayRefundReceipt?> MakeRefundAsync(string paymentId, decimal amount, string currency,
        string externalIdentifier, string reason, CancellationToken cancellationToken = default)
        => Task.FromResult<PaymentGatewayRefundReceipt?>(null);
    Task<IReadOnlyList<PaymentGatewayRefundStatus>> GetRefundStatusAsync(string key, string keyType = "RefundId", CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PaymentGatewayRefundStatus>>([]);
}

public sealed record PaymentGatewayRefundReceipt(string RefundId, string RefundReference, string ExternalIdentifier, decimal Amount);
public sealed record PaymentGatewayRefundStatus(string RefundId, string RefundReference, string ExternalIdentifier,
    string InvoiceId, decimal Amount, decimal RefundAmount, string BaseCurrency, string Status);

public sealed record PaymentGatewayMethod(
    int PaymentMethodId,
    string Code,
    string NameAr,
    string NameEn,
    decimal? ServiceCharge,
    decimal? TotalAmount,
    string CurrencyCode,
    bool IsDirectPayment,
    string? ImageUrl = null);

public sealed record PaymentGatewayStatusResponse(
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

public sealed record PaymentCreationReceipt(string PaymentId, string PaymentUrl, string TrackId)
{
    // PaymentId is the original provider creation identifier. PaymentHandle binds all application operations to one environment-specific attempt.
    public string PaymentHandle => "pz-" + TrackId;
}
