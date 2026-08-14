using System.Text.Json;
using System.Text.Json.Serialization;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
using MealMate.Domain.AdminDashboard;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public static class SubscriptionPricingSettingsDefaults
{
    public const string Key = "programs_subscriptions.pricing";

    public static SubscriptionPricingSettingsDocument Create() => new()
    {
        Mode = "global",
        Global = new PricingProfile
        {
            Rate6Days = 30m,
            Rate12Days = 22m,
            Rate26Days = 15m,
            CustomRate = 30m,
            FridayRate = 0m,
            // Zero keeps the guard disabled until an administrator deliberately enables it.
            MaxRestaurantBoxPrice26 = 0m,
        },
        Durations = new List<DurationOptionSettings>
        {
            new() { Id = "DUR-6", Days = 6, NameAr = "6 أيام", NameEn = "6 days", IsCustom = false, IsActive = true, AllowsFriday = true },
            new() { Id = "DUR-12", Days = 12, NameAr = "12 يومًا", NameEn = "12 days", IsCustom = false, IsActive = true, AllowsFriday = true },
            new() { Id = "DUR-26", Days = 26, NameAr = "26 يومًا", NameEn = "26 days", IsCustom = false, IsActive = true, AllowsFriday = true },
            new()
            {
                Id = "DUR-CUSTOM",
                Days = 7,
                NameAr = "مدة مخصصة",
                NameEn = "Custom duration",
                IsCustom = true,
                IsActive = true,
                AllowsFriday = true,
                MinDays = 7,
                MaxDays = 25,
            },
        },
        TierLabels = new TierLabelsSettings { BasicAr = "أساسي", BasicEn = "Basic", PlatinumAr = "بلاتينيوم", PlatinumEn = "Platinum", EliteAr = "إيليت", EliteEn = "Elite" },
    };

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class SubscriptionPricingSettingsDocument
{
    public string Mode { get; set; } = "global";
    public PricingProfile Global { get; set; } = new();
    public Dictionary<Guid, PricingProfile> PerBundle { get; set; } = new();
    public List<DurationOptionSettings> Durations { get; set; } = new();
    public TierLabelsSettings TierLabels { get; set; } = new();
}

public sealed class PricingProfile
{
    /// <summary>
    /// C07: "curve" = interpolate from Rate6Days (Rmax) to Rate26Days (Rmin).
    /// "manual" = discrete Rate6/12/26 + custom fixed/ranges.
    /// </summary>
    public string RateStrategy { get; set; } = "manual";

    public decimal Rate6Days { get; set; }
    public decimal Rate12Days { get; set; }
    public decimal Rate26Days { get; set; }
    public decimal CustomRate { get; set; }
    public string CustomRateMode { get; set; } = "fixed";
    public List<CustomDurationRateRange> CustomRateRanges { get; set; } = new();
    /// <summary>C07 gamma; only used when RateStrategy = curve. Default 1 (linear in transformed space).</summary>
    public decimal CurveGamma { get; set; } = 1m;
    public decimal FridayRate { get; set; }
    /// <summary>
    /// Maximum price a restaurant may submit for a 26-service-day box, in KWD.
    /// Zero means no ceiling. A per-bundle value greater than zero overrides the global value.
    /// </summary>
    public decimal MaxRestaurantBoxPrice26 { get; set; }

    [JsonIgnore]
    public decimal MaxCommissionPct => Rate6Days;

    [JsonIgnore]
    public decimal MinCommissionPct => Rate26Days;
}

public sealed class CustomDurationRateRange
{
    public int FromDays { get; set; }
    public int? ToDays { get; set; }
    public decimal Rate { get; set; }
}

public static class PricingProfileExtensions
{
    public const int CurveDShort = 6;
    public const int CurveDLong = 26;

    public static decimal ResolveRate(this PricingProfile profile, int days)
    {
        if (string.Equals(profile.RateStrategy, "curve", StringComparison.OrdinalIgnoreCase))
            return ResolveCurveRate(profile.Rate6Days, profile.Rate26Days, days, profile.CurveGamma);

        if (days is 6) return profile.Rate6Days;
        if (days is 12) return profile.Rate12Days;
        if (days is 26) return profile.Rate26Days;
        if (profile.CustomRateMode == "ranges")
        {
            var range = profile.CustomRateRanges
                .OrderBy(x => x.FromDays)
                .FirstOrDefault(x => days >= x.FromDays && (!x.ToDays.HasValue || days <= x.ToDays.Value));
            if (range is not null) return range.Rate;
        }
        return profile.CustomRate;
    }

    /// <summary>C07 dynamic duration pricing curve (rates as percent 0–100).</summary>
    public static decimal ResolveCurveRate(decimal rMaxPct, decimal rMinPct, int days, decimal gamma = 1m)
    {
        var d = Math.Max(1, days);
        var rMax = Math.Clamp(rMaxPct, 0m, 100m);
        var rMin = Math.Clamp(rMinPct, 0m, 100m);
        if (rMax < rMin) (rMax, rMin) = (rMin, rMax);
        var g = gamma <= 0m ? 1m : gamma;

        if (d >= CurveDLong) return Math.Round(rMin, 2, MidpointRounding.AwayFromZero);
        if (d <= CurveDShort) return Math.Round(rMax, 2, MidpointRounding.AwayFromZero);

        var fraction = (decimal)Math.Pow((double)(CurveDLong - d) / (double)(CurveDLong - CurveDShort), (double)g);
        var rate = rMin + (rMax - rMin) * fraction;
        return Math.Round(rate, 2, MidpointRounding.AwayFromZero);
    }

    public static void SyncDerivedRatesFromCurve(this PricingProfile profile)
    {
        if (!string.Equals(profile.RateStrategy, "curve", StringComparison.OrdinalIgnoreCase))
            return;
        profile.Rate12Days = ResolveCurveRate(profile.Rate6Days, profile.Rate26Days, 12, profile.CurveGamma);
        profile.CustomRate = ResolveCurveRate(profile.Rate6Days, profile.Rate26Days, 10, profile.CurveGamma);
        profile.CustomRateMode = "fixed";
    }
}

public sealed class DurationOptionSettings
{
    public const string CustomId = "DUR-CUSTOM";
    public const int AbsoluteMinDays = 6;
    public const int AbsoluteMaxDays = 90;

    public string Id { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public int Days { get; set; }
    public bool IsCustom { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowsFriday { get; set; } = true;
    /// <summary>Custom picker lower bound inclusive. Unused for presets.</summary>
    public int? MinDays { get; set; }
    /// <summary>Custom picker upper bound inclusive. Unused for presets.</summary>
    public int? MaxDays { get; set; }
}

public sealed class TierLabelsSettings
{
    public string BasicAr { get; set; } = "أساسي";
    public string BasicEn { get; set; } = "Basic";
    public string PlatinumAr { get; set; } = "بلاتينيوم";
    public string PlatinumEn { get; set; } = "Platinum";
    public string EliteAr { get; set; } = "إيليت";
    public string EliteEn { get; set; } = "Elite";
    public string? BasicLogoStorageKey { get; set; }
    public string? PlatinumLogoStorageKey { get; set; }
    public string? EliteLogoStorageKey { get; set; }
}

public static class SubscriptionPricingSettingsReader
{
    public static SubscriptionPricingSettingsDocument Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return SubscriptionPricingSettingsDefaults.Create();
        try
        {
            var value = JsonSerializer.Deserialize<SubscriptionPricingSettingsDocument>(json, SubscriptionPricingSettingsDefaults.JsonOptions)
                ?? SubscriptionPricingSettingsDefaults.Create();
            return Normalize(value);
        }
        catch (JsonException)
        {
            return SubscriptionPricingSettingsDefaults.Create();
        }
    }

    public static SubscriptionPricingSettingsDocument Normalize(SubscriptionPricingSettingsDocument value)
    {
        var defaults = SubscriptionPricingSettingsDefaults.Create();
        value.Global ??= defaults.Global;
        value.PerBundle ??= new();
        NormalizeProfile(value.Global, defaults.Global);
        foreach (var profile in value.PerBundle.Values) NormalizeProfile(profile, defaults.Global);
        value.Durations ??= defaults.Durations;
        value.TierLabels ??= defaults.TierLabels;
        value.TierLabels.BasicAr = NormalizeLabel(value.TierLabels.BasicAr, defaults.TierLabels.BasicAr);
        value.TierLabels.BasicEn = NormalizeLabel(value.TierLabels.BasicEn, defaults.TierLabels.BasicEn);
        value.TierLabels.PlatinumAr = NormalizeLabel(value.TierLabels.PlatinumAr, defaults.TierLabels.PlatinumAr);
        value.TierLabels.PlatinumEn = NormalizeLabel(value.TierLabels.PlatinumEn, defaults.TierLabels.PlatinumEn);
        value.TierLabels.EliteAr = NormalizeLabel(value.TierLabels.EliteAr, defaults.TierLabels.EliteAr);
        value.TierLabels.EliteEn = NormalizeLabel(value.TierLabels.EliteEn, defaults.TierLabels.EliteEn);
        value.TierLabels.BasicLogoStorageKey = NormalizeLogo(value.TierLabels.BasicLogoStorageKey);
        value.TierLabels.PlatinumLogoStorageKey = NormalizeLogo(value.TierLabels.PlatinumLogoStorageKey);
        value.TierLabels.EliteLogoStorageKey = NormalizeLogo(value.TierLabels.EliteLogoStorageKey);
        var presets = value.Durations
            .Where(x => !x.IsCustom && x.Days is 6 or 12 or 26)
            .GroupBy(x => x.Days)
            .Select(g => g.First())
            .OrderBy(x => x.Days)
            .ToList();
        foreach (var preset in presets)
        {
            preset.MinDays = null;
            preset.MaxDays = null;
            if (string.IsNullOrWhiteSpace(preset.Id))
                preset.Id = preset.Days switch { 6 => "DUR-6", 12 => "DUR-12", _ => "DUR-26" };
        }

        // Keep a single custom picker card. Extra custom rows are dropped.
        var custom = value.Durations.FirstOrDefault(x => x.IsCustom)
            ?? defaults.Durations.First(x => x.IsCustom);
        custom.Id = string.IsNullOrWhiteSpace(custom.Id) ? DurationOptionSettings.CustomId : custom.Id;
        custom.IsCustom = true;
        custom.MinDays = Math.Clamp(custom.MinDays ?? custom.Days, DurationOptionSettings.AbsoluteMinDays, DurationOptionSettings.AbsoluteMaxDays);
        custom.MaxDays = Math.Clamp(custom.MaxDays ?? Math.Max(custom.MinDays.Value, custom.Days), custom.MinDays.Value, DurationOptionSettings.AbsoluteMaxDays);
        custom.Days = custom.MinDays.Value;

        value.Durations = presets.Concat([custom]).ToList();
        if (!value.Durations.Any(x => !x.IsCustom && x.Days == 6)
            || !value.Durations.Any(x => !x.IsCustom && x.Days == 12)
            || !value.Durations.Any(x => !x.IsCustom && x.Days == 26))
        {
            value.Durations = defaults.Durations;
        }
        return value;
    }

    private static string NormalizeLabel(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeLogo(string? key)
    {
        var normalized = CatalogImageStorage.Normalize(key);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        return CatalogImageStorage.HasAllowedPrefix(normalized, CatalogImageStorage.TierPrefix)
            ? normalized
            : null;
    }

    private static void NormalizeProfile(PricingProfile profile, PricingProfile fallback)
    {
        profile.RateStrategy = profile.RateStrategy is "curve" or "manual" ? profile.RateStrategy : "manual";
        profile.CurveGamma = profile.CurveGamma <= 0m ? 1m : Math.Clamp(profile.CurveGamma, 0.1m, 5m);
        profile.CustomRateMode = profile.CustomRateMode is "fixed" or "ranges" ? profile.CustomRateMode : "fixed";
        profile.CustomRateRanges ??= new();
        profile.CustomRateRanges = profile.CustomRateRanges
            .Where(x => x.FromDays >= 6 && (!x.ToDays.HasValue || x.ToDays.Value >= x.FromDays))
            .OrderBy(x => x.FromDays)
            .ToList();
        profile.MaxRestaurantBoxPrice26 = Math.Max(0m, profile.MaxRestaurantBoxPrice26);
        profile.SyncDerivedRatesFromCurve();
    }
}

public static class SubscriptionPricingSettingsValidator
{
    public static Error? Validate(SubscriptionPricingSettingsDocument value)
    {
        if (value.Mode is not ("global" or "per_bundle"))
            return Error.Validation("pricing_settings.mode_invalid", "Pricing mode must be global or per_bundle.");

        var profileError = ValidateProfile(value.Global, "global");
        if (profileError is not null) return profileError;
        foreach (var profile in value.PerBundle)
        {
            profileError = ValidateProfile(profile.Value, $"bundle:{profile.Key}");
            if (profileError is not null) return profileError;
        }

        if (!value.Durations.Any(x => !x.IsCustom && x.Days == 6)
            || !value.Durations.Any(x => !x.IsCustom && x.Days == 12)
            || !value.Durations.Any(x => !x.IsCustom && x.Days == 26))
            return Error.Validation("pricing_settings.required_durations", "Durations 6, 12 and 26 are required.");

        if (value.Durations.Count(x => x.IsCustom) > 1)
            return Error.Validation("pricing_settings.custom_duration_unique", "Only one custom duration picker is allowed.");

        var custom = value.Durations.FirstOrDefault(x => x.IsCustom);
        if (custom is not null)
        {
            var minDays = custom.MinDays ?? custom.Days;
            var maxDays = custom.MaxDays ?? custom.Days;
            if (minDays < DurationOptionSettings.AbsoluteMinDays)
                return Error.Validation("pricing_settings.duration_minimum", "Custom duration min days must be at least 6.");
            if (maxDays > DurationOptionSettings.AbsoluteMaxDays)
                return Error.Validation("pricing_settings.duration_maximum", "Custom duration max days cannot exceed 90.");
            if (maxDays < minDays)
                return Error.Validation("pricing_settings.custom_duration_range", "Custom duration max days must be greater than or equal to min days.");
        }

        return null;
    }

    private static Error? ValidateProfile(PricingProfile profile, string scope)
    {
        if (profile.RateStrategy is not ("curve" or "manual"))
            return Error.Validation("pricing_settings.rate_strategy", $"Pricing rate strategy for {scope} must be curve or manual.");

        var rates = new[] { profile.Rate6Days, profile.Rate12Days, profile.Rate26Days, profile.CustomRate, profile.FridayRate };
        if (rates.Any(x => x is < 0 or > 100))
            return Error.Validation("pricing_settings.rate_range", $"Pricing rates for {scope} must be between 0 and 100 percent.");
        if (profile.Rate6Days < profile.Rate26Days)
            return Error.Validation("pricing_settings.rate_order", "The short-duration (6-day) rate must be greater than or equal to the long-duration (26-day) rate.");
        if (profile.RateStrategy == "manual" && (profile.Rate6Days < profile.Rate12Days || profile.Rate12Days < profile.Rate26Days))
            return Error.Validation("pricing_settings.rate_order", "The 6-day rate must be greater than the 12-day rate, which must be greater than the 26-day rate.");
        if (profile.CurveGamma is < 0.1m or > 5m)
            return Error.Validation("pricing_settings.curve_gamma", "Curve gamma must be between 0.1 and 5.");
        if (profile.CustomRateMode is not ("fixed" or "ranges"))
            return Error.Validation("pricing_settings.custom_rate_mode", "Custom duration rate mode must be fixed or ranges.");
        if (profile.CustomRateRanges.Any(x => x.FromDays < 6 || (x.ToDays.HasValue && x.ToDays.Value < x.FromDays) || x.Rate is < 0 or > 100))
            return Error.Validation("pricing_settings.custom_rate_ranges", "Custom duration ranges are invalid.");
        if (profile.MaxRestaurantBoxPrice26 is < 0 or > 10000)
            return Error.Validation("pricing_settings.max_restaurant_box_price", "The restaurant box price ceiling must be between 0 and 10,000 KWD.");
        return null;
    }
}
