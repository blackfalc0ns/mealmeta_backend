using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Finance.Governance;

public sealed record FinanceRuleListItem(
    Guid Id, string RuleKey, string VersionCode, Guid LegalEntityId, RuleScopeType ScopeType,
    Guid? ScopeEntityId, RuleApprovalStatus Status, DateTime EffectiveFromUtc, DateTime? EffectiveToUtc,
    string? Reason, string? PayloadHash, string? SubmittedContentHash, int RowVersion);

public sealed record FinanceRuleDetails(
    Guid Id, string RuleKey, string VersionCode, Guid LegalEntityId, RuleScopeType ScopeType,
    Guid? ScopeEntityId, RuleApprovalStatus Status, DateTime EffectiveFromUtc, DateTime? EffectiveToUtc,
    int SchemaVersion, string PayloadJson, string? Reason, Guid? PreviousVersionId, Guid? OwnerUserId,
    Guid? CreatedByUserId, string? PayloadHash, string? SubmittedContentHash, int RowVersion,
    DateTime? SubmittedAtUtc, DateTime? ApprovedAtUtc, DateTime? RejectedAtUtc, Guid? CheckerUserId);

public sealed record FinanceLegalEntityOption(Guid Id, string LegalNameEn, string LegalNameAr, string Currency);
public sealed record FinanceReportingProfileOption(Guid LegalEntityId, string Code, string ReportType, ReportingProfileStatus Status);
public sealed record FinanceRuleSimulation(bool IsValid, IReadOnlyList<string> Issues, string CanonicalPayloadHash);

public interface IFinanceGovernanceQueries
{
    Task<IReadOnlyList<FinanceRuleListItem>> ListRulesAsync(Guid? legalEntityId, RuleApprovalStatus? status, int take, CancellationToken cancellationToken = default);
    Task<Result<FinanceRuleDetails>> GetRuleAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinanceLegalEntityOption>> GetLegalEntitiesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FinanceReportingProfileOption>> GetReportingProfilesAsync(Guid? legalEntityId, CancellationToken cancellationToken = default);
    Task<Result<FinanceRuleSimulation>> SimulateAsync(FinancialRuleCreateRequest request, CancellationToken cancellationToken = default);
}

public sealed class FinanceGovernanceQueries : IFinanceGovernanceQueries
{
    private readonly IAppDbContext _db;
    private readonly FinancialRulePayloadValidator _validator = new();

    public FinanceGovernanceQueries(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<FinanceRuleListItem>> ListRulesAsync(Guid? legalEntityId, RuleApprovalStatus? status, int take, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        var query = _db.RuleVersions.AsNoTracking().Where(x => x.LegalEntityId != Guid.Empty);
        if (legalEntityId.HasValue) query = query.Where(x => x.LegalEntityId == legalEntityId.Value);
        if (status.HasValue) query = query.Where(x => x.ApprovalStatus == status.Value);
        return await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Take(take)
            .Select(x => new FinanceRuleListItem(x.Id, x.RuleKey, x.VersionCode, x.LegalEntityId, x.ScopeType,
                x.ScopeEntityId, x.ApprovalStatus, x.EffectiveFromUtc, x.EffectiveToUtc, x.Reason,
                x.PayloadHash, x.SubmittedContentHash, x.RowVersion)).ToListAsync(cancellationToken);
    }

    public async Task<Result<FinanceRuleDetails>> GetRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _db.RuleVersions.AsNoTracking().Where(x => x.Id == id && x.LegalEntityId != Guid.Empty)
            .Select(x => new FinanceRuleDetails(x.Id, x.RuleKey, x.VersionCode, x.LegalEntityId, x.ScopeType,
                x.ScopeEntityId, x.ApprovalStatus, x.EffectiveFromUtc, x.EffectiveToUtc, x.SchemaVersion,
                x.PayloadJson, x.Reason, x.PreviousVersionId, x.OwnerUserId, x.CreatedByUserId, x.PayloadHash,
                x.SubmittedContentHash, x.RowVersion, x.SubmittedAtUtc, x.ApprovedAtUtc, x.RejectedAtUtc, x.CheckerUserId))
            .SingleOrDefaultAsync(cancellationToken);
        return row is null
            ? Result.Failure<FinanceRuleDetails>(Error.NotFound("finance.rule_version_not_found"))
            : Result.Success(row);
    }

