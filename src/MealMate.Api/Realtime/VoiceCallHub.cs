using System.Collections.Concurrent;
using System.Security.Claims;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Application.Features.VoiceCalls.Constants;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Domain.VoiceCalls;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Api.Realtime;

/// <summary>
/// Real-time SignalR Hub for WebRTC 1-to-1 Voice Calling signaling.
/// Relays SDP offers, SDP answers, ICE candidates, and client WebRTC lifecycle state reports.
/// Media audio flows Peer-to-Peer or via Coturn TURN relay and NEVER traverses this Hub.
/// </summary>
[Authorize]
public sealed class VoiceCallHub : Hub
{
    public const string HubPath = "/hubs/voice-call";

    // Standardized realtime event names pointing to centralized VoiceCallEvents
    public const string EventConnectionEstablished = VoiceCallEvents.ConnectionEstablished;
    public const string EventIncomingCall = VoiceCallEvents.IncomingCall;
    public const string EventCallRinging = VoiceCallEvents.CallRinging;
    public const string EventCallAnswered = VoiceCallEvents.CallAnswered;
    public const string EventCallRejected = VoiceCallEvents.CallRejected;
    public const string EventCallCancelled = VoiceCallEvents.CallCancelled;
    public const string EventCallConnecting = VoiceCallEvents.CallConnecting;
    public const string EventCallConnected = VoiceCallEvents.CallConnected;
    public const string EventCallEnded = VoiceCallEvents.CallEnded;
    public const string EventCallMissed = VoiceCallEvents.CallMissed;
    public const string EventCallFailed = VoiceCallEvents.CallFailed;

    public const string EventRtcOffer = VoiceCallEvents.RtcOffer;
    public const string EventRtcAnswer = VoiceCallEvents.RtcAnswer;
    public const string EventRtcIceCandidate = VoiceCallEvents.RtcIceCandidate;
    public const string EventPeerMuteStateChanged = VoiceCallEvents.PeerMuteStateChanged;

    private const int MaxSdpPayloadLength = 65536; // 64 KB
    private const int MaxIceCandidateLength = 4096; // 4 KB
    private const int MaxSignalingRatePerSecond = 60; // Max 60 messages/sec per connection

    // In-memory sliding rate limiter per connection
    private static readonly ConcurrentDictionary<string, (int Count, long WindowStartTicks)> RateLimiters = new();

    private readonly IAppDbContext _db;
    private readonly ISender _sender;
    private readonly ILogger<VoiceCallHub> _logger;

    public VoiceCallHub(
        IAppDbContext db,
        ISender sender,
        ILogger<VoiceCallHub> logger)
    {
        _db = db;
        _sender = sender;
        _logger = logger;
    }

    public static string GroupForUser(Guid userId) => $"user:{userId:D}";
    public static string GroupForUser(string userId) => $"user:{userId.Trim().ToLowerInvariant()}";
    public static string GroupForCall(Guid callId) => $"call:{callId:D}";
    public static string GroupForCall(string callId) => $"call:{callId.Trim().ToLowerInvariant()}";

