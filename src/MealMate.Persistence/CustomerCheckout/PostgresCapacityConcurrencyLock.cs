using MealMate.Application.Features.CustomerCheckout;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence.CustomerCheckout;

public sealed class PostgresCapacityConcurrencyLock : ICapacityConcurrencyLock
{
    private readonly AppDbContext _db;
    public PostgresCapacityConcurrencyLock(AppDbContext db) => _db = db;

    public Task AcquirePaymentAsync(string paymentId, CancellationToken cancellationToken = default)
        => AcquireKeyAsync($"payment:{paymentId.Trim().ToUpperInvariant()}", cancellationToken);

    public Task AcquireSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
        => AcquireKeyAsync($"subscription:{subscriptionId:N}", cancellationToken);

    public async Task AcquireAsync(Guid restaurantId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken = default)
    {
        foreach (var date in dates.Distinct().OrderBy(x => x))
            await AcquireKeyAsync($"capacity:{restaurantId:N}:{date:yyyy-MM-dd}", cancellationToken);
    }

    private async Task AcquireKeyAsync(string key, CancellationToken cancellationToken)
        => await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
}