    public async Task<IReadOnlyList<FinanceLegalEntityOption>> GetLegalEntitiesAsync(CancellationToken cancellationToken = default) =>
        await _db.LegalEntities.AsNoTracking().Where(x => x.Status == LegalEntityStatus.Active)
            .OrderBy(x => x.LegalNameEn).Take(200)
            .Select(x => new FinanceLegalEntityOption(x.Id, x.LegalNameEn, x.LegalNameAr, x.Currency)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FinanceReportingProfileOption>> GetReportingProfilesAsync(Guid? legalEntityId, CancellationToken cancellationToken = default)
    {
        var query = _db.ReportingProfiles.AsNoTracking();
        if (legalEntityId.HasValue) query = query.Where(x => x.LegalEntityId == legalEntityId.Value);
        return await query.OrderBy(x => x.LegalEntityId).ThenBy(x => x.Code).ThenBy(x => x.ReportType).Take(500)
            .Select(x => new FinanceReportingProfileOption(x.LegalEntityId, x.Code, x.ReportType, x.Status)).ToListAsync(cancellationToken);
    }

    public async Task<Result<FinanceRuleSimulation>> SimulateAsync(FinancialRuleCreateRequest request, CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();
        if (request.LegalEntityId == Guid.Empty || !await _db.LegalEntities.AsNoTracking()
                .AnyAsync(x => x.Id == request.LegalEntityId && x.Status == LegalEntityStatus.Active, cancellationToken))
            issues.Add("finance.rule_legal_entity_inactive_or_missing");
        if (!Enum.IsDefined(request.ScopeType) || (request.ScopeType != RuleScopeType.Global && (!request.ScopeEntityId.HasValue || request.ScopeEntityId.Value == Guid.Empty)))
            issues.Add("finance.rule_scope_invalid");
        if (request.EffectiveFromUtc.Kind != DateTimeKind.Utc || request.EffectiveFromUtc.Ticks % 10 != 0
            || request.EffectiveToUtc is { Kind: not DateTimeKind.Utc }
            || request.EffectiveToUtc is { Ticks: var endTicks } && endTicks % 10 != 0
            || request.EffectiveToUtc is { } end && end <= request.EffectiveFromUtc)
            issues.Add("finance.rule_window_invalid");
        if (string.IsNullOrWhiteSpace(request.RuleKey) || request.RuleKey.Length > 100
            || string.IsNullOrWhiteSpace(request.VersionCode) || request.VersionCode.Length > 50
            || request.Reason?.Length > 1000)
            issues.Add("finance.rule_identity_required");
        var error = _validator.Validate(request.RuleKey, request.SchemaVersion, request.PayloadJson);
        if (error is not null) issues.Add(error.Code);
        if (issues.Count > 0) return Result.Success(new FinanceRuleSimulation(false, issues.Distinct().ToArray(), string.Empty));

        var normalized = RuleVersionScope.Normalize(request.ScopeType, request.ScopeEntityId);
        var occupyingStatuses = new[] { RuleApprovalStatus.Approved, RuleApprovalStatus.Active, RuleApprovalStatus.Superseded };
        var overlaps = await _db.RuleVersions.AsNoTracking().AnyAsync(x => x.LegalEntityId == request.LegalEntityId
            && x.RuleKey == request.RuleKey && x.ScopeType == request.ScopeType && x.NormalizedScopeId == normalized
            && occupyingStatuses.Contains(x.ApprovalStatus)
            && (request.EffectiveToUtc == null || x.EffectiveFromUtc < request.EffectiveToUtc)
            && (x.EffectiveToUtc == null || request.EffectiveFromUtc < x.EffectiveToUtc), cancellationToken);
        if (overlaps) return Result.Success(new FinanceRuleSimulation(false, ["finance.rule_scope_overlap"], string.Empty));
        try
        {
            return Result.Success(new FinanceRuleSimulation(true, [], RuleVersionPayloadHash.Compute(request.PayloadJson)));
        }
        catch (System.Text.Json.JsonException)
        {
            return Result.Success(new FinanceRuleSimulation(false, ["finance.rule_payload_invalid"], string.Empty));
        }
    }
}
