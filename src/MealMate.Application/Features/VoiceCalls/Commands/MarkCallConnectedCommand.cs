using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Commands;

public sealed record MarkCallConnectedCommand(Guid CallId) : ICommand<CallSessionDto>;

public sealed class MarkCallConnectedCommandHandler : ICommandHandler<MarkCallConnectedCommand, CallSessionDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier _notifier;

    public MarkCallConnectedCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier? notifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier ?? MealMate.Application.Features.VoiceCalls.Abstractions.NullCallSignalingNotifier.Instance;
    }

    public async Task<Result<CallSessionDto>> Handle(MarkCallConnectedCommand request, CancellationToken cancellationToken)
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

        if (session.CallerUserId != currentUserId && session.ReceiverUserId != currentUserId)
        {
            return VoiceCallErrors.Forbidden();
        }

        if (session.Status == CallStatus.Active)
        {
            // Idempotent retry: already active
            return CallSessionDto.FromEntity(session);
        }

        if (session.Status != CallStatus.Connecting)
        {
            return VoiceCallErrors.InvalidState($"Cannot transition to Active from status '{session.Status}'. Call must be in Connecting status.");
        }

        var now = DateTime.UtcNow;
        session.MarkConnected(now);

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

            if (reloaded.Status == CallStatus.Active)
            {
                return CallSessionDto.FromEntity(reloaded);
            }

            return VoiceCallErrors.ConcurrencyConflict();
        }

        try
        {
            await _notifier.NotifyCallConnectedAsync(
                session.Id,
                session.CallerUserId,
                session.ReceiverUserId,
                session.ConnectedAtUtc ?? now,
                cancellationToken);
        }
        catch
        {
            // Transient notification failure must not rollback or fail persistent call state
        }

        return CallSessionDto.FromEntity(session);
    }
}
