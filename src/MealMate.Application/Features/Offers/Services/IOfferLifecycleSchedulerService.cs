namespace MealMate.Application.Features.Offers.Services;

public sealed record OfferLifecycleResult(int ActivatedCount, int EndedCount);

public interface IOfferLifecycleSchedulerService
{
    Task<OfferLifecycleResult> ProcessLifecycleTransitionsAsync(CancellationToken cancellationToken = default);
}
