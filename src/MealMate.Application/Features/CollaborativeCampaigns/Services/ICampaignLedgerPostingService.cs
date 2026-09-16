using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public interface ICampaignLedgerPostingService
{
    Task<Result<Guid>> PostPurchaseEntriesAsync(
        CampaignCheckoutSession session,
        CampaignPaymentTransaction transaction,
        CancellationToken cancellationToken = default);

    Task<Result<Guid>> PostRefundEntriesAsync(
        CampaignCheckoutSession session,
        CampaignPaymentTransaction transaction,
        string refundReason,
        CancellationToken cancellationToken = default);
}
