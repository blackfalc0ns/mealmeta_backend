using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DocumentTemplates;
using MealMate.Domain.Common;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("MEALMATE_DB_CONNECTION")
    ?? "Host=ep-flat-fire-as6z3qpd-pooler.c-4.eu-central-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=npg_fiW8FPK0xlsm;SSL Mode=Require;Channel Binding=Require;Timeout=60;Command Timeout=120";

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString, npgsql =>
    {
        npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
        npgsql.UseNetTopologySuite();
    })
    .Options;

await using var db = new AppDbContext(options, new NoOpDomainEventDispatcher());
var clock = new SystemDateTimeProvider();

var before = await db.DocumentTemplates.AsNoTracking()
    .Select(x => new { x.NameEn, x.Kind, x.Status })
    .ToListAsync();
Console.WriteLine($"DocumentTemplates before: {before.Count}");
foreach (var row in before)
    Console.WriteLine($"  - {row.Kind} | {row.Status} | {row.NameEn}");

var handler = new SeedDocumentTemplatesCommandHandler(db, clock);
var result = await handler.Handle(new SeedDocumentTemplatesCommand(), CancellationToken.None);
if (result.IsFailure)
{
    Console.Error.WriteLine($"Seed failed: {result.Error.Code} - {result.Error.Description}");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine($"Seed touched: {result.Value.Count}");
foreach (var item in result.Value)
    Console.WriteLine($"  - {item.Kind} | {item.Status} | {item.NameEn} | v{item.Version}");

var after = await db.DocumentTemplates.AsNoTracking()
    .OrderBy(x => x.Kind)
    .ThenBy(x => x.NameEn)
    .Select(x => new { x.NameEn, x.Kind, x.Status, x.PageWidthPx, x.PageHeightPx })
    .ToListAsync();
Console.WriteLine($"DocumentTemplates after: {after.Count}");
foreach (var row in after)
    Console.WriteLine($"  - {row.Kind} | {row.Status} | {row.NameEn} | {row.PageWidthPx}x{row.PageHeightPx}");

sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
}
