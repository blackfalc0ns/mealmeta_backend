namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public interface IPaymentRefundGateway
{
    Task<bool> IssueRefundAsync(
        string paymentProvider,
        string providerTransactionId,
        decimal amount,
        string currency,
        string reason,
        CancellationToken cancellationToken = default);
}
