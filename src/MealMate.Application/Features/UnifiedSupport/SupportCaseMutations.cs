using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed partial class SupportCaseService
{
    public Task<Result<SupportCaseDto>> CreateAsync(CreateSupportCaseRequest request, CancellationToken ct = default)
        => _transactions.ExecuteResultAsync(token => CreateCoreAsync(request, token), ct);

    private async Task<Result<SupportCaseDto>> CreateCoreAsync(CreateSupportCaseRequest request, CancellationToken ct)
    {
        var actorId = ActorId;
        if (actorId is null) return Unauthorized();
        if (!ValidKey(request.IdempotencyKey) || !_access.CanCreate(request.Channel)
            || !Enum.IsDefined(request.Channel) || !Enum.IsDefined(request.Kind)
            || (request.RelatedResourceId.HasValue != (request.RelatedResourceType is not null)))
            return Invalid();
        if (request.Channel == SupportCaseChannel.Customer)
        {
            if (!CustomerSupportCatalog.TryNormalizeReservedCategory(request.Kind, request.Category,
                    out var isReservedCustomerCategory, out var canonicalCustomerCategory))
                return Invalid();
            if (isReservedCustomerCategory)
                request = request with { Category = canonicalCustomerCategory };
        }
        if (request.Channel == SupportCaseChannel.Restaurant && _currentUser.RestaurantId is null)
            return Forbidden();
        if (request.Channel == SupportCaseChannel.DeliveryManager)
        {
            if (_currentUser.RestaurantId is not { } managerRestaurantId || !_currentUser.UserId.HasValue
                || !await _db.RestaurantProfiles.AnyAsync(r => r.Id == managerRestaurantId && !r.IsDeleted && !r.IsArchived, ct))
                return Forbidden();
            var managers = await _db.GetDeliveryManagerUserIdsAsync(managerRestaurantId, ct);
            if (!managers.Contains(_currentUser.UserId.Value)) return Forbidden();
        }
        if (request.Channel == SupportCaseChannel.Driver && request.Kind == SupportCaseKind.OperationalReport)
            return Error.Validation(SupportCaseProblemCodes.OperationalRouteRequired);
        await _locks.LockSupportHandoverKeyAsync(actorId.Value, request.IdempotencyKey, ct);

        var resource = await ResolveCustomerResourceAsync(request.Channel, actorId.Value,
            request.RelatedResourceType, request.RelatedResourceId, ct);
        if (!resource.Supported) return Invalid();
        if (!resource.Found) return NotFound();

        var owner = SupportCaseOwnerTeam.PlatformSupport;
        var triage = await ResolveTriageGeographyAsync(request.Channel, actorId.Value, ct);
        var selectedRestaurantId = request.Channel is SupportCaseChannel.Restaurant or SupportCaseChannel.DeliveryManager
            ? _currentUser.RestaurantId : resource.RestaurantId;
        var hash = Hash(new { Operation = "create", Request = request });
        try
        {
            var replay = await FindReplayAsync(actorId.Value, request.IdempotencyKey, hash, ct);
            if (replay is not null)
            {
                var existing = await _db.SupportCases.SingleOrDefaultAsync(x => x.Id == replay.SupportCaseId, ct);
                if (existing is null || !await _access.CanReadAsync(existing, ct)) return NotFound();
                return Result.Success(await ProjectAsync(existing, ct));
            }
        }
        catch (SupportCaseIdempotencyException) { return Conflict(SupportCaseProblemCodes.IdempotencyKeyReused); }

        SupportCase supportCase;
        try
        {
            supportCase = SupportCase.Create(request.Channel, request.Kind, owner, request.Category, request.Subject,
                request.Description, actorId.Value, Now, restaurantId: selectedRestaurantId,
                relatedResourceType: resource.ResourceType, relatedResourceId: request.RelatedResourceId,
                triageCountryId: triage.CountryId, triageRegionId: triage.RegionId);
        }
        catch (ArgumentException) { return Invalid(); }
        _db.SupportCases.Add(supportCase);

        var activePolicy = await _db.SupportTimingPolicies
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.IsActive, ct);
        var matchedRule = activePolicy?.FindMatchingRule(supportCase.Channel, supportCase.Priority, supportCase.Category);

        SupportCaseTimingCycle initialCycle;
        if (activePolicy is not null && matchedRule is not null)
        {
            initialCycle = SupportCaseTimingCycle.StartConfigured(
                supportCase.Id,
                cycleNumber: 1,
                activePolicy.Id,
                activePolicy.Version,
                matchedRule.Id,
                Now,
                firstResponseDeadlineUtc: Now.Add(matchedRule.FirstResponseDeadline),
                resolutionDeadlineUtc: Now.Add(matchedRule.ResolutionDeadline),
                escalationThreshold: matchedRule.EscalationThreshold);
        }
        else
        {
            initialCycle = SupportCaseTimingCycle.StartUnconfigured(
                supportCase.Id,
                cycleNumber: 1,
                Now,
                activePolicy?.Id,
                activePolicy?.Version,
                activePolicy is null ? "No active support timing policy published." : "No timing rule matched case classification.");
        }
        supportCase.SetActiveTimingCycle(initialCycle.Id);
        _db.SupportCaseTimingCycles.Add(initialCycle);

        var supportEvent = new SupportCaseEvent
        {
            SupportCaseId = supportCase.Id,
            ActorUserId = actorId.Value,
            EventType = "Created",
            IdempotencyKey = request.IdempotencyKey,
            PayloadHash = hash,
            CreatedAtUtc = Now,
        };
        _db.SupportCaseEvents.Add(supportEvent);
        await NotifyPlatformOperatorsAsync(supportCase, supportEvent, Now, "support.case.created", ct);
        supportEvent.ToValue = supportCase.TriageStatus.ToString();
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success(await ProjectAsync(supportCase, ct));
    }

    public async Task<Result<SupportCaseDto>> AddMessageAsync(Guid caseId, AddSupportMessageRequest request, CancellationToken ct = default)
    {
        return await AddMessageCoreAsync(caseId, request.Body, request.ExpectedVersion, request.IdempotencyKey, request.TaskId, request.AttachmentKeys,
            internalNote: false, ct);
    }

    public async Task<Result<SupportCaseDto>> AddInternalNoteAsync(Guid caseId, AddSupportInternalNoteRequest request, CancellationToken ct = default)
    {
        if (ActorId is null) return Unauthorized();
        if (!_access.CanWriteInternal) return Forbidden();
        return await AddMessageCoreAsync(caseId, request.Body, request.ExpectedVersion, request.IdempotencyKey, null, request.AttachmentKeys,
            internalNote: true, ct);
    }

    private async Task<Result<SupportCaseDto>> AddMessageCoreAsync(Guid caseId, string body, uint expectedVersion, string idempotencyKey, Guid? taskId, IReadOnlyList<string>? attachmentKeys, bool internalNote, CancellationToken ct)
        => await _transactions.ExecuteResultAsync(token => AddMessageTransactionAsync(caseId, body, expectedVersion,
            idempotencyKey, taskId, attachmentKeys, internalNote, token), ct);

    private async Task<Result<SupportCaseDto>> AddMessageTransactionAsync(Guid caseId, string body, uint expectedVersion, string idempotencyKey, Guid? taskId, IReadOnlyList<string>? requestedAttachmentKeys, bool internalNote, CancellationToken ct)
    {
        var actorId = ActorId;
        if (actorId is null) return Unauthorized();
        if (!ValidKey(idempotencyKey) || string.IsNullOrWhiteSpace(body) || body.Trim().Length > 4000) return Invalid();
        var attachmentKeys = requestedAttachmentKeys?.ToArray() ?? [];
        if (attachmentKeys.Length > 5 || attachmentKeys.Any(string.IsNullOrWhiteSpace)
            || attachmentKeys.Distinct(StringComparer.Ordinal).Count() != attachmentKeys.Length)
            return Error.Validation(SupportCaseProblemCodes.AttachmentUnsupported);
        await _locks.LockSupportHandoverKeyAsync(actorId.Value, idempotencyKey, ct);
        await _locks.LockSupportCaseAsync(caseId, ct);
        var supportCase = await _db.SupportCases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (supportCase is null || !await _access.CanReadAsync(supportCase, ct)) return NotFound();
        var isRequester = supportCase.RequesterUserId == actorId.Value
            || (_currentUser.UserType == MealMate.Domain.Enums.UserType.Restaurant
                && supportCase.Channel == SupportCaseChannel.Restaurant
                && supportCase.RestaurantId == _currentUser.RestaurantId);
        if (internalNote)
        {
            if (!_access.CanWriteInternal || (!_access.IsSupportAdmin && !_access.IsSuperAdmin)
                || !await _access.HasPersistedPermissionAsync(MealMate.Application.Common.Security.PermissionCodes.SupportInternal, ct)) return Forbidden();
        }
        else if ((_access.IsSupportAdmin || _access.IsSuperAdmin)
            && (!_access.CanReply || !await _access.HasPersistedPermissionAsync(MealMate.Application.Common.Security.PermissionCodes.SupportReply, ct))) return Forbidden();
        else if (_access.CanReply)
        {
            if (taskId.HasValue && !await CanAccessTaskAsync(supportCase, taskId.Value, actorId.Value, ct, requireOpen: false)) return Forbidden();
        }
        else if (isRequester)
        {
            if (taskId.HasValue) return Forbidden();
        }
        else if (!taskId.HasValue || !await CanAccessTaskAsync(supportCase, taskId.Value, actorId.Value, ct, requireOpen: false)) return Forbidden();
        var visibility = internalNote ? SupportCaseMessageVisibility.Internal : SupportCaseMessageVisibility.Participant;
        var hash = Hash(new { Operation = internalNote ? "internal-message" : "message", CaseId = caseId, Body = body.Trim(), TaskId = taskId, ExpectedVersion = expectedVersion, AttachmentKeys = attachmentKeys });
        try
        {
            var replay = await FindReplayAsync(actorId.Value, idempotencyKey, hash, ct);
            if (replay is not null) return Result.Success(await ProjectAsync(supportCase, ct));
        }
        catch (SupportCaseIdempotencyException) { return Conflict(SupportCaseProblemCodes.IdempotencyKeyReused); }
        if (!internalNote && supportCase.Status is (SupportCaseStatus.Resolved or SupportCaseStatus.Closed))
            return Conflict(SupportCaseProblemCodes.InvalidTransition);
        if (!internalNote && taskId.HasValue && !await CanAccessTaskAsync(supportCase, taskId.Value, actorId.Value, ct)) return Forbidden();
        if (!await HasVersionAsync(supportCase, expectedVersion, ct)) return Conflict(SupportCaseProblemCodes.VersionConflict);
        if (attachmentKeys.Length > 0 && !await ValidateAttachmentKeysAsync(attachmentKeys, actorId.Value, ct))
            return Error.Validation(SupportCaseProblemCodes.AttachmentUnsupported);

        var now = Now;
        var from = supportCase.Status;
        if (!internalNote && isRequester && supportCase.Status == SupportCaseStatus.WaitingOnRequester)
            supportCase.TryTransitionTo(SupportCaseStatus.InProgress);
        _db.SupportCaseMessages.Add(new SupportCaseMessage
        {
            SupportCaseId = caseId,
            SupportCaseTaskId = taskId,
            AuthorUserId = actorId.Value,
            Visibility = visibility,
            Body = body.Trim(),
            AttachmentKeys = attachmentKeys,
            CreatedAtUtc = now,
        });
        supportCase.ModifiedAtUtc = now;
        supportCase.ModifiedByUserId = actorId.Value;
        _db.MarkSupportCaseModified(supportCase);
        var supportEvent = new SupportCaseEvent
        {
            SupportCaseId = caseId, ActorUserId = actorId.Value, EventType = internalNote ? "InternalNoteAdded" : "MessageAdded",
            FromValue = from.ToString(), ToValue = supportCase.Status.ToString(),
            IdempotencyKey = idempotencyKey, PayloadHash = hash, CreatedAtUtc = now,
        };
        _db.SupportCaseEvents.Add(supportEvent);
        if (!internalNote && taskId is null && !isRequester
            && await _access.IsPersistedPlatformOperatorForFirstResponseAsync(ct)
            && supportCase.ActiveTimingCycleId.HasValue)
        {
            var cycle = await _db.SupportCaseTimingCycles.FindAsync([supportCase.ActiveTimingCycleId.Value], ct);
            if (cycle is not null && !cycle.FirstResponseAtUtc.HasValue)
            {
                cycle.RecordFirstResponse(now);
                _db.SupportCaseEvents.Add(new SupportCaseEvent
                {
                    SupportCaseId = supportCase.Id,
                    ActorUserId = actorId.Value,
                    EventType = "timing.first_response_recorded",
                    CreatedAtUtc = now,
                });
            }
        }
        if (!internalNote && _access.CanReply)
        {
            if (taskId is { } taskRecipientId)
            {
                var taskRecipient = await _db.SupportCaseTasks.SingleOrDefaultAsync(t => t.Id == taskRecipientId && t.SupportCaseId == caseId, ct);
                if (taskRecipient is not null) await NotifyTaskRecipientAsync(supportCase, taskRecipient, supportEvent, now, ct, assignment: false);
            }
            else await NotifyRequesterAsync(supportCase, supportEvent, now, ct);
        }
        else if (!internalNote)
            await NotifyPlatformOperatorsAsync(supportCase, supportEvent, now, "support.case.message", ct);
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success(await ProjectAsync(supportCase, ct));
        }
        catch (DbUpdateConcurrencyException) { return Conflict(SupportCaseProblemCodes.VersionConflict); }
    }

    private async Task<bool> CanAccessTaskAsync(SupportCase supportCase, Guid taskId, Guid actorId, CancellationToken ct, bool requireOpen = true)
    {
        var task = await _db.SupportCaseTasks.SingleOrDefaultAsync(t => t.Id == taskId && t.SupportCaseId == supportCase.Id, ct);
        if (task is null || task.Status == SupportCaseTaskStatus.Cancelled || requireOpen && task.Status != SupportCaseTaskStatus.Open) return false;
        if (_access.IsSupportAdmin || _access.IsSuperAdmin) return _access.CanReply;
        if (task.RecipientType == SupportCaseTaskRecipientType.DeliveryManager)
            return _currentUser.UserType == UserType.DeliveryManager && task.RecipientUserId == actorId
                && task.RestaurantId is { } taskRestaurantId && taskRestaurantId == _currentUser.RestaurantId
                && await _access.IsCurrentDeliveryManagerForRestaurantAsync(taskRestaurantId, ct);
        return _currentUser.UserType == UserType.Restaurant && task.RecipientType == SupportCaseTaskRecipientType.Restaurant && _currentUser.RestaurantId.HasValue
            && task.RestaurantId == _currentUser.RestaurantId;
    }

    public Task<Result<SupportCaseDto>> ExecuteActionAsync(Guid caseId, SupportCaseActionRequest request, CancellationToken ct = default)
        => _transactions.ExecuteResultAsync(token => ExecuteActionTransactionAsync(caseId, request, token), ct);

    private async Task<Result<SupportCaseDto>> ExecuteActionTransactionAsync(Guid caseId, SupportCaseActionRequest request, CancellationToken ct)
    {
        var actorId = ActorId;
        if (actorId is null) return Unauthorized();
        if (!ValidKey(request.IdempotencyKey) || string.IsNullOrWhiteSpace(request.Action)) return Invalid();
        var action = request.Action.Trim().ToLowerInvariant();
        await _locks.LockSupportHandoverKeyAsync(actorId.Value, request.IdempotencyKey, ct);
        await _locks.LockSupportCaseAsync(caseId, ct);
        var supportCase = await _db.SupportCases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (supportCase is null || !await _access.CanReadAsync(supportCase, ct)) return NotFound();
        var requesterActor = supportCase.RequesterUserId == actorId.Value
            || (_currentUser.UserType == UserType.Restaurant && supportCase.Channel == SupportCaseChannel.Restaurant
                && supportCase.RestaurantId == _currentUser.RestaurantId);
        var requesterReopen = action == "reopen" && requesterActor;
        var requesterClose = action == "close" && requesterActor;
        if (_access.IsSupportAdmin || _access.IsSuperAdmin)
        {
            var permission = MealMate.Application.Common.Security.PermissionCodes.SupportManage;
            if (!await _access.HasPersistedPermissionAsync(permission, ct)) return Forbidden();
        }
        SupportCaseTask? completionTask = null;
        SupportCaseTask? createdTask = null;
        if ((action is "complete-party-task" or "cancel-party-task") && request.TaskId is { } completionTaskId)
            completionTask = await _db.SupportCaseTasks.SingleOrDefaultAsync(t => t.Id == completionTaskId && t.SupportCaseId == supportCase.Id, ct);
        var isAssignedTaskRecipient = completionTask is not null && (
            _currentUser.UserType == UserType.Restaurant && completionTask.RecipientType == SupportCaseTaskRecipientType.Restaurant && completionTask.RestaurantId == _currentUser.RestaurantId
            || completionTask.RecipientType == SupportCaseTaskRecipientType.DeliveryManager && completionTask.RecipientUserId == actorId.Value
                && completionTask.RestaurantId is { } taskRestaurantId
                && await _access.IsCurrentDeliveryManagerForRestaurantAsync(taskRestaurantId, ct));
        if (!requesterReopen && !requesterClose && !_access.CanManage && !(action == "complete-party-task" && isAssignedTaskRecipient)) return Forbidden();
        var hash = Hash(new { Operation = "action", CaseId = caseId, Request = request with { Action = action } });
        try
        {
            var replay = await FindReplayAsync(actorId.Value, request.IdempotencyKey, hash, ct);
            if (replay is not null) return Result.Success(await ProjectAsync(supportCase, ct));
        }
        catch (SupportCaseIdempotencyException) { return Conflict(SupportCaseProblemCodes.IdempotencyKeyReused); }
        if (!await HasVersionAsync(supportCase, request.ExpectedVersion, ct)) return Conflict(SupportCaseProblemCodes.VersionConflict);

        var from = supportCase.Status;
        var now = Now;
        var eventType = action;
        Guid? takeoverPreviousOwnerId = null;
        switch (action)
        {
            case "acknowledge":
                if (supportCase.AssignedToUserId.HasValue && supportCase.AssignedToUserId != actorId.Value)
                    return Conflict(SupportCaseProblemCodes.InvalidTransition);
                if (!_access.CanManage || !supportCase.TryTransitionTo(SupportCaseStatus.Acknowledged)) return Conflict(SupportCaseProblemCodes.InvalidTransition);
                supportCase.Assign(actorId.Value, actorId.Value, now);
                break;
            case "start":
                if (supportCase.AssignedToUserId.HasValue && supportCase.AssignedToUserId != actorId.Value)
                    return Conflict(SupportCaseProblemCodes.InvalidTransition);
                if (!_access.CanManage || !supportCase.TryTransitionTo(SupportCaseStatus.InProgress)) return Conflict(SupportCaseProblemCodes.InvalidTransition);
                supportCase.Assign(actorId.Value, actorId.Value, now);
                break;
            case "request-information":
                if (!_access.CanManage || !ValidReason(request.Reason) || !supportCase.TryTransitionTo(SupportCaseStatus.WaitingOnRequester)) return Conflict(SupportCaseProblemCodes.InvalidTransition);
                _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = caseId, AuthorUserId = actorId.Value,
                    Visibility = SupportCaseMessageVisibility.Participant, Body = request.Reason!.Trim(), CreatedAtUtc = now });
                if (supportCase.ActiveTimingCycleId.HasValue
                    && await _access.IsPersistedPlatformOperatorForFirstResponseAsync(ct))
                {
                    var cycle = await _db.SupportCaseTimingCycles.FindAsync([supportCase.ActiveTimingCycleId.Value], ct);
                    if (cycle is not null && !cycle.FirstResponseAtUtc.HasValue)
                    {
                        cycle.RecordFirstResponse(now);
                    }
                }
                break;
            case "resolve":
                if (!_access.CanManage || !ValidReason(request.Reason)
                    || supportCase.PendingTransferToUserId.HasValue
                    || await _db.SupportCaseTasks.AnyAsync(t => t.SupportCaseId == caseId && t.Status == SupportCaseTaskStatus.Open, ct)
                    || await HasUnresolvedOperationalLinkAsync(caseId, ct)
                    || !supportCase.TryTransitionTo(SupportCaseStatus.Resolved)) return Conflict(SupportCaseProblemCodes.InvalidTransition);
                _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = caseId, AuthorUserId = actorId.Value,
                    Visibility = SupportCaseMessageVisibility.Participant, Body = request.Reason!.Trim(), CreatedAtUtc = now });
                if (supportCase.ActiveTimingCycleId.HasValue)
                {
                    var cycle = await _db.SupportCaseTimingCycles.FindAsync([supportCase.ActiveTimingCycleId.Value], ct);
                    if (cycle is not null && !cycle.ResolvedAtUtc.HasValue)
                    {
                        cycle.RecordResolution(now);
                        _db.SupportCaseEvents.Add(new SupportCaseEvent
                        {
                            SupportCaseId = supportCase.Id,
                            ActorUserId = actorId.Value,
                            EventType = "timing.resolved_recorded",
                            CreatedAtUtc = now,
                        });
                    }
                }
                break;
            case "reopen":
                if (!requesterReopen && !_access.CanManage) return Forbidden();
                if (!ValidReason(request.Reason) || supportCase.Status is not (SupportCaseStatus.Resolved or SupportCaseStatus.Closed)
                    || !supportCase.TryTransitionTo(SupportCaseStatus.InProgress)) return Conflict(SupportCaseProblemCodes.InvalidTransition);
                _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = caseId, AuthorUserId = actorId.Value,
                    Visibility = SupportCaseMessageVisibility.Participant, Body = request.Reason!.Trim(), CreatedAtUtc = now });
                var previousCycle = await _db.SupportCaseTimingCycles
                    .Where(c => c.SupportCaseId == caseId)
                    .OrderByDescending(c => c.CycleNumber)
                    .FirstOrDefaultAsync(ct);
                int nextCycleNumber = (previousCycle?.CycleNumber ?? 0) + 1;
                var activePolicy = await _db.SupportTimingPolicies
                    .Include(p => p.Rules)
                    .FirstOrDefaultAsync(p => p.IsActive, ct);
                var matchedRule = activePolicy?.FindMatchingRule(supportCase.Channel, supportCase.Priority, supportCase.Category);
                SupportCaseTimingCycle newCycle;
                if (activePolicy is not null && matchedRule is not null)
                {
                    newCycle = SupportCaseTimingCycle.StartConfigured(
                        supportCase.Id,
                        nextCycleNumber,
                        activePolicy.Id,
                        activePolicy.Version,
                        matchedRule.Id,
                        now,
                        firstResponseDeadlineUtc: now.Add(matchedRule.FirstResponseDeadline),
                        resolutionDeadlineUtc: now.Add(matchedRule.ResolutionDeadline),
                        escalationThreshold: matchedRule.EscalationThreshold);
                }
                else
                {
                    newCycle = SupportCaseTimingCycle.StartUnconfigured(
                        supportCase.Id,
                        nextCycleNumber,
                        now,
                        activePolicy?.Id,
                        activePolicy?.Version,
                        activePolicy is null ? "No active support timing policy published." : "No timing rule matched case classification.");
                }
                supportCase.SetActiveTimingCycle(newCycle.Id);
                _db.SupportCaseTimingCycles.Add(newCycle);
                _db.SupportCaseEvents.Add(new SupportCaseEvent
                {
                    SupportCaseId = supportCase.Id,
                    ActorUserId = actorId.Value,
                    EventType = "timing.cycle_started",
                    ToValue = $"Cycle {nextCycleNumber}",
                    CreatedAtUtc = now,
                });
                break;
            case "escalate":
                if (!_access.CanManage) return Forbidden();
                if (!ValidReason(request.Reason) || supportCase.Status is SupportCaseStatus.Resolved or SupportCaseStatus.Closed)
                    return Conflict(SupportCaseProblemCodes.InvalidTransition);
                supportCase.ChangeOwner(SupportCaseOwnerTeam.PlatformSupport, supportCase.AssignedToUserId, actorId.Value, now);
                _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = caseId, AuthorUserId = actorId.Value,
                    Visibility = SupportCaseMessageVisibility.Internal, Body = request.Reason!.Trim(), CreatedAtUtc = now });
                if (supportCase.ActiveTimingCycleId.HasValue)
                {
                    var cycle = await _db.SupportCaseTimingCycles.FindAsync([supportCase.ActiveTimingCycleId.Value], ct);
                    if (cycle is not null && !cycle.IsEscalated)
                    {
                        cycle.Escalate(request.Reason!.Trim(), now);
                    }
                }
                break;
            case "take-over-inactive-assignment":
                if (!_access.CanManage || !_access.IsSupportAdmin && !_access.IsSuperAdmin
                    || !ValidReason(request.Reason) || !supportCase.AssignedToUserId.HasValue
                    || supportCase.Status is SupportCaseStatus.Resolved or SupportCaseStatus.Closed
                    || !await IsEligibleManagerAsync(actorId.Value, supportCase, ct)) return Forbidden();
                var eligibleOwners = await _access.GetEligibleOperatorsAsync(supportCase, requireManage: true, ct);
                if (eligibleOwners.Any(x => x.UserId == supportCase.AssignedToUserId.Value))
                    return Conflict(SupportCaseProblemCodes.InvalidTransition);
                takeoverPreviousOwnerId = supportCase.AssignedToUserId;
                supportCase.TransferAssignment(actorId.Value, actorId.Value, now);
                _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = caseId, AuthorUserId = actorId.Value,
                    Visibility = SupportCaseMessageVisibility.Internal, Body = request.Reason!.Trim(), CreatedAtUtc = now });
                break;
            case "create-party-task":
                if (!_access.CanManage || (createdTask = await TryCreateTaskAsync(supportCase, request, actorId.Value, now, ct)) is null) return Invalid();
                if (supportCase.Status == SupportCaseStatus.New) supportCase.TryTransitionTo(SupportCaseStatus.InProgress);
                if (supportCase.Status != SupportCaseStatus.WaitingOnParty
                    && !supportCase.TryTransitionTo(SupportCaseStatus.WaitingOnParty)) return Conflict(SupportCaseProblemCodes.InvalidTransition);
                break;
            case "complete-party-task":
                if (completionTask is null || (!isAssignedTaskRecipient && !_access.CanManage)
                    || !completionTask.Complete(actorId.Value, request.TaskResult ?? string.Empty, now)) return Forbidden();
                if (supportCase.Status == SupportCaseStatus.WaitingOnParty
                    && !await _db.SupportCaseTasks.AnyAsync(t => t.SupportCaseId == caseId
                        && t.Id != completionTask.Id && t.Status == SupportCaseTaskStatus.Open, ct))
                    supportCase.TryTransitionTo(SupportCaseStatus.InProgress);
                break;
            case "cancel-party-task":
                if (!_access.CanManage || completionTask is null || !ValidReason(request.Reason)
                    || !completionTask.Cancel(actorId.Value, request.Reason!)) return Conflict(SupportCaseProblemCodes.InvalidTransition);
                _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = caseId, AuthorUserId = actorId.Value,
                    SupportCaseTaskId = completionTask.Id, Visibility = SupportCaseMessageVisibility.Internal,
                    Body = request.Reason!.Trim(), CreatedAtUtc = now });
                if (supportCase.Status == SupportCaseStatus.WaitingOnParty
                    && !await _db.SupportCaseTasks.AnyAsync(t => t.SupportCaseId == caseId
                        && t.Id != completionTask.Id && t.Status == SupportCaseTaskStatus.Open, ct))
                    supportCase.TryTransitionTo(SupportCaseStatus.InProgress);
                break;
            case "close":
                if (!requesterClose && !_access.CanManage) return Forbidden();
                if (request.Reason is not null && request.Reason.Trim().Length > 1000) return Invalid();
                if (!supportCase.TryTransitionTo(SupportCaseStatus.Closed)) return Conflict(SupportCaseProblemCodes.InvalidTransition);
                if (!string.IsNullOrWhiteSpace(request.Reason))
                    _db.SupportCaseMessages.Add(new SupportCaseMessage { SupportCaseId = caseId, AuthorUserId = actorId.Value,
                        Visibility = SupportCaseMessageVisibility.Participant, Body = request.Reason.Trim(), CreatedAtUtc = now });
                if (supportCase.ActiveTimingCycleId.HasValue)
                {
                    var cycle = await _db.SupportCaseTimingCycles.FindAsync([supportCase.ActiveTimingCycleId.Value], ct);
                    if (cycle is not null && !cycle.ClosedAtUtc.HasValue)
                    {
                        cycle.RecordClose(now);
                    }
                }
                break;
            default:
                return Invalid();
        }

        supportCase.ModifiedAtUtc = now;
        supportCase.ModifiedByUserId = actorId.Value;
        _db.MarkSupportCaseModified(supportCase);
        var supportEvent = new SupportCaseEvent
        {
            SupportCaseId = caseId, ActorUserId = actorId.Value, EventType = eventType,
            FromValue = action == "take-over-inactive-assignment" ? takeoverPreviousOwnerId?.ToString("D") : from.ToString(),
            ToValue = action == "take-over-inactive-assignment" ? actorId.Value.ToString("D") : supportCase.Status.ToString(), IdempotencyKey = request.IdempotencyKey,
            PayloadHash = hash, CreatedAtUtc = now,
        };
        _db.SupportCaseEvents.Add(supportEvent);
        if (action is "request-information" or "resolve") await NotifyRequesterAsync(supportCase, supportEvent, now, ct);
        if (action == "complete-party-task" && isAssignedTaskRecipient)
            await NotifyPlatformOperatorsAsync(supportCase, supportEvent, now, "support.case.task.completed", ct);
        if (action == "create-party-task" && createdTask is not null)
            await NotifyTaskRecipientAsync(supportCase, createdTask, supportEvent, now, ct);
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success(await ProjectAsync(supportCase, ct));
        }
        catch (DbUpdateConcurrencyException) { return Conflict(SupportCaseProblemCodes.VersionConflict); }
    }

    private async Task<SupportCaseTask?> TryCreateTaskAsync(SupportCase supportCase, SupportCaseActionRequest request, Guid actorId, DateTime now, CancellationToken ct)
    {
        if (!request.TaskType.HasValue || !Enum.IsDefined(request.TaskType.Value) || !request.RecipientType.HasValue
            || request.RecipientType.Value is not (SupportCaseTaskRecipientType.Restaurant or SupportCaseTaskRecipientType.DeliveryManager)
            || request.RestaurantId is not { } restaurantId
            || restaurantId == Guid.Empty || string.IsNullOrWhiteSpace(request.TaskSubject) || request.TaskSubject.Trim().Length > 200
            || string.IsNullOrWhiteSpace(request.TaskQuestion) || request.TaskQuestion.Trim().Length > 2000
            || (request.DueAtUtc.HasValue && request.DueAtUtc.Value.Kind != DateTimeKind.Utc)) return null;
        if (supportCase.RestaurantId != restaurantId
            || !await _db.RestaurantProfiles.AnyAsync(r => r.Id == restaurantId && !r.IsDeleted && !r.IsArchived, ct)) return null;
        if (request.RecipientType == SupportCaseTaskRecipientType.Restaurant && request.TaskType != SupportCaseTaskType.RestaurantEvidence
            || request.RecipientType == SupportCaseTaskRecipientType.DeliveryManager && request.TaskType != SupportCaseTaskType.DeliveryAction)
            return null;
        if (request.RecipientType == SupportCaseTaskRecipientType.DeliveryManager)
        {
            if (!request.RecipientUserId.HasValue) return null;
            var managers = await _db.GetDeliveryManagerUserIdsAsync(restaurantId, ct);
            if (!managers.Contains(request.RecipientUserId.Value)) return null;
        }
        else if (request.RecipientUserId.HasValue) return null;
        var task = new SupportCaseTask
        {
            SupportCaseId = supportCase.Id, Type = request.TaskType.Value, RecipientType = request.RecipientType.Value,
            RestaurantId = restaurantId, RecipientUserId = request.RecipientUserId, Subject = request.TaskSubject.Trim(), Question = request.TaskQuestion.Trim(),
            CreatedByUserId = actorId, CreatedAtUtc = now, DueAtUtc = request.DueAtUtc,
        };
        _db.SupportCaseTasks.Add(task);
        return task;
    }

    private static bool ValidReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 1000;

    public Task<Result<SupportCaseDto>> AddLinkAsync(Guid caseId, AddSupportCaseLinkRequest request, CancellationToken ct = default)
        => _transactions.ExecuteResultAsync(token => AddLinkCoreAsync(caseId, request, token), ct);

    private async Task<Result<SupportCaseDto>> AddLinkCoreAsync(Guid caseId, AddSupportCaseLinkRequest request, CancellationToken ct)
    {
        if (ActorId is null) return Unauthorized();
        if (!ValidKey(request.IdempotencyKey) || request.ResourceId == Guid.Empty) return Invalid();
        await _locks.LockSupportHandoverKeyAsync(ActorId.Value, request.IdempotencyKey, ct);
        await _locks.LockSupportCaseAsync(caseId, ct);
        var supportCase = await _db.SupportCases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (supportCase is null || !await _access.CanReadAsync(supportCase, ct)) return NotFound();
        if (!_access.CanManage || !await _access.HasPersistedPermissionAsync(
                MealMate.Application.Common.Security.PermissionCodes.SupportManage, ct)) return Forbidden();
        if (!Enum.IsDefined(request.Relation) || request.Relation is not (SupportCaseLinkRelation.OperationalSource or SupportCaseLinkRelation.Incident)
            || !string.Equals(request.ResourceType, "DriverComplaint", StringComparison.Ordinal)
            || request.ResourceId == Guid.Empty || !ValidKey(request.IdempotencyKey))
            return Error.Validation(SupportCaseProblemCodes.LinkTargetUnsupported);

        var complaint = await _db.Complaints.SingleOrDefaultAsync(x => x.Id == request.ResourceId
            && x.DriverProfileId != null && DriverSupportIssueProjection.SupportedCategoryValues.Contains(x.Category), ct);
        if (complaint is null || supportCase.RestaurantId != complaint.RestaurantId
            || !await _access.CanReadRestaurantAsync(complaint.RestaurantId, ct)) return NotFound();

        var hash = Hash(new { Operation = "link", CaseId = caseId, Request = request });
        try
        {
            var replay = await FindReplayAsync(ActorId.Value, request.IdempotencyKey, hash, ct);
            if (replay is not null) return Result.Success(await ProjectAsync(supportCase, ct));
        }
        catch (SupportCaseIdempotencyException) { return Conflict(SupportCaseProblemCodes.IdempotencyKeyReused); }
        if (supportCase.Status is SupportCaseStatus.Resolved or SupportCaseStatus.Closed)
            return Conflict(SupportCaseProblemCodes.InvalidTransition);
        if (!await HasVersionAsync(supportCase, request.ExpectedVersion, ct)) return Conflict(SupportCaseProblemCodes.VersionConflict);

        var now = Now;
        var linkExists = await _db.SupportCaseLinks.AnyAsync(x => x.SupportCaseId == caseId && x.Relation == request.Relation
            && x.ResourceType == request.ResourceType && x.ResourceId == request.ResourceId, ct);
        if (!linkExists) _db.SupportCaseLinks.Add(new SupportCaseLink
        {
            SupportCaseId = caseId, Relation = request.Relation, ResourceType = request.ResourceType,
            ResourceId = request.ResourceId, CreatedByUserId = ActorId.Value, CreatedAtUtc = now,
        });
        supportCase.ModifiedAtUtc = now;
        supportCase.ModifiedByUserId = ActorId.Value;
        _db.MarkSupportCaseModified(supportCase);
        _db.SupportCaseEvents.Add(new SupportCaseEvent
        {
            SupportCaseId = caseId, ActorUserId = ActorId.Value, EventType = "link",
            FromValue = null, ToValue = request.ResourceType, IdempotencyKey = request.IdempotencyKey,
            PayloadHash = hash, CreatedAtUtc = now,
        });
        try
        {
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Success(await ProjectAsync(supportCase, ct));
        }
        catch (DbUpdateConcurrencyException) { return Conflict(SupportCaseProblemCodes.VersionConflict); }
    }

    private async Task<bool> HasUnresolvedOperationalLinkAsync(Guid caseId, CancellationToken ct)
    {
        var links = await _db.SupportCaseLinks.Where(l => l.SupportCaseId == caseId
            && (l.Relation == SupportCaseLinkRelation.OperationalSource || l.Relation == SupportCaseLinkRelation.Incident)).ToListAsync(ct);
        foreach (var link in links)
        {
            if (!string.Equals(link.ResourceType, "DriverComplaint", StringComparison.Ordinal)) return true;
            var complaint = await _db.Complaints.AsNoTracking().SingleOrDefaultAsync(c => c.Id == link.ResourceId
                && c.DriverProfileId != null && DriverSupportIssueProjection.SupportedCategoryValues.Contains(c.Category), ct);
            if (complaint is null || complaint.Status is not (ComplaintStatus.Resolved or ComplaintStatus.Closed or ComplaintStatus.Rejected))
                return true;
        }
        return false;
    }
}
