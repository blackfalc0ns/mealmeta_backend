using MealMate.Application.Common.Abstractions;
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
        foreach (var day in mutableDays)
        {
            if (overrides.TryGetValue(TrialPrefix + day.Id, out var json))
            {
                var trial = JsonSerializer.Deserialize<TrialDeadlineOverride>(json, OperationalPolicyRules.JsonOptions)!;
                day.SelectionDeadlineUtc = trial.DeadlineUtc;
            }
            else if (published) day.SelectionDeadlineUtc = policies.CalculateSelectionDeadlineUtc(day.ServiceDate, settings);
        }
    }
}
