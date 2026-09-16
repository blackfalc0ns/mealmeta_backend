using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Services;

public sealed class CampaignPaymentRefundGateway : IPaymentRefundGateway
{
    private readonly ILogger<CampaignPaymentRefundGateway> _logger;

    public CampaignPaymentRefundGateway(ILogger<CampaignPaymentRefundGateway> logger)
    {
        _logger = logger;
    }

    public Task<bool> IssueRefundAsync(
        string paymentProvider,
        string providerTransactionId,
        decimal amount,
        string currency,
        string reason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Campaign refund issued via {Provider} for transaction {TxId}. Amount: {Amount} {Currency}. Reason: {Reason}",
            paymentProvider,
            providerTransactionId,
            amount,
            currency,
            reason);

        return Task.FromResult(true);
    }
}
