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
    public const string HistoryPrefix = "operational_policies.history.";
    private static readonly TimeZoneInfo KuwaitTimeZone = ResolveKuwaitTimeZone();
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider? _clock;
    public OperationalPolicyService(IAppDbContext db, IMemoryCache cache, IAuditLogger audit, IDateTimeProvider? clock = null)
    { _db = db; _audit = audit; _clock = clock; }

    public Task<OperationalPolicySettings> GetSettingsAsync(CancellationToken cancellationToken = default)
        => OperationalPolicyRules.ReadAsync(_db, cancellationToken);

    public async Task<Result<OperationalPolicySettings>> UpdateSettingsAsync(OperationalPolicySettings newSettings, Guid adminUserId, CancellationToken cancellationToken = default)
    {
        var error = OperationalPolicyRules.Validate(newSettings);
        if (error is not null) return Result<OperationalPolicySettings>.Failure(error);
        if (adminUserId == Guid.Empty) return Result<OperationalPolicySettings>.Failure("Authenticated administrator is required.");
        var existing = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Key == SettingKey, cancellationToken);
        if (existing is not null && _db is DbContext context) await context.Entry(existing).ReloadAsync(cancellationToken);
        var before = await GetSettingsAsync(cancellationToken);
        if (newSettings.Version != before.Version) return Result<OperationalPolicySettings>.Failure("Policy version changed. Refresh before publishing.");
        var now = _clock?.UtcNow ?? DateTime.UtcNow;
        var published = newSettings with { Version = before.Version + 1, PublishedBy = adminUserId, PublishedAtUtc = now, ChangeReason = newSettings.ChangeReason?.Trim(), AutoSelection = newSettings.AutoSelection ?? new(), Renewal = newSettings.Renewal ?? new() };
        var publication = new PolicyPublication(published.Version, adminUserId, now, published.ChangeReason ?? "Policy update", before, published);
        if (existing is null)
        {
            existing = new SystemSetting { Key = SettingKey, CreatedByUserId = adminUserId, CreatedAtUtc = now };
            _db.SystemSettings.Add(existing);
        }
        existing.ValueJson = JsonSerializer.Serialize(published, OperationalPolicyRules.JsonOptions);
        existing.IsActive = true;
        existing.Description = "Operational policies";
        existing.ModifiedByUserId = adminUserId;
        existing.ModifiedAtUtc = now;
        var history = new SystemSetting { Key = HistoryPrefix + published.Version.ToString("D20"), ValueJson = JsonSerializer.Serialize(publication, OperationalPolicyRules.JsonOptions), Description = "Immutable policy publication", CreatedByUserId = adminUserId, CreatedAtUtc = now };
        _db.SystemSettings.Add(history);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.InnerException?.GetType().GetProperty("SqlState")?.GetValue(ex.InnerException)?.ToString() == "23505")
        {
            if (_db is DbContext failedContext) { failedContext.Entry(history).State = EntityState.Detached; failedContext.Entry(existing).State = EntityState.Detached; }
            return Result<OperationalPolicySettings>.Failure("Policy publication conflicted with another writer. Refresh before publishing.");
        }
        await _audit.LogAsync(new AuditEntry("OperationalPolicies.Published", nameof(SystemSetting), existing.Id, Before: before, After: published, Reason: published.ChangeReason, SourceChannel: "AdminDashboard"), cancellationToken);
        return Result<OperationalPolicySettings>.Success(published);
    }

    public DateTime CalculateSelectionDeadlineUtc(DateOnly serviceDate, OperationalPolicySettings? settings = null)
    {
        settings ??= OperationalPolicyRules.Read(_db);
        if (settings.Cutoff.EnableFixedDailyHour)
        {
            var leadDays = Math.Max(1, (int)Math.Ceiling(settings.Cutoff.EffectiveLeadMinutes / 1440.0));
            var local = DateTime.SpecifyKind(serviceDate.AddDays(-leadDays).ToDateTime(new TimeOnly(settings.Cutoff.FixedDailyHourKuwait, 0)), DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, KuwaitTimeZone);
        }
        var midnight = DateTime.SpecifyKind(serviceDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(midnight, KuwaitTimeZone).AddMinutes(-settings.Cutoff.EffectiveLeadMinutes);
    }
    public DateTime CalculateFirstDaySelectionDeadlineUtc(DateTime paymentVerifiedAtUtc, DateOnly firstDeliveryDate, OperationalPolicySettings? settings = null)
    {
        settings ??= OperationalPolicyRules.Read(_db);
        var windowEnd = paymentVerifiedAtUtc.AddHours(settings.Cutoff.EffectiveFirstDayWindowHours);
        var deliveryInstant = KuwaitMidnightUtc(firstDeliveryDate);
        // Only a window that still closes before first delivery is valid; a late payment keeps
        // the standard cutoff (already past) so the late-payment path handles dispatch.
        if (windowEnd < deliveryInstant) return windowEnd;
        return CalculateSelectionDeadlineUtc(firstDeliveryDate, settings);
    }

    private static DateTime KuwaitMidnightUtc(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, KuwaitTimeZone);
    }

    private static TimeZoneInfo ResolveKuwaitTimeZone()
    {
        foreach (var id in new[] { "Asia/Kuwait", "Arab Standard Time" })
        { try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { } }
        return TimeZoneInfo.CreateCustomTimeZone("Kuwait", TimeSpan.FromHours(3), "Kuwait", "Kuwait");
    }
}
