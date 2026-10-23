using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.ComplaintsSupport;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport.Timing;

public sealed class SupportTimingPolicyService
{
    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IApplicationTransactionExecutor _transactions;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly IApplicationLockManager _locks;
    private readonly SupportCaseAccess _access;

    public SupportTimingPolicyService(
        IAppDbContext db,
        IUnitOfWork unitOfWork,
        IApplicationTransactionExecutor transactions,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        IApplicationLockManager locks)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _transactions = transactions;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _locks = locks;
        _access = new SupportCaseAccess(db, currentUser);
    }

    private Guid? ActorId => _currentUser.IsAuthenticated ? _currentUser.UserId : null;
    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<Result<SupportTimingPolicyDto?>> GetActivePolicyAsync(CancellationToken ct = default)
    {
        var access = await CheckPolicyReadAccessAsync(ct);
        if (access is { } denied) return denied;
        var policy = await _db.SupportTimingPolicies
            .AsNoTracking()
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.IsActive, ct);

        return Result.Success(policy is null ? null : MapToDto(policy));
    }

    public async Task<Result<IReadOnlyList<SupportTimingPolicyDto>>> ListPoliciesAsync(CancellationToken ct = default)
    {
        var access = await CheckPolicyReadAccessAsync(ct);
        if (access is { } denied) return denied;
        var policies = await _db.SupportTimingPolicies
            .AsNoTracking()
            .Include(p => p.Rules)
            .OrderByDescending(p => p.Version)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<SupportTimingPolicyDto>>(policies.Select(MapToDto).ToList());
    }

    public async Task<Result<SupportTimingPolicyDto>> GetPolicyByIdAsync(Guid id, CancellationToken ct = default)
    {
        var access = await CheckPolicyReadAccessAsync(ct);
        if (access is { } denied) return denied;
        var policy = await _db.SupportTimingPolicies
            .AsNoTracking()
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (policy is null)
            return Error.NotFound(SupportCaseProblemCodes.NotFound);

        return Result.Success(MapToDto(policy));
    }

    public async Task<Result<SupportTimingPolicyDto>> PublishPolicyAsync(
        PublishSupportTimingPolicyRequest request,
        CancellationToken ct = default)
    {
        var actorId = ActorId;
        if (actorId is null)
            return Error.Unauthorized(SupportCaseProblemCodes.AuthenticationRequired);

        var persistedActor = (await _db.GetActiveSupportOperatorCandidatesAsync(ct))
            .SingleOrDefault(candidate => candidate.UserId == actorId.Value);
        if (persistedActor is null || persistedActor.UserType != MealMate.Domain.Enums.UserType.Admin
            || !persistedActor.Roles.Contains(AppRoles.SuperAdmin))
            return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);

        return await _transactions.ExecuteResultAsync(token => PublishWithinTransactionAsync(request, actorId.Value, token), ct);
    }

    private async Task<Result<SupportTimingPolicyDto>> PublishWithinTransactionAsync(
        PublishSupportTimingPolicyRequest request, Guid actorId, CancellationToken ct)
    {
        // EF's execution strategy can rerun this delegate on the same context after rollback.
        // Detach only policy entities so each retry observes committed catalog state.
        foreach (var rule in _db.SupportTimingRules.Local.ToList()) _db.DetachTrackedEntity(rule);
        foreach (var policy in _db.SupportTimingPolicies.Local.ToList()) _db.DetachTrackedEntity(policy);

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200
            || request.Description?.Length > 1000 || string.IsNullOrWhiteSpace(request.PublicationReason)
            || request.PublicationReason.Trim().Length > 1000 || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 128 || request.ExpectedVersion < 0
            || request.Rules is null || request.Rules.Count == 0 || request.Rules.Any(rule => rule is null))
            return Error.Validation(SupportCaseProblemCodes.InvalidRequest);

        foreach (var rule in request.Rules)
        {
            if (!double.IsFinite(rule.FirstResponseMinutes) || !double.IsFinite(rule.ResolutionMinutes)
                || !double.IsFinite(rule.EscalationThresholdMinutes) || rule.FirstResponseMinutes <= 0
                || rule.ResolutionMinutes <= 0 || rule.EscalationThresholdMinutes <= 0
                || rule.FirstResponseMinutes > rule.ResolutionMinutes || rule.Category?.Length > 100
                || rule.EvaluationOrder < 0 || rule.Channel is { } channel && !Enum.IsDefined(channel)
                || rule.Priority is { } priority && !Enum.IsDefined(priority))
                return Error.Validation(SupportCaseProblemCodes.InvalidRequest);
            try
            {
                _ = TimeSpan.FromMinutes(rule.FirstResponseMinutes);
                _ = TimeSpan.FromMinutes(rule.ResolutionMinutes);
                _ = TimeSpan.FromMinutes(rule.EscalationThresholdMinutes);
            }
            catch (OverflowException)
            {
                return Error.Validation(SupportCaseProblemCodes.InvalidRequest);
            }
        }

        await _locks.LockSupportTimingPolicyCatalogAsync(ct);

        var existingWithKey = await _db.SupportTimingPolicies.Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.IdempotencyKey == request.IdempotencyKey.Trim(), ct);
        if (existingWithKey is not null)
            return existingWithKey.PublishedByUserId == actorId
                && (request.ExpectedVersion == 0 || request.ExpectedVersion == existingWithKey.Version)
                && IsSameRequest(existingWithKey, request)
                ? Result.Success(MapToDto(existingWithKey))
                : Error.Conflict(SupportCaseProblemCodes.IdempotencyKeyReused);

        var nextVersion = (await _db.SupportTimingPolicies.MaxAsync(p => (int?)p.Version, ct) ?? 0) + 1;
        if (request.ExpectedVersion > 0 && request.ExpectedVersion != nextVersion)
            return Error.Conflict(SupportCaseProblemCodes.VersionConflict);

        SupportTimingPolicy newPolicy;
        try
        {
            newPolicy = SupportTimingPolicy.Create(nextVersion, request.Name, request.Description, actorId,
                request.PublicationReason, request.IdempotencyKey.Trim(), Now);
            foreach (var rule in request.Rules.OrderBy(r => r.EvaluationOrder))
            {
                newPolicy.AddRule(rule.Channel, rule.Priority, rule.Category,
                    TimeSpan.FromMinutes(rule.FirstResponseMinutes), TimeSpan.FromMinutes(rule.ResolutionMinutes),
                    TimeSpan.FromMinutes(rule.EscalationThresholdMinutes), rule.RequiresImmediateAck, rule.EvaluationOrder);
            }
        }
        catch (ArgumentException)
        {
            return Error.Validation(SupportCaseProblemCodes.InvalidRequest);
        }
        catch (OverflowException)
        {
            return Error.Validation(SupportCaseProblemCodes.InvalidRequest);
        }

        var activePolicies = await _db.SupportTimingPolicies.Where(p => p.IsActive).ToListAsync(ct);
        foreach (var active in activePolicies) active.Deactivate();
        _db.SupportTimingPolicies.Add(newPolicy);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success(MapToDto(newPolicy));
    }

    private static bool IsSameRequest(SupportTimingPolicy existing, PublishSupportTimingPolicyRequest request)
    {
        var same = existing.Name == request.Name.Trim()
            && existing.Description == (string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim())
            && existing.PublicationReason == request.PublicationReason.Trim()
            && existing.Rules.Count == request.Rules.Count;
        if (!same) return false;
        var oldRules = existing.Rules.OrderBy(x => x.EvaluationOrder).ToList();
        var newRules = request.Rules.OrderBy(x => x.EvaluationOrder).ToList();
        for (var i = 0; i < oldRules.Count; i++)
        {
            var oldRule = oldRules[i];
            var newRule = newRules[i];
            if (oldRule.Channel != newRule.Channel || oldRule.Priority != newRule.Priority
                || oldRule.Category != (string.IsNullOrWhiteSpace(newRule.Category) ? null : newRule.Category.Trim())
                || oldRule.FirstResponseDeadline != TimeSpan.FromMinutes(newRule.FirstResponseMinutes)
                || oldRule.ResolutionDeadline != TimeSpan.FromMinutes(newRule.ResolutionMinutes)
                || oldRule.EscalationThreshold != TimeSpan.FromMinutes(newRule.EscalationThresholdMinutes)
                || oldRule.RequiresImmediateAck != newRule.RequiresImmediateAck
                || oldRule.EvaluationOrder != newRule.EvaluationOrder) return false;
        }
        return true;
    }

    private async Task<Error?> CheckPolicyReadAccessAsync(CancellationToken ct)
    {
        if (ActorId is not { } actorId) return Error.Unauthorized(SupportCaseProblemCodes.AuthenticationRequired);
        var candidate = (await _db.GetActiveSupportOperatorCandidatesAsync(ct)).SingleOrDefault(x => x.UserId == actorId);
        if (candidate is null || candidate.UserType != MealMate.Domain.Enums.UserType.Admin
            || !(candidate.Roles.Contains(AppRoles.SuperAdmin)
                || candidate.PermissionCodes.Contains(PermissionCodes.SupportView)))
            return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
        return null;
    }

    public async Task<Result<IReadOnlyList<SupportCaseTimingCycleDto>>> GetCyclesForCaseAsync(
        Guid caseId,
        CancellationToken ct = default)
    {
        var actorId = ActorId;
        if (actorId is null)
            return Error.Unauthorized(SupportCaseProblemCodes.AuthenticationRequired);

        var supportCase = await _db.SupportCases.SingleOrDefaultAsync(c => c.Id == caseId, ct);
        if (supportCase is null)
            return Error.NotFound(SupportCaseProblemCodes.NotFound);

        if (!await _access.CanReadAsync(supportCase, ct))
            return Error.NotFound(SupportCaseProblemCodes.NotFound);

        var hasView = await _access.HasPersistedPermissionAsync(PermissionCodes.SupportView, ct);
        if (!hasView)
            return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);

        var cycles = await _db.SupportCaseTimingCycles
            .AsNoTracking()
            .Where(c => c.SupportCaseId == caseId)
            .OrderBy(c => c.CycleNumber)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<SupportCaseTimingCycleDto>>(cycles.Select(MapCycleToDto).ToList());
    }

    private static SupportTimingPolicyDto MapToDto(SupportTimingPolicy policy)
    {
        return new SupportTimingPolicyDto(
            policy.Id,
            policy.Version,
            policy.Name,
            policy.Description,
            policy.IsActive,
            policy.PublishedAtUtc,
            policy.PublishedByUserId,
            policy.PublicationReason,
            policy.Rules.Select(r => new SupportTimingRuleDto(
                r.Id,
                r.PolicyId,
                r.Channel?.ToString(),
                r.Priority?.ToString(),
                r.Category,
                r.FirstResponseDeadline.TotalMinutes,
                r.ResolutionDeadline.TotalMinutes,
                r.EscalationThreshold.TotalMinutes,
                r.RequiresImmediateAck,
                r.EvaluationOrder)).ToList());
    }

    public static SupportCaseTimingCycleDto MapCycleToDto(SupportCaseTimingCycle cycle)
    {
        return new SupportCaseTimingCycleDto(
            cycle.Id,
            cycle.SupportCaseId,
            cycle.CycleNumber,
            cycle.PolicyId,
            cycle.PolicyVersion,
            cycle.MatchedRuleId,
            cycle.StartedAtUtc,
            cycle.FirstResponseDeadlineUtc,
            cycle.ResolutionDeadlineUtc,
            cycle.FirstResponseAtUtc,
            cycle.ResolvedAtUtc,
            cycle.ClosedAtUtc,
            cycle.IsFirstResponseBreached,
            cycle.IsResolutionBreached,
            cycle.IsEscalated,
            cycle.EscalatedAtUtc,
            cycle.EscalationReason,
            cycle.IsPolicyUnconfigured,
            cycle.IsUnrouted,
            cycle.Notes,
            cycle.EscalationDeadlineUtc);
    }
}
