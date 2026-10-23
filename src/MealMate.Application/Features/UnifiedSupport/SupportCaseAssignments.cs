using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed partial class SupportCaseService
{
    public async Task<Result<IReadOnlyList<SupportCaseAssignmentRecipientDto>>> GetAssignmentRecipientsAsync(
        Guid caseId, CancellationToken ct = default)
    {
        if (ActorId is not { } actorId) return Unauthorized();
        var supportCase = await _db.SupportCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (supportCase is null || !await _access.CanReadAsync(supportCase, ct)) return NotFound();
        if (!await IsEligibleManagerAsync(actorId, supportCase, ct)) return Forbidden();
        var candidates = await _access.GetEligibleOperatorsAsync(supportCase, requireManage: true, ct);
        var recipients = new List<SupportCaseAssignmentRecipientDto>(candidates.Count);
        foreach (var candidate in candidates.OrderBy(x => x.UserId))
        {
            var name = await _db.GetUserDisplayNameAsync(candidate.UserId, ct);
            recipients.Add(new SupportCaseAssignmentRecipientDto(candidate.UserId, string.IsNullOrWhiteSpace(name) ? null : name));
        }
        return Result.Success<IReadOnlyList<SupportCaseAssignmentRecipientDto>>(recipients);
    }

    public Task<Result<SupportCaseDto>> RequestAssignmentTransferAsync(Guid caseId,
        SupportCaseTransferRequest request, CancellationToken ct = default)
        => _transactions.ExecuteResultAsync(token => RequestAssignmentTransferCoreAsync(caseId, request, token), ct);

    private async Task<Result<SupportCaseDto>> RequestAssignmentTransferCoreAsync(Guid caseId,
        SupportCaseTransferRequest request, CancellationToken ct)
    {
        if (ActorId is not { } actorId) return Unauthorized();
        if (request.TargetUserId == Guid.Empty || !ValidReason(request.Reason) || !ValidKey(request.IdempotencyKey)) return Invalid();
        await _locks.LockSupportHandoverKeyAsync(actorId, request.IdempotencyKey, ct);
        await _locks.LockSupportCaseAsync(caseId, ct);
        var supportCase = await _db.SupportCases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (supportCase is null || !await _access.CanReadAsync(supportCase, ct)) return NotFound();
        if (!await IsEligibleManagerAsync(actorId, supportCase, ct)) return Forbidden();
        var hash = Hash(new { Operation = "assignment-transfer-request", CaseId = caseId, Request = request });
        var now = Now;
        try
        {
            var replay = await FindReplayAsync(actorId, request.IdempotencyKey, hash, ct);
            if (replay is not null) return await ReplayDtoAsync(replay, supportCase, ct);
        }
        catch (SupportCaseIdempotencyException) { return Conflict(SupportCaseProblemCodes.IdempotencyKeyReused); }
        if (!await HasVersionAsync(supportCase, request.ExpectedVersion, ct)) return Conflict(SupportCaseProblemCodes.VersionConflict);
        if (supportCase.AssignedToUserId != actorId || supportCase.PendingTransferToUserId.HasValue
            || supportCase.Status is SupportCaseStatus.Resolved or SupportCaseStatus.Closed
            || supportCase.AssignedToUserId == request.TargetUserId) return Conflict(SupportCaseProblemCodes.InvalidTransition);
        var eligible = await _access.GetEligibleOperatorsAsync(supportCase, requireManage: true, ct);
        if (!eligible.Any(x => x.UserId == request.TargetUserId)) return Invalid();
        var previousOwner = supportCase.AssignedToUserId;
        supportCase.RequestAssignmentTransfer(request.TargetUserId, request.Reason, actorId, now);
        return await SaveAssignmentEventAsync(supportCase, actorId, request.IdempotencyKey, hash,
            "assignment-transfer-requested", previousOwner?.ToString("D"), request.TargetUserId.ToString("D"), now, ct,
            request.Reason, "support.case.assignment.transfer-requested", request.TargetUserId);
    }

    public Task<Result<SupportCaseDto>> DecideAssignmentTransferAsync(Guid caseId, string decision,
        SupportCaseTransferDecisionRequest request, CancellationToken ct = default)
        => _transactions.ExecuteResultAsync(token => DecideAssignmentTransferCoreAsync(caseId, decision, request, token), ct);

    private async Task<Result<SupportCaseDto>> DecideAssignmentTransferCoreAsync(Guid caseId, string decision,
        SupportCaseTransferDecisionRequest request, CancellationToken ct)
    {
        if (ActorId is not { } actorId) return Unauthorized();
        decision = decision.Trim().ToLowerInvariant();
        if (decision is not ("accept" or "decline" or "cancel") || !ValidKey(request.IdempotencyKey)
            || request.Reason is { } reason && reason.Trim().Length > 1000) return Invalid();
        await _locks.LockSupportHandoverKeyAsync(actorId, request.IdempotencyKey, ct);
        await _locks.LockSupportCaseAsync(caseId, ct);
        var supportCase = await _db.SupportCases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (supportCase is null || !await _access.CanReadAsync(supportCase, ct)) return NotFound();
        if (!await IsEligibleManagerAsync(actorId, supportCase, ct)) return Forbidden();
        var hash = Hash(new { Operation = $"assignment-transfer-{decision}", CaseId = caseId, Request = request });
        try
        {
            var replay = await FindReplayAsync(actorId, request.IdempotencyKey, hash, ct);
            if (replay is not null) return await ReplayDtoAsync(replay, supportCase, ct);
        }
        catch (SupportCaseIdempotencyException) { return Conflict(SupportCaseProblemCodes.IdempotencyKeyReused); }
        if (decision is "accept" or "decline")
        {
            if (supportCase.PendingTransferToUserId != actorId) return Forbidden();
            if (decision == "decline" && !ValidReason(request.Reason)) return Invalid();
        }
        else if (supportCase.AssignedToUserId != actorId) return Forbidden();
        if (supportCase.Status is SupportCaseStatus.Resolved or SupportCaseStatus.Closed)
            return Conflict(SupportCaseProblemCodes.InvalidTransition);
        if (!await HasVersionAsync(supportCase, request.ExpectedVersion, ct)) return Conflict(SupportCaseProblemCodes.VersionConflict);
        if (!supportCase.PendingTransferToUserId.HasValue) return Conflict(SupportCaseProblemCodes.InvalidTransition);
        var now = Now;
        var previous = supportCase.AssignedToUserId;
        var counterparty = decision == "cancel" ? supportCase.PendingTransferToUserId : previous;
        if (decision == "accept") supportCase.TransferAssignment(actorId, actorId, now);
        else if (!supportCase.CancelAssignmentTransfer(actorId, now)) return Conflict(SupportCaseProblemCodes.InvalidTransition);
        if (!string.IsNullOrWhiteSpace(request.Reason))
            _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = caseId, AuthorUserId = actorId,
                Visibility = SupportCaseMessageVisibility.Internal, Body = request.Reason.Trim(), CreatedAtUtc = now });
        return await SaveAssignmentEventAsync(supportCase, actorId, request.IdempotencyKey, hash,
            decision switch { "accept" => "assignment-transfer-accepted", "decline" => "assignment-transfer-declined", _ => "assignment-transfer-cancelled" },
            previous?.ToString("D"), decision == "accept" ? actorId.ToString("D") : null, now, ct,
            notificationKey: decision switch { "accept" => "support.case.assignment.transfer-accepted", "decline" => "support.case.assignment.transfer-declined", _ => "support.case.assignment.transfer-cancelled" },
            notificationTarget: counterparty);
    }

    public Task<Result<SupportCaseDto>> ReassignPartyTaskAsync(Guid caseId, Guid taskId,
        ReassignSupportTaskRequest request, CancellationToken ct = default)
        => _transactions.ExecuteResultAsync(token => ReassignPartyTaskCoreAsync(caseId, taskId, request, token), ct);

    private async Task<Result<SupportCaseDto>> ReassignPartyTaskCoreAsync(Guid caseId, Guid taskId,
        ReassignSupportTaskRequest request, CancellationToken ct)
    {
        if (ActorId is not { } actorId) return Unauthorized();
        if (request.RecipientUserId == Guid.Empty || !ValidReason(request.Reason) || !ValidKey(request.IdempotencyKey)) return Invalid();
        await _locks.LockSupportHandoverKeyAsync(actorId, request.IdempotencyKey, ct);
        await _locks.LockSupportCaseAsync(caseId, ct);
        var supportCase = await _db.SupportCases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (supportCase is null || !await _access.CanReadAsync(supportCase, ct)) return NotFound();
        if (!await IsEligibleManagerAsync(actorId, supportCase, ct)) return Forbidden();
        var task = await _db.SupportCaseTasks.SingleOrDefaultAsync(x => x.Id == taskId && x.SupportCaseId == caseId, ct);
        if (task is null) return NotFound();
        var hash = Hash(new { Operation = "party-task-reassign", CaseId = caseId, TaskId = taskId, Request = request });
        try
        {
            var replay = await FindReplayAsync(actorId, request.IdempotencyKey, hash, ct);
            if (replay is not null) return await ReplayDtoAsync(replay, supportCase, ct);
        }
        catch (SupportCaseIdempotencyException) { return Conflict(SupportCaseProblemCodes.IdempotencyKeyReused); }
        if (task.Status != SupportCaseTaskStatus.Open || task.RecipientType != SupportCaseTaskRecipientType.DeliveryManager
            || task.Type != SupportCaseTaskType.DeliveryAction || task.RestaurantId is not { } restaurantId
            || request.RecipientUserId == task.RecipientUserId) return Conflict(SupportCaseProblemCodes.InvalidTransition);
        if (supportCase.Status is SupportCaseStatus.Resolved or SupportCaseStatus.Closed)
            return Conflict(SupportCaseProblemCodes.InvalidTransition);
        if (!await HasVersionAsync(supportCase, request.ExpectedVersion, ct)) return Conflict(SupportCaseProblemCodes.VersionConflict);
        var recipients = await _db.GetDeliveryManagerUserIdsAsync(restaurantId, ct);
        if (!recipients.Contains(request.RecipientUserId)) return Invalid();
        var previous = task.RecipientUserId;
        var now = Now;
        task.RecipientUserId = request.RecipientUserId;
        supportCase.ModifiedAtUtc = now;
        supportCase.ModifiedByUserId = actorId;
        _db.MarkSupportCaseModified(supportCase);
        _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = caseId, SupportCaseTaskId = taskId,
            AuthorUserId = actorId, Visibility = SupportCaseMessageVisibility.Internal, Body = request.Reason.Trim(), CreatedAtUtc = now });
        var supportEvent = new SupportCaseEvent { SupportCaseId = caseId, ActorUserId = actorId,
            EventType = "party-task-reassigned", FromValue = previous?.ToString("D"), ToValue = request.RecipientUserId.ToString("D"),
            IdempotencyKey = request.IdempotencyKey, PayloadHash = hash, CreatedAtUtc = now };
        _db.SupportCaseEvents.Add(supportEvent);
        await NotifyTaskRecipientAsync(supportCase, task, supportEvent, now, ct, assignment: true);
        await NotifyPlatformOperatorsAsync(supportCase, supportEvent, now, "support.case.task.reassigned", ct);
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success(await ProjectAsync(supportCase, ct));
        }
        catch (DbUpdateConcurrencyException) { return Conflict(SupportCaseProblemCodes.VersionConflict); }
    }

    private async Task<bool> IsEligibleManagerAsync(Guid actorId, SupportCase supportCase, CancellationToken ct)
    {
        var candidates = await _db.GetActiveSupportOperatorCandidatesAsync(ct);
        var candidate = candidates.SingleOrDefault(x => x.UserId == actorId);
        return candidate is not null && await _access.CandidateCanManageCaseAsync(candidate, supportCase, ct);
    }

    private async Task<bool> CanTakeOverAssignmentAsync(SupportCase supportCase, CancellationToken ct)
    {
        if (ActorId is not { } actorId || (!_access.IsSupportAdmin && !_access.IsSuperAdmin)
            || !_access.CanManage || supportCase.AssignedToUserId is not { } ownerId
            || supportCase.Status is SupportCaseStatus.Resolved or SupportCaseStatus.Closed
            || !await IsEligibleManagerAsync(actorId, supportCase, ct)) return false;
        var eligible = await _access.GetEligibleOperatorsAsync(supportCase, requireManage: true, ct);
        return !eligible.Any(x => x.UserId == ownerId);
    }

    private async Task<Result<SupportCaseDto>> SaveAssignmentEventAsync(SupportCase supportCase, Guid actorId,
        string key, string hash, string eventType, string? from, string? to, DateTime now, CancellationToken ct,
        string? auditReason = null, string? notificationKey = null, Guid? notificationTarget = null)
    {
        supportCase.ModifiedAtUtc = now;
        supportCase.ModifiedByUserId = actorId;
        _db.MarkSupportCaseModified(supportCase);
        var supportEvent = new SupportCaseEvent { SupportCaseId = supportCase.Id, ActorUserId = actorId,
            EventType = eventType, FromValue = from, ToValue = to, IdempotencyKey = key, PayloadHash = hash, CreatedAtUtc = now };
        _db.SupportCaseEvents.Add(supportEvent);
        if (!string.IsNullOrWhiteSpace(auditReason))
            _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = supportCase.Id, AuthorUserId = actorId,
                Visibility = SupportCaseMessageVisibility.Internal, Body = auditReason.Trim(), CreatedAtUtc = now });
        if (notificationKey is not null)
            await NotifyPlatformOperatorsAsync(supportCase, supportEvent, now, notificationKey, ct, notificationTarget);
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success(await ProjectAsync(supportCase, ct));
        }
        catch (DbUpdateConcurrencyException) { return Conflict(SupportCaseProblemCodes.VersionConflict); }
    }

    private async Task<SupportCaseEvent?> FindReplaySafeAsync(Guid actorId, string key, string hash, CancellationToken ct)
    {
        try { return await FindReplayAsync(actorId, key, hash, ct); }
        catch (SupportCaseIdempotencyException) { throw; }
    }

    private static SupportCaseEvent? ReplayResult(SupportCaseEvent? replay) => replay;

    private async Task<Result<SupportCaseDto>> ReplayDtoAsync(SupportCaseEvent replay, SupportCase currentCase, CancellationToken ct)
    {
        var caseRow = await _db.SupportCases.SingleOrDefaultAsync(x => x.Id == replay.SupportCaseId, ct);
        return caseRow is null ? NotFound() : Result.Success(await ProjectAsync(caseRow, ct));
    }
}
