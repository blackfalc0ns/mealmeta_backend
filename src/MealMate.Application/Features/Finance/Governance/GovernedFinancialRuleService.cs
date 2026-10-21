using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Finance.Governance;

public sealed record FinancialRuleCreateRequest(
    Guid LegalEntityId,
    string RuleKey,
    string VersionCode,
    RuleScopeType ScopeType,
    Guid? ScopeEntityId,
    int SchemaVersion,
    string PayloadJson,
    string? Reason,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc);

public sealed record FinancialRuleVersionCreated(Guid RuleVersionId, string PayloadHash, string NormalizedScopeId);

public sealed record FinancialRuleSubmitResult(Guid RuleVersionId, string SubmittedPayloadHash, string SubmissionFingerprint, int RowVersion, DateTime SubmittedAtUtc);

public sealed record FinancialRuleApproveResult(Guid RuleVersionId, RuleApprovalStatus Status, string PayloadHash, Guid CheckerUserId, DateTime DecidedAtUtc);

public sealed record FinancialRuleRejectResult(Guid RuleVersionId, RuleApprovalStatus Status, DateTime RejectedAtUtc);

public interface IGovernedFinancialRuleService
{
    Task<Result<FinancialRuleVersionCreated>> CreateDraftAsync(FinancialRuleCreateRequest request, CancellationToken cancellationToken = default);

    Task<Result<FinancialRuleSubmitResult>> SubmitForApprovalAsync(Guid ruleVersionId, CancellationToken cancellationToken = default);

    Task<Result<FinancialRuleApproveResult>> ApproveAsync(Guid ruleVersionId, string expectedFingerprint, int expectedRowVersion, CancellationToken cancellationToken = default);

    Task<Result<FinancialRuleRejectResult>> RejectAsync(Guid ruleVersionId, string expectedFingerprint, int expectedRowVersion, string reason, CancellationToken cancellationToken = default);
}

public sealed class GovernedFinancialRuleService : IGovernedFinancialRuleService
{
    private static readonly RuleApprovalStatus[] OccupyingStatuses =
    [
        RuleApprovalStatus.Approved,
        RuleApprovalStatus.Active,
        RuleApprovalStatus.Superseded,
    ];

    private const string DuplicateVersionIndexName = "IX_RuleVersions_RuleKey_VersionCode";

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IApplicationTransactionExecutor _txExecutor;
    private readonly IApplicationLockManager _lockManager;
    private readonly FinancialRulePayloadValidator _payloadValidator = new();

