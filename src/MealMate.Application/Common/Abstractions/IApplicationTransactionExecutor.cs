namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Universal application transaction executor wrapping operations inside a transactional execution strategy.
/// </summary>
public interface IApplicationTransactionExecutor
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}
