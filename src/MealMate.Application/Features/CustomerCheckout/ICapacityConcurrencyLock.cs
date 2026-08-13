namespace MealMate.Application.Features.CustomerCheckout;

public interface ICapacityConcurrencyLock
{
    Task AcquireAsync(Guid restaurantId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken = default);
}
