using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.V2.Commands;

/// <summary>
/// T09 v2 lifecycle mutations. Every handler: participant check (out-of-scope →
/// NotFound), device-ownership check (winning device only), domain transition under
/// the sorted participant locks, Busy release on terminal, Sequence advance and
/// lifecycle outbox staging in the SAME commit (V03/V04/V07 semantics).
/// </summary>
internal static class VoiceCallV2EventStager
{
    public static ApplicationOutboxMessage Stage(IAppDbContext db, CallSession call, string eventType, DateTime now)
    {
        var sequence = call.AdvanceSequence();
        var eventId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId,
            protocolVersion = 2,
            callId = call.Id,
            sequence,
            eventType,
            occurredAtUtc = now,
            tripStopId = call.TripStopId
        });

        var message = new ApplicationOutboxMessage
        {
            EventType = $"voice-call.lifecycle.{eventType}",
            AggregateType = "CallSession",
            AggregateId = call.Id.ToString(),
            DeduplicationKey = $"voice-call:{call.Id}:{sequence}:{eventType}",
            EventVersion = 2,
            PayloadJson = payload,
            CreatedAtUtc = now
        };
        db.OutboxMessages.Add(message);
        return message;
    }
}

internal static class VoiceCallV2Guard
{
    public static Error NotFound() => VoiceCallErrors.TripStopNotFound();

    public static bool IsParticipant(CallSession call, Guid userId) =>
        call.CallerUserId == userId || call.ReceiverUserId == userId;

    /// <summary>True when this device may act for the given side. Before a receiver claim any owned device may act.</summary>
    public static bool DeviceOwnsSide(CallSession call, bool isCaller, Guid deviceSessionId) =>
        isCaller
            ? call.CallerDeviceSessionId == deviceSessionId
            : call.ReceiverDeviceSessionId == null || call.ReceiverDeviceSessionId == deviceSessionId;

    public static CallSnapshotDto ToSnapshot(CallSession call, bool isCaller) => new(
        call.Id,
        call.TripStopId,
        2,
        call.Sequence,
        call.Status.ToString(),
        call.EndReason?.ToString(),
        call.StartedAtUtc,
        call.AcceptedAtUtc,
        call.ConnectedAtUtc,
        call.EndedAtUtc,
        call.DurationSeconds,
        call.RingExpiresAtUtc,
        call.ReceiverDeviceSessionId is { } winner && (call.Status is CallStatus.Accepted or CallStatus.Connecting or CallStatus.Active)
            ? winner
            : null,
        isCaller ? "Caller" : "Receiver");
}

// ── Answer ───────────────────────────────────────────────────────────────────

public sealed record AnswerVoiceCallV2Command(
    Guid CallId,
    VoiceCallDeviceContext Actor) : ICommand<CallSnapshotDto>, IVoiceCallV2Command;

public sealed class AnswerVoiceCallV2CommandHandler : ICommandHandler<AnswerVoiceCallV2Command, CallSnapshotDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IVoiceCallParticipantLockService _lockService;

    public AnswerVoiceCallV2CommandHandler(
        IAppDbContext db,
        IDateTimeProvider clock,
        IVoiceCallParticipantLockService? lockService = null)
    {
        _db = db;
        _clock = clock;
        _lockService = lockService ?? NullVoiceCallParticipantLockService.Instance;
    }

    public async Task<Result<CallSnapshotDto>> Handle(AnswerVoiceCallV2Command request, CancellationToken cancellationToken)
    {
        var actor = request.Actor;
        var preLoad = await _db.CallSessions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);
        if (preLoad is null || !VoiceCallV2Guard.IsParticipant(preLoad, actor.UserId) || preLoad.ReceiverUserId != actor.UserId)
        {
            return Result.Failure<CallSnapshotDto>(VoiceCallV2Guard.NotFound());
        }

        var lockResult = await _lockService.ExecuteInParticipantLockAsync(
            preLoad.CallerUserId,
            preLoad.ReceiverUserId,
            async ct =>
            {
                var call = await _db.CallSessions.FirstAsync(c => c.Id == request.CallId, ct);

                if (call.IsTerminal)
                {
                    return Result.Failure<CallSnapshotDto>(VoiceCallErrors.CallAlreadyEnded());
                }

                if (call.Status != CallStatus.Ringing)
                {
                    // Winner retry is idempotent; any other device loses atomically.
                    return call.ReceiverDeviceSessionId == actor.DeviceSessionId
                        ? Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller: false))
                        : Result.Failure<CallSnapshotDto>(AnsweredElsewhere());
                }

                call.ClaimReceiverDevice(actor.DeviceSessionId, _clock.UtcNow);
                VoiceCallV2EventStager.Stage(_db, call, "answered", _clock.UtcNow);
                await _db.SaveChangesAsync(ct);
                return Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller: false));
            },
            cancellationToken: cancellationToken);

        return lockResult.IsSuccess
            ? Result.Success(lockResult.Value)
            : Result.Failure<CallSnapshotDto>(lockResult.Error);
    }

    private static Error AnsweredElsewhere() =>
        Error.Conflict("calls.answered_elsewhere", "This call was answered on another device.");
}

