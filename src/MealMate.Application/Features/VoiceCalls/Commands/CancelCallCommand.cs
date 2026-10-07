using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Commands;

public sealed record CancelCallCommand(
    Guid CallId,
    string? Reason = null) : ICommand<CallSessionDto>, ISelfPersistingCommand;

public sealed class CancelCallCommandHandler : ICommandHandler<CancelCallCommand, CallSessionDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICallSignalingNotifier _notifier;
    private readonly IVoiceCallPushNotifier _pushNotifier;

    public CancelCallCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ICallSignalingNotifier? notifier = null,
        IVoiceCallPushNotifier? pushNotifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier ?? NullCallSignalingNotifier.Instance;
        _pushNotifier = pushNotifier ?? NullVoiceCallPushNotifier.Instance;
    }

    public async Task<Result<CallSessionDto>> Handle(CancelCallCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue || _currentUser.UserId.Value == Guid.Empty)
        {
            return VoiceCallErrors.Unauthorized();
        }

        var currentUserId = _currentUser.UserId.Value;

        var session = await _db.CallSessions
            .FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

        if (session is null)
        {
            return VoiceCallErrors.NotFound();
        }

        // V04: legacy mutations serve legacy sessions only - a ProtocolVersion=2 call
        // is device-bound and must be controlled through the v2 surface.
        if (session.ProtocolVersion == 2)
        {
            return VoiceCallErrors.V2DeviceControlRequired();
        }

        // Only the caller can cancel an outgoing call before answer
        if (session.CallerUserId != currentUserId)
        {
            return VoiceCallErrors.Forbidden();
        }

        if (session.Status == CallStatus.Cancelled)
        {
            // Idempotent retry: already cancelled
            return CallSessionDto.FromEntity(session);
        }

        if (session.Status == CallStatus.Accepted || session.Status == CallStatus.Connecting || session.Status == CallStatus.Active)
        {
            return VoiceCallErrors.CallAlreadyAnswered();
        }

        if (session.Status != CallStatus.Created && session.Status != CallStatus.Ringing)
        {
            return VoiceCallErrors.InvalidState($"Cannot cancel call from status '{session.Status}'. Only Created or Ringing calls can be cancelled.");
        }

        var now = DateTime.UtcNow;
        session.Cancel(now);

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
            var reloaded = await _db.CallSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

            if (reloaded is null)
            {
                return VoiceCallErrors.NotFound();
            }

            if (reloaded.Status == CallStatus.Cancelled)
            {
                return CallSessionDto.FromEntity(reloaded);
            }

            if (reloaded.Status == CallStatus.Accepted || reloaded.Status == CallStatus.Connecting || reloaded.Status == CallStatus.Active)
            {
                return VoiceCallErrors.CallAlreadyAnswered();
            }

            return VoiceCallErrors.ConcurrencyConflict();
        }

        try
        {
            await _notifier.NotifyCallCancelledAsync(
                session.Id,
                session.ReceiverUserId,
                request.Reason ?? CallEndReason.CallerCancelled.ToString(),
                session.EndedAtUtc ?? now,
                cancellationToken);
        }
        catch
        {
            // Transient notification failure must not rollback or fail persistent call state
        }

        try
        {
            await _pushNotifier.SendCallCancelledPushAsync(
                session.Id,
                session.ReceiverUserId,
                cancellationToken);
        }
        catch
        {
            // Push delivery failure must not rollback or fail persistent call state
        }

        return CallSessionDto.FromEntity(session);
    }
}
