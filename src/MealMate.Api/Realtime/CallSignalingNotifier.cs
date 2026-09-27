using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Constants;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MealMate.Api.Realtime;

/// <summary>
/// Dispatches real-time WebRTC voice call events to connected clients via SignalR.
/// Decoupled from FCM push notifications (deferred to Part 5).
/// </summary>
public sealed class CallSignalingNotifier : ICallSignalingNotifier
{
    private readonly IHubContext<VoiceCallHub> _hubContext;
    private readonly ILogger<CallSignalingNotifier> _logger;

    public CallSignalingNotifier(
        IHubContext<VoiceCallHub> hubContext,
        ILogger<CallSignalingNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyIncomingCallAsync(
        Guid callId,
        Guid receiverUserId,
        Guid callerUserId,
        string callerType,
        Guid? tripStopId,
        DateTime startedAtUtc,
        DateTime? ringingAtUtc,
        CancellationToken cancellationToken = default)
    {
        var receiverGroup = VoiceCallHub.GroupForUser(receiverUserId);
        var payload = new
        {
            callId,
            callerUserId,
            callerType,
            tripStopId,
            startedAtUtc,
            ringingAtUtc,
            sentAtUtc = DateTime.UtcNow
        };

        try
        {
            await _hubContext.Clients.Group(receiverGroup)
                .SendAsync(VoiceCallEvents.IncomingCall, payload, cancellationToken);
            _logger.LogInformation("Dispatched {Event} to {Group} for Call {CallId}",
                VoiceCallEvents.IncomingCall, receiverGroup, callId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR incoming call event to user group {Group}", receiverGroup);
        }
    }

    public async Task NotifyCallRingingAsync(
        Guid callId,
        Guid callerUserId,
        DateTime ringingAtUtc,
        CancellationToken cancellationToken = default)
    {
        var callerGroup = VoiceCallHub.GroupForUser(callerUserId);
        try
        {
            await _hubContext.Clients.Group(callerGroup)
                .SendAsync(VoiceCallEvents.CallRinging, new
                {
                    callId,
                    ringingAtUtc,
                    sentAtUtc = DateTime.UtcNow
                }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR call ringing event to {Group}", callerGroup);
        }
    }

    public Task NotifyCallAnsweredAsync(
        Guid callId,
        Guid callerUserId,
        DateTime answeredAtUtc,
        CancellationToken cancellationToken = default)
        => NotifyCallAnsweredAsync(callId, callerUserId, answeredAtUtc, null, cancellationToken);

    public async Task NotifyCallAnsweredAsync(
        Guid callId,
        Guid callerUserId,
        DateTime answeredAtUtc,
        Guid? receiverUserId,
        CancellationToken cancellationToken = default)
    {
        var callerGroup = VoiceCallHub.GroupForUser(callerUserId);
        var payload = new
        {
            callId,
            answeredAtUtc,
            sentAtUtc = DateTime.UtcNow
        };

        try
        {
            await _hubContext.Clients.Group(callerGroup)
                .SendAsync(VoiceCallEvents.CallAnswered, payload, cancellationToken);
            _logger.LogInformation("Dispatched {Event} to {Group} for Call {CallId}",
                VoiceCallEvents.CallAnswered, callerGroup, callId);

            // Multi-device support: Notify other receiver devices so their ringing UI stops
            if (receiverUserId.HasValue && receiverUserId.Value != Guid.Empty)
            {
                var receiverGroup = VoiceCallHub.GroupForUser(receiverUserId.Value);
                await _hubContext.Clients.Group(receiverGroup)
                    .SendAsync(VoiceCallEvents.CallAnswered, payload, cancellationToken);
                _logger.LogInformation("Dispatched {Event} to receiver group {Group} for Call {CallId}",
                    VoiceCallEvents.CallAnswered, receiverGroup, callId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR call answered event for Call {CallId}", callId);
        }
    }

    public async Task NotifyCallRejectedAsync(
        Guid callId,
        Guid callerUserId,
        string? reason,
        DateTime rejectedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var callerGroup = VoiceCallHub.GroupForUser(callerUserId);
        try
        {
            await _hubContext.Clients.Group(callerGroup)
                .SendAsync(VoiceCallEvents.CallRejected, new
                {
                    callId,
                    reason,
                    rejectedAtUtc,
                    sentAtUtc = DateTime.UtcNow
                }, cancellationToken);
            _logger.LogInformation("Dispatched {Event} to {Group} for Call {CallId}",
                VoiceCallEvents.CallRejected, callerGroup, callId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR call rejected event to user group {Group}", callerGroup);
        }
    }

    public async Task NotifyCallCancelledAsync(
        Guid callId,
        Guid receiverUserId,
        string? reason,
        DateTime cancelledAtUtc,
        CancellationToken cancellationToken = default)
    {
        var receiverGroup = VoiceCallHub.GroupForUser(receiverUserId);
        try
        {
            await _hubContext.Clients.Group(receiverGroup)
                .SendAsync(VoiceCallEvents.CallCancelled, new
                {
                    callId,
                    reason,
                    cancelledAtUtc,
                    sentAtUtc = DateTime.UtcNow
                }, cancellationToken);
            _logger.LogInformation("Dispatched {Event} to {Group} for Call {CallId}",
                VoiceCallEvents.CallCancelled, receiverGroup, callId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR call cancelled event to user group {Group}", receiverGroup);
        }
    }

    public async Task NotifyCallConnectingAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        DateTime timestampUtc,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            callId,
            timestampUtc,
            sentAtUtc = DateTime.UtcNow
        };

        try
        {
            await _hubContext.Clients.Group(VoiceCallHub.GroupForUser(callerUserId))
                .SendAsync(VoiceCallEvents.CallConnecting, payload, cancellationToken);
            await _hubContext.Clients.Group(VoiceCallHub.GroupForUser(receiverUserId))
                .SendAsync(VoiceCallEvents.CallConnecting, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR call connecting event for call {CallId}", callId);
        }
    }

    public async Task NotifyCallConnectedAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        DateTime connectedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            callId,
            connectedAtUtc,
            sentAtUtc = DateTime.UtcNow
        };

        try
        {
            await _hubContext.Clients.Group(VoiceCallHub.GroupForUser(callerUserId))
                .SendAsync(VoiceCallEvents.CallConnected, payload, cancellationToken);
            await _hubContext.Clients.Group(VoiceCallHub.GroupForUser(receiverUserId))
                .SendAsync(VoiceCallEvents.CallConnected, payload, cancellationToken);
            _logger.LogInformation("Dispatched {Event} for Call {CallId}",
                VoiceCallEvents.CallConnected, callId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR call connected event for call {CallId}", callId);
        }
    }

    public async Task NotifyCallEndedAsync(
        Guid callId,
        Guid peerUserId,
        string? reason,
        int? durationSeconds,
        DateTime endedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var peerGroup = VoiceCallHub.GroupForUser(peerUserId);
        var payload = new
        {
            callId,
            reason,
            durationSeconds,
            endedAtUtc,
            sentAtUtc = DateTime.UtcNow
        };

        try
        {
            await _hubContext.Clients.Group(peerGroup)
                .SendAsync(VoiceCallEvents.CallEnded, payload, cancellationToken);
            _logger.LogInformation("Dispatched {Event} to {Group} for Call {CallId}",
                VoiceCallEvents.CallEnded, peerGroup, callId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR call ended event for call {CallId}", callId);
        }
    }

    public async Task NotifyCallMissedAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        DateTime missedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            callId,
            missedAtUtc,
            sentAtUtc = DateTime.UtcNow
        };

        try
        {
            await _hubContext.Clients.Group(VoiceCallHub.GroupForUser(callerUserId))
                .SendAsync(VoiceCallEvents.CallMissed, payload, cancellationToken);
            await _hubContext.Clients.Group(VoiceCallHub.GroupForUser(receiverUserId))
                .SendAsync(VoiceCallEvents.CallMissed, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR call missed event for call {CallId}", callId);
        }
    }

    public async Task NotifyCallFailedAsync(
        Guid callId,
        Guid callerUserId,
        Guid receiverUserId,
        string? reason,
        DateTime failedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            callId,
            reason,
            failedAtUtc,
            sentAtUtc = DateTime.UtcNow
        };

        try
        {
            await _hubContext.Clients.Group(VoiceCallHub.GroupForUser(callerUserId))
                .SendAsync(VoiceCallEvents.CallFailed, payload, cancellationToken);
            await _hubContext.Clients.Group(VoiceCallHub.GroupForUser(receiverUserId))
                .SendAsync(VoiceCallEvents.CallFailed, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send SignalR call failed event for call {CallId}", callId);
        }
    }
}
