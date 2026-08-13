namespace MealMate.Application.Features.CustomerCheckout;

public interface ICapacityConcurrencyLock
{
    Task AcquirePaymentAsync(string paymentId, CancellationToken cancellationToken = default);
    Task AcquireSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
    Task AcquireAsync(Guid restaurantId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken = default);
}
