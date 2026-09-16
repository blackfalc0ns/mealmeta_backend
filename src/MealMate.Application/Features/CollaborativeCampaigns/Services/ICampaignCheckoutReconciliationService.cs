namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public interface ICampaignCheckoutReconciliationService
{
    Task<int> ReconcileStuckSessionsAsync(CancellationToken cancellationToken = default);
}
