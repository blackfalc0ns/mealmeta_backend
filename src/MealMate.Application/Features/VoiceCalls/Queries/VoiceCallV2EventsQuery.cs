using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Errors;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Queries;

/// <summary>
/// Authenticated lifecycle replay for one call (T09/v2 contract §3): events read
/// from the durable outbox log, ordered by sequence, after a sequence watermark.
/// Ephemeral RTC (SDP/ICE) never replays here — the payload is lifecycle metadata only.
/// </summary>
public sealed record VoiceCallV2EventDto(
    Guid EventId,
    int ProtocolVersion,
    Guid CallId,
    long Sequence,
    string EventType,
    DateTime OccurredAtUtc);

public sealed record GetVoiceCallV2EventsQuery(Guid CallId, Guid ActorUserId, long AfterSequence) : IQuery<IReadOnlyList<VoiceCallV2EventDto>>;

public sealed class GetVoiceCallV2EventsQueryHandler : IQueryHandler<GetVoiceCallV2EventsQuery, IReadOnlyList<VoiceCallV2EventDto>>
{
    private readonly IAppDbContext _db;

    public GetVoiceCallV2EventsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<IReadOnlyList<VoiceCallV2EventDto>>> Handle(GetVoiceCallV2EventsQuery request, CancellationToken cancellationToken)
    {
        var call = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CallId, cancellationToken);

        if (call is null || (call.CallerUserId != request.ActorUserId && call.ReceiverUserId != request.ActorUserId))
        {
            return Result.Failure<IReadOnlyList<VoiceCallV2EventDto>>(VoiceCallErrors.NotFound());
        }

        var events = await _db.OutboxMessages
            .AsNoTracking()
            .Where(o => o.AggregateId == call.Id.ToString()
                        && o.EventType.StartsWith("voice-call.lifecycle.")
                        && o.ProcessedAtUtc != null)
            .OrderBy(o => o.CreatedAtUtc).ThenBy(o => o.Id)
            .ToListAsync(cancellationToken);

        var dtos = new List<VoiceCallV2EventDto>(events.Count);
        foreach (var o in events)
        {
            long sequence = 0;
            Guid eventId = o.Id;
            string eventType = o.EventType.Replace("voice-call.lifecycle.", string.Empty);
            var occurredAt = o.CreatedAtUtc;

            try
            {
                var payload = System.Text.Json.JsonDocument.Parse(o.PayloadJson).RootElement;
                if (payload.TryGetProperty("sequence", out var seq)) sequence = seq.GetInt64();
                if (payload.TryGetProperty("eventId", out var eid) && Guid.TryParse(eid.GetString(), out var parsed)) eventId = parsed;
                if (payload.TryGetProperty("eventType", out var et)) eventType = et.GetString() ?? eventType;
                if (payload.TryGetProperty("occurredAtUtc", out var oc) && oc.TryGetDateTime(out var ocDt)) occurredAt = ocDt;
            }
            catch (System.Text.Json.JsonException)
            {
                // Malformed payload keeps the outbox-derived fallbacks above.
            }

            if (sequence <= request.AfterSequence)
            {
                continue;
            }

            dtos.Add(new VoiceCallV2EventDto(eventId, 2, call.Id, sequence, eventType, occurredAt));
        }

        return Result.Success((IReadOnlyList<VoiceCallV2EventDto>)dtos);
    }
}
