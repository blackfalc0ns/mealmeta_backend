namespace MealMate.Application.Features.CustomerCheckout;

public interface ICheckoutTransactionExecutor
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
