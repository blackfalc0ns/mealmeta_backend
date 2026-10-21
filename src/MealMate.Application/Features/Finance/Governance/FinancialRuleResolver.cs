using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Finance.Governance;

public enum RuleResolutionStatus : byte
{
    Resolved = 0,
    Missing = 1,
    Conflict = 2,
}

public sealed record RuleScopeQuery(Guid LegalEntityId, string RuleKey, RuleScopeType ScopeType, Guid? ScopeEntityId)
{
    public string NormalizedScopeId => RuleVersionScope.Normalize(ScopeType, ScopeEntityId);
}

public sealed record RuleResolution(
    Guid VersionId,
    string VersionCode,
    string RuleKey,
    Guid LegalEntityId,
    RuleScopeType ScopeType,
    string Scope,
    string PayloadHash,
    int SchemaVersion,
    string PayloadJson,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc);

public sealed record RuleResolutionResult(
    RuleResolutionStatus Status,
    RuleResolution? Resolution,
    IReadOnlyList<Guid> ConflictVersionIds)
{
    public static RuleResolutionResult Resolved(RuleResolution resolution) =>
        new(RuleResolutionStatus.Resolved, resolution, []);

    public static RuleResolutionResult Missing() =>
        new(RuleResolutionStatus.Missing, null, []);

    public static RuleResolutionResult Conflict(IReadOnlyList<Guid> conflictVersionIds) =>
        new(RuleResolutionStatus.Conflict, null, conflictVersionIds);
}

public interface IFinancialRuleResolver
{
    Task<RuleResolutionResult> ResolveEffectiveAsync(RuleScopeQuery scope, DateTime atUtc, CancellationToken cancellationToken = default);

    Task<RuleResolution?> ResolveSavedAsync(Guid ruleVersionId, CancellationToken cancellationToken = default);
}

public sealed class FinancialRuleResolver : IFinancialRuleResolver
{
    private static readonly RuleApprovalStatus[] UsableStatuses =
    [
        RuleApprovalStatus.Approved,
        RuleApprovalStatus.Active,
    ];

    private static readonly RuleApprovalStatus[] SavedSnapshotStatuses =
    [
        RuleApprovalStatus.Approved,
        RuleApprovalStatus.Active,
        RuleApprovalStatus.Superseded,
    ];

    private readonly IAppDbContext _db;

    public FinancialRuleResolver(IAppDbContext db) => _db = db;

    public async Task<RuleResolutionResult> ResolveEffectiveAsync(RuleScopeQuery scope, DateTime atUtc, CancellationToken cancellationToken = default)
    {
        var normalizedScopeId = scope.NormalizedScopeId;
        var rows = await _db.RuleVersions
            .AsNoTracking()
            .Where(x => x.RuleKey == scope.RuleKey
                && x.LegalEntityId == scope.LegalEntityId
                && x.ScopeType == scope.ScopeType
                && x.NormalizedScopeId == normalizedScopeId
                && UsableStatuses.Contains(x.ApprovalStatus)
                && x.EffectiveFromUtc <= atUtc
                && (x.EffectiveToUtc == null || atUtc < x.EffectiveToUtc))
            .OrderBy(x => x.VersionCode)
            .ToListAsync(cancellationToken);

        return rows.Count switch
        {
            0 => RuleResolutionResult.Missing(),
            1 => RuleResolutionResult.Resolved(ToResolution(rows[0])),
            _ => RuleResolutionResult.Conflict(rows.Select(x => x.Id).ToList()),
        };
    }

    public async Task<RuleResolution?> ResolveSavedAsync(Guid ruleVersionId, CancellationToken cancellationToken = default)
    {
        var row = await _db.RuleVersions
            .AsNoTracking()
            .Where(x => x.Id == ruleVersionId && SavedSnapshotStatuses.Contains(x.ApprovalStatus))
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : ToResolution(row);
    }

    private static RuleResolution ToResolution(RuleVersion row) =>
        new(
            row.Id,
            row.VersionCode,
            row.RuleKey,
            row.LegalEntityId,
            row.ScopeType,
            row.NormalizedScopeId,
            row.PayloadHash ?? string.Empty,
            row.SchemaVersion,
            row.PayloadJson,
            row.EffectiveFromUtc,
            row.EffectiveToUtc);
}
