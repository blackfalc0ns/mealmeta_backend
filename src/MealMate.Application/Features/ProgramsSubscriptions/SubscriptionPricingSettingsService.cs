using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
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

        var rows = await _db.SystemSettings
            .Where(x => x.Key == SubscriptionPricingSettingsDefaults.Key)
            .ToListAsync(ct);

        var previous = SubscriptionPricingSettingsReader.Deserialize(rows.FirstOrDefault(x => x.IsActive)?.ValueJson);
        var previouslyActivePackageIds = previous.Family.Packages
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToHashSet();
        var requestedActivePackageIds = normalized.Family.Packages
            .Where(x => x.IsActive)
            .Select(x => x.Id)
            .ToHashSet();
        var packageIdsBecomingUnavailable = previouslyActivePackageIds
            .Except(requestedActivePackageIds)
            .ToList();
        if (packageIdsBecomingUnavailable.Count > 0)
        {
            var affectedCount = await _db.FamilyGroups.AsNoTracking()
                .CountAsync(x => (x.Status == FamilyGroupStatus.Draft || x.Status == FamilyGroupStatus.CheckoutPending)
                    && x.SelectedFamilyPackageId != null
                    && packageIdsBecomingUnavailable.Contains(x.SelectedFamilyPackageId.Value),
                    ct);
            if (affectedCount > 0)
            {
                return new Error(
                    "family.package_in_use",
                    "Package is referenced by {0} open family groups.",
                    ErrorType.Conflict,
                    [affectedCount]);
            }
        }

        var json = JsonSerializer.Serialize(normalized, SubscriptionPricingSettingsDefaults.JsonOptions);

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
