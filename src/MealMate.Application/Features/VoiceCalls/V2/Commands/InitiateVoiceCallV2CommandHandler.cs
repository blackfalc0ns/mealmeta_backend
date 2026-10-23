using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using VoiceCallOptions = MealMate.Application.Features.VoiceCalls.Options.VoiceCallOptions;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using ApplicationOutboxMessage = MealMate.Domain.Common.OutboxMessage;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.V2.Commands;

/// <summary>
/// v2 device-owned call initiation (T08/G01). The actor's device context is resolved
/// at the API boundary (JWT + control headers) and carried in the command — the
/// request body never supplies identity.
/// </summary>
public sealed record InitiateVoiceCallV2Command(
    Guid TripStopId,
    Guid ClientRequestId,
    VoiceCallDeviceContext Actor) : ICommand<CallSnapshotDto>, IVoiceCallV2Command;

/// <summary>Authenticated device context for a v2 mutation (resolved at the boundary).</summary>
public sealed record VoiceCallDeviceContext(Guid UserId, Guid DeviceSessionId)
{
    /// <summary>Trusted JWT expiry supplied by the API, never by the request body.</summary>
    public DateTime? AccessTokenExpiresAtUtc { get; init; }
}

/// <summary>Request body for v2 initiation — identity never comes from here.</summary>
public sealed record InitiateVoiceCallV2BodyRequest(Guid TripStopId, Guid ClientRequestId);

/// <summary>
/// T08/G01 — atomic v2 initiation. One transaction under the sorted participant locks:
/// participants re-resolution + eligibility → operation receipt → Busy rows →
/// CallSession (device-bound, deadlines) → audit + outbox lifecycle event → commit.
/// The receipt binds (actor, device, stop): an honest retry returns the current
/// snapshot without re-applying the radius rule; a key reused for a different
/// request is a 409 mismatch (G05 semantics).
/// </summary>
public sealed class InitiateVoiceCallV2CommandHandler : ICommandHandler<InitiateVoiceCallV2Command, CallSnapshotDto>
{
    public const string ReceiptScope = "voice-calls:v2:initiate";

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IDeliveryContactEligibilityService _eligibilityService;
    private readonly IVoiceCallParticipantLockService _lockService;
    private readonly ICallSignalingNotifier _notifier;
    private readonly IVoiceCallPushNotifier _pushNotifier;
    private readonly Microsoft.Extensions.Options.IOptions<VoiceCallOptions> _options;

    public InitiateVoiceCallV2CommandHandler(
        IAppDbContext db,
        IDateTimeProvider clock,
        IDeliveryContactEligibilityService eligibilityService,
        IVoiceCallParticipantLockService? lockService = null,
        ICallSignalingNotifier? notifier = null,
        IVoiceCallPushNotifier? pushNotifier = null,
        Microsoft.Extensions.Options.IOptions<VoiceCallOptions>? options = null)
    {
        _db = db;
        _clock = clock;
        _eligibilityService = eligibilityService;
        _lockService = lockService ?? NullVoiceCallParticipantLockService.Instance;
        _notifier = notifier ?? NullCallSignalingNotifier.Instance;
        _pushNotifier = pushNotifier ?? NullVoiceCallPushNotifier.Instance;
        _options = options ?? Microsoft.Extensions.Options.Options.Create(new VoiceCallOptions());
    }

    public async Task<Result<CallSnapshotDto>> Handle(InitiateVoiceCallV2Command request, CancellationToken cancellationToken)
    {
        var callerUserId = request.Actor.UserId;
        var deviceSessionId = request.Actor.DeviceSessionId;

        // 1. Fast receipt lookup FIRST — it is keyed by (actor, key) alone, so an honest
        // retry or a key-misuse probe resolves before any stop-state exposure (G05/G07).
        var requestHash = ComputeRequestHash(request.TripStopId, deviceSessionId);
        var existingReceipt = await _db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Scope == ReceiptScope
                                      && r.Key == request.ClientRequestId.ToString("N")
                                      && r.UserId == callerUserId, cancellationToken);
        if (existingReceipt is not null)
        {
            if (existingReceipt.RequestHash != requestHash || existingReceipt.ResourceUri is null)
            {
                return Result.Failure<CallSnapshotDto>(IdempotencyMismatch());
            }

            var receiptCallId = Guid.Parse(existingReceipt.ResourceUri);
            var receiptCall = await _db.CallSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == receiptCallId, cancellationToken);
            if (receiptCall is null)
            {
                return Result.Failure<CallSnapshotDto>(VoiceCallErrors.NotFound());
            }