// ── Reject ───────────────────────────────────────────────────────────────────

public sealed record RejectVoiceCallV2Command(
    Guid CallId,
    VoiceCallDeviceContext Actor) : ICommand<CallSnapshotDto>, IVoiceCallV2Command;

public sealed class RejectVoiceCallV2CommandHandler : ICommandHandler<RejectVoiceCallV2Command, CallSnapshotDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IVoiceCallParticipantLockService _lockService;

    public RejectVoiceCallV2CommandHandler(
        IAppDbContext db,
        IDateTimeProvider clock,
        IVoiceCallParticipantLockService? lockService = null)
    {
        _db = db;
        _clock = clock;
        _lockService = lockService ?? NullVoiceCallParticipantLockService.Instance;
    }

    public async Task<Result<CallSnapshotDto>> Handle(RejectVoiceCallV2Command request, CancellationToken cancellationToken)
    {
        var actor = request.Actor;
        var preLoad = await _db.CallSessions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);
        if (preLoad is null || !VoiceCallV2Guard.IsParticipant(preLoad, actor.UserId) || preLoad.ReceiverUserId != actor.UserId)
        {
            return Result.Failure<CallSnapshotDto>(VoiceCallV2Guard.NotFound());
        }

        if (!VoiceCallV2Guard.DeviceOwnsSide(preLoad, isCaller: false, actor.DeviceSessionId))
        {
            return Result.Failure<CallSnapshotDto>(DeviceDenied());
        }

        var lockResult = await _lockService.ExecuteInParticipantLockAsync(
            preLoad.CallerUserId,
            preLoad.ReceiverUserId,
            async ct =>
            {
                var call = await _db.CallSessions.FirstAsync(c => c.Id == request.CallId, ct);

                if (call.IsTerminal)
                {
                    return Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller: false));
                }

                if (call.Status != CallStatus.Created && call.Status != CallStatus.Ringing)
                {
                    return Result.Failure<CallSnapshotDto>(VoiceCallErrors.InvalidState(
                        $"Cannot reject a call from status '{call.Status}'."));
                }

                call.Reject(_clock.UtcNow);
                ReleaseBusy(call.Id);
                call.EnsureTerminalEventId();
                VoiceCallV2EventStager.Stage(_db, call, "rejected", _clock.UtcNow);
                await _db.SaveChangesAsync(ct);
                return Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller: false));
            },
            cancellationToken: cancellationToken);

        return lockResult.IsSuccess
            ? Result.Success(lockResult.Value)
            : Result.Failure<CallSnapshotDto>(lockResult.Error);
    }

    private void ReleaseBusy(Guid callId)
    {
        var busy = _db.VoiceCallBusyParticipants.Where(b => b.CallId == callId).ToList();
        if (busy.Count > 0)
        {
            _db.VoiceCallBusyParticipants.RemoveRange(busy);
        }
    }

    private static Error DeviceDenied() =>
        Error.Forbidden("voice.device_control_denied", "The winning device owns this call's control.");
}

// ── Cancel (caller side) ─────────────────────────────────────────────────────

