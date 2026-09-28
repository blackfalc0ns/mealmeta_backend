using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence.Common;

/// <summary>
/// PostgreSQL implementation of IApplicationTransactionExecutor using EF Core execution strategy
/// and atomic database transactions.
/// </summary>
public sealed class PostgresApplicationTransactionExecutor : IApplicationTransactionExecutor, ICheckoutTransactionExecutor
{
    private readonly AppDbContext _db;

    public PostgresApplicationTransactionExecutor(AppDbContext db) => _db = db;

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
