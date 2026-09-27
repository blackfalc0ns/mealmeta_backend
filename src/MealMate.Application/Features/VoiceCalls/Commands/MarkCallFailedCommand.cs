using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Commands;

public sealed record MarkCallFailedCommand(
    Guid CallId,
    CallEndReason Reason = CallEndReason.ConnectionFailed,
    bool IsSystem = false) : ICommand<CallSessionDto>;

public sealed class MarkCallFailedCommandHandler : ICommandHandler<MarkCallFailedCommand, CallSessionDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier _notifier;

    public MarkCallFailedCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier? notifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier ?? MealMate.Application.Features.VoiceCalls.Abstractions.NullCallSignalingNotifier.Instance;
    }

    public async Task<Result<CallSessionDto>> Handle(MarkCallFailedCommand request, CancellationToken cancellationToken)
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

        if (!request.IsSystem)
        {
            var currentUserId = _currentUser.UserId!.Value;
            if (session.CallerUserId != currentUserId && session.ReceiverUserId != currentUserId)
            {
                return VoiceCallErrors.Forbidden();
            }
        }

        if (session.Status == CallStatus.Failed)
        {
            return CallSessionDto.FromEntity(session);
        }

        if (session.Status == CallStatus.Ended)
        {
            return VoiceCallErrors.CallAlreadyEnded();
        }

        if (session.Status != CallStatus.Created &&
            session.Status != CallStatus.Ringing &&
            session.Status != CallStatus.Accepted &&
            session.Status != CallStatus.Connecting)
        {
            return VoiceCallErrors.InvalidState($"Cannot mark call as failed from status '{session.Status}'.");
        }

        var now = DateTime.UtcNow;
        session.MarkFailed(now, request.Reason);

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

            if (reloaded.Status == CallStatus.Failed)
            {
                return CallSessionDto.FromEntity(reloaded);
            }

            return VoiceCallErrors.ConcurrencyConflict();
        }

        try
        {
            await _notifier.NotifyCallFailedAsync(
                session.Id,
                session.CallerUserId,
                session.ReceiverUserId,
                request.Reason.ToString(),
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