public sealed record CancelVoiceCallV2Command(
    Guid CallId,
    VoiceCallDeviceContext Actor) : ICommand<CallSnapshotDto>, IVoiceCallV2Command;

public sealed class CancelVoiceCallV2CommandHandler : ICommandHandler<CancelVoiceCallV2Command, CallSnapshotDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IVoiceCallParticipantLockService _lockService;

    public CancelVoiceCallV2CommandHandler(
        IAppDbContext db,
        IDateTimeProvider clock,
        IVoiceCallParticipantLockService? lockService = null)
    {
        _db = db;
        _clock = clock;
        _lockService = lockService ?? NullVoiceCallParticipantLockService.Instance;
    }

    public async Task<Result<CallSnapshotDto>> Handle(CancelVoiceCallV2Command request, CancellationToken cancellationToken)
    {
        var actor = request.Actor;
        var preLoad = await _db.CallSessions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);
        if (preLoad is null || !VoiceCallV2Guard.IsParticipant(preLoad, actor.UserId) || preLoad.CallerUserId != actor.UserId)
        {
            return Result.Failure<CallSnapshotDto>(VoiceCallV2Guard.NotFound());
        }

        if (!VoiceCallV2Guard.DeviceOwnsSide(preLoad, isCaller: true, actor.DeviceSessionId))
        {
            return Result.Failure<CallSnapshotDto>(DeviceDenied());
        }

        var lockResult = await _lockService.ExecuteInParticipantLockAsync(
            preLoad.CallerUserId,
            preLoad.ReceiverUserId,
            async ct =>
            {
                var call = await _db.CallSessions.FirstAsync(c => c.Id == request.CallId, ct);

                if (call.IsTerminal)
                {
                    return Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller: true));
                }

                if (call.Status != CallStatus.Created && call.Status != CallStatus.Ringing)
                {
                    return Result.Failure<CallSnapshotDto>(VoiceCallErrors.InvalidState(
                        $"Cannot cancel a call from status '{call.Status}'. Use end instead."));
                }

                call.Cancel(_clock.UtcNow);
                ReleaseBusy(call.Id);
                call.EnsureTerminalEventId();
                VoiceCallV2EventStager.Stage(_db, call, "cancelled", _clock.UtcNow);
                await _db.SaveChangesAsync(ct);
                return Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller: true));
            },
            cancellationToken: cancellationToken);

        return lockResult.IsSuccess
            ? Result.Success(lockResult.Value)
            : Result.Failure<CallSnapshotDto>(lockResult.Error);
    }

    private void ReleaseBusy(Guid callId)
    {
        var busy = _db.VoiceCallBusyParticipants.Where(b => b.CallId == callId).ToList();
        if (busy.Count > 0)
        {
            _db.VoiceCallBusyParticipants.RemoveRange(busy);
        }
    }

    private static Error DeviceDenied() =>
        Error.Forbidden("voice.device_control_denied", "The winning device owns this call's control.");
}

// ── End / hangup ─────────────────────────────────────────────────────────────

public sealed record EndVoiceCallV2Command(
    Guid CallId,
    VoiceCallDeviceContext Actor) : ICommand<CallSnapshotDto>, IVoiceCallV2Command;