    public GovernedFinancialRuleService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IApplicationTransactionExecutor txExecutor,
        IApplicationLockManager lockManager)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _txExecutor = txExecutor;
        _lockManager = lockManager;
    }

    public Task<Result<FinancialRuleVersionCreated>> CreateDraftAsync(FinancialRuleCreateRequest request, CancellationToken cancellationToken = default) =>
        _txExecutor.ExecuteResultAsync(async ct =>
        {
            var identityError = RequireIdentity();
            if (identityError is not null)
            {
                return Result.Failure<FinancialRuleVersionCreated>(identityError);
            }

            if (string.IsNullOrWhiteSpace(request.RuleKey) || request.RuleKey.Length > 100
                || string.IsNullOrWhiteSpace(request.VersionCode) || request.VersionCode.Length > 50
                || request.Reason?.Length > 1000)
            {
                return Result.Failure<FinancialRuleVersionCreated>(Error.Validation("finance.rule_identity_required"));
            }

            if (request.LegalEntityId == Guid.Empty)
            {
                return Result.Failure<FinancialRuleVersionCreated>(Error.Validation("finance.rule_legal_entity_missing"));
            }

            var legalEntityIsActive = await _db.LegalEntities.AsNoTracking()
                .AnyAsync(x => x.Id == request.LegalEntityId && x.Status == LegalEntityStatus.Active, ct);
            if (!legalEntityIsActive)
            {
                return Result.Failure<FinancialRuleVersionCreated>(Error.Validation("finance.rule_legal_entity_inactive_or_missing"));
            }

            if (!Enum.IsDefined(request.ScopeType))
            {
                return Result.Failure<FinancialRuleVersionCreated>(Error.Validation("finance.rule_scope_invalid"));
            }

            string normalizedScopeId;
            if (request.ScopeType != RuleScopeType.Global && request.ScopeEntityId == Guid.Empty)
            {
                return Result.Failure<FinancialRuleVersionCreated>(Error.Validation("finance.rule_scope_invalid"));
            }
            try
            {
                normalizedScopeId = RuleVersionScope.Normalize(request.ScopeType, request.ScopeEntityId);
            }
            catch (ArgumentException)
            {
                return Result.Failure<FinancialRuleVersionCreated>(Error.Validation("finance.rule_scope_invalid"));
            }

            var payloadError = _payloadValidator.Validate(request.RuleKey, request.SchemaVersion, request.PayloadJson);
            if (payloadError is not null)
            {
                return Result.Failure<FinancialRuleVersionCreated>(payloadError);
            }

            if (request.EffectiveFromUtc.Kind != DateTimeKind.Utc
                || request.EffectiveFromUtc.Ticks % 10 != 0
                || request.EffectiveToUtc is { Kind: not DateTimeKind.Utc }
                || request.EffectiveToUtc is { Ticks: var endTicks } && endTicks % 10 != 0
                || request.EffectiveToUtc is { } effectiveTo && effectiveTo <= request.EffectiveFromUtc)
            {
                return Result.Failure<FinancialRuleVersionCreated>(Error.Validation("finance.rule_window_invalid"));
            }

            await _lockManager.LockFinancialRuleScopeAsync(
                request.LegalEntityId, request.RuleKey, request.ScopeType, request.ScopeEntityId, ct);

            var duplicateVersionExists = await _db.RuleVersions.AsNoTracking()
                .AnyAsync(x => x.RuleKey == request.RuleKey && x.VersionCode == request.VersionCode, ct);
            if (duplicateVersionExists)
            {
                return Result.Failure<FinancialRuleVersionCreated>(Error.Conflict("finance.duplicate_version_code"));
            }

            var now = _clock.UtcNow;
            var payloadHash = RuleVersionPayloadHash.Compute(request.PayloadJson);
            var row = new RuleVersion
            {
                RuleKey = request.RuleKey,
                VersionCode = request.VersionCode,
                ScopeType = request.ScopeType,
                ScopeEntityId = request.ScopeType == RuleScopeType.Global ? null : request.ScopeEntityId,
                ApprovalStatus = RuleApprovalStatus.Draft,
                EffectiveFromUtc = request.EffectiveFromUtc,
                EffectiveToUtc = request.EffectiveToUtc,
                LegalEntityId = request.LegalEntityId,
                NormalizedScopeId = normalizedScopeId,
                SchemaVersion = request.SchemaVersion,
                PayloadHash = payloadHash,
                PayloadJson = request.PayloadJson,
                Reason = request.Reason,
                OwnerUserId = _currentUser.UserId,
                CreatedByUserId = _currentUser.UserId,
            };

            _db.RuleVersions.Add(row);
            _db.FinancialAuditTrails.Add(new FinancialAuditTrail
            {
                Action = "FinancialRuleDraftCreated",
                EntityType = "RuleVersion",
                EntityId = row.Id,
                ActorUserId = _currentUser.UserId,
                OccurredAtUtc = now,
                AfterJson = JsonSerializer.Serialize(new
                {
                    RuleKey = row.RuleKey,
                    VersionCode = row.VersionCode,
                    Scope = normalizedScopeId,
                    row.PayloadHash,
                }),
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains(DuplicateVersionIndexName) == true)
            {
                return Result.Failure<FinancialRuleVersionCreated>(Error.Conflict("finance.duplicate_version_code"));
            }

            return Result.Success(new FinancialRuleVersionCreated(row.Id, payloadHash, normalizedScopeId));
        }, cancellationToken);

    public Task<Result<FinancialRuleSubmitResult>> SubmitForApprovalAsync(Guid ruleVersionId, CancellationToken cancellationToken = default) =>
        _txExecutor.ExecuteResultAsync(async ct =>
        {
            var identityError = RequireIdentity();
            if (identityError is not null)
            {
                return Result.Failure<FinancialRuleSubmitResult>(identityError);
            }

            var row = await _db.RuleVersions
                .SingleOrDefaultAsync(x => x.Id == ruleVersionId, ct);
            if (row is null)
            {
                return Result.Failure<FinancialRuleSubmitResult>(Error.NotFound("finance.rule_version_not_found"));
            }

            if (row.CreatedByUserId is null || row.CreatedByUserId != _currentUser.UserId)
            {
                return Result.Failure<FinancialRuleSubmitResult>(Error.Forbidden("finance.creator_only_submit"));
            }

            if (row.ApprovalStatus == RuleApprovalStatus.PendingApproval)
            {
                var now = _clock.UtcNow;
                return Result.Success(new FinancialRuleSubmitResult(row.Id, row.SubmittedPayloadHash ?? row.PayloadHash ?? string.Empty, row.SubmittedContentHash ?? string.Empty, row.RowVersion, row.SubmittedAtUtc ?? now));
            }

            if (row.ApprovalStatus != RuleApprovalStatus.Draft)
            {
                return Result.Failure<FinancialRuleSubmitResult>(Error.Conflict("finance.state_conflict"));
            }

            var normalizedNow = _clock.UtcNow;
            var beforeStatus = row.ApprovalStatus;
            row.PayloadHash = RuleVersionPayloadHash.Compute(row.PayloadJson);
            row.SubmittedPayloadHash = row.PayloadHash;
            row.SubmittedAtUtc = normalizedNow;
            row.ApprovalStatus = RuleApprovalStatus.PendingApproval;
            row.RowVersion++;
            row.SubmittedContentHash = RuleVersionPayloadHash.ComputeGovernanceFingerprint(row);

            _db.FinancialAuditTrails.Add(new FinancialAuditTrail
            {
                Action = "FinancialRuleVersionSubmitted",
                EntityType = "RuleVersion",
                EntityId = row.Id,
                ActorUserId = _currentUser.UserId,
                OccurredAtUtc = normalizedNow,
                BeforeJson = JsonSerializer.Serialize(new { ApprovalStatus = beforeStatus.ToString() }),
                AfterJson = JsonSerializer.Serialize(new
                {
                    ApprovalStatus = RuleApprovalStatus.PendingApproval.ToString(),
                    row.PayloadHash,
                    row.SubmittedAtUtc,
                }),
            });

            await _db.SaveChangesAsync(ct);
            return Result.Success(new FinancialRuleSubmitResult(row.Id, row.SubmittedPayloadHash!, row.SubmittedContentHash!, row.RowVersion, row.SubmittedAtUtc!.Value));
        }, cancellationToken);

    public async Task<Result<FinancialRuleApproveResult>> ApproveAsync(Guid ruleVersionId, string expectedFingerprint, int expectedRowVersion, CancellationToken cancellationToken = default)
    {
        var result = await _txExecutor.ExecuteResultAsync(async ct =>
        {
            var identityError = RequireIdentity();
            if (identityError is not null)
            {
                return Result.Failure<FinancialRuleApproveResult>(identityError);
            }

            var scopeRow = await _db.RuleVersions.AsNoTracking()
                .Where(x => x.Id == ruleVersionId)
                .Select(x => new { x.LegalEntityId, x.RuleKey, x.ScopeType, x.ScopeEntityId })
                .SingleOrDefaultAsync(ct);
            if (scopeRow is null)
            {
                return Result.Failure<FinancialRuleApproveResult>(Error.NotFound("finance.rule_version_not_found"));
            }

            await _lockManager.LockFinancialRuleScopeAsync(
                scopeRow.LegalEntityId, scopeRow.RuleKey, scopeRow.ScopeType, scopeRow.ScopeEntityId, ct);

            var row = await _db.RuleVersions.SingleAsync(x => x.Id == ruleVersionId, ct);
            var now = _clock.UtcNow;
            var me = _currentUser.UserId!.Value;

            if (row.ApprovalStatus is RuleApprovalStatus.Approved or RuleApprovalStatus.Active)
            {
                if (row.ApprovalStatus == RuleApprovalStatus.Approved
                    && row.CheckerUserId == me
                    && expectedRowVersion == row.RowVersion - 1
                    && string.Equals(expectedFingerprint, row.SubmittedContentHash, StringComparison.Ordinal)
                    && string.Equals(RuleVersionPayloadHash.ComputeGovernanceFingerprint(row), row.SubmittedContentHash, StringComparison.Ordinal))
                {
                    return Result.Success(ToApproveResult(row));
                }

                return Result.Failure<FinancialRuleApproveResult>(Error.Conflict("finance.state_conflict"));
            }

            if (row.ApprovalStatus != RuleApprovalStatus.PendingApproval)
            {
                return Result.Failure<FinancialRuleApproveResult>(Error.Conflict("finance.state_conflict"));
            }

            if (row.CreatedByUserId is null)
            {
                return Result.Failure<FinancialRuleApproveResult>(Error.Validation("finance.rule_maker_unknown"));
            }

            if (row.CreatedByUserId == me)
            {
                return Result.Failure<FinancialRuleApproveResult>(Error.Forbidden("finance.self_approval"));
            }

            var submittedHash = row.SubmittedPayloadHash;
            var submittedContentHash = row.SubmittedContentHash;
            if (string.IsNullOrEmpty(submittedHash)
                || RuleVersionPayloadHash.Compute(row.PayloadJson) != submittedHash
                || string.IsNullOrEmpty(submittedContentHash)
                || !string.Equals(expectedFingerprint, submittedContentHash, StringComparison.Ordinal)
                || expectedRowVersion != row.RowVersion
                || !string.Equals(RuleVersionPayloadHash.ComputeGovernanceFingerprint(row), submittedContentHash, StringComparison.Ordinal))
            {
                return Result.Failure<FinancialRuleApproveResult>(Error.Conflict("finance.stale_payload"));
            }

            var conflictingCount = await _db.RuleVersions.AsNoTracking()
                .Where(x => x.Id != row.Id
                    && x.RuleKey == row.RuleKey
                    && x.LegalEntityId == row.LegalEntityId
                    && x.ScopeType == row.ScopeType
                    && x.NormalizedScopeId == row.NormalizedScopeId
                    && OccupyingStatuses.Contains(x.ApprovalStatus)
                    && (row.EffectiveToUtc == null || x.EffectiveFromUtc < row.EffectiveToUtc)
                    && (x.EffectiveToUtc == null || row.EffectiveFromUtc < x.EffectiveToUtc))
                .CountAsync(ct);
            if (conflictingCount > 0)
            {
                return Result.Failure<FinancialRuleApproveResult>(Error.ConflictWithMetadata("finance.rule_scope_overlap",
                    new Dictionary<string, object?> { ["ConflictingVersionCount"] = conflictingCount }));
            }

            var beforeStatus = row.ApprovalStatus;
            row.ApprovalStatus = RuleApprovalStatus.Approved;
            row.ApprovedAtUtc = now;
            row.CheckerUserId = me;
            row.RowVersion++;

            _db.FinancialAuditTrails.Add(new FinancialAuditTrail
            {
                Action = "FinancialRuleVersionApproved",
                EntityType = "RuleVersion",
                EntityId = row.Id,
                ActorUserId = me,
                OccurredAtUtc = now,
                BeforeJson = JsonSerializer.Serialize(new { ApprovalStatus = beforeStatus.ToString() }),
                AfterJson = JsonSerializer.Serialize(new
                {
                    ApprovalStatus = RuleApprovalStatus.Approved.ToString(),
                    row.CheckerUserId,
                    row.ApprovedAtUtc,
                    row.PayloadHash,
                    row.SubmittedPayloadHash,
                }),
            });

            _db.OutboxMessages.Add(new MealMate.Domain.Common.OutboxMessage
            {
                EventType = "FinancialRuleVersionApproved",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    RuleVersionId = row.Id,
                    row.RuleKey,
                    VersionCode = row.VersionCode,
                    row.LegalEntityId,
                    row.ScopeType,
                    Scope = row.NormalizedScopeId,
                    MakerUserId = row.CreatedByUserId,
                    CheckerUserId = me,
                    row.PayloadHash,
                    row.EffectiveFromUtc,
                    row.EffectiveToUtc,
                    ApprovedAtUtc = now,
                }),
                CreatedAtUtc = now,
                AggregateType = "RuleVersion",
                AggregateId = row.Id.ToString(),
                CorrelationId = $"financial-rule-approved:{row.Id:N}",
                DeduplicationKey = $"financial-rule-approved:{row.Id:N}",
            });

            await _db.SaveChangesAsync(ct);
            return Result.Success(ToApproveResult(row));
        }, cancellationToken);

        if (result.IsFailure && result.Error.Code is "finance.self_approval" or "finance.stale_payload" or "finance.rule_scope_overlap" or "finance.state_conflict")
        {
            await RecordDeniedApprovalAsync(ruleVersionId, result.Error.Code, cancellationToken);
        }

        return result;
    }

    public async Task<Result<FinancialRuleRejectResult>> RejectAsync(Guid ruleVersionId, string expectedFingerprint, int expectedRowVersion, string reason, CancellationToken cancellationToken = default)
    {
        var result = await _txExecutor.ExecuteResultAsync(async ct =>
        {
            var identityError = RequireIdentity();
            if (identityError is not null)
            {
                return Result.Failure<FinancialRuleRejectResult>(identityError);
            }

            var scopeRow = await _db.RuleVersions.AsNoTracking()
                .Where(x => x.Id == ruleVersionId)
                .Select(x => new { x.LegalEntityId, x.RuleKey, x.ScopeType, x.ScopeEntityId })
                .SingleOrDefaultAsync(ct);
            if (scopeRow is null)
            {
                return Result.Failure<FinancialRuleRejectResult>(Error.NotFound("finance.rule_version_not_found"));
            }

            await _lockManager.LockFinancialRuleScopeAsync(
                scopeRow.LegalEntityId, scopeRow.RuleKey, scopeRow.ScopeType, scopeRow.ScopeEntityId, ct);

            var row = await _db.RuleVersions.SingleAsync(x => x.Id == ruleVersionId, ct);
            var now = _clock.UtcNow;
            var me = _currentUser.UserId!.Value;

            if (row.ApprovalStatus == RuleApprovalStatus.Rejected)
            {
                if (row.CheckerUserId == me
                    && expectedRowVersion == row.RowVersion - 1
                    && string.Equals(expectedFingerprint, row.SubmittedContentHash, StringComparison.Ordinal)
                    && string.Equals(RuleVersionPayloadHash.ComputeGovernanceFingerprint(row), row.SubmittedContentHash, StringComparison.Ordinal))
                {
                    return Result.Success(new FinancialRuleRejectResult(row.Id, row.ApprovalStatus, row.RejectedAtUtc ?? now));
                }

                return Result.Failure<FinancialRuleRejectResult>(Error.Conflict("finance.state_conflict"));
            }

            if (row.ApprovalStatus != RuleApprovalStatus.PendingApproval)
            {
                return Result.Failure<FinancialRuleRejectResult>(Error.Conflict("finance.state_conflict"));
            }

            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3 || reason.Length > 1000)
            {
                return Result.Failure<FinancialRuleRejectResult>(Error.Validation("finance.rejection_reason_required"));
            }

            if (row.CreatedByUserId is null)
            {
                return Result.Failure<FinancialRuleRejectResult>(Error.Validation("finance.rule_maker_unknown"));
            }

            if (row.CreatedByUserId == me)
            {
                return Result.Failure<FinancialRuleRejectResult>(Error.Forbidden("finance.self_approval"));
            }

            if (string.IsNullOrEmpty(row.SubmittedContentHash)
                || !string.Equals(expectedFingerprint, row.SubmittedContentHash, StringComparison.Ordinal)
                || expectedRowVersion != row.RowVersion
                || !string.Equals(RuleVersionPayloadHash.ComputeGovernanceFingerprint(row), row.SubmittedContentHash, StringComparison.Ordinal))
            {
                return Result.Failure<FinancialRuleRejectResult>(Error.Conflict("finance.stale_payload"));
            }

            var beforeStatus = row.ApprovalStatus;
            row.ApprovalStatus = RuleApprovalStatus.Rejected;
            row.RejectedAtUtc = now;
            row.CheckerUserId = me;
            row.RowVersion++;

            _db.FinancialAuditTrails.Add(new FinancialAuditTrail
            {
                Action = "FinancialRuleVersionRejected",
                EntityType = "RuleVersion",
                EntityId = row.Id,
                ActorUserId = me,
                OccurredAtUtc = now,
                BeforeJson = JsonSerializer.Serialize(new { ApprovalStatus = beforeStatus.ToString() }),
                AfterJson = JsonSerializer.Serialize(new
                {
                    ApprovalStatus = RuleApprovalStatus.Rejected.ToString(),
                    row.CheckerUserId,
                    row.RejectedAtUtc,
                    Reason = reason.Trim(),
                }),
            });

            await _db.SaveChangesAsync(ct);
            return Result.Success(new FinancialRuleRejectResult(row.Id, row.ApprovalStatus, now));
        }, cancellationToken);

        if (result.IsFailure && result.Error.Code is "finance.self_approval" or "finance.stale_payload" or "finance.state_conflict")
        {
            await RecordDeniedApprovalAsync(ruleVersionId, result.Error.Code, cancellationToken);
        }

        return result;
    }

    private async Task RecordDeniedApprovalAsync(Guid ruleVersionId, string reason, CancellationToken ct)
    {
        var exists = await _db.RuleVersions.AsNoTracking().AnyAsync(x => x.Id == ruleVersionId, ct);
        if (!exists) return;

        _db.FinancialAuditTrails.Add(new FinancialAuditTrail
        {
            Action = "FinancialRuleApprovalDenied",
            EntityType = "RuleVersion",
            EntityId = ruleVersionId,
            ActorUserId = _currentUser.UserId,
            OccurredAtUtc = _clock.UtcNow,
            AfterJson = JsonSerializer.Serialize(new { Reason = reason }),
        });
        await _db.SaveChangesAsync(ct);
    }

    private static FinancialRuleApproveResult ToApproveResult(RuleVersion row) =>
        new(row.Id, row.ApprovalStatus, row.PayloadHash ?? string.Empty, row.CheckerUserId!.Value, (row.ApprovedAtUtc ?? row.RejectedAtUtc)!.Value);

    private Error? RequireIdentity() =>
        _currentUser.UserId is null ? Error.Unauthorized("finance.identity_required") : null;

}
