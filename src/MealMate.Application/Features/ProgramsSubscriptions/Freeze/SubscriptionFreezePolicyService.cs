using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.AdminDashboard;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Freeze;

public sealed class SubscriptionFreezePolicyService : ISubscriptionFreezePolicyService
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;

    public SubscriptionFreezePolicyService(IAppDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<SubscriptionFreezePolicyDocument>> GetPolicyAsync(CancellationToken ct = default)
    {
        var row = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == SubscriptionFreezePolicyDefaults.Key && x.IsActive, ct);

        return Result.Success(SubscriptionFreezePolicyDefaults.Deserialize(row?.ValueJson));
    }

    public async Task<Result<SubscriptionFreezePolicyDocument>> UpdatePolicyAsync(
        SubscriptionFreezePolicyDocument document,
        CancellationToken ct = default)
    {
        var error = SubscriptionFreezePolicyDefaults.Validate(document);
        if (error is not null)
            return Error.Validation("validation.invalid", error);

        var json = JsonSerializer.Serialize(document, SubscriptionFreezePolicyDefaults.JsonOptions);
        var rows = await _db.SystemSettings
            .Where(x => x.Key == SubscriptionFreezePolicyDefaults.Key)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            _db.SystemSettings.Add(new SystemSetting
            {
                Key = SubscriptionFreezePolicyDefaults.Key,
                ValueJson = json,
                Description = "Subscription freeze policy",
                IsActive = true,
            });
        }
        else
        {
            foreach (var row in rows)
            {
                row.ValueJson = json;
                row.IsActive = true;
                row.ModifiedAtUtc = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry(
            "subscription_freeze_policy.updated",
            nameof(SystemSetting),
            After: document,
            SourceChannel: "admin-api"), ct);

        return Result.Success(document);
    }
}
