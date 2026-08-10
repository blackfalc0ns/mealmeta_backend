using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MealMate.Persistence;

/// <summary>
/// Used by EF Core tooling (<c>dotnet ef migrations</c>) to build the context
/// without the full host. The connection string is read from the
/// <c>MEALMATE_DB_CONNECTION</c> environment variable, falling back to a local default.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MEALMATE_DB_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=ep-tiny-breeze-agq6d3fw-pooler.c-2.eu-central-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_gXWyf4c5LVCq;SSL Mode=Require;Channel Binding=Require";
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppDbContextFactory).Assembly.GetName().Name);
                    npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
                })
            .Options;

        return new AppDbContext(options, new NoOpDomainEventDispatcher());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(
            IEnumerable<IDomainEvent> domainEvents,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
