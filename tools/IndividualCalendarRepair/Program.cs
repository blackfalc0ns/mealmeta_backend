using IndividualCalendarRepair;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

// IndividualCalendarRepair — report-only audit (default) or operator-approved apply for legacy
// individual-subscription days whose ConfirmedAtUtc stamp came from the old payment fulfillment
// path. Report: subscriptionId, dayId, serviceDate, status, deadline and the classification
// reason — no personal data beyond the identifiers needed to act. Apply: reads an explicit
// operator-reviewed candidate list, re-checks every day under a transaction, clears only stamps
// that are still proven repairable, and writes a before/after audit record per day.
//
// Usage (from backend/tools/IndividualCalendarRepair):
//   MEALMATE_DB_CONNECTION="..." dotnet run                       → report to stdout
//   MEALMATE_DB_CONNECTION="..." dotnet run -- --apply candidates.json [--operator <userId>]
//
// candidates.json includes subscriptionId, dayId and expectedConfirmedAtUtc from the reviewed report.
// Only days listed here and
// still classifying as RepairablePaymentStamp at apply time are touched.

var connectionString = Environment.GetEnvironmentVariable("MEALMATE_DB_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("MEALMATE_DB_CONNECTION is required.");
    return 2;
}

var apply = args.Contains("--apply");
string? candidatesPath = null;
var operatorId = Guid.Empty;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--apply") candidatesPath = args[i + 1];
    if (args[i] == "--operator") Guid.TryParse(args[i + 1], out operatorId);
}

if (apply && candidatesPath is null)
{
    Console.Error.WriteLine("--apply requires the reviewed candidates file path.");
    return 2;
}

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null))
    .Options;

await using var db = new AppDbContext(options, new NoOpDomainEventDispatcher());

var report = await BuildReportAsync(db);
WriteReport(report);

if (!apply) return 0;

var candidates = LegacyDayRepairCandidate.Parse(File.ReadAllText(candidatesPath!));
if (candidates.Any(x => x.SubscriptionId == Guid.Empty || x.DayId == Guid.Empty || x.ExpectedConfirmedAtUtc is null)
    || candidates.DistinctBy(x => x.DayId).Count() != candidates.Count)
{
    Console.Error.WriteLine("Each candidate requires subscriptionId, dayId and expectedConfirmedAtUtc; duplicate days are not allowed.");
    return 2;
}
return await ApplyAsync(db, report, candidates, operatorId);

static async Task<List<LegacyDayVerdictRow>> BuildReportAsync(AppDbContext db)
{
    var days = await (
        from day in db.SubscriptionDays.AsNoTracking()
        join subscription in db.Subscriptions.AsNoTracking() on day.SubscriptionId equals subscription.Id
        orderby day.SubscriptionId, day.ServiceDate
        select new { day, subscription })
        .ToListAsync();

    var dayIds = days.Select(x => x.day.Id).ToList();
    var dispatchedSet = (await db.RestaurantDailyOrders.AsNoTracking()
            .Where(o => dayIds.Contains(o.SubscriptionDayId))
            .Select(o => o.SubscriptionDayId)
            .Distinct()
            .ToListAsync())
        .ToHashSet();

    // Proof that the stamp came from the old payment fulfillment path: a fulfillment audit
    // record for the subscription. Timing equality alone is never sufficient.
    var subscriptionIds = days.Select(x => x.subscription.Id).Distinct().ToList();
    var audits = await db.AuditLogs.AsNoTracking()
        .Where(a => a.Action.Contains("Fulfill") && a.EntityId != null &&
            (subscriptionIds.Contains(a.EntityId.Value) || dayIds.Contains(a.EntityId.Value)))
        .Select(a => new { a.EntityId, a.AfterJson }).ToListAsync();

    return days.Select(pair =>
    {
        var facts = new LegacyDayFacts(
            pair.subscription.Id,
            pair.day.Id,
            pair.day.ServiceDate,
            pair.day.Status,
            pair.day.ConfirmedAtUtc,
            pair.day.LockAtUtc,
            pair.day.SelectionDeadlineUtc,
            IndividualSubscription: pair.subscription.FamilyGroupId is null,
            pair.subscription.Status,
            pair.subscription.PaymentVerifiedAtUtc,
            HasDispatchEvidence: dispatchedSet.Contains(pair.day.Id),
            HasLegacyFulfillmentAuditRecord: audits.Any(a =>
                (a.EntityId == pair.subscription.Id || a.EntityId == pair.day.Id) &&
                LegacyDayStampClassifier.HasProvenPaymentStamp(a.AfterJson, pair.subscription.Id, pair.day.Id, pair.day.ConfirmedAtUtc)));
        return new LegacyDayVerdictRow(facts, LegacyDayStampClassifier.Classify(facts));
    }).ToList();
}

