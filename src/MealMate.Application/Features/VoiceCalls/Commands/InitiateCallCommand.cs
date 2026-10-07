using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Commands;

public sealed record InitiateCallCommand(
    Guid TripStopId,
    Guid ClientRequestId) : ICommand<InitiateCallResponseDto>, ISelfPersistingCommand;

public sealed class InitiateCallCommandHandler : ICommandHandler<InitiateCallCommand, InitiateCallResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICallSignalingNotifier _notifier;
    private readonly IVoiceCallPushNotifier _pushNotifier;
    private readonly IVoiceCallParticipantLockService _lockService;
    private readonly MealMate.Application.Features.VoiceCalls.Abstractions.IDeliveryContactEligibilityService _eligibilityService;
    private readonly Microsoft.Extensions.Options.IOptions<MealMate.Application.Features.VoiceCalls.Options.VoiceCallOptions> _options;

    public InitiateCallCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ICallSignalingNotifier? notifier = null,
        IVoiceCallPushNotifier? pushNotifier = null,
        IVoiceCallParticipantLockService? lockService = null,
        MealMate.Application.Features.VoiceCalls.Abstractions.IDeliveryContactEligibilityService? eligibilityService = null,
        Microsoft.Extensions.Options.IOptions<MealMate.Application.Features.VoiceCalls.Options.VoiceCallOptions>? options = null)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier ?? NullCallSignalingNotifier.Instance;
        _pushNotifier = pushNotifier ?? NullVoiceCallPushNotifier.Instance;
        _lockService = lockService ?? NullVoiceCallParticipantLockService.Instance;
        // Eligibility is a hard security/policy gate: without the real service the
        // handler must fail closed rather than skip the check (G04/T04).
        _eligibilityService = eligibilityService
            ?? throw new InvalidOperationException("IDeliveryContactEligibilityService is required for call initiation.");
        _options = options ?? Microsoft.Extensions.Options.Options.Create(new MealMate.Application.Features.VoiceCalls.Options.VoiceCallOptions());
    }

    public async Task<Result<InitiateCallResponseDto>> Handle(InitiateCallCommand request, CancellationToken cancellationToken)
    {
        // 1. Authenticate caller identity from server context
        if (!_currentUser.UserId.HasValue || _currentUser.UserId.Value == Guid.Empty)
        {
            return VoiceCallErrors.Unauthorized();
        }

        var callerUserId = _currentUser.UserId.Value;

        // Legacy capability gate (T05/AC20): once the v2 client cohort is live the
        // legacy creation switch closes; existing sessions keep their read/terminal
        // paths, but no NEW call may bypass the v2 device-ownership policy.
        if (!_options.Value.LegacyInitiationEnabled)
        {
            return LegacyInitiationDisabled();
        }

        // 2. Fast idempotency with stop binding (G05): the same key for a DIFFERENT
        // stop is a conflict; an honest retry returns the existing call.
        var existingCall = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CallerUserId == callerUserId && c.ClientRequestId == request.ClientRequestId, cancellationToken);

        if (existingCall is not null)
        {
            if (existingCall.TripStopId != request.TripStopId)
            {
                return IdempotencyMismatch();
            }

            return InitiateCallResponseDto.FromEntity(existingCall);
        }

        // 3. Load TripStop with assigned delivery trip
        var stop = await _db.TripStops
            .Include(s => s.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .FirstOrDefaultAsync(s => s.Id == request.TripStopId, cancellationToken);

        if (stop is null)
        {
            return VoiceCallErrors.TripStopNotFound();
        }

        // 4. Contact-eligibility policy (fast fail for UI parity; re-checked under lock).
        var fastEligibility = await _eligibilityService.EvaluateAsync(request.TripStopId, callerUserId, cancellationToken);
        if (fastEligibility.IsFailure)
        {
            return Result.Failure<InitiateCallResponseDto>(fastEligibility.Error);
        }

        if (!fastEligibility.Value.CanInitiate)
        {
            return VoiceCallErrors.ContactNotEligible(fastEligibility.Value.ReasonCode);
        }

        // 5. Resolve Customer and Driver identities through the SHARED participant
        // resolution (G02): the driver side is the newest ACTIVE assignment only.
        var participants = await _eligibilityService.ResolveParticipantsAsync(request.TripStopId, cancellationToken);
        if (participants.IsFailure)
        {
            return Result.Failure<InitiateCallResponseDto>(participants.Error);
        }

        var customerUserId = participants.Value.CustomerUserId;
        var driverUserId = participants.Value.DriverUserId;

        // 6. Determine participant roles and authorization
        CallParticipantType callerType;
        CallParticipantType receiverType;
        Guid receiverUserId;

        if (driverUserId.HasValue && driverUserId.Value == callerUserId)
        {
            callerType = CallParticipantType.Driver;
            receiverType = CallParticipantType.Customer;

            if (!customerUserId.HasValue || customerUserId.Value == Guid.Empty)
            {
                return VoiceCallErrors.UnresolvedCustomer();
            }

            receiverUserId = customerUserId.Value;
        }
        else if (customerUserId.HasValue && customerUserId.Value == callerUserId)
        {
            callerType = CallParticipantType.Customer;
            receiverType = CallParticipantType.Driver;

            if (!driverUserId.HasValue || driverUserId.Value == Guid.Empty)
            {
                return VoiceCallErrors.UnassignedDelivery();
            }

            receiverUserId = driverUserId.Value;
        }
        else
        {
            // Authenticated user is neither the customer nor the assigned driver
            return VoiceCallErrors.TripStopNotFound();
        }

        if (callerUserId == receiverUserId)
        {
            return VoiceCallErrors.SelfCallNotAllowed();
        }

        // 7. Fast pre-lock busy detection check
        var fastCallerBusy = await _db.CallSessions
            .AsNoTracking()
            .AnyAsync(
                c => (c.CallerUserId == callerUserId || c.ReceiverUserId == callerUserId)
                     && (c.Status == CallStatus.Created
                         || c.Status == CallStatus.Ringing
                         || c.Status == CallStatus.Accepted
                         || c.Status == CallStatus.Connecting
                         || c.Status == CallStatus.Active),
                cancellationToken);

        if (fastCallerBusy)
        {
            return VoiceCallErrors.CallerBusy();
        }

        var fastReceiverBusy = await _db.CallSessions
            .AsNoTracking()
            .AnyAsync(
                c => (c.CallerUserId == receiverUserId || c.ReceiverUserId == receiverUserId)
                     && (c.Status == CallStatus.Created
                         || c.Status == CallStatus.Ringing
                         || c.Status == CallStatus.Accepted
                         || c.Status == CallStatus.Connecting
                         || c.Status == CallStatus.Active),
                cancellationToken);

        if (fastReceiverBusy)
        {
            return VoiceCallErrors.RecipientBusy();
        }

        // 8. Acquire cross-request participant locks; the whole unit runs inside the
        // retry execution strategy (F03/V01), so tracked creations are re-staged on
        // every attempt and a unique-violation reconciles the raced winner.
        CallSession? session = null;
        AuditLog? auditLog = null;

        var lockResult = await _lockService.ExecuteInParticipantLockAsync(
            callerUserId,
            receiverUserId,
            async ct =>
            {
                // Re-check idempotency under lock with stop binding (G05).
                var existingCallUnderLock = await _db.CallSessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CallerUserId == callerUserId && c.ClientRequestId == request.ClientRequestId, ct);

                if (existingCallUnderLock is not null)
                {
                    if (existingCallUnderLock.TripStopId != request.TripStopId)
                    {
                        return IdempotencyMismatchSession();
                    }

                    return Result.Success(existingCallUnderLock);
                }

                // Re-resolve participants + eligibility atomically (AC18/V02). The
                // locked values are consumed only when they MATCH the pair the locks
                // were acquired for - a changed pair is a retriable conflict.
                var lockedParticipants = await _eligibilityService.ResolveParticipantsAsync(request.TripStopId, ct);
                if (lockedParticipants.IsFailure)
                {
                    return Result.Failure<CallSession>(lockedParticipants.Error);
                }

                var lockedEligibility = await _eligibilityService.EvaluateAsync(request.TripStopId, callerUserId, ct);
                if (lockedEligibility.IsFailure)
                {
                    return Result.Failure<CallSession>(lockedEligibility.Error);
                }

                if (!lockedEligibility.Value.CanInitiate)
                {
                    return Result.Failure<CallSession>(VoiceCallErrors.ContactNotEligible(lockedEligibility.Value.ReasonCode));
                }

                if (lockedParticipants.Value.CustomerUserId != customerUserId
                    || lockedParticipants.Value.DriverUserId != driverUserId)
                {
                    return ParticipantsChanged();
                }

                // Re-check busy detection under lock to guarantee no simultaneous overlapping calls
                var callerIsBusy = await _db.CallSessions
                    .AsNoTracking()
                    .AnyAsync(
                        c => (c.CallerUserId == callerUserId || c.ReceiverUserId == callerUserId)
                             && (c.Status == CallStatus.Created
                                 || c.Status == CallStatus.Ringing
                                 || c.Status == CallStatus.Accepted
                                 || c.Status == CallStatus.Connecting
                                 || c.Status == CallStatus.Active),
                        ct);

                if (callerIsBusy)
                {
                    return Result.Failure<CallSession>(VoiceCallErrors.CallerBusy());
                }

                var receiverIsBusy = await _db.CallSessions
                    .AsNoTracking()
                    .AnyAsync(
                        c => (c.CallerUserId == receiverUserId || c.ReceiverUserId == receiverUserId)
                             && (c.Status == CallStatus.Created
                                 || c.Status == CallStatus.Ringing
                                 || c.Status == CallStatus.Accepted
                                 || c.Status == CallStatus.Connecting
                                 || c.Status == CallStatus.Active),
                        ct);

                if (receiverIsBusy)
                {
                    return Result.Failure<CallSession>(VoiceCallErrors.RecipientBusy());
                }

                // V01: entities are created ONCE (deterministic ids), but every attempt
                // RE-STAGES them - after a rolled-back commit EF marks accepted entities
                // Unchanged, so re-Add is what re-issues the INSERTs.
                if (session is null)
                {
                    var now = DateTime.UtcNow;
                    session = new CallSession(
                        tripStopId: request.TripStopId,
                        callerUserId: callerUserId,
                        callerType: callerType,
                        receiverUserId: receiverUserId,
                        receiverType: receiverType,
                        clientRequestId: request.ClientRequestId,
                        startedAtUtc: now);

                    session.MarkRinging(now);

                    auditLog = new AuditLog
                    {
                        Action = "VoiceCallInitiated",
                        EntityType = "CallSession",
                        EntityId = session.Id,
                        Reason = $"1-to-1 in-app VoIP call initiated from {callerType} to {receiverType} on TripStop {request.TripStopId}",
                        ActorUserId = callerUserId,
                        OccurredAtUtc = now
                    };
                }

                _db.CallSessions.Add(session);
                _db.AuditLogs.Add(auditLog!);

                await _db.SaveChangesAsync(ct);

                return Result.Success(session);
            },
            reconcileOnUniqueViolationAsync: async ct =>
            {
                // V07: every staged entity must leave the tracker on reconciliation.
                if (session is not null) _db.DetachTrackedEntity(session);
                if (auditLog is not null) _db.DetachTrackedEntity(auditLog);

                var racedCall = await _db.CallSessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CallerUserId == callerUserId && c.ClientRequestId == request.ClientRequestId, ct);

                return racedCall is null ? default : Result.Success(racedCall);
            },
            cancellationToken);

        if (lockResult.IsFailure)
        {
            return Result.Failure<InitiateCallResponseDto>(lockResult.Error);
        }

        var callSession = lockResult.Value;

        // 9. Post-Commit Realtime and Push (strictly AFTER the transaction commits).
        if (callSession.Status == CallStatus.Ringing)
        {
            try
            {
                await _notifier.NotifyIncomingCallAsync(
                    callSession.Id,
                    callSession.ReceiverUserId,
                    callSession.CallerUserId,
                    callSession.CallerType.ToString(),
                    callSession.TripStopId,
                    callSession.StartedAtUtc,
                    callSession.RingingAtUtc,
                    cancellationToken);

                if (callSession.RingingAtUtc.HasValue)
                {
                    await _notifier.NotifyCallRingingAsync(
                        callSession.Id,
                        callSession.CallerUserId,
                        callSession.RingingAtUtc.Value,
                        cancellationToken);
                }
            }
            catch
            {
                // Transient realtime notification errors must not rollback or fail persistent call creation
            }

            try
            {
                await _pushNotifier.SendIncomingCallPushAsync(
                    callSession.Id,
                    callSession.ReceiverUserId,
                    callSession.CallerUserId,
                    callSession.CallerType.ToString(),
                    callSession.TripStopId,
                    cancellationToken);
            }
            catch
            {
                // Transient push notification errors must not rollback or fail persistent call creation
            }
        }

        return InitiateCallResponseDto.FromEntity(callSession);
    }

    private static Result<CallSession> ParticipantsChanged() =>
        Result.Failure<CallSession>(
            Error.Conflict("calls.participants_changed",
                "The delivery participants changed while the call was being created. Retry the request."));

    private static Result<CallSession> IdempotencyMismatchSession() =>
        Result.Failure<CallSession>(
            Error.Conflict("calls.idempotency_mismatch",
                "This clientRequestId was already used for a different delivery stop."));

    private static Result<InitiateCallResponseDto> IdempotencyMismatch() =>
        Result.Failure<InitiateCallResponseDto>(
            Error.Conflict("calls.idempotency_mismatch",
                "This clientRequestId was already used for a different delivery stop."));

    private static Result<InitiateCallResponseDto> LegacyInitiationDisabled() =>
        Result.Failure<InitiateCallResponseDto>(
            Error.Conflict("calls.legacy_initiation_disabled",
                "Legacy call creation is disabled. Upgrade to the v2 voice-call protocol."));
}
