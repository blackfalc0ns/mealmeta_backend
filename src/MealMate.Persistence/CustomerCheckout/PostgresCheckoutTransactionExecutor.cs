using MealMate.Application.Features.CustomerCheckout;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence.CustomerCheckout;

public sealed class PostgresCheckoutTransactionExecutor : ICheckoutTransactionExecutor
{
    private readonly AppDbContext _db;
    public PostgresCheckoutTransactionExecutor(AppDbContext db) => _db = db;

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async ct =>
        {
            await operation(ct);
            return true;
        }, cancellationToken);
    }
}