static void WriteReport(List<LegacyDayVerdictRow> report)
{
    Console.WriteLine("subscriptionId\tdayId\tserviceDate\tstatus\tdeadlineUtc\tconfirmedAtUtc\tclassification\treason");
    foreach (var row in report)
    {
        if (row.Verdict.Classification is LegacyDayClassification.OutOfScope or LegacyDayClassification.PastDeadlineOpen)
            continue; // healthy or unrelated days stay out of the repair report
        Console.WriteLine($"{row.Facts.SubscriptionId}\t{row.Facts.DayId}\t{row.Facts.ServiceDate:yyyy-MM-dd}\t{row.Facts.Status}\t{row.Facts.SelectionDeadlineUtc:O}\t{row.Facts.ConfirmedAtUtc:O}\t{row.Verdict.Classification}\t{row.Verdict.Reason}");
    }
    var candidates = report.Count(x => x.Verdict.RepairEligible);
    Console.Error.WriteLine($"Report: {report.Count} days scanned, {candidates} repair candidates (apply requires the reviewed candidate list).");
}

static async Task<int> ApplyAsync(AppDbContext db, List<LegacyDayVerdictRow> report, List<LegacyDayRepairCandidate> candidates, Guid operatorId)
{
    var candidateDayIds = candidates.Select(c => c.DayId).ToHashSet();
    var repairable = report.Where(r => candidateDayIds.Contains(r.Facts.DayId)).ToList();
    foreach (var dayId in candidateDayIds.Except(report.Select(r => r.Facts.DayId)))
        Console.Error.WriteLine($"Skipped unknown dayId {dayId}.");

    var applied = 0;
    var skipped = 0;
    foreach (var row in repairable)
    {
        var candidate = candidates.Single(x => x.DayId == row.Facts.DayId);
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync();
            // Match the application lock order, then lock the day: dispatch and confirmation cannot
            // race the current-state evidence check and stamp write.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"Subscriptions\" WHERE \"Id\" = {candidate.SubscriptionId} FOR UPDATE");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT \"Id\" FROM \"SubscriptionDays\" WHERE \"Id\" = {candidate.DayId} FOR UPDATE");
            var day = await db.SubscriptionDays.FirstOrDefaultAsync(d => d.Id == row.Facts.DayId);
            var subscription = day is null ? null : await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == day.SubscriptionId);
            if (day is null || subscription is null || subscription.Id != candidate.SubscriptionId)
            {
                skipped++;
                await transaction.RollbackAsync();
                return;
            }

            var currentAudits = await db.AuditLogs.AsNoTracking().Where(a => a.Action.Contains("Fulfill") &&
                (a.EntityId == subscription.Id || a.EntityId == day.Id)).Select(a => a.AfterJson).ToListAsync();
            var before = new LegacyDayFacts(
                subscription.Id, day.Id, day.ServiceDate, day.Status, day.ConfirmedAtUtc, day.LockAtUtc, day.SelectionDeadlineUtc,
                subscription.FamilyGroupId is null, subscription.Status, subscription.PaymentVerifiedAtUtc,
                HasDispatchEvidence: await db.RestaurantDailyOrders.AnyAsync(o => o.SubscriptionDayId == day.Id),
                HasLegacyFulfillmentAuditRecord: currentAudits.Any(json =>
                    LegacyDayStampClassifier.HasProvenPaymentStamp(json, subscription.Id, day.Id, day.ConfirmedAtUtc)));

            if (!LegacyDayStampClassifier.CanApply(before, candidate.ExpectedConfirmedAtUtc!.Value, out var freshVerdict, out var skipReason))
            {
                skipped++;
                Console.WriteLine($"{day.Id}\tSKIP\t{skipReason}\tcurrent={freshVerdict.Classification}");
                await transaction.RollbackAsync();
                return;
            }

            var beforeStamp = day.ConfirmedAtUtc;
            day.ConfirmedAtUtc = null;
            day.ModifiedAtUtc = DateTime.UtcNow;

            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = operatorId == Guid.Empty ? null : operatorId,
                Action = "IndividualCalendarRepair.StampCleared",
                EntityType = nameof(SubscriptionDay),
                EntityId = day.Id,
                BeforeJson = JsonSerializer.Serialize(new { ConfirmedAtUtc = beforeStamp }),
                AfterJson = JsonSerializer.Serialize(new { ConfirmedAtUtc = (DateTime?)null }),
                Reason = freshVerdict.Reason,
                SourceChannel = "IndividualCalendarRepair",
                OccurredAtUtc = DateTime.UtcNow,
            });

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            applied++;
            Console.WriteLine($"{day.Id}\tAPPLIED\tstamp_cleared\tsubscription={subscription.Id}");
        });
    }

    Console.Error.WriteLine($"Apply complete: {applied} cleared, {skipped} skipped.");
    return 0;
}

sealed record LegacyDayVerdictRow(LegacyDayFacts Facts, LegacyDayVerdict Verdict);

sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IEnumerable<MealMate.Domain.Common.IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
