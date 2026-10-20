using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MealMate.Application.Features.AdminDashboard.OperationalPolicies;

public static class OperationalPolicyRules
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public static string? Validate(OperationalPolicySettings? s)
    {
        if (s?.Cutoff is null || s.Cancellation is null || s.Freeze is null) return "All policy groups are required.";
        if (s.Cutoff.CutoffLeadHours is < 0 or > 168 || s.Cutoff.KitchenConfirmSlaHours is < 0 or > 168) return "Legacy lead hours must be between 0 and 168.";
        if (s.Cutoff.EffectiveLeadMinutes is < 1 or > 10080) return "Cutoff must be between 1 minute and 7 days.";
        if (s.Cutoff.FixedDailyHourKuwait is < 0 or > 23) return "Fixed hour must be between 0 and 23.";
        if (s.Cutoff.EffectiveConfirmationMinutes is < 1 or > 10080) return "Restaurant confirmation SLA must be between 1 minute and 7 days.";
        if (s.Cutoff.FirstDaySelectionWindowHours is < 1 or > 72) return "First-day selection window must be between 1 and 72 hours.";
        if (!Enum.IsDefined(s.Cancellation.FeeType) || s.Cancellation.FeeValue < 0 || s.Cancellation.FeeValue > 1000000 || (s.Cancellation.FeeType == CancellationFeeType.Percentage && s.Cancellation.FeeValue > 100)) return "Cancellation fee is invalid.";
        if (s.Cancellation.KitchenLockedRefundPercentage != 0) return "Committed kitchen days cannot be refunded through this policy.";
        if (!s.Cancellation.RefundToCashWallet) return "Only cash wallet refunds are supported. Bank refunds require a payment-provider refund workflow.";
        if (s.Freeze.LeadDaysNotice is < 0 or > 365 || s.Freeze.MinDays is < 1 or > 365 || s.Freeze.MaxDays < s.Freeze.MinDays || s.Freeze.MaxDays > 365 || s.Freeze.MaxOccurrences is < 0 or > 1000 || s.Freeze.MinGapDays is < 0 or > 365 || s.Freeze.MaxTotalDays is < 0 or > 3650) return "Freeze duration, notice, occurrence, gap or total allowance is invalid.";
        if (s.AutoSelection?.MinutesBeforeCutoff is < 0 or > 10080) return "Automatic selection lead must be between 0 minutes and 7 days.";
        if (s.Renewal?.AvailableDaysBeforeEnd is < 0 or > 365) return "Renewal availability must be between 0 and 365 days.";
        if (s.DeliveryDelay?.SevereDelayThresholdMinutes is < 1 or > 240) return "Severe delivery delay threshold must be between 1 and 240 minutes.";
        if (s.ChangeReason?.Length > 1000) return "Change reason must not exceed 1000 characters.";
        return null;
    }

    public static OperationalPolicySettings Parse(string json)
    {
        var s = JsonSerializer.Deserialize<OperationalPolicySettings>(json, JsonOptions) ?? throw new InvalidOperationException("Stored operational policy is empty.");
        return s with { AutoSelection = s.AutoSelection ?? new(), Renewal = s.Renewal ?? new(), DeliveryDelay = s.DeliveryDelay ?? new() };
    }

    public static async Task<OperationalPolicySettings> ReadAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var json = await db.SystemSettings.AsNoTracking().Where(x => x.Key == OperationalPolicyService.SettingKey && x.IsActive).Select(x => x.ValueJson).FirstOrDefaultAsync(ct);
        if (json is not null) return Parse(json);
        var legacy = await db.SystemSettings.AsNoTracking().Where(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive).Select(x => x.ValueJson).FirstOrDefaultAsync(ct);
        return Defaults(legacy);
    }
    public static OperationalPolicySettings Read(IAppDbContext db)
    {
        var json = db.SystemSettings.AsNoTracking().Where(x => x.Key == OperationalPolicyService.SettingKey && x.IsActive).Select(x => x.ValueJson).FirstOrDefault();
        if (json is not null) return Parse(json);
        return Defaults(db.SystemSettings.AsNoTracking().Where(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive).Select(x => x.ValueJson).FirstOrDefault());
    }
    private static OperationalPolicySettings Defaults(string? legacy)
    {
        // Keep the actual legacy freeze behavior until the administrator publishes a policy.
        var f = SubscriptionFreezePolicyDefaults.Deserialize(legacy);
        return new OperationalPolicySettings() with { Freeze = new FreezePolicySettings(f.StartDelayDays, 1, f.DefaultDurationDays, 1000, f.ExtendSubscriptionEnd), AutoSelection = new(), Renewal = new(), DeliveryDelay = new() };
    }
    public static bool IsRenewalAvailable(OperationalPolicySettings settings, DateOnly endDate, DateOnly today)
        => settings.Renewal?.Enabled != false && (settings.Renewal?.AvailableDaysBeforeEnd is not { } days || today >= endDate.AddDays(-days));
}
