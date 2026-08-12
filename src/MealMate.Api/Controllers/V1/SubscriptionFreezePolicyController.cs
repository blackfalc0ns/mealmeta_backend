using System.Text.Json;
using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.AdminDashboard;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/subscription_freeze_policy")]
public sealed class SubscriptionFreezePolicyController : ApiControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;

    public SubscriptionFreezePolicyController(IAppDbContext db, IAuditLogger audit) => (_db, _audit) = (db, audit);

    [HttpGet]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var row = await _db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive, ct);
        return Ok(SubscriptionFreezePolicyDefaults.Deserialize(row?.ValueJson));
    }

    [HttpPut]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> Put([FromBody] SubscriptionFreezePolicyDocument document, CancellationToken ct)
    {
        var error = SubscriptionFreezePolicyDefaults.Validate(document);
        if (error is not null) return HandleResult(Result.Failure(Error.Validation("validation.invalid", error)));
        var json = JsonSerializer.Serialize(document, SubscriptionFreezePolicyDefaults.JsonOptions);
        var rows = await _db.SystemSettings.Where(x => x.Key == SubscriptionFreezePolicyDefaults.Key).ToListAsync(ct);
        if (rows.Count == 0) _db.SystemSettings.Add(new SystemSetting { Key = SubscriptionFreezePolicyDefaults.Key, ValueJson = json, Description = "Subscription freeze policy", IsActive = true });
        else foreach (var row in rows) { row.ValueJson = json; row.IsActive = true; row.ModifiedAtUtc = DateTime.UtcNow; }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(new AuditEntry("subscription_freeze_policy.updated", nameof(SystemSetting), After: document, SourceChannel: "admin-api"), ct);
        return Ok(document);
    }
}
