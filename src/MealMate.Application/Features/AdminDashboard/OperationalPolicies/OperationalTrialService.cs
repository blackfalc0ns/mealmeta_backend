using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCalendar;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.AdminDashboard.OperationalPolicies;

public sealed record TrialRequest(int Minutes, string Reason);
public sealed record TrialDayState(Guid SubscriptionId, DateOnly ServiceDate, string Status, DateTime? DeadlineUtc,
    DateTime? LockedAtUtc, int SelectionCount, int OrderCount, string? OverrideReason);

public sealed class OperationalTrialService(IAppDbContext db, IDateTimeProvider clock, IHostEnvironment environment,
    IOperationalPolicyService policies, IApplicationTransactionExecutor? transactions = null, IApplicationLockManager? locks = null)
{
    public bool Enabled => environment.IsDevelopment() || environment.IsEnvironment("Test");
    private static Error Unavailable => Error.Forbidden("operational_trials.unavailable", "Rapid trials are available only in Development and Test environments.");
    private Task<Result<TrialDayState>> Execute(Guid id, Func<CancellationToken, Task<Result<TrialDayState>>> operation, CancellationToken ct)
        => transactions is null ? operation(ct) : transactions.ExecuteResultAsync<TrialDayState>(async token =>
        { if (locks is not null) await locks.LockSubscriptionAsync(id, token); return await operation(token); }, ct);

    public Task<Result<TrialDayState>> ConfigureAsync(Guid subscriptionId, DateOnly date, TrialRequest request, Guid actor, CancellationToken ct)
    {
        if (!Enabled) return Task.FromResult(Result.Failure<TrialDayState>(Unavailable));
        if (actor == Guid.Empty || request.Minutes is < 1 or > 60 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
            return Task.FromResult(Result.Failure<TrialDayState>(Error.Validation("operational_trials.invalid", "Provide an administrator, a reason and between 1 and 60 minutes.")));
        return Execute(subscriptionId, async token =>
        {
            var day = await db.SubscriptionDays.FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId && x.ServiceDate == date, token);
            if (day is null) return Error.NotFound("operational_trials.day_not_found");
            if (!OperationalDeadlineRules.IsMutable(day)) return Error.Conflict("operational_trials.committed", "Choose another open service day for a new trial. Committed days cannot be reopened.");
            await StoreOverride(day, clock.UtcNow.AddMinutes(request.Minutes), actor, request.Reason.Trim(), token);
            await db.SaveChangesAsync(token);
            return await InspectAsync(subscriptionId, date, token);
        }, ct);
    }

    private async Task StoreOverride(MealMate.Domain.CalendarMeals.SubscriptionDay day, DateTime deadline, Guid actor, string reason, CancellationToken ct)
    {
        var key = OperationalDeadlineRules.TrialPrefix + day.Id;
        var row = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) { row = new SystemSetting { Key = key, CreatedByUserId = actor, CreatedAtUtc = clock.UtcNow }; db.SystemSettings.Add(row); }
        row.IsActive = true; row.ModifiedByUserId = actor; row.ModifiedAtUtc = clock.UtcNow;
        row.ValueJson = JsonSerializer.Serialize(new TrialDeadlineOverride(day.SubscriptionId, day.ServiceDate, deadline, actor, reason, clock.UtcNow));
        day.SelectionDeadlineUtc = deadline;
        RecordAction(day.SubscriptionId, actor, "deadline", reason);
    }

    private void RecordAction(Guid id, Guid actor, string action, string reason)
        => db.SystemSettings.Add(new SystemSetting { Key = "operational_policies.trial_action." + Guid.NewGuid(),
            ValueJson = JsonSerializer.Serialize(new { SubscriptionId = id, ActorId = actor, Action = action, Reason = reason, AtUtc = clock.UtcNow }),
            CreatedAtUtc = clock.UtcNow, CreatedByUserId = actor });

    public async Task<Result<TrialDayState>> InspectAsync(Guid id, DateOnly date, CancellationToken ct)
    {
        if (!Enabled) return Unavailable;
        var day = await db.SubscriptionDays.AsNoTracking().FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, ct);
        if (day is null) return Error.NotFound("operational_trials.day_not_found");
        await OperationalDeadlineRules.RefreshAsync(db, new[] { day }, policies, true, ct);
        var json = await db.SystemSettings.AsNoTracking().Where(x => x.Key == OperationalDeadlineRules.TrialPrefix + day.Id && x.IsActive).Select(x => x.ValueJson).FirstOrDefaultAsync(ct);
        var trial = json is null ? null : JsonSerializer.Deserialize<TrialDeadlineOverride>(json, OperationalPolicyRules.JsonOptions);
        return new TrialDayState(id, date, day.Status.ToString(), day.SelectionDeadlineUtc, day.LockAtUtc,
            await db.DailyMealSelections.CountAsync(x => x.SubscriptionDayId == day.Id, ct),
            await db.RestaurantDailyOrders.CountAsync(x => x.SubscriptionDayId == day.Id, ct), trial?.Reason);
    }

    public Task<Result<TrialDayState>> RunAsync(Guid id, DateOnly date, string action, string reason, Guid actor, CustomerCalendarEngine engine, CancellationToken ct)
    {
        if (!Enabled) return Task.FromResult(Result.Failure<TrialDayState>(Unavailable));
        if (actor == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000 || action is not ("select" or "dispatch" or "clear"))
            return Task.FromResult(Result.Failure<TrialDayState>(Error.Validation("operational_trials.invalid", "A valid action and reason are required.")));
        return Execute(id, async token =>
        {
            var day = await db.SubscriptionDays.FirstOrDefaultAsync(x => x.SubscriptionId == id && x.ServiceDate == date, token);
            if (day is null) return Error.NotFound("operational_trials.day_not_found");
            if (action == "clear")
            {
                var row = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == OperationalDeadlineRules.TrialPrefix + day.Id, token);
                if (row is not null) { row.IsActive = false; row.ModifiedAtUtc = clock.UtcNow; row.ModifiedByUserId = actor; }
                if (OperationalDeadlineRules.IsMutable(day)) day.SelectionDeadlineUtc = policies.CalculateSelectionDeadlineUtc(date, await policies.GetSettingsAsync(token));
                RecordAction(id, actor, action, reason.Trim());
                await db.SaveChangesAsync(token); return await InspectAsync(id, date, token);
            }
            if (action == "dispatch" && day.Status is SubscriptionDayStatus.SentToRestaurant or SubscriptionDayStatus.ConfirmedByRestaurant or SubscriptionDayStatus.Preparing or SubscriptionDayStatus.OutForDelivery or SubscriptionDayStatus.Delivered)
                return await InspectAsync(id, date, token);
            if (!OperationalDeadlineRules.IsMutable(day)) return Error.Conflict("operational_trials.committed", "This day has already been committed. Choose another open day.");
            if (action == "dispatch") { await StoreOverride(day, clock.UtcNow, actor, reason.Trim(), token); await db.SaveChangesAsync(token); }
            var result = await engine.AutoSelectFullBoxAsync(id, date, action == "dispatch", false, token);
            if (result.IsFailure) return result.Error;
            RecordAction(id, actor, action, reason.Trim()); await db.SaveChangesAsync(token);
            return await InspectAsync(id, date, token);
        }, ct);
    }
}
