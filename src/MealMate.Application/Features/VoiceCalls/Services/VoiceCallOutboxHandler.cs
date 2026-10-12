using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.VoiceCalls;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;

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
    private readonly MealMate.Application.Features.VoiceCalls.V2.Commands.DeliveryContactCaseService? _contactCases;
    private readonly ILogger<VoiceCallOutboxHandler> _logger;
    private readonly IVoiceCallV2PushTransport? _v2Push;
    private readonly IVoiceCallV2RealtimeTransport? _v2Realtime;

    public VoiceCallOutboxHandler(
        IAppDbContext db,
        IDateTimeProvider clock,
        IVoiceCallPushNotifier pushNotifier,
        ICallSignalingNotifier? signalingNotifier = null,
        MealMate.Application.Features.VoiceCalls.V2.Commands.DeliveryContactCaseService? contactCases = null,
        ILogger<VoiceCallOutboxHandler>? logger = null,
        IVoiceCallV2PushTransport? v2Push = null,
        IVoiceCallV2RealtimeTransport? v2Realtime = null)
    {
        _db = db;
        _clock = clock;
        _pushNotifier = pushNotifier;
        _signalingNotifier = signalingNotifier ?? NullCallSignalingNotifier.Instance;
        _contactCases = contactCases;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<VoiceCallOutboxHandler>.Instance;
        _v2Push = v2Push;
        _v2Realtime = v2Realtime;
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

        if (call.ProtocolVersion == 2)
        {
            await HandleV2Async(notification, payload, call, eventType, sequence, cancellationToken);
            return;
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
                    delivery.MarkSuppressed(_clock.UtcNow, VoiceCallPushReasons.ObsoleteLifecycle);
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

    private async Task HandleV2Async(OutboxLifecycleNotification notification, JsonElement payload,
        CallSession call, string eventType, long sequence, CancellationToken cancellationToken)
    {
        if (_v2Push is null || _v2Realtime is null)
            throw new InvalidOperationException("V2 voice transports are not registered.");
        if (eventType is not ("incoming" or "answered" or "rejected" or "cancelled" or "missed" or "ended" or "failed" or "connecting" or "connected"))
            throw new InvalidOperationException("Unsupported voice lifecycle event.");
        var eventId = payload.TryGetProperty("eventId", out var id) && id.TryGetGuid(out var parsedId)
            ? parsedId : notification.MessageId;
        var occurredAt = payload.TryGetProperty("occurredAtUtc", out var occurred) && occurred.TryGetDateTime(out var parsedTime)
            ? parsedTime : notification.CreatedAtUtc;
        var users = eventType == "incoming" ? new[] { call.ReceiverUserId } : new[] { call.CallerUserId, call.ReceiverUserId };
        var now = _clock.UtcNow;
        var devices = await _db.VoiceCallDeviceSessions
            .Where(s => users.Contains(s.UserId) && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .ToListAsync(cancellationToken);

        // Attempt qualification belongs to the committed missed event, even if
        // every recipient has logged out or temporarily lacks a push route.
        if (eventType == "missed" && _contactCases is not null)
            await _contactCases.RecordAttemptForMissedCallAsync(call, cancellationToken);

        var failures = new List<Exception>();
        foreach (var device in devices)
        {
            foreach (var channel in new[] { VoiceCallEventDelivery.ChannelSignalR, VoiceCallEventDelivery.ChannelPush })
            {
                var delivery = await _db.VoiceCallEventDeliveries.FirstOrDefaultAsync(d => d.EventId == eventId
                    && d.Channel == channel && d.RecipientDeviceSessionId == device.Id, cancellationToken);
                if (delivery?.Status is VoiceCallEventDelivery.StatusAcceptedByProvider
                    or VoiceCallEventDelivery.StatusDispatchedToHub)
                    continue;
                if (delivery?.Status is VoiceCallEventDelivery.StatusSuppressed)
                {
                    // A route-staleness suppression is recoverable while the ring is
                    // still live: a token refresh landing mid-dispatch must not drop
                    // the ring permanently. Obsolete/terminal/expired events, hub
                    // suppressions and provider-confirmed invalid routes are never
                    // resurrected — the revision guard below still runs first.
                    var recoverable = channel == VoiceCallEventDelivery.ChannelPush
                        && eventType == "incoming"
                        && (delivery.ReasonCode == VoiceCallPushReasons.InactiveFcmRoute
                            || delivery.ReasonCode == VoiceCallPushReasons.MissingOwnedFcmRoute
                            || delivery.ReasonCode == VoiceCallPushReasons.MissingFcmReference);
                    if (!recoverable)
                    {
                        continue;
                    }

                    delivery.ResetForRetry(_clock.UtcNow);
                }
                if (delivery is null)
                {
                    delivery = new(eventId, channel, device.Id, call.Id, sequence, _clock.UtcNow);
                    _db.VoiceCallEventDeliveries.Add(delivery);
                }
                // Reload before each target to suppress queued incoming rings after
                // answer/cancel/expiry. Devices still reconcile the snapshot on arrival.
                var current = await _db.CallSessions.AsNoTracking().SingleAsync(c => c.Id == call.Id, cancellationToken);
                // Revision consistency: an envelope must never carry an older event's
                // type/sequence beside a newer state snapshot. A superseded event (the
                // call's committed sequence already moved past it) is recorded as
                // obsolete_lifecycle and not dispatched — the newer staged event owns
                // the delivery. Same-sequence replays stay eligible: the crash-after-
                // send window remains at-least-once and needs client dedupe by eventId.
                if (current.Sequence > sequence
                    || (eventType == "incoming" && (current.Status != CallStatus.Ringing
                        || current.RingExpiresAtUtc is null || current.RingExpiresAtUtc <= _clock.UtcNow)))
                {
                    delivery.MarkSuppressed(_clock.UtcNow, VoiceCallPushReasons.ObsoleteLifecycle);
                    await _db.SaveChangesAsync(cancellationToken);
                    continue;
                }
                var envelope = new CallEventEnvelope<CallSnapshotDto>(eventId, 2, call.Id, sequence, eventType,
                    occurredAt, _clock.UtcNow, eventType == "incoming" ? current.RingExpiresAtUtc : null,
                    VoiceCallSnapshots.ForParticipant(current, device.UserId));
                // The devices list was loaded before the per-target loop; a token
                // refresh completing in between (even during the awaited SignalR send
                // above) moves the session's committed route. Re-read the session so
                // the push never races on the stale tracked binding — the fresh read
                // must bypass the change tracker to see the committed rebind.
                var pushDevice = device;
                if (channel == VoiceCallEventDelivery.ChannelPush)
                {
                    var freshSession = await _db.VoiceCallDeviceSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Id == device.Id, cancellationToken);
                    if (freshSession is null || freshSession.UserId != device.UserId
                        || freshSession.InstallationId != device.InstallationId
                        || freshSession.Platform != device.Platform
                        || freshSession.RevokedAtUtc is not null
                        || freshSession.ExpiresAtUtc <= _clock.UtcNow)
                    {
                        var reason = freshSession?.RevokedAtUtc is not null
                            ? VoiceCallPushReasons.SessionRevoked
                            : freshSession is not null && freshSession.ExpiresAtUtc <= _clock.UtcNow
                                ? VoiceCallPushReasons.SessionExpired
                                : VoiceCallPushReasons.MissingOwnedFcmRoute;
                        delivery.MarkSuppressed(_clock.UtcNow, reason);
                        await _db.SaveChangesAsync(cancellationToken);
                        continue;
                    }
                    pushDevice = freshSession;
                }
                // Persist the pending row before crossing the network boundary.
                // Stable identity lets devices deduplicate the crash-after-send window.
                await _db.SaveChangesAsync(cancellationToken);
                try
                {
                    if (channel == VoiceCallEventDelivery.ChannelSignalR)
                    {
                        if (await _v2Realtime.SendAsync(device.Id, envelope, cancellationToken))
                            delivery.MarkDispatchedToHub(_clock.UtcNow);
                        else delivery.MarkSuppressed(_clock.UtcNow, VoiceCallPushReasons.HubUnbound);
                    }
                    else
                    {
                        var result = await _v2Push.SendAsync(pushDevice, envelope, cancellationToken);
                        if (result.Outcome == VoiceCallPushOutcome.Accepted)
                            delivery.MarkAccepted(result.ProviderMessageId, _clock.UtcNow);
                        else if (eventType == "incoming" && result.Outcome == VoiceCallPushOutcome.NoRoute
                            && result.ReasonCode is VoiceCallPushReasons.InactiveFcmRoute
                                or VoiceCallPushReasons.MissingOwnedFcmRoute
                                or VoiceCallPushReasons.MissingFcmReference)
                        {
                            // Keep the durable outbox message pending. Its worker owns
                            // bounded retry/backoff; the next attempt rechecks revision
                            // and deadline before it can send a refreshed route.
                            delivery.MarkSuppressed(_clock.UtcNow, result.ReasonCode);
                            delivery.MarkFailedForRetry(_clock.UtcNow.AddSeconds(5), _clock.UtcNow);
                            failures.Add(new InvalidOperationException("Incoming voice push route requires retry."));
                        }
                        else delivery.MarkSuppressed(_clock.UtcNow, result.ReasonCode ?? VoiceCallPushReasons.TransportRejected);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    delivery.MarkFailedForRetry(_clock.UtcNow.AddSeconds(5), _clock.UtcNow);
                    // Safe classification only: exception type plus identifiers. Provider
                    // errors are never dumped with tokens, payloads or credentials.
                    _logger.LogWarning(
                        "Voice lifecycle dispatch failed ({ExceptionType}) for call {CallId} event {EventId} channel {Channel} device {DeviceSessionId}; retry scheduled.",
                        ex.GetType().Name, call.Id, eventId, channel, device.Id);
                    failures.Add(ex);
                }
                // Save each accepted target before moving on: a later target's
                // failure must not roll back evidence and resend successful targets.
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        if (failures.Count > 0) throw new AggregateException("Voice lifecycle delivery requires retry.", failures);
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
                // T14: durable qualified-attempt recording rides the same dispatched
                // event - crash-safe, idempotent via the unique CallId index.
                if (_contactCases is not null && call.ProtocolVersion == 2)
                {
                    await _contactCases.RecordAttemptForMissedCallAsync(call, cancellationToken);
                }
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