public sealed class EndVoiceCallV2CommandHandler : ICommandHandler<EndVoiceCallV2Command, CallSnapshotDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IVoiceCallParticipantLockService _lockService;

    public EndVoiceCallV2CommandHandler(
        IAppDbContext db,
        IDateTimeProvider clock,
        IVoiceCallParticipantLockService? lockService = null)
    {
        _db = db;
        _clock = clock;
        _lockService = lockService ?? NullVoiceCallParticipantLockService.Instance;
    }

    public async Task<Result<CallSnapshotDto>> Handle(EndVoiceCallV2Command request, CancellationToken cancellationToken)
    {
        var actor = request.Actor;
        var preLoad = await _db.CallSessions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);
        if (preLoad is null || !VoiceCallV2Guard.IsParticipant(preLoad, actor.UserId))
        {
            return Result.Failure<CallSnapshotDto>(VoiceCallV2Guard.NotFound());
        }

        var isCaller = preLoad.CallerUserId == actor.UserId;
        if (!VoiceCallV2Guard.DeviceOwnsSide(preLoad, isCaller, actor.DeviceSessionId))
        {
            return Result.Failure<CallSnapshotDto>(DeviceDenied());
        }

        var lockResult = await _lockService.ExecuteInParticipantLockAsync(
            preLoad.CallerUserId,
            preLoad.ReceiverUserId,
            async ct =>
            {
                var call = await _db.CallSessions.FirstAsync(c => c.Id == request.CallId, ct);

                if (call.IsTerminal)
                {
                    return Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller));
                }

                string eventType;
                if (call.Status == CallStatus.Active)
                {
                    call.End(_clock.UtcNow, isCaller ? CallEndReason.CallerEnded : CallEndReason.ReceiverEnded);
                    eventType = "ended";
                }
                else if (call.Status is CallStatus.Accepted or CallStatus.Connecting)
                {
                    // Explicit hangup before media connected — honest reason, never
                    // a misleading ConnectionFailed.
                    call.MarkFailed(_clock.UtcNow, CallEndReason.EndedBeforeConnect);
                    eventType = "failed";
                }
                else
                {
                    return Result.Failure<CallSnapshotDto>(VoiceCallErrors.InvalidState(
                        $"Cannot end a call from status '{call.Status}'."));
                }

                ReleaseBusy(call.Id);
                call.EnsureTerminalEventId();
                VoiceCallV2EventStager.Stage(_db, call, eventType, _clock.UtcNow);
                await _db.SaveChangesAsync(ct);
                return Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller));
            },
            cancellationToken: cancellationToken);

        return lockResult.IsSuccess
            ? Result.Success(lockResult.Value)
            : Result.Failure<CallSnapshotDto>(lockResult.Error);
    }

    private void ReleaseBusy(Guid callId)
    {
        var busy = _db.VoiceCallBusyParticipants.Where(b => b.CallId == callId).ToList();
        if (busy.Count > 0)
        {
            _db.VoiceCallBusyParticipants.RemoveRange(busy);
        }
    }

    private static Error DeviceDenied() =>
        Error.Forbidden("voice.device_control_denied", "The winning device owns this call's control.");
}

// ── Media reports ────────────────────────────────────────────────────────────

public sealed record ReportMediaStateV2Command(
    Guid CallId,
    VoiceCallDeviceContext Actor,
    bool Connected,
    bool IsCallerSide) : ICommand<CallSnapshotDto>, IVoiceCallV2Command;

public sealed class ReportMediaStateV2CommandHandler : ICommandHandler<ReportMediaStateV2Command, CallSnapshotDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IVoiceCallParticipantLockService _lockService;

    public ReportMediaStateV2CommandHandler(
        IAppDbContext db,
        IDateTimeProvider clock,
        IVoiceCallParticipantLockService? lockService = null)
    {
        _db = db;
        _clock = clock;
        _lockService = lockService ?? NullVoiceCallParticipantLockService.Instance;
    }

    public async Task<Result<CallSnapshotDto>> Handle(ReportMediaStateV2Command request, CancellationToken cancellationToken)
    {
        var actor = request.Actor;
        var preLoad = await _db.CallSessions.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);
        if (preLoad is null || !VoiceCallV2Guard.IsParticipant(preLoad, actor.UserId))
        {
            return Result.Failure<CallSnapshotDto>(VoiceCallV2Guard.NotFound());
        }

        var isCaller = preLoad.CallerUserId == actor.UserId;
        if (!VoiceCallV2Guard.DeviceOwnsSide(preLoad, isCaller, actor.DeviceSessionId))
        {
            return Result.Failure<CallSnapshotDto>(DeviceDenied());
        }

        var lockResult = await _lockService.ExecuteInParticipantLockAsync(
            preLoad.CallerUserId,
            preLoad.ReceiverUserId,
            async ct =>
            {
                var call = await _db.CallSessions.FirstAsync(c => c.Id == request.CallId, ct);

                if (call.IsTerminal)
                {
                    return Result.Failure<CallSnapshotDto>(VoiceCallErrors.CallAlreadyEnded());
                }

                var becameActive = false;
                if (!request.Connected)
                {
                    if (call.Status == CallStatus.Accepted)
                    {
                        call.MarkConnecting(_clock.UtcNow);
                        VoiceCallV2EventStager.Stage(_db, call, "connecting", _clock.UtcNow);
                    }
                    else if (call.Status != CallStatus.Connecting && call.Status != CallStatus.Active)
                    {
                        return Result.Failure<CallSnapshotDto>(VoiceCallErrors.InvalidState(
                            $"Cannot report connecting from status '{call.Status}'."));
                    }
                }
                else
                {
                    var wasActive = call.Status == CallStatus.Active;
                    call.ReportConnectedSide(isCaller, _clock.UtcNow);
                    becameActive = !wasActive && call.Status == CallStatus.Active;
                    if (becameActive)
                    {
                        VoiceCallV2EventStager.Stage(_db, call, "connected", _clock.UtcNow);
                    }
                }

                await _db.SaveChangesAsync(ct);
                return Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller));
            },
            cancellationToken: cancellationToken);

        return lockResult.IsSuccess
            ? Result.Success(lockResult.Value)
            : Result.Failure<CallSnapshotDto>(lockResult.Error);
    }

    private static Error DeviceDenied() =>
        Error.Forbidden("voice.device_control_denied", "The winning device owns this call's control.");
}

