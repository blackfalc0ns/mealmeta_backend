using System.Text.Json;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public sealed record SubscriptionFreezePolicyDocument(
    int DefaultDurationDays = 7,
    int StartDelayDays = 3,
    bool ExtendSubscriptionEnd = true,
    int MinDays = 1,
    int MaxOccurrences = 1000,
    int MinGapDays = 0,
    int? MaxTotalDays = null,
    bool Enabled = true);

public static class SubscriptionFreezePolicyDefaults
{
    public const string Key = "programs_subscriptions.freeze_policy";
    public static readonly SubscriptionFreezePolicyDocument Document = new();
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static SubscriptionFreezePolicyDocument Deserialize(string? json)
    {
        try { return JsonSerializer.Deserialize<SubscriptionFreezePolicyDocument>(json ?? string.Empty, JsonOptions) ?? Document; }
        catch (JsonException) { return Document; }
    }

    public static string? Validate(SubscriptionFreezePolicyDocument document) =>
        document.DefaultDurationDays is < 1 or > 30 ? "Default freeze duration must be between 1 and 30 days."
        : document.StartDelayDays is < 0 or > 14 ? "Freeze start delay must be between 0 and 14 days."
        : null;

    public static async Task<SubscriptionFreezePolicyDocument> ReadAsync(MealMate.Application.Common.Abstractions.IAppDbContext db, CancellationToken ct = default)
    {
        var policy = (await MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyRules.ReadAsync(db, ct)).Freeze;
        return new(policy.MaxDays, policy.LeadDaysNotice, policy.ExtendSubscriptionEnd, policy.MinDays, policy.MaxOccurrences, policy.MinGapDays, policy.MaxTotalDays, policy.Enabled);
    }

    public static async Task<string?> ValidateRequestAsync(MealMate.Application.Common.Abstractions.IAppDbContext db,
        Guid subscriptionId, DateOnly start, DateOnly end, DateOnly today, SubscriptionFreezePolicyDocument policy,
        CancellationToken ct, Guid? excludedRequest = null)
    {
        if (!policy.Enabled) return "Subscription freezing is disabled.";
        var days = end.DayNumber - start.DayNumber + 1;
        if (start < today.AddDays(policy.StartDelayDays) || end < start) return "Freeze start is before the required notice or the date range is invalid.";
        if (days < policy.MinDays || days > policy.DefaultDurationDays) return $"Freeze duration must be between {policy.MinDays} and {policy.DefaultDurationDays} days.";
        var history = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.SubscriptionFreezeRequests.Where(x => x.SubscriptionId == subscriptionId && x.Id != excludedRequest &&
            (x.Status == MealMate.Domain.Enums.VerificationStatus.Approved || x.Status == MealMate.Domain.Enums.VerificationStatus.Submitted)), ct);
        if (history.Count >= policy.MaxOccurrences) return "Freeze occurrence allowance has been exhausted.";
        if (policy.MaxTotalDays is { } total && history.Sum(x => x.FreezeDays) + days > total) return "Total freeze day allowance has been exhausted.";
        if (history.Any(x => start.DayNumber <= x.FreezeEndDate.DayNumber + policy.MinGapDays && end.DayNumber >= x.FreezeStartDate.DayNumber - policy.MinGapDays)) return "Freeze overlaps another request or violates the minimum gap.";
        return null;
    }
}