    public override async Task OnConnectedAsync()
    {
        var userId = GetCurrentUserId();

        if (userId.HasValue)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForUser(userId.Value));
            _logger.LogInformation("VoiceCallHub connection established for user {UserId} (ConnectionId: {ConnectionId})", userId.Value, Context.ConnectionId);
        }
        else
        {
            _logger.LogWarning("Anonymous or unresolvable connection attempt on VoiceCallHub (ConnectionId: {ConnectionId})", Context.ConnectionId);
        }

        await Clients.Caller.SendAsync(EventConnectionEstablished, new
        {
            connectionId = Context.ConnectionId,
            userId = userId?.ToString("D"),
            connectedAtUtc = DateTime.UtcNow,
            status = "Connected",
            message = "Connected to MealMate Voice Call Signaling Hub."
        });

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        RateLimiters.TryRemove(Context.ConnectionId, out _);

        var userId = GetCurrentUserId();
        if (userId.HasValue)
        {
            // CRITICAL: A temporary signaling transport disconnect does NOT terminate the WebRTC call session.
            // Peer-to-peer or TURN relayed media audio may continue uninterrupted.
            _logger.LogInformation("VoiceCallHub connection disconnected for user {UserId}. Active call session is preserved.", userId.Value);
        }

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Relays a WebRTC SDP Offer from caller/offerer to the answering peer.
    /// </summary>
    public async Task SendOffer(Guid callId, RtcSessionDescriptionDto offer)
    {
        var userId = EnsureAuthenticatedUser();
        CheckSignalingRateLimit();

        if (offer is null || string.IsNullOrWhiteSpace(offer.Sdp))
            throw new HubException("SDP offer cannot be null or empty.");

        if (offer.Sdp.Length > MaxSdpPayloadLength)
            throw new HubException($"SDP offer exceeds maximum permitted size of {MaxSdpPayloadLength} characters.");

        var session = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == callId);

        if (session is null)
            throw new HubException("Call session not found.");

        if (session.ProtocolVersion == 2)
            throw new HubException("upgrade_required: this call uses the v2 protocol; signaling requires the v2 Hub with a device session.");

        if (session.CallerUserId != userId && session.ReceiverUserId != userId)
            throw new HubException("Access denied: You are not an authorized participant in this call.");

        if (!CallStatePolicy.CanSignalWebRtc(session.Status))
            throw new HubException($"Cannot relay SDP offer in call status '{session.Status}'.");

        var targetUserId = session.CallerUserId == userId ? session.ReceiverUserId : session.CallerUserId;

        _logger.LogInformation("Relaying WebRTC SDP Offer for Call {CallId} from {SenderUserId} to {TargetUserId}",
            callId, userId, targetUserId);

        var envelope = new RtcSignalingEnvelopeDto<RtcSessionDescriptionDto>(
            callId,
            userId,
            offer,
            DateTime.UtcNow);

        await Clients.Group(GroupForUser(targetUserId))
            .SendAsync(EventRtcOffer, envelope);
    }

    /// <summary>
    /// Relays a WebRTC SDP Answer from answering peer to the offerer.
    /// Supported in Accepted, Connecting, and Active (for ICE restart renegotiation).
    /// </summary>
    public async Task SendAnswer(Guid callId, RtcSessionDescriptionDto answer)
    {
        var userId = EnsureAuthenticatedUser();
        CheckSignalingRateLimit();

        if (answer is null || string.IsNullOrWhiteSpace(answer.Sdp))
            throw new HubException("SDP answer cannot be null or empty.");

        if (answer.Sdp.Length > MaxSdpPayloadLength)
            throw new HubException($"SDP answer exceeds maximum permitted size of {MaxSdpPayloadLength} characters.");

        var session = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == callId);

        if (session is null)
            throw new HubException("Call session not found.");

        if (session.ProtocolVersion == 2)
            throw new HubException("upgrade_required: this call uses the v2 protocol; signaling requires the v2 Hub with a device session.");

        if (session.CallerUserId != userId && session.ReceiverUserId != userId)
            throw new HubException("Access denied: You are not an authorized participant in this call.");

        if (!CallStatePolicy.CanSignalWebRtc(session.Status))
            throw new HubException($"Cannot relay SDP answer in call status '{session.Status}'.");

        var targetUserId = session.CallerUserId == userId ? session.ReceiverUserId : session.CallerUserId;

        _logger.LogInformation("Relaying WebRTC SDP Answer for Call {CallId} from {SenderUserId} to {TargetUserId}",
            callId, userId, targetUserId);

        var envelope = new RtcSignalingEnvelopeDto<RtcSessionDescriptionDto>(
            callId,
            userId,
            answer,
            DateTime.UtcNow);

        await Clients.Group(GroupForUser(targetUserId))
            .SendAsync(EventRtcAnswer, envelope);
    }

    /// <summary>
    /// Relays an ICE Candidate to the peer. Supported in Accepted, Connecting, and Active (for ICE restart).
    /// </summary>
    public async Task SendIceCandidate(Guid callId, RtcIceCandidateDto candidate)
    {
        var userId = EnsureAuthenticatedUser();
        CheckSignalingRateLimit();

        if (candidate is null || string.IsNullOrWhiteSpace(candidate.Candidate))
            throw new HubException("ICE candidate cannot be null or empty.");

        if (candidate.Candidate.Length > MaxIceCandidateLength)
            throw new HubException($"ICE candidate exceeds maximum permitted size of {MaxIceCandidateLength} characters.");

        var session = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == callId);

        if (session is null)
            throw new HubException("Call session not found.");

        if (session.ProtocolVersion == 2)
            throw new HubException("upgrade_required: this call uses the v2 protocol; signaling requires the v2 Hub with a device session.");

        if (session.CallerUserId != userId && session.ReceiverUserId != userId)
            throw new HubException("Access denied: You are not an authorized participant in this call.");

        // ICE candidate relaying is valid during setup and during Active calls (for ICE restarts on network switches)
        if (!CallStatePolicy.CanSignalWebRtc(session.Status))
            throw new HubException($"Cannot relay ICE candidate in call status '{session.Status}'.");

        var targetUserId = session.CallerUserId == userId ? session.ReceiverUserId : session.CallerUserId;

        // Structured logging WITHOUT exposing sensitive IP/candidate content
        _logger.LogDebug("Relaying WebRTC ICE Candidate for Call {CallId} from {SenderUserId} to {TargetUserId}",
            callId, userId, targetUserId);

        var envelope = new RtcSignalingEnvelopeDto<RtcIceCandidateDto>(
            callId,
            userId,
            candidate,
            DateTime.UtcNow);

        await Clients.Group(GroupForUser(targetUserId))
            .SendAsync(EventRtcIceCandidate, envelope);
    }

    /// <summary>
    /// Reports that the client has initiated WebRTC peer connection setup (Accepted -> Connecting).
    /// </summary>
    public async Task ReportConnecting(Guid callId)
    {
        EnsureAuthenticatedUser();
        var result = await _sender.Send(new MarkCallConnectingCommand(callId));
        if (result.IsFailure)
        {
            throw new HubException(result.Error.Description);
        }
    }

    /// <summary>
    /// Reports that WebRTC peer connection media has successfully connected (Connecting -> Active).
    /// </summary>
    public async Task ReportConnected(Guid callId)
    {
        EnsureAuthenticatedUser();
        var result = await _sender.Send(new MarkCallConnectedCommand(callId));
        if (result.IsFailure)
        {
            throw new HubException(result.Error.Description);
        }
    }

    /// <summary>
    /// Reports that WebRTC peer connection media setup failed.
    /// Maps client failure reason safely to server-side CallEndReason.
    /// </summary>
    public async Task ReportConnectionFailed(Guid callId, string? reason = null)
    {
        EnsureAuthenticatedUser();
        var clientReason = ClientConnectionFailureReasonExtensions.FromString(reason);
        var endReason = clientReason.ToCallEndReason();
        var result = await _sender.Send(new MarkCallFailedCommand(callId, endReason));
        if (result.IsFailure)
        {
            throw new HubException(result.Error.Description);
        }
    }

    /// <summary>
    /// Relays microphone mute state changes to the peer during an active call.
    /// </summary>
    public async Task MuteStateChanged(Guid callId, bool isMuted)
    {
        var userId = EnsureAuthenticatedUser();

        var session = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == callId);

        if (session is null || (session.CallerUserId != userId && session.ReceiverUserId != userId))
            return;

        var targetUserId = session.CallerUserId == userId ? session.ReceiverUserId : session.CallerUserId;

        await Clients.Group(GroupForUser(targetUserId))
            .SendAsync(EventPeerMuteStateChanged, new
            {
                callId,
                senderUserId = userId,
                isMuted,
                updatedAtUtc = DateTime.UtcNow
            });
    }

    private Guid EnsureAuthenticatedUser()
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            throw new HubException("Unauthorized: Authentication credentials required.");
        }
        return userId.Value;
    }

    private Guid? GetCurrentUserId()
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? Context.User?.FindFirstValue(AppClaimTypes.UserId)
                  ?? Context.User?.FindFirstValue("sub")
                  ?? Context.User?.FindFirstValue("uid");

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private void CheckSignalingRateLimit()
    {
        var connectionId = Context.ConnectionId;
        var nowTicks = DateTime.UtcNow.Ticks;
        var oneSecondTicks = TimeSpan.FromSeconds(1).Ticks;

        RateLimiters.AddOrUpdate(
            connectionId,
            _ => (1, nowTicks),
            (_, current) =>
            {
                if (nowTicks - current.WindowStartTicks > oneSecondTicks)
                {
                    return (1, nowTicks);
                }

                if (current.Count >= MaxSignalingRatePerSecond)
                {
                    throw new HubException("Signaling rate limit exceeded. Please throttle requests.");
                }

                return (current.Count + 1, current.WindowStartTicks);
            });
    }
}
