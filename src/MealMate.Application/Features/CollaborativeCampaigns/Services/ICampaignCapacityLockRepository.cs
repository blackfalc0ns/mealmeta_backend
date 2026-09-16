using MealMate.Domain.CollaborativeCampaigns;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public interface ICampaignCapacityLockRepository
{
    Task<Campaign?> LockCampaignAsync(Guid campaignId, CancellationToken ct = default);

    Task<IReadOnlyList<CampaignRestaurantCapacity>> LockDailyCapacitiesAsync(
        Guid campaignId,
        IReadOnlyList<(DateOnly ServiceDate, Guid RestaurantId)> targets,
        CancellationToken ct = default);

    Task<T> ExecuteInTransactionWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken ct = default);
}