            var receiptRole = receiptCall.CallerUserId == callerUserId
                ? receiptCall.CallerType
                : receiptCall.ReceiverType;
            return Result.Success(ToSnapshot(receiptCall, receiptRole));
        }

        if (RequiresAuthRefresh(request.Actor))
            return Result.Failure<CallSnapshotDto>(AuthRefreshRequired());

        // 2. Fast participants + eligibility (UI parity; re-checked under lock).
        var fastParticipants = await _eligibilityService.ResolveParticipantsAsync(request.TripStopId, cancellationToken);
        if (fastParticipants.IsFailure)
        {
            return Result.Failure<CallSnapshotDto>(fastParticipants.Error);
        }

        var resolved = fastParticipants.Value;
        Guid receiverUserId;
        CallParticipantType callerType;
        CallParticipantType receiverType;

        if (resolved.DriverUserId == callerUserId)
        {
            receiverUserId = resolved.CustomerUserId!.Value;
            callerType = CallParticipantType.Driver;
            receiverType = CallParticipantType.Customer;
        }
        else if (resolved.CustomerUserId == callerUserId)
        {
            if (resolved.DriverUserId is null || resolved.DriverUserId.Value == Guid.Empty)
            {
                return Result.Failure<CallSnapshotDto>(VoiceCallErrors.ContactNotEligible("contact.no_active_assignment"));
            }

            receiverUserId = resolved.DriverUserId.Value;
            callerType = CallParticipantType.Customer;
            receiverType = CallParticipantType.Driver;
        }
        else
        {
            // Out-of-scope actor learns nothing about the stop (G07 semantics).
            return Result.Failure<CallSnapshotDto>(VoiceCallErrors.TripStopNotFound());
        }

        var fastEligibility = await _eligibilityService.EvaluateAsync(request.TripStopId, callerUserId, cancellationToken);
        if (fastEligibility.IsFailure)
        {
            return Result.Failure<CallSnapshotDto>(fastEligibility.Error);
        }

        if (!fastEligibility.Value.CanInitiate)
        {
            return Result.Failure<CallSnapshotDto>(VoiceCallErrors.ContactNotEligible(fastEligibility.Value.ReasonCode));
        }

        // 3. Atomic creation under the sorted participant locks.
        CallSession? createdSession = null;
        VoiceCallBusyParticipant? stagedCallerBusy = null;
        VoiceCallBusyParticipant? stagedReceiverBusy = null;
        IdempotencyRecord? stagedReceipt = null;
        AuditLog? stagedAudit = null;
        ApplicationOutboxMessage? stagedOutbox = null;
        var lockResult = await _lockService.ExecuteInParticipantLockAsync(
            callerUserId,
            receiverUserId,
            async ct =>
            {
                // Re-resolve participants + eligibility atomically (G02/AC18/V02). The
                // locked values are CONSUMED: if the participant pair changed after the
                // fast lookup, the acquired locks guard the OLD pair — the only safe
                // outcome is a retriable conflict, never creation with stale identities.
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

                // V02 ordering: the pair-change conflict fires before anything consumes
                // the locked state — the acquired locks guard the OLD pair, so no part of
                // the write boundary may proceed with the new identities.
                if (lockedParticipants.Value.CustomerUserId != resolved.CustomerUserId
                    || lockedParticipants.Value.DriverUserId != resolved.DriverUserId)
                {
                    return ParticipantsChanged();
                }

                // Receipt reconciliation (commit ambiguity): same request → winner's call.
                var receiptUnderLock = await _db.IdempotencyRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Scope == ReceiptScope
                                              && r.Key == request.ClientRequestId.ToString("N")
                                              && r.UserId == callerUserId, ct);
                if (receiptUnderLock is not null)
                {
                    if (receiptUnderLock.RequestHash != requestHash || receiptUnderLock.ResourceUri is null)
                    {
                        return IdempotencyMismatchSession();
                    }

                    var winner = await _db.CallSessions
                        .AsNoTracking()
                        .FirstAsync(c => c.Id == Guid.Parse(receiptUnderLock.ResourceUri), ct);
                    return Result.Success(winner);
                }

                // Recheck after awaited reads/lock acquisition, before staging writes.
                if (RequiresAuthRefresh(request.Actor))
                    return Result.Failure<CallSession>(AuthRefreshRequired());

                // Busy: v2 database Busy rows (cross-role, one slot per user) or a
                // legacy ongoing call (drain-compat while v1 sessions live).
                var callerBusy = await _db.VoiceCallBusyParticipants.AnyAsync(b => b.UserId == callerUserId, ct)
                                 || await _db.CallSessions.AnyAsync(c =>
                                     (c.CallerUserId == callerUserId || c.ReceiverUserId == callerUserId)
                                     && (c.Status == CallStatus.Created
                                         || c.Status == CallStatus.Ringing
                                         || c.Status == CallStatus.Accepted
                                         || c.Status == CallStatus.Connecting
                                         || c.Status == CallStatus.Active), ct);
                if (callerBusy)
                {
                    return Result.Failure<CallSession>(VoiceCallErrors.CallerBusy());
                }

                var receiverBusy = await _db.VoiceCallBusyParticipants.AnyAsync(b => b.UserId == receiverUserId, ct)
                                   || await _db.CallSessions.AnyAsync(c =>
                                       (c.CallerUserId == receiverUserId || c.ReceiverUserId == receiverUserId)
                                       && (c.Status == CallStatus.Created
                                           || c.Status == CallStatus.Ringing
                                           || c.Status == CallStatus.Accepted
                                           || c.Status == CallStatus.Connecting
                                           || c.Status == CallStatus.Active), ct);
                if (receiverBusy)
                {
                    return Result.Failure<CallSession>(VoiceCallErrors.RecipientBusy());
                }

                // V01: entities are created ONCE (deterministic ids), but every attempt
                // RE-STAGES all of them — after a rolled-back commit EF marks accepted
                // entities Unchanged, so re-Add is what re-issues the INSERTs. Skipping
                // the re-stage returned a successful Ringing call that never existed.
                if (createdSession is null)
                {
                    var now = _clock.UtcNow;
                    createdSession = new CallSession(
                        request.TripStopId,
                        callerUserId,
                        callerType,
                        receiverUserId,
                        receiverType,
                        request.ClientRequestId,
                        now);
                    createdSession.MarkRingingV2(now, now.AddSeconds(Math.Max(5, _options.Value.RingTimeoutSeconds)), deviceSessionId);

                    stagedCallerBusy = new VoiceCallBusyParticipant(callerUserId, createdSession.Id, now);
                    stagedReceiverBusy = new VoiceCallBusyParticipant(receiverUserId, createdSession.Id, now);
                    stagedReceipt = new IdempotencyRecord
                    {
                        Scope = ReceiptScope,
                        Key = request.ClientRequestId.ToString("N"),
                        RequestHash = requestHash,
                        ResourceUri = createdSession.Id.ToString(),
                        UserId = callerUserId,
                        CreatedAtUtc = now,
                        ExpiresAtUtc = now.AddDays(1)
                    };
                    stagedAudit = new AuditLog
                    {
                        Action = "VoiceCallInitiatedV2",
                        EntityType = "CallSession",
                        EntityId = createdSession.Id,
                        Reason = $"v2 in-app VoIP call initiated from {callerType} (device {deviceSessionId}) to {receiverType} on TripStop {request.TripStopId}",
                        ActorUserId = callerUserId,
                        OccurredAtUtc = now
                    };

                    // Lifecycle event staged in the SAME transaction (V05 wires the dispatcher).
                    var eventPayload = JsonSerializer.Serialize(new
                    {
                        eventId = createdSession.Id,
                        protocolVersion = 2,
                        callId = createdSession.Id,
                        sequence = createdSession.Sequence,
                        eventType = "incoming",
                        occurredAtUtc = now,
                        tripStopId = request.TripStopId,
                        deadlineAtUtc = createdSession.RingExpiresAtUtc
                    });
                    stagedOutbox = new ApplicationOutboxMessage
                    {
                        EventType = "voice-call.lifecycle.incoming",
                        AggregateType = "CallSession",
                        AggregateId = createdSession.Id.ToString(),
                        DeduplicationKey = $"voice-call:{createdSession.Id}:{createdSession.Sequence}:incoming",
                        EventVersion = 2,
                        PayloadJson = eventPayload,
                        CreatedAtUtc = now
                    };
                }

                _db.CallSessions.Add(createdSession);
                _db.VoiceCallBusyParticipants.Add(stagedCallerBusy!);
                _db.VoiceCallBusyParticipants.Add(stagedReceiverBusy!);
                _db.IdempotencyRecords.Add(stagedReceipt!);
                _db.AuditLogs.Add(stagedAudit!);
                _db.OutboxMessages.Add(stagedOutbox!);

                await _db.SaveChangesAsync(ct);
                return Result.Success(createdSession);
            },
            reconcileOnUniqueViolationAsync: async ct =>
            {
                // V07: a racing identical request committed first (unique caller+key
                // index). Every staged entity must leave the tracker — the pipeline
                // must never re-save any of them outside the intended transaction.
                if (createdSession is not null) _db.DetachTrackedEntity(createdSession);
                if (stagedCallerBusy is not null) _db.DetachTrackedEntity(stagedCallerBusy);
                if (stagedReceiverBusy is not null) _db.DetachTrackedEntity(stagedReceiverBusy);
                if (stagedReceipt is not null) _db.DetachTrackedEntity(stagedReceipt);
                if (stagedAudit is not null) _db.DetachTrackedEntity(stagedAudit);
                if (stagedOutbox is not null) _db.DetachTrackedEntity(stagedOutbox);

                var raced = await _db.CallSessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CallerUserId == callerUserId
                                              && c.ClientRequestId == request.ClientRequestId, ct);
                return raced is null ? default : Result.Success(raced);
            },
            cancellationToken);

        if (lockResult.IsFailure)
        {
            return Result.Failure<CallSnapshotDto>(lockResult.Error);
        }

        var session = lockResult.Value;

        // V05: NO inline dispatch. The staged outbox event is the single durable send
        // path — VoiceCallOutboxHandler owns delivery, per-device records and retries,
        // so a committed call can never lose its incoming notification.

        return Result.Success(ToSnapshot(session, callerType));
    }

    private bool RequiresAuthRefresh(VoiceCallDeviceContext actor)
    {
        // Internal command callers may omit JWT metadata. The HTTP boundary always
        // supplies it. Cover the entire ring/setup window plus transport clock skew.
        var minimumSeconds = Math.Max(5d, _options.Value.RingTimeoutSeconds)
            + Math.Max(0d, _options.Value.AcceptedSetupTimeoutSeconds)
            + Math.Max(0d, _options.Value.ConnectingTimeoutSeconds) + 30d;
        return actor.AccessTokenExpiresAtUtc is { } expiry
            && (expiry - _clock.UtcNow).TotalSeconds <= minimumSeconds;
    }

    private static Error AuthRefreshRequired() => Error.Conflict("calls.auth_refresh_required",
        "Refresh your session before starting a new call, then retry with the same clientRequestId.");

    private static CallSnapshotDto ToSnapshot(CallSession session, CallParticipantType localRole) => new(
        session.Id,
        session.TripStopId,
        2,
        session.Sequence,
        session.Status.ToString(),
        session.EndReason?.ToString(),
        session.StartedAtUtc,
        session.AcceptedAtUtc,
        session.ConnectedAtUtc,
        session.EndedAtUtc,
        session.DurationSeconds,
        session.RingExpiresAtUtc,
        session.Status is CallStatus.Accepted or CallStatus.Connecting or CallStatus.Active
            ? session.ReceiverDeviceSessionId
            : null,
        localRole.ToString());

    private static string ComputeRequestHash(Guid tripStopId, Guid deviceSessionId)
    {
        var canonical = $"v2-initiate|{tripStopId}|{deviceSessionId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static Error IdempotencyMismatch() =>
        Error.Conflict("calls.idempotency_mismatch",
            "This clientRequestId was already used for a different request.");

    private static Result<CallSession> IdempotencyMismatchSession() =>
        Result.Failure<CallSession>(IdempotencyMismatch());

    private static Result<CallSession> ParticipantsChanged() =>
        Result.Failure<CallSession>(
            Error.Conflict("calls.participants_changed",
                "The delivery participants changed while the call was being created. Retry the request."));
}
