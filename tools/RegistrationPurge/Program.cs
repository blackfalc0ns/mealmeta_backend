using MealMate.Application.Common.Abstractions;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("MEALMATE_DB_CONNECTION")
    ?? "Host=ep-flat-fire-as6z3qpd-pooler.c-4.eu-central-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_fiW8FPK0xlsm;SSL Mode=Require;Channel Binding=Require";

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null))
    .Options;

await using var db = new AppDbContext(options, new NoOpDomainEventDispatcher());
var before = await db.RestaurantRegistrations.CountAsync();
Console.WriteLine($"Restaurant registrations before: {before}");

if (before > 0)
{
    await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"RestaurantRegistrations\" CASCADE");
}

var after = await db.RestaurantRegistrations.CountAsync();
Console.WriteLine($"Restaurant registrations after: {after}");

var restaurantUsersBefore = await db.Database.SqlQueryRaw<int>(
    "SELECT COUNT(*)::int AS \"Value\" FROM \"Users\" WHERE \"UserType\" = 2")
    .SingleAsync();
Console.WriteLine($"Restaurant users before: {restaurantUsersBefore}");

if (restaurantUsersBefore > 0)
{
    await using var transaction = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Users\" WHERE \"UserType\" = 2");
    await transaction.CommitAsync();
}

var restaurantUsersAfter = await db.Database.SqlQueryRaw<int>(
    "SELECT COUNT(*)::int AS \"Value\" FROM \"Users\" WHERE \"UserType\" = 2")
    .SingleAsync();
Console.WriteLine($"Restaurant users after: {restaurantUsersAfter}");

sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IEnumerable<MealMate.Domain.Common.IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
