using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
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

var today = DateOnly.FromDateTime(DateTime.UtcNow);
var windowEnd = today.AddDays(3);
var eligible = new[]
{
    SubscriptionDayStatus.ConfirmedByRestaurant,
    SubscriptionDayStatus.Preparing,
    SubscriptionDayStatus.OutForDelivery,
    SubscriptionDayStatus.Delivered,
};

Console.WriteLine("Published templates:");
foreach (var t in await db.DocumentTemplates.AsNoTracking()
             .Where(x => x.Status == DocumentTemplateStatus.Published)
             .OrderBy(x => x.Kind)
             .Select(x => new { x.Kind, x.NameEn, x.NameAr })
             .ToListAsync())
    Console.WriteLine($"  PUB {t.Kind}: {t.NameEn}");

Console.WriteLine("Active pack templates:");
foreach (var t in await db.DocumentTemplates.AsNoTracking()
             .Where(x => x.Status != DocumentTemplateStatus.Archived)
             .OrderBy(x => x.Kind).ThenBy(x => x.NameEn)
             .Select(x => new { x.Kind, x.Status, x.NameEn })
             .ToListAsync())
    Console.WriteLine($"  {t.Status} {t.Kind}: {t.NameEn}");

var days = await db.SubscriptionDays.AsNoTracking()
    .Where(x => x.ServiceDate >= today && x.ServiceDate <= windowEnd
                && eligible.Contains(x.Status)
                && x.RestaurantId != null)
    .OrderBy(x => x.ServiceDate)
    .ThenByDescending(x => x.ConfirmedAtUtc)
    .Take(20)
    .Select(x => new
    {
        x.Id,
        x.ServiceDate,
        x.Status,
        x.RestaurantId,
        HasBarcode = db.MealBarcodes.Any(b => b.SubscriptionDayId == x.Id),
        HasInvoice = db.MealInvoices.Any(i => i.SubscriptionDayId == x.Id),
        BoxLabels = db.MealLabels.Count(l => l.SubscriptionDayId == x.Id && l.Kind == MealLabelKind.Box),
        MealLabels = db.MealLabels.Count(l => l.SubscriptionDayId == x.Id && l.Kind == MealLabelKind.Meal),
        Meals = db.DailyMealSelections.Count(s => s.SubscriptionDayId == x.Id),
    })
    .ToListAsync();

var restaurantIds = days.Select(d => d.RestaurantId!.Value).Distinct().ToList();
var restaurants = await db.RestaurantProfiles.AsNoTracking()
    .Where(r => restaurantIds.Contains(r.Id))
    .Select(r => new { r.Id, r.TradeNameEn })
    .ToListAsync();

Console.WriteLine($"Confirmed days {today}..{windowEnd}: {days.Count}");
foreach (var day in days)
{
    var restaurant = restaurants.FirstOrDefault(r => r.Id == day.RestaurantId);
    var ready = day.HasBarcode && day.HasInvoice && day.BoxLabels > 0 && day.MealLabels == day.Meals && day.Meals > 0;
    Console.WriteLine(
        $"{day.ServiceDate} | {day.Id} | {restaurant?.TradeNameEn ?? "?"} | {day.Status} | meals={day.Meals} bc={day.HasBarcode} inv={day.HasInvoice} box={day.BoxLabels} ml={day.MealLabels} | {(ready ? "READY" : "NEEDS_DOCS")}");
}

if (days.Count == 0)
{
    var anyRecent = await db.SubscriptionDays.AsNoTracking()
        .Where(x => eligible.Contains(x.Status) && x.RestaurantId != null)
        .OrderByDescending(x => x.ServiceDate)
        .Take(10)
        .Select(x => new { x.Id, x.ServiceDate, x.Status, x.RestaurantId })
        .ToListAsync();
    Console.WriteLine($"Fallback recent confirmed days: {anyRecent.Count}");
    foreach (var day in anyRecent)
        Console.WriteLine($"  {day.ServiceDate} | {day.Id} | {day.Status} | restaurant={day.RestaurantId}");
}

sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
