namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Persists staged changes. A single <see cref="SaveChangesAsync"/> is already
/// atomic across all tracked entities; use <see cref="BeginTransactionAsync"/> only
/// when a use case needs multiple saves or external calls to commit together.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>An explicit database transaction. Rolls back if disposed uncommitted.</summary>
public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
