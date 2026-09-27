using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Commands;

public sealed record AnswerCallCommand(Guid CallId) : ICommand<CallSessionDto>;

public sealed class AnswerCallCommandHandler : ICommandHandler<AnswerCallCommand, CallSessionDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier _notifier;

    public AnswerCallCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier? notifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier ?? MealMate.Application.Features.VoiceCalls.Abstractions.NullCallSignalingNotifier.Instance;
    }

    public async Task<Result<CallSessionDto>> Handle(AnswerCallCommand request, CancellationToken cancellationToken)
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

        // Only the designated receiver can answer the call
        if (session.ReceiverUserId != currentUserId)
        {
            return VoiceCallErrors.Forbidden();
        }

        // Verify valid call state before transition
        if (session.Status == CallStatus.Accepted)
        {
            // Idempotent retry: already accepted by this receiver
            return CallSessionDto.FromEntity(session);
        }

        if (session.Status == CallStatus.Cancelled)
        {
            return VoiceCallErrors.CallAlreadyCancelled();
        }

        if (session.Status != CallStatus.Ringing)
        {
            return VoiceCallErrors.InvalidState($"Cannot answer call from status '{session.Status}'. Call must be in Ringing status.");
        }

        var now = DateTime.UtcNow;
        session.Accept(now);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Re-fetch winning state from database to determine resolution
            var reloaded = await _db.CallSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

            if (reloaded is null)
            {
                return VoiceCallErrors.NotFound();
            }

            if (reloaded.Status == CallStatus.Cancelled)
            {
                return VoiceCallErrors.CallAlreadyCancelled();
            }

            if (reloaded.Status == CallStatus.Accepted)
            {
                return CallSessionDto.FromEntity(reloaded);
            }

            return VoiceCallErrors.ConcurrencyConflict();
        }

        try
        {
            await _notifier.NotifyCallAnsweredAsync(
                session.Id,
                session.CallerUserId,
                session.AcceptedAtUtc ?? now,
                session.ReceiverUserId,
                cancellationToken);
        }
        catch
        {
            // Transient notification failure must not rollback or fail persistent call state
        }

        return CallSessionDto.FromEntity(session);
    }
}