// ── Heartbeat ────────────────────────────────────────────────────────────────

public sealed record HeartbeatVoiceCallV2Command(
    Guid CallId,
    VoiceCallDeviceContext Actor) : ICommand<CallSnapshotDto>, IVoiceCallV2Command;

public sealed class HeartbeatVoiceCallV2CommandHandler : ICommandHandler<HeartbeatVoiceCallV2Command, CallSnapshotDto>
{
    /// <summary>Proposed media lease window; pending G-POLICY adoption.</summary>
    public static readonly TimeSpan MediaLeaseWindow = TimeSpan.FromSeconds(45);

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public HeartbeatVoiceCallV2CommandHandler(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<CallSnapshotDto>> Handle(HeartbeatVoiceCallV2Command request, CancellationToken cancellationToken)
    {
        var actor = request.Actor;
        var call = await _db.CallSessions.FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);
        if (call is null || !VoiceCallV2Guard.IsParticipant(call, actor.UserId))
        {
            return Result.Failure<CallSnapshotDto>(VoiceCallV2Guard.NotFound());
        }

        var isCaller = call.CallerUserId == actor.UserId;
        if (!VoiceCallV2Guard.DeviceOwnsSide(call, isCaller, actor.DeviceSessionId))
        {
            return Result.Failure<CallSnapshotDto>(DeviceDenied());
        }

        if (call.IsTerminal)
        {
            return Result.Failure<CallSnapshotDto>(VoiceCallErrors.CallAlreadyEnded());
        }

        call.ApplyHeartbeat(isCaller, _clock.UtcNow, _clock.UtcNow.Add(MediaLeaseWindow));
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller));
    }

    private static Error DeviceDenied() =>
        Error.Forbidden("voice.device_control_denied", "The winning device owns this call's control.");
}

// ── Ringing acknowledgement ──────────────────────────────────────────────────

public sealed record AcknowledgeRingingV2Command(
    Guid CallId,
    VoiceCallDeviceContext Actor) : ICommand<CallSnapshotDto>, IVoiceCallV2Command;

public sealed class AcknowledgeRingingV2CommandHandler : ICommandHandler<AcknowledgeRingingV2Command, CallSnapshotDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public AcknowledgeRingingV2CommandHandler(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<CallSnapshotDto>> Handle(AcknowledgeRingingV2Command request, CancellationToken cancellationToken)
    {
        var actor = request.Actor;
        var call = await _db.CallSessions.FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);
        if (call is null || call.ReceiverUserId != actor.UserId)
        {
            return Result.Failure<CallSnapshotDto>(VoiceCallV2Guard.NotFound());
        }

        // A receipt — an owned receiver device saw the incoming UI. It never claims
        // media ownership; the answer command does that atomically.
        var receipt = await _db.VoiceCallDeviceReceipts
            .FirstOrDefaultAsync(r => r.CallId == call.Id && r.DeviceSessionId == actor.DeviceSessionId, cancellationToken);
        if (receipt is null)
        {
            receipt = new VoiceCallDeviceReceipt(call.Id, actor.DeviceSessionId, _clock.UtcNow);
            _db.VoiceCallDeviceReceipts.Add(receipt);
        }

        call.ReceiverRingingAckAtUtc ??= _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller: false));
    }
}

