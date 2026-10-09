using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MealMate.Application.Features.AdminDashboard.OperationalPolicies;
public sealed record TrialDeadlineOverride(Guid SubscriptionId, DateOnly ServiceDate, DateTime DeadlineUtc, Guid ActorId, string Reason, DateTime CreatedAtUtc);

public static class OperationalDeadlineRules
{
    public const string TrialPrefix = "operational_policies.trial.";
    public static bool IsMutable(SubscriptionDay day) => day.LockAtUtc is null && day.Status is SubscriptionDayStatus.Open or SubscriptionDayStatus.CustomerSelected or SubscriptionDayStatus.AutoSelected or SubscriptionDayStatus.ReplacementRequired;
    public static async Task RefreshAsync(IAppDbContext db, IEnumerable<SubscriptionDay> days, IOperationalPolicyService policies, bool allowTrial, CancellationToken ct)
    {
        var settings = await policies.GetSettingsAsync(ct);
        var published = await db.SystemSettings.AsNoTracking().AnyAsync(x => x.Key == OperationalPolicyService.SettingKey && x.IsActive, ct);
        var mutableDays = days.Where(IsMutable).ToList();
        var keys = mutableDays.Select(x => TrialPrefix + x.Id).ToList();
        var overrides = allowTrial && keys.Count > 0 ? await db.SystemSettings.AsNoTracking().Where(x => keys.Contains(x.Key) && x.IsActive).ToDictionaryAsync(x => x.Key, x => x.ValueJson, ct) : new Dictionary<string, string>();

        // The first service day of an individual subscription keeps the approved payment+window
        // exception across policy publications. The anchor is the stored PaymentVerifiedAtUtc and
        // the subscription's true first service date over ALL of its days — never the first item
        // of the possibly partial batch received here, and never a time derived from this refresh
        // or from a fulfillment retry. Subscriptions without documented payment keep the standard
        // cutoff; family subscriptions keep their existing behavior.
        var firstDayWindows = mutableDays.Count > 0
            ? await LoadFirstDayWindowsAsync(db, mutableDays, ct)
            : new Dictionary<Guid, FirstDayWindow>();

        foreach (var day in mutableDays)
        {
            if (overrides.TryGetValue(TrialPrefix + day.Id, out var json))
            {
                var trial = JsonSerializer.Deserialize<TrialDeadlineOverride>(json, OperationalPolicyRules.JsonOptions)!;
                day.SelectionDeadlineUtc = trial.DeadlineUtc;
            }
            else if (firstDayWindows.TryGetValue(day.SubscriptionId, out var window)
                && window.PaymentVerifiedAtUtc is { } verifiedAt
                && day.ServiceDate == window.FirstServiceDate)
            {
                day.SelectionDeadlineUtc = policies.CalculateFirstDaySelectionDeadlineUtc(verifiedAt, window.FirstServiceDate, settings);
            }
            else if (published)
            {
                var configuredDeadline = policies.CalculateSelectionDeadlineUtc(day.ServiceDate, settings);
                day.SelectionDeadlineUtc = firstDayWindows.ContainsKey(day.SubscriptionId)
                    ? SubscriptionScheduleService.EnforceStandardSelectionDeadline(day.ServiceDate, configuredDeadline)
                    : configuredDeadline;
            }
            else if (firstDayWindows.ContainsKey(day.SubscriptionId))
            {
                // Repair legacy short deadlines even before any operational policy publication.
                day.SelectionDeadlineUtc = SubscriptionScheduleService.EnforceStandardSelectionDeadline(
                    day.ServiceDate, day.SelectionDeadlineUtc ?? policies.CalculateSelectionDeadlineUtc(day.ServiceDate, settings));
            }
        }
    }

    private sealed record FirstDayWindow(DateOnly FirstServiceDate, DateTime? PaymentVerifiedAtUtc);

    private static async Task<Dictionary<Guid, FirstDayWindow>> LoadFirstDayWindowsAsync(
        IAppDbContext db, IReadOnlyCollection<SubscriptionDay> mutableDays, CancellationToken ct)
    {
        var subscriptionIds = mutableDays.Select(x => x.SubscriptionId).Distinct().ToList();
        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(x => subscriptionIds.Contains(x.Id) && x.FamilyGroupId == null)
            .Select(x => new { x.Id, x.PaymentVerifiedAtUtc })
            .ToListAsync(ct);
        var firstDates = await db.SubscriptionDays.AsNoTracking()
            .Where(x => subscriptionIds.Contains(x.SubscriptionId))
            .GroupBy(x => x.SubscriptionId)
            .Select(g => new { SubscriptionId = g.Key, FirstServiceDate = g.Min(x => x.ServiceDate) })
            .ToDictionaryAsync(x => x.SubscriptionId, x => x.FirstServiceDate, ct);
        return subscriptions.ToDictionary(
            x => x.Id,
            x => new FirstDayWindow(firstDates.GetValueOrDefault(x.Id), x.PaymentVerifiedAtUtc));
    }
}
