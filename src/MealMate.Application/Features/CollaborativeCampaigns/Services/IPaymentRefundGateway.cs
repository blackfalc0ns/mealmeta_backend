namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public enum RefundGatewayState { Pending, Confirmed, Rejected, NeedsReview }
public sealed record RefundGatewayResult(RefundGatewayState State, string? ProviderReference = null, string? Detail = null)
{
    public bool IsConfirmed => State == RefundGatewayState.Confirmed;
}

public interface IPaymentRefundGateway
{
    Task<RefundGatewayResult> RequestRefundAsync(string paymentProvider, string paymentId, decimal amount,
        string currency, string reason, string operationReference, CancellationToken cancellationToken = default)
        => Task.FromResult(new RefundGatewayResult(RefundGatewayState.NeedsReview, Detail: "Provider refund confirmation is unavailable."));
    Task<RefundGatewayResult> ReconcileAsync(Guid operationId, CancellationToken cancellationToken = default)
        => Task.FromResult(new RefundGatewayResult(RefundGatewayState.NeedsReview, Detail: "Provider refund reconciliation is unavailable."));
    Task<bool> IssueRefundAsync(
        string paymentProvider,
        string providerTransactionId,
        decimal amount,
        string currency,
        string reason,
        CancellationToken cancellationToken = default);
}
