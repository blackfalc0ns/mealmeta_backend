using MealMate.Application.Features.CustomerCheckout;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Persistence.CustomerCheckout;

public sealed class PostgresCapacityConcurrencyLock : ICapacityConcurrencyLock
{
    private readonly AppDbContext _db;
    public PostgresCapacityConcurrencyLock(AppDbContext db) => _db = db;

    public async Task AcquireAsync(Guid restaurantId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken = default)
    {
        foreach (var date in dates.Distinct().OrderBy(x => x))
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({restaurantId + ":" + date.ToString("yyyy-MM-dd")}, 0))", cancellationToken);
    }
}
