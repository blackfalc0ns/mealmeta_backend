using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Commands;

public sealed record MarkCallMissedCommand(Guid CallId) : ICommand<CallSessionDto>;

public sealed class MarkCallMissedCommandHandler : ICommandHandler<MarkCallMissedCommand, CallSessionDto>
{
    private readonly IAppDbContext _db;
    private readonly ICallSignalingNotifier _notifier;
    private readonly IVoiceCallPushNotifier _pushNotifier;

    public MarkCallMissedCommandHandler(
        IAppDbContext db,
        ICallSignalingNotifier? notifier = null,
        IVoiceCallPushNotifier? pushNotifier = null)
    {
        _db = db;
        _notifier = notifier ?? NullCallSignalingNotifier.Instance;
        _pushNotifier = pushNotifier ?? NullVoiceCallPushNotifier.Instance;
    }

    public async Task<Result<CallSessionDto>> Handle(MarkCallMissedCommand request, CancellationToken cancellationToken)
    {
        var session = await _db.CallSessions
            .FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

        if (session is null)
        {
            return VoiceCallErrors.NotFound();
        }

        if (session.Status == CallStatus.Missed)
        {
            return CallSessionDto.FromEntity(session);
        }

        if (session.Status != CallStatus.Created && session.Status != CallStatus.Ringing)
        {
            return VoiceCallErrors.InvalidState($"Cannot mark call as missed from status '{session.Status}'.");
        }

        var now = DateTime.UtcNow;
        session.MarkMissed(now);

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

            if (reloaded.Status == CallStatus.Missed)
            {
                return CallSessionDto.FromEntity(reloaded);
            }

            return VoiceCallErrors.ConcurrencyConflict();
        }

        try
        {
            await _notifier.NotifyCallMissedAsync(
                session.Id,
                session.CallerUserId,
                session.ReceiverUserId,
                session.EndedAtUtc ?? now,
                cancellationToken);
        }
        catch
        {
            // Transient notification failure must not rollback or fail persistent call state
        }

        try
        {
            await _pushNotifier.SendCallMissedPushAsync(
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
