namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Universal application transaction executor wrapping operations inside a transactional execution strategy.
/// </summary>
public interface IApplicationTransactionExecutor
{
    bool HasActiveTransaction => false;
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);

    Task<MealMate.Application.Common.Models.Result<T>> ExecuteResultAsync<T>(
        Func<CancellationToken, Task<MealMate.Application.Common.Models.Result<T>>> operation, CancellationToken cancellationToken = default)
        => ExecuteAsync(operation, cancellationToken);

    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async ct =>
        {
            await operation(ct);
            return true;
        }, cancellationToken);
    }
}
