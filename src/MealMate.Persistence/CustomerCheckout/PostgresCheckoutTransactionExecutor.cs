using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Persistence.Common;

namespace MealMate.Persistence.CustomerCheckout;

public sealed class PostgresCheckoutTransactionExecutor : ICheckoutTransactionExecutor
{
    private readonly IApplicationTransactionExecutor _executor;
    public PostgresCheckoutTransactionExecutor(IApplicationTransactionExecutor executor) => _executor = executor;
    public PostgresCheckoutTransactionExecutor(AppDbContext db) : this(new PostgresApplicationTransactionExecutor(db)) { }

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        => _executor.ExecuteAsync(operation, cancellationToken);

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => _executor.ExecuteAsync(operation, cancellationToken);
}
