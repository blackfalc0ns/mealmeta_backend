using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MealMate.Application.Features.AdminDashboard.OperationalPolicies;

public sealed class OperationalPolicyService : IOperationalPolicyService
{
    public const string SettingKey = "operational_policies.settings";
    public const string CacheKey = "operational_policies.cache";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly OperationalPolicySettings DefaultSettings = new();
    private static readonly TimeZoneInfo KuwaitTimeZone = ResolveKuwaitTimeZone();

    private readonly IAppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider? _clock;

    public OperationalPolicyService(
        IAppDbContext db,
        IMemoryCache cache,
        IAuditLogger audit,
        IDateTimeProvider? clock = null)
    {
        _db = db;
        _cache = cache;
        _audit = audit;
        _clock = clock;
    }

    public async Task<OperationalPolicySettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out OperationalPolicySettings? cached) && cached is not null)
        {
            return cached;
        }

        var setting = await _db.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SettingKey, cancellationToken);

        if (setting is null || string.IsNullOrWhiteSpace(setting.ValueJson))
        {
            _cache.Set(CacheKey, DefaultSettings, TimeSpan.FromMinutes(30));
            return DefaultSettings;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<OperationalPolicySettings>(setting.ValueJson, JsonOptions);
            var effective = parsed ?? DefaultSettings;
            _cache.Set(CacheKey, effective, TimeSpan.FromMinutes(30));
            return effective;
        }
        catch (JsonException)
        {
            _cache.Set(CacheKey, DefaultSettings, TimeSpan.FromMinutes(30));
            return DefaultSettings;
        }
    }

    public async Task<Result<OperationalPolicySettings>> UpdateSettingsAsync(
        OperationalPolicySettings newSettings,
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        if (newSettings.Cutoff.CutoffLeadHours < 1)
        {
            return Result<OperationalPolicySettings>.Failure("Cutoff lead hours must be at least 1 hour.");
        }

        if (newSettings.Cutoff.FixedDailyHourKuwait is < 0 or > 23)
        {
            return Result<OperationalPolicySettings>.Failure("Fixed daily hour must be between 0 and 23.");
        }

        if (newSettings.Cancellation.FeeValue < 0)
        {
            return Result<OperationalPolicySettings>.Failure("Cancellation fee cannot be negative.");
        }

        if (newSettings.Cancellation.FeeType == CancellationFeeType.Percentage && newSettings.Cancellation.FeeValue > 100)
        {
            return Result<OperationalPolicySettings>.Failure("Percentage cancellation fee cannot exceed 100%.");
        }

        var existing = await _db.SystemSettings
            .FirstOrDefaultAsync(x => x.Key == SettingKey, cancellationToken);

        var json = JsonSerializer.Serialize(newSettings, JsonOptions);
        var nowUtc = _clock?.UtcNow ?? DateTime.UtcNow;

        if (existing is null)
        {
            existing = new SystemSetting
            {
                Key = SettingKey,
                ValueJson = json,
                Description = "Operational and financial policy configurations",
                IsActive = true,
                CreatedByUserId = adminUserId,
                CreatedAtUtc = nowUtc
            };
            _db.SystemSettings.Add(existing);
        }
        else
        {
            existing.ValueJson = json;
            existing.IsActive = true;
            existing.ModifiedByUserId = adminUserId;
            existing.ModifiedAtUtc = nowUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _cache.Set(CacheKey, newSettings, TimeSpan.FromMinutes(30));

        await _audit.LogAsync(new AuditEntry(
            Action: "OperationalPolicies.Updated",
            EntityType: nameof(SystemSetting),
            EntityId: existing.Id,
            After: newSettings,
            SourceChannel: "AdminDashboard"
        ), cancellationToken);

        return Result<OperationalPolicySettings>.Success(newSettings);
    }

    public DateTime CalculateSelectionDeadlineUtc(DateOnly serviceDate, OperationalPolicySettings? settings = null)
    {
        settings ??= _cache.TryGetValue(CacheKey, out OperationalPolicySettings? cached) && cached is not null
            ? cached
            : DefaultSettings;

        if (settings.Cutoff.EnableFixedDailyHour)
        {
            var leadDays = Math.Max(1, (int)Math.Ceiling(settings.Cutoff.CutoffLeadHours / 24.0));
            var targetDate = serviceDate.AddDays(-leadDays);
            var targetTime = new TimeOnly(settings.Cutoff.FixedDailyHourKuwait, 0);
            var localDateTime = DateTime.SpecifyKind(targetDate.ToDateTime(targetTime), DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(localDateTime, KuwaitTimeZone);
        }

        var kuwaitMidnightLocal = DateTime.SpecifyKind(serviceDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var kuwaitMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(kuwaitMidnightLocal, KuwaitTimeZone);
        return kuwaitMidnightUtc.AddHours(-settings.Cutoff.CutoffLeadHours);
    }

    private static TimeZoneInfo ResolveKuwaitTimeZone()
    {
        foreach (var id in new[] { "Asia/Kuwait", "Arab Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Kuwait", TimeSpan.FromHours(3), "Kuwait", "Kuwait");
    }
}
