using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Application.Features.VoiceCalls.Options;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MealMate.Application.Features.VoiceCalls.Commands;

/// <summary>
/// Conditional v2 expiry (T11/G08): the recovery worker passes the state, sequence and
/// deadline it SELECTED; the transition happens only when the persisted call still
/// matches all three inside the write boundary. A call that moved on (Connecting,
/// Active, terminal) is returned untouched - a stale timeout can never kill a
/// progressing call (F05).
/// </summary>
public sealed record ExpireVoiceCallCommand(
    Guid CallId,
    CallStatus ExpectedStatus,
    long ExpectedSequence,
    DateTime? ExpectedDeadlineUtc,
    CallEndReason Reason) : ICommand<CallSessionDto>, ISelfPersistingCommand;

public sealed class ExpireVoiceCallCommandHandler : ICommandHandler<ExpireVoiceCallCommand, CallSessionDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly VoiceCallOptions _options;

    public ExpireVoiceCallCommandHandler(IAppDbContext db, IDateTimeProvider clock, IOptions<VoiceCallOptions>? options = null)
    {
        _db = db;
        _clock = clock;
        _options = options?.Value ?? new VoiceCallOptions();
    }

    internal static DateTime? Deadline(CallSession call, CallEndReason reason, VoiceCallOptions options) => call.Status switch
    {
        CallStatus.Created or CallStatus.Ringing => call.RingExpiresAtUtc,
        CallStatus.Accepted => call.SetupExpiresAtUtc ?? call.AcceptedAtUtc?.AddSeconds(Math.Max(5, options.AcceptedSetupTimeoutSeconds)),
        CallStatus.Connecting => call.SetupExpiresAtUtc ?? (call.ConnectingAtUtc ?? call.AcceptedAtUtc)?.AddSeconds(Math.Max(5, options.ConnectingTimeoutSeconds)),
        CallStatus.Active when reason == CallEndReason.Timeout => call.ConnectedAtUtc?.AddMinutes(Math.Max(10, options.MaximumCallDurationMinutes)),
        CallStatus.Active => call.MediaLeaseExpiresAtUtc ?? call.ConnectedAtUtc?.AddSeconds(Math.Max(5, options.MediaLeaseSeconds)),
        _ => null
    };

    public async Task<Result<CallSessionDto>> Handle(ExpireVoiceCallCommand request, CancellationToken cancellationToken)
    {
        var call = await _db.CallSessions
            .FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

        if (call is null)
        {
            return VoiceCallErrors.NotFound();
        }

        // Conditional predicate: all three expectations must still hold at write time.
        if (call.Status != request.ExpectedStatus
            || call.Sequence != request.ExpectedSequence
            || Deadline(call, request.Reason, _options) != request.ExpectedDeadlineUtc)
        {
            // The call moved on; return the current state untouched.
            return CallSessionDto.FromEntity(call);
        }

        if (request.ExpectedDeadlineUtc.HasValue && request.ExpectedDeadlineUtc.Value > _clock.UtcNow)
        {
            // Not actually due yet (worker raced ahead of the deadline).
            return CallSessionDto.FromEntity(call);
        }

        string eventType;
        switch (request.ExpectedStatus)
        {
            case CallStatus.Created or CallStatus.Ringing:
                call.MarkMissed(_clock.UtcNow);
                eventType = "missed";
                break;
            case CallStatus.Accepted or CallStatus.Connecting:
                call.MarkFailed(_clock.UtcNow, request.Reason);
                eventType = "failed";
                break;
            case CallStatus.Active:
                call.End(_clock.UtcNow, request.Reason);
                eventType = "ended";
                break;
            default:
                return VoiceCallErrors.InvalidState(
                    $"Expiry does not apply to status '{request.ExpectedStatus}'.");
        }

        call.EnsureTerminalEventId();

        // Stage the terminal lifecycle event in the SAME commit so the durable
        // consumer records per-device delivery AND the F14 qualified attempt (T14).
        MealMate.Application.Features.VoiceCalls.V2.Commands.VoiceCallV2EventStager.Stage(
            _db, call, eventType, _clock.UtcNow);

        // Busy release + terminal lifecycle event in the SAME commit (V03).
        var busySlots = await _db.VoiceCallBusyParticipants
            .Where(b => b.CallId == call.Id)
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
            // A heartbeat or transition won after the predicate read. Never leave
            // this losing terminal update, busy release or outbox event tracked.
            if (_db is DbContext context) context.ChangeTracker.Clear();
            return VoiceCallErrors.ConcurrencyConflict();
        }
        return CallSessionDto.FromEntity(call);
    }
}
