using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.PrepDocuments;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

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
var clock = new SystemClock();
var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Branding:MealMateLogoUrl"] = "/assets/branding/mealmate-logo.png",
    })
    .Build();

var eligible = new[]
{
    SubscriptionDayStatus.ConfirmedByRestaurant,
    SubscriptionDayStatus.Preparing,
    SubscriptionDayStatus.OutForDelivery,
    SubscriptionDayStatus.Delivered,
};

var candidate = await db.SubscriptionDays
    .Where(x => eligible.Contains(x.Status) && x.RestaurantId != null)
    .Select(x => new
    {
        Day = x,
        MealCount = db.DailyMealSelections.Count(s => s.SubscriptionDayId == x.Id),
    })
    .Where(x => x.MealCount > 0)
    .OrderByDescending(x => x.Day.ServiceDate)
    .ThenByDescending(x => x.MealCount)
    .FirstOrDefaultAsync();

if (candidate is null)
{
    Console.Error.WriteLine("No confirmed subscription day with meals found.");
    Environment.ExitCode = 1;
    return;
}

var day = candidate.Day;
var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
var oldDate = day.ServiceDate;
day.ServiceDate = tomorrow;
day.Status = SubscriptionDayStatus.ConfirmedByRestaurant;
day.ModifiedAtUtc = clock.UtcNow;
await db.SaveChangesAsync();

var restaurant = await db.RestaurantProfiles.AsNoTracking()
    .FirstAsync(x => x.Id == day.RestaurantId!.Value);

var ownerEmail = await (
    from u in db.Users.AsNoTracking()
    join ur in db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
    join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
    where u.RestaurantId == restaurant.Id
    select u.Email
).FirstOrDefaultAsync();

if (string.IsNullOrWhiteSpace(ownerEmail))
{
    ownerEmail = await db.Users.AsNoTracking()
        .Where(u => u.RestaurantId == restaurant.Id)
        .Select(u => u.Email)
        .FirstOrDefaultAsync();
}

Console.WriteLine($"Moved day {day.Id}");
Console.WriteLine($"  restaurant: {restaurant.TradeNameEn} ({restaurant.Id})");
Console.WriteLine($"  login email: {ownerEmail ?? "(unknown)"}");
Console.WriteLine($"  serviceDate: {oldDate} -> {tomorrow}");
Console.WriteLine($"  meals: {candidate.MealCount}");

var generator = new PrepDocumentsGenerator(db, clock, config, NullLogger<PrepDocumentsGenerator>.Instance);
var result = await generator.GenerateForDayAsync(day.Id, forceReissue: true, rotateBarcode: true);
if (result.IsFailure)
{
    Console.Error.WriteLine($"Generate failed: {result.Error.Code} - {result.Error.Description}");
    Environment.ExitCode = 1;
    return;
}

var barcode = await db.MealBarcodes.AsNoTracking().AnyAsync(x => x.SubscriptionDayId == day.Id);
var invoice = await db.MealInvoices.AsNoTracking().AnyAsync(x => x.SubscriptionDayId == day.Id);
var box = await db.MealLabels.AsNoTracking().CountAsync(x => x.SubscriptionDayId == day.Id && x.Kind == MealLabelKind.Box);
var mealLabels = await db.MealLabels.AsNoTracking().CountAsync(x => x.SubscriptionDayId == day.Id && x.Kind == MealLabelKind.Meal);

Console.WriteLine("Prep documents generated:");
Console.WriteLine($"  barcode={barcode} invoice={invoice} boxLabels={box} mealLabels={mealLabels}");
Console.WriteLine();
Console.WriteLine("Try restaurant UI:");
Console.WriteLine("  1) http://localhost:4201/restaurant/login");
Console.WriteLine($"  2) Login: {ownerEmail ?? "dietcare.hq@mealmate.kw"} / Restaurant#2026");
Console.WriteLine("  3) Open http://localhost:4201/restaurant/orders/labels");
Console.WriteLine("  4) Preview / Print the ready order");

sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

sealed class SystemClock : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);
}
