using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Commands;

public sealed record MarkCallConnectingCommand(Guid CallId) : ICommand<CallSessionDto>, ISelfPersistingCommand;

public sealed class MarkCallConnectingCommandHandler : ICommandHandler<MarkCallConnectingCommand, CallSessionDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier _notifier;

    public MarkCallConnectingCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        MealMate.Application.Features.VoiceCalls.Abstractions.ICallSignalingNotifier? notifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _notifier = notifier ?? MealMate.Application.Features.VoiceCalls.Abstractions.NullCallSignalingNotifier.Instance;
    }

    public async Task<Result<CallSessionDto>> Handle(MarkCallConnectingCommand request, CancellationToken cancellationToken)
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

        // Either participant is authorized to signal connecting state
        if (session.CallerUserId != currentUserId && session.ReceiverUserId != currentUserId)
        {
            return VoiceCallErrors.Forbidden();
        }

        if (session.Status == CallStatus.Connecting || session.Status == CallStatus.Active)
        {
            // Idempotent: already connecting or media already active
            return CallSessionDto.FromEntity(session);
        }

        if (session.Status != CallStatus.Accepted)
        {
            return VoiceCallErrors.InvalidState($"Cannot transition to Connecting from status '{session.Status}'. Call must be in Accepted status.");
        }

        var now = DateTime.UtcNow;
        session.MarkConnecting(now);

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

            if (reloaded.Status == CallStatus.Connecting || reloaded.Status == CallStatus.Active)
            {
                return CallSessionDto.FromEntity(reloaded);
            }

            return VoiceCallErrors.ConcurrencyConflict();
        }

        try
        {
            await _notifier.NotifyCallConnectingAsync(
                session.Id,
                session.CallerUserId,
                session.ReceiverUserId,
                DateTime.UtcNow,
                cancellationToken);
        }
        catch
        {
            // Transient notification failure must not rollback or fail persistent call state
        }

        return CallSessionDto.FromEntity(session);
    }
}
