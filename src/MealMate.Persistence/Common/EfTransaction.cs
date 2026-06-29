using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;

namespace MealMate.Persistence.Common;

/// <summary>Adapts an EF Core transaction to the application <see cref="ITransaction"/>.</summary>
public sealed class EfTransaction : ITransaction
{
    private readonly IDbContextTransaction _transaction;
    private bool _completed;

    public EfTransaction(IDbContextTransaction transaction) => _transaction = transaction;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.CommitAsync(cancellationToken);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.RollbackAsync(cancellationToken);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            await _transaction.RollbackAsync();
        }

        await _transaction.DisposeAsync();
    }
}
