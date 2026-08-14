using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public sealed record AllowedDurationDecision(
    bool IsAllowed,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsCustom,
    bool AllowsFriday,
    int? MinDays,
    int? MaxDays);

public interface IAllowedSubscriptionDurationService
{
    Task<SubscriptionPricingSettingsDocument> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<AllowedDurationDecision> EvaluateAsync(int durationDays, bool includeFriday, CancellationToken cancellationToken = default);
}

public sealed class AllowedSubscriptionDurationService : IAllowedSubscriptionDurationService
{
    private readonly IAppDbContext _db;

    public AllowedSubscriptionDurationService(IAppDbContext db) => _db = db;

    public async Task<SubscriptionPricingSettingsDocument> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var row = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        return SubscriptionPricingSettingsReader.Deserialize(row?.ValueJson);
    }

    public async Task<AllowedDurationDecision> EvaluateAsync(int durationDays, bool includeFriday, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        var activePresets = settings.Durations
            .Where(x => !x.IsCustom && x.IsActive && x.Days is 6 or 12 or 26)
            .ToList();
        var custom = settings.Durations.FirstOrDefault(x => x.IsCustom);

        var matchingPreset = activePresets.FirstOrDefault(x => x.Days == durationDays);
        if (matchingPreset is not null)
        {
            if (includeFriday && !matchingPreset.AllowsFriday)
            {
                return new AllowedDurationDecision(false, "subscription_flow.friday_unavailable",
                    "Friday is not allowed for this duration.", false, false, null, null);
            }

            return new AllowedDurationDecision(true, null, null, false, matchingPreset.AllowsFriday, null, null);
        }

        if (custom is null || !custom.IsActive)
        {
            return new AllowedDurationDecision(false, "subscription_flow.duration_not_available",
                "The selected duration is not available.", false, false, null, null);
        }

        var minDays = custom.MinDays ?? DurationOptionSettings.AbsoluteMinDays;
        var maxDays = custom.MaxDays ?? DurationOptionSettings.AbsoluteMaxDays;
        if (durationDays < minDays || durationDays > maxDays)
        {
            return new AllowedDurationDecision(false, "subscription_flow.duration_not_available",
                $"Custom duration must be between {minDays} and {maxDays} days.", true, custom.AllowsFriday, minDays, maxDays);
        }

        if (includeFriday && !custom.AllowsFriday)
        {
            return new AllowedDurationDecision(false, "subscription_flow.friday_unavailable",
                "Friday is not allowed for custom durations.", true, false, minDays, maxDays);
        }

        return new AllowedDurationDecision(true, null, null, true, custom.AllowsFriday, minDays, maxDays);
    }
}
