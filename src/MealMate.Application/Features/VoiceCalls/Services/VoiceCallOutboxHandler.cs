using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.VoiceCalls;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.VoiceCalls.Services;

/// <summary>
/// V05/T10: the durable lifecycle consumer for voice-call outbox events. For every
/// `voice-call.lifecycle.*` message it (1) re-loads the authoritative call state,
/// (2) suppresses obsolete events (a terminal call must never ring), (3) creates
/// idempotent per-(event, channel, device) delivery records, and (4) dispatches
/// through the real transports. A dispatch failure propagates, so the outbox worker
/// leaves the message unprocessed and retries — an unacknowledged event is never
/// marked complete, and no handler means no voice event can silently "succeed".
/// </summary>
public sealed class VoiceCallOutboxHandler : INotificationHandler<OutboxLifecycleNotification>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IVoiceCallPushNotifier _pushNotifier;
    private readonly ICallSignalingNotifier _signalingNotifier;
    private readonly ILogger<VoiceCallOutboxHandler> _logger;

    public VoiceCallOutboxHandler(
        IAppDbContext db,
        IDateTimeProvider clock,
        IVoiceCallPushNotifier pushNotifier,
        ICallSignalingNotifier? signalingNotifier = null,
        ILogger<VoiceCallOutboxHandler>? logger = null)
    {
        _db = db;
        _clock = clock;
        _pushNotifier = pushNotifier;
        _signalingNotifier = signalingNotifier ?? NullCallSignalingNotifier.Instance;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<VoiceCallOutboxHandler>.Instance;
    }

    public async Task Handle(OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        if (!notification.EventType.StartsWith("voice-call.lifecycle.", StringComparison.Ordinal))
        {
            return; // unrelated domain event — other consumers own it
        }

        var payload = JsonDocument.Parse(notification.PayloadJson).RootElement;
        var callId = payload.GetProperty("callId").GetGuid();
        var eventType = payload.GetProperty("eventType").GetString()!;
        var sequence = payload.TryGetProperty("sequence", out var seqEl) ? seqEl.GetInt64() : 0;

        var call = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == callId, cancellationToken);

        if (call is null)
        {
            // Committed event without its call row is a data inconsistency: fail the
            // attempt so the worker retries instead of acknowledging a phantom event.
            throw new InvalidOperationException($"Voice-call lifecycle event {notification.MessageId} references missing call {callId}.");
        }

        // Obsolete-incoming guard: a call that already left Ringing (or ended) must
        // never receive a fresh ring — record the suppression durably instead.
        var isObsoleteIncoming = eventType == "incoming" &&
                                 (call.IsTerminal || call.Status != CallStatus.Ringing);

        var recipientUserIds = eventType == "incoming"
            ? new[] { call.ReceiverUserId }
            : new[] { call.CallerUserId, call.ReceiverUserId };

        var deviceSessions = await _db.VoiceCallDeviceSessions
            .AsNoTracking()
            .Where(s => recipientUserIds.Contains(s.UserId) && s.RevokedAtUtc == null)
            .Select(s => new { s.Id, s.UserId })
            .ToListAsync(cancellationToken);

        foreach (var recipientUserId in recipientUserIds.Distinct())
        {
            var devices = deviceSessions.Where(d => d.UserId == recipientUserId).ToList();

            // v2 calls target their bound devices; v1 (drain-compat) keeps an
            // account-level row with no device session.
            var targets = call.ProtocolVersion == 2 && devices.Count > 0
                ? devices.Select(d => (Guid?)d.Id).ToList()
                : new List<Guid?> { null };

            foreach (var deviceSessionId in targets)
            {
                var delivery = await _db.VoiceCallEventDeliveries
                    .FirstOrDefaultAsync(d => d.EventId == notification.MessageId
                                              && d.Channel == VoiceCallEventDelivery.ChannelPush
                                              && d.RecipientDeviceSessionId == deviceSessionId, cancellationToken);

                if (delivery is null)
                {
                    delivery = new VoiceCallEventDelivery(
                        notification.MessageId,
                        VoiceCallEventDelivery.ChannelPush,
                        deviceSessionId,
                        callId,
                        sequence,
                        _clock.UtcNow);
                    _db.VoiceCallEventDeliveries.Add(delivery);
                }

                if (isObsoleteIncoming)
                {
                    delivery.MarkSuppressed(_clock.UtcNow);
                    continue;
                }

                await DispatchAsync(call, eventType, notification, cancellationToken);

                // The transport accepted the event. This is AcceptedByProvider — device
                // acknowledgement (ringing-ack/terminal ack) is a separate receipt.
                delivery.MarkAccepted(providerMessageId: null, _clock.UtcNow);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (isObsoleteIncoming)
        {
            _logger.LogInformation("Voice lifecycle event {EventId} suppressed for terminal/moved call {CallId}.", notification.MessageId, callId);
        }
    }

    private async Task DispatchAsync(CallSession call, string eventType, OutboxLifecycleNotification notification, CancellationToken cancellationToken)
    {
        switch (eventType)
        {
            case "incoming":
                await _pushNotifier.SendIncomingCallPushAsync(
                    call.Id, call.ReceiverUserId, call.CallerUserId,
                    call.CallerType.ToString(), call.TripStopId, cancellationToken);
                await _signalingNotifier.NotifyIncomingCallAsync(
                    call.Id, call.ReceiverUserId, call.CallerUserId,
                    call.CallerType.ToString(), call.TripStopId,
                    call.StartedAtUtc, call.RingingAtUtc, cancellationToken);
                break;
            case "cancelled":
                await _pushNotifier.SendCallCancelledPushAsync(call.Id, call.ReceiverUserId, cancellationToken);
                await _signalingNotifier.NotifyCallCancelledAsync(
                    call.Id, call.ReceiverUserId, call.EndReason?.ToString(), call.EndedAtUtc ?? _clock.UtcNow, cancellationToken);
                break;
            case "missed":
                await _pushNotifier.SendCallMissedPushAsync(call.Id, call.ReceiverUserId, cancellationToken);
                break;
            case "answered":
                await _signalingNotifier.NotifyCallAnsweredAsync(
                    call.Id, call.CallerUserId, call.AcceptedAtUtc ?? _clock.UtcNow,
                    call.ReceiverUserId, cancellationToken);
                break;
            case "rejected":
                await _signalingNotifier.NotifyCallRejectedAsync(
                    call.Id, call.CallerUserId, call.EndReason?.ToString(), call.EndedAtUtc ?? _clock.UtcNow, cancellationToken);
                break;
            case "ended":
                await _signalingNotifier.NotifyCallEndedAsync(
                    call.Id, call.CallerUserId, call.EndReason?.ToString(), call.DurationSeconds,
                    call.EndedAtUtc ?? _clock.UtcNow, cancellationToken);
                break;
            case "failed":
                await _signalingNotifier.NotifyCallFailedAsync(
                    call.Id, call.CallerUserId, call.ReceiverUserId, call.EndReason?.ToString(),
                    call.EndedAtUtc ?? _clock.UtcNow, cancellationToken);
                break;
            default:
                // connecting/connected have no push transport yet (T12/T13 own those);
                // the durable delivery row records the event, dispatch lands with them.
                break;
        }
    }
}
