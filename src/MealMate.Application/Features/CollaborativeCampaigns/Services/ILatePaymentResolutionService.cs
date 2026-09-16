using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public record LatePaymentResolutionResult(
    bool Success,
    CampaignCheckoutSessionStatus NewSessionStatus,
    bool RequiresRefund,
    string Message);

public interface ILatePaymentResolutionService
{
    Task<LatePaymentResolutionResult> ResolveLatePaymentAsync(
        CampaignCheckoutSession session,
        CampaignPaymentTransaction transaction,
        CancellationToken cancellationToken = default);
}
