using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Commands;

public sealed record RejectCallCommand(
    Guid CallId,
    string? Reason = null) : ICommand<CallSessionDto>, ISelfPersistingCommand;

public sealed class RejectCallCommandHandler : ICommandHandler<RejectCallCommand, CallSessionDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier _notifier;

    public RejectCallCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier? notifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier ?? MealMate.Application.Features.VoiceCalls.Abstractions.NullCallSignalingNotifier.Instance;
    }

    public async Task<Result<CallSessionDto>> Handle(RejectCallCommand request, CancellationToken cancellationToken)
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

        // Only the receiver can reject an incoming call
        if (session.ReceiverUserId != currentUserId)
        {
            return VoiceCallErrors.Forbidden();
        }

        if (session.Status == CallStatus.Rejected)
        {
            // Idempotent retry: already rejected
            return CallSessionDto.FromEntity(session);
        }

        if (session.Status == CallStatus.Cancelled)
        {
            return VoiceCallErrors.CallAlreadyCancelled();
        }

        if (session.Status != CallStatus.Created && session.Status != CallStatus.Ringing)
        {
            return VoiceCallErrors.InvalidState($"Cannot reject call from status '{session.Status}'. Only Created or Ringing calls can be rejected.");
        }

        var now = DateTime.UtcNow;
        session.Reject(now, CallEndReason.ReceiverRejected);

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

            if (reloaded.Status == CallStatus.Rejected)
            {
                return CallSessionDto.FromEntity(reloaded);
            }

            return VoiceCallErrors.ConcurrencyConflict();
        }

        try
        {
            await _notifier.NotifyCallRejectedAsync(
                session.Id,
                session.CallerUserId,
                request.Reason ?? CallEndReason.ReceiverRejected.ToString(),
                session.EndedAtUtc ?? now,
                cancellationToken);
        }
        catch
        {
            // Transient notification failure must not rollback or fail persistent call state
        }

        return CallSessionDto.FromEntity(session);
    }
}
