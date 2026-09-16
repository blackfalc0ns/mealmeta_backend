namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed record CampaignLifecycleResult(int ActivatedCount, int EndedCount);

public interface ICampaignLifecycleSchedulerService
{
    Task<CampaignLifecycleResult> ProcessLifecycleTransitionsAsync(CancellationToken cancellationToken = default);
}
