using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Commands;

public sealed record EndCallCommand(
    Guid CallId,
    string? Reason = null,
    bool IsSystem = false) : ICommand<CallSessionDto>, ISelfPersistingCommand;

public sealed class EndCallCommandHandler : ICommandHandler<EndCallCommand, CallSessionDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier _notifier;

    public EndCallCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier? notifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier ?? MealMate.Application.Features.VoiceCalls.Abstractions.NullCallSignalingNotifier.Instance;
    }

    public async Task<Result<CallSessionDto>> Handle(EndCallCommand request, CancellationToken cancellationToken)
    {
        if (!request.IsSystem)
        {
            if (!_currentUser.UserId.HasValue || _currentUser.UserId.Value == Guid.Empty)
            {
                return VoiceCallErrors.Unauthorized();
            }
        }

        var session = await _db.CallSessions
            .FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

        if (session is null)
        {
            return VoiceCallErrors.NotFound();
        }

        // V04: legacy mutations serve legacy sessions only. System recovery paths
        // (IsSystem) stay protocol-agnostic so timeouts can still terminate v2 calls.
        if (session.ProtocolVersion == 2 && !request.IsSystem)
        {
            return VoiceCallErrors.V2DeviceControlRequired();
        }

        if (!request.IsSystem)
        {
            var currentUserId = _currentUser.UserId!.Value;
            if (session.CallerUserId != currentUserId && session.ReceiverUserId != currentUserId)
            {
                return VoiceCallErrors.Forbidden();
            }
        }

        // True Idempotency: If already Ended, return current state without error
        if (session.Status == CallStatus.Ended)
        {
            return CallSessionDto.FromEntity(session);
        }

        // Incompatible terminal states cannot be converted to Ended
        if (session.Status is CallStatus.Rejected or CallStatus.Cancelled or CallStatus.Missed or CallStatus.Failed)
        {
            return CallSessionDto.FromEntity(session);
        }

        var now = DateTime.UtcNow;

        if (request.IsSystem)
        {
            if (session.Status == CallStatus.Active)
            {
                session.End(now, CallEndReason.Timeout);
            }
            else
            {
                session.MarkFailed(now, CallEndReason.Timeout);
            }
        }
        else
        {
            var currentUserId = _currentUser.UserId!.Value;
            var isCaller = session.CallerUserId == currentUserId;

            if (session.Status == CallStatus.Active)
            {
                var endReason = isCaller ? CallEndReason.CallerEnded : CallEndReason.ReceiverEnded;
                session.End(now, endReason);
            }
            else if (session.Status is CallStatus.Created or CallStatus.Ringing)
            {
                if (isCaller)
                {
                    session.Cancel(now);
                }
                else
                {
                    session.Reject(now, CallEndReason.ReceiverRejected);
                }
            }
            else if (session.Status is CallStatus.Accepted or CallStatus.Connecting)
            {
                session.MarkFailed(now, CallEndReason.ConnectionFailed);
            }
        }

        // V03: terminal mutation, Busy release and lifecycle commit as one unit -
        // a terminal call must never leave its participants permanently reserved.
        var busySlots = await _db.VoiceCallBusyParticipants
            .Where(b => b.CallId == session.Id)
            .ToListAsync(cancellationToken);
        if (busySlots.Count > 0)
        {
            _db.VoiceCallBusyParticipants.RemoveRange(busySlots);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // If concurrent termination occurred, reload state
            var reloaded = await _db.CallSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

            if (reloaded is null)
            {
                return VoiceCallErrors.NotFound();
            }

            if (reloaded.IsTerminal)
            {
                return CallSessionDto.FromEntity(reloaded);
            }

            return VoiceCallErrors.ConcurrencyConflict();
        }

        try
        {
            var endReasonStr = request.Reason ?? session.EndReason?.ToString() ?? "CallEnded";
            if (request.IsSystem)
            {
                await _notifier.NotifyCallEndedAsync(
                    session.Id,
                    session.CallerUserId,
                    endReasonStr,
                    session.DurationSeconds,
                    session.EndedAtUtc ?? now,
                    cancellationToken);

                await _notifier.NotifyCallEndedAsync(
                    session.Id,
                    session.ReceiverUserId,
                    endReasonStr,
                    session.DurationSeconds,
                    session.EndedAtUtc ?? now,
                    cancellationToken);
            }
            else
            {
                var currentUserId = _currentUser.UserId!.Value;
                var peerUserId = session.CallerUserId == currentUserId ? session.ReceiverUserId : session.CallerUserId;
                await _notifier.NotifyCallEndedAsync(
                    session.Id,
                    peerUserId,
                    endReasonStr,
                    session.DurationSeconds,
                    session.EndedAtUtc ?? now,
                    cancellationToken);
            }
        }
        catch
        {
            // Transient notification failure must not rollback or fail persistent call state
        }

        return CallSessionDto.FromEntity(session);
    }
}
