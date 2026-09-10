using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.AdminDashboard;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public sealed class SubscriptionPricingSettingsService : ISubscriptionPricingSettingsService
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;

    public SubscriptionPricingSettingsService(IAppDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<SubscriptionPricingSettingsDocument>> GetSettingsAsync(CancellationToken ct = default)
    {
        var row = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);

        return Result.Success(SubscriptionPricingSettingsReader.Deserialize(row?.ValueJson));
    }

    public async Task<Result<TierLabelsSettings>> GetTierLabelsAsync(CancellationToken ct = default)
    {
        var row = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, ct);

        return Result.Success(SubscriptionPricingSettingsReader.Deserialize(row?.ValueJson).TierLabels);
    }

    public async Task<Result<SubscriptionPricingSettingsDocument>> UpdateSettingsAsync(
        SubscriptionPricingSettingsDocument document,
        CancellationToken ct = default)
    {
        var normalized = SubscriptionPricingSettingsReader.Normalize(document);
        var validation = SubscriptionPricingSettingsValidator.Validate(normalized);
        if (validation is not null) return validation;

        var json = JsonSerializer.Serialize(normalized, SubscriptionPricingSettingsDefaults.JsonOptions);
        var rows = await _db.SystemSettings
            .Where(x => x.Key == SubscriptionPricingSettingsDefaults.Key)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            _db.SystemSettings.Add(new SystemSetting
            {
                Key = SubscriptionPricingSettingsDefaults.Key,
                ValueJson = json,
                Description = "Subscription duration and pricing policy",
                IsActive = true,
            });
        }
        else
        {
            var modifiedAtUtc = DateTime.UtcNow;
            foreach (var row in rows)
            {
                row.ValueJson = json;
                row.ModifiedAtUtc = modifiedAtUtc;
            }

            if (!rows.Any(x => x.IsActive))
            {
                rows[0].IsActive = true;
            }
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry(
            Action: "subscription_pricing_settings.updated",
            EntityType: nameof(SystemSetting),
            After: normalized,
            SourceChannel: "admin-api"), ct);

        return Result.Success(normalized);
    }
}