// ── Snapshot queries ─────────────────────────────────────────────────────────

public sealed record GetVoiceCallV2SnapshotQuery(Guid CallId, Guid ActorUserId) : IQuery<CallSnapshotDto>;

public sealed class GetVoiceCallV2SnapshotQueryHandler : IQueryHandler<GetVoiceCallV2SnapshotQuery, CallSnapshotDto>
{
    private readonly IAppDbContext _db;

    public GetVoiceCallV2SnapshotQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<CallSnapshotDto>> Handle(GetVoiceCallV2SnapshotQuery request, CancellationToken cancellationToken)
    {
        var call = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

        if (call is null || !VoiceCallV2Guard.IsParticipant(call, request.ActorUserId))
        {
            return Result.Failure<CallSnapshotDto>(VoiceCallV2Guard.NotFound());
        }

        return Result.Success(VoiceCallV2Guard.ToSnapshot(call, isCaller: call.CallerUserId == request.ActorUserId));
    }
}

public sealed record GetActiveVoiceCallV2Query(Guid ActorUserId) : IQuery<CallSnapshotDto?>;

public sealed class GetActiveVoiceCallV2QueryHandler : IQueryHandler<GetActiveVoiceCallV2Query, CallSnapshotDto?>
{
    private readonly IAppDbContext _db;

    public GetActiveVoiceCallV2QueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<CallSnapshotDto?>> Handle(GetActiveVoiceCallV2Query request, CancellationToken cancellationToken)
    {
        var call = await _db.CallSessions
            .AsNoTracking()
            .Where(c => (c.CallerUserId == request.ActorUserId || c.ReceiverUserId == request.ActorUserId)
                        && (c.Status == CallStatus.Created || c.Status == CallStatus.Ringing
                            || c.Status == CallStatus.Accepted || c.Status == CallStatus.Connecting
                            || c.Status == CallStatus.Active)
                        && c.ProtocolVersion == 2)
            .OrderByDescending(c => c.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(call is null
            ? null
            : VoiceCallV2Guard.ToSnapshot(call, isCaller: call.CallerUserId == request.ActorUserId));
    }
}

public sealed record GetVoiceCallV2HistoryQuery(Guid TripStopId, Guid ActorUserId, int Page, int PageSize) : IQuery<PagedResult<CallSnapshotDto>>;

public sealed class GetVoiceCallV2HistoryQueryHandler : IQueryHandler<GetVoiceCallV2HistoryQuery, PagedResult<CallSnapshotDto>>
{
    private readonly IAppDbContext _db;
    private readonly IVoiceCallAuthorizationService _authorizationService;

    public GetVoiceCallV2HistoryQueryHandler(IAppDbContext db, IVoiceCallAuthorizationService authorizationService)
    {
        _db = db;
        _authorizationService = authorizationService;
    }

    public async Task<Result<PagedResult<CallSnapshotDto>>> Handle(GetVoiceCallV2HistoryQuery request, CancellationToken cancellationToken)
    {
        var authorization = await _authorizationService.AuthorizeTripStopParticipantAsync(
            request.TripStopId, request.ActorUserId, cancellationToken);
        if (authorization.IsFailure)
        {
            return Result.Failure<PagedResult<CallSnapshotDto>>(authorization.Error);
        }

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var query = _db.CallSessions
            .AsNoTracking()
            .Where(c => c.TripStopId == request.TripStopId && c.ProtocolVersion == 2);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.StartedAtUtc).ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var snapshots = items
            .Select(c => VoiceCallV2Guard.ToSnapshot(c, isCaller: c.CallerUserId == request.ActorUserId))
            .ToList();

        return Result.Success(new PagedResult<CallSnapshotDto>(snapshots, total, page, pageSize));
    }
}
