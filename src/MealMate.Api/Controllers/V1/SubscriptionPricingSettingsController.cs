using System.Text.Json;
using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Common.Security;
using MealMate.Domain.AdminDashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/subscription_pricing_settings")]
public sealed class SubscriptionPricingSettingsController : ApiControllerBase
{
    private readonly IAppDbContext db;
    private readonly IAuditLogger audit;

    public SubscriptionPricingSettingsController(IAppDbContext db, IAuditLogger audit)
    {
        this.db = db;
        this.audit = audit;
    }

    [HttpGet]
    [HasPermission(PermissionCodes.ProgramsView)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var row = await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive, cancellationToken);
        return Ok(SubscriptionPricingSettingsReader.Deserialize(row?.ValueJson));
    }

    [HttpGet("tier_labels")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTierLabels(CancellationToken cancellationToken)
    {
        var row = await db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Key == SubscriptionPricingSettingsDefaults.Key && x.IsActive,
                cancellationToken);
        return Ok(SubscriptionPricingSettingsReader.Deserialize(row?.ValueJson).TierLabels);
    }

    [HttpPut]
    [HasPermission(PermissionCodes.ProgramsManage)]
    public async Task<IActionResult> Put([FromBody] SubscriptionPricingSettingsDocument document, CancellationToken cancellationToken)
    {
        var normalized = SubscriptionPricingSettingsReader.Normalize(document);
        var validation = SubscriptionPricingSettingsValidator.Validate(normalized);
        if (validation is not null) return HandleResult(Result.Failure(validation));

        var json = JsonSerializer.Serialize(normalized, SubscriptionPricingSettingsDefaults.JsonOptions);
        var rows = await db.SystemSettings
            .Where(x => x.Key == SubscriptionPricingSettingsDefaults.Key)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            db.SystemSettings.Add(new SystemSetting
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

        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(new AuditEntry(
            Action: "subscription_pricing_settings.updated",
            EntityType: nameof(SystemSetting),
            After: normalized,
            SourceChannel: "admin-api"), cancellationToken);

        return Ok(normalized);
    }
}
