using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Domain.VoiceCalls;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MealMate.Application.Features.VoiceCalls.Options;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using Microsoft.Extensions.Options;

namespace MealMate.Api.Realtime;

/// <summary>
/// v2 device-scoped signaling hub (T12). Connections authenticate with the driver/customer
/// JWT, then bind ONE owned device-control session (session id + proof) via
/// <see cref="BindDeviceSession"/>. RTC media (SDP/ICE) relays strictly between the two
/// winning device sessions of a call — never to the whole account group (F08/G09).
/// Lifecycle control stays on REST; committed lifecycle envelopes arrive here.
/// RTC invocations require four arguments: call ID, typed payload, client message
/// ID, and nonnegative negotiation generation. Identity is relayed unchanged.
/// </summary>
[Authorize]
public sealed class VoiceCallV2Hub : Hub
{
    public const string EventCall = "call:event";
    public const string EventRtcOffer = "rtc:offer";
    public const string EventRtcAnswer = "rtc:answer";
    public const string EventRtcIceCandidate = "rtc:ice-candidate";

    public const int MaxSdpPayloadBytes = 65_536;
    public const int MaxIcePayloadBytes = 4_096;
    // JSON escapes may expand one SDP byte to six bytes, plus invocation metadata.
    public const long MaximumReceiveMessageSize = 400_000;

    private readonly IAppDbContext _db;
    private readonly IVoiceCallDeviceSessionService _deviceSessions;
    private readonly ILogger<VoiceCallV2Hub> _logger;
    private readonly VoiceCallV2ConnectionRegistry _connections;
    private readonly IDateTimeProvider _clock;
    private readonly IOptions<VoiceCallOptions> _options;

    public VoiceCallV2Hub(
        IAppDbContext db,
        IVoiceCallDeviceSessionService deviceSessions,
        ILogger<VoiceCallV2Hub> logger,
        VoiceCallV2ConnectionRegistry connections,
        IDateTimeProvider clock,
        IOptions<VoiceCallOptions> options)
    {
        _db = db;
        _deviceSessions = deviceSessions;
        _logger = logger;
        _connections = connections;
        _clock = clock;
        _options = options;
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(
                Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? Context.User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.UserId),
                out var id)
            ? id
            : null;

    private Guid BoundDeviceSessionId =>
        Context.Items.TryGetValue("voice:deviceSessionId", out var value) && value is Guid id ? id : Guid.Empty;

    /// <summary>
    /// Binds the connection to one owned device-control session (proof validated
    /// server-side; never in the URL/query). Rebinding requires a valid proof again.
    /// </summary>
    public async Task BindDeviceSession(Guid deviceSessionId, string controlProof)
    {
        EnsureEnabled();
        if (CurrentUserId is not { } userId)
        {
            throw new HubException("unauthorized: connect with ?access_token=<JWT>");
        }

        var resolution = await _deviceSessions.ResolveAsync(userId, deviceSessionId, controlProof, Context.ConnectionAborted);
        if (!resolution.IsSuccess)
        {
            throw new HubException("forbidden: device session is not valid for this account");
        }

        await _connections.BindAsync(Context.ConnectionId, userId, deviceSessionId, async previous =>
        {
            if (previous.HasValue && previous.Value != deviceSessionId)
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupForDevice(previous.Value));
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForDevice(deviceSessionId));
            Context.Items["voice:deviceSessionId"] = deviceSessionId;
        });
        await Clients.Caller.SendAsync("bound", new { deviceSessionId });
    }

    /// <summary>Relays an SDP offer to the peer's winning device session only.</summary>
    public async Task SendOffer(Guid callId, RtcSessionDescriptionDto offer, Guid messageId, int negotiationGeneration)
    {
        var (senderUserId, senderDevice) = await EnsureBoundDeviceAsync();
        ValidateIdentity(messageId, negotiationGeneration);
        ValidateSdp(offer, "offer");

        var call = await LoadCallForSignalingAsync(callId, senderUserId, senderDevice);
        var (targetUserId, targetDevice) = ResolvePeer(call, senderUserId, senderDevice);

        _logger.LogInformation("v2 RTC offer for Call {CallId} from device {Device} to device {Peer}",
            callId, senderDevice, targetDevice);

        var envelope = new RtcRelayEnvelope<RtcSessionDescriptionDto>(
            messageId, callId, negotiationGeneration, senderDevice, _clock.UtcNow, offer);
        await RelayAsync(targetDevice, EventRtcOffer, envelope);
    }

    /// <summary>Relays an SDP answer to the peer's winning device session only.</summary>
    public async Task SendAnswer(Guid callId, RtcSessionDescriptionDto answer, Guid messageId, int negotiationGeneration)
    {
        var (senderUserId, senderDevice) = await EnsureBoundDeviceAsync();
        ValidateIdentity(messageId, negotiationGeneration);
        ValidateSdp(answer, "answer");

        var call = await LoadCallForSignalingAsync(callId, senderUserId, senderDevice);
        var (targetUserId, targetDevice) = ResolvePeer(call, senderUserId, senderDevice);

        var envelope = new RtcRelayEnvelope<RtcSessionDescriptionDto>(
            messageId, callId, negotiationGeneration, senderDevice, _clock.UtcNow, answer);
        await RelayAsync(targetDevice, EventRtcAnswer, envelope);
    }

    /// <summary>Relays an ICE candidate to the peer's winning device session only.</summary>
    public async Task SendIceCandidate(Guid callId, RtcIceCandidateDto candidate, Guid messageId, int negotiationGeneration)
    {
        var (senderUserId, senderDevice) = await EnsureBoundDeviceAsync();
        ValidateIdentity(messageId, negotiationGeneration);

        if (candidate is null || string.IsNullOrWhiteSpace(candidate.Candidate))
            throw new HubException("ICE candidate cannot be null or empty.");
        if (System.Text.Encoding.UTF8.GetByteCount(candidate.Candidate) > MaxIcePayloadBytes)
            throw new HubException($"ICE candidate exceeds maximum permitted size of {MaxIcePayloadBytes} bytes.");

        var call = await LoadCallForSignalingAsync(callId, senderUserId, senderDevice);
        var (_, targetDevice) = ResolvePeer(call, senderUserId, senderDevice);

        var envelope = new RtcRelayEnvelope<RtcIceCandidateDto>(
            messageId, callId, negotiationGeneration, senderDevice, _clock.UtcNow, candidate);
        await RelayAsync(targetDevice, EventRtcIceCandidate, envelope);
    }

    private async Task<(Guid UserId, Guid DeviceSessionId)> EnsureBoundDeviceAsync()
    {
        EnsureEnabled();
        if (CurrentUserId is not { } userId)
            throw new HubException("unauthorized");
        if (BoundDeviceSessionId == Guid.Empty)
            throw new HubException("device session not bound; invoke BindDeviceSession first");
        var valid = await _db.Set<VoiceCallDeviceSession>().AsNoTracking().AnyAsync(s =>
            s.Id == BoundDeviceSessionId && s.UserId == userId && s.RevokedAtUtc == null &&
            s.ExpiresAtUtc > _clock.UtcNow, Context.ConnectionAborted);
        if (!valid)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupForDevice(BoundDeviceSessionId));
            Context.Items.Remove("voice:deviceSessionId");
            _connections.Remove(Context.ConnectionId);
            throw new HubException("forbidden: device session is not valid for this account");
        }
        return (userId, BoundDeviceSessionId);
    }

    private static void ValidateSdp(RtcSessionDescriptionDto? description, string expectedType)
    {
        if (description?.Type != expectedType)
            throw new HubException($"SDP type must be '{expectedType}'.");
        var sdp = description.Sdp;
        if (string.IsNullOrWhiteSpace(sdp))
            throw new HubException("SDP cannot be null or empty.");
        if (System.Text.Encoding.UTF8.GetByteCount(sdp) > MaxSdpPayloadBytes)
            throw new HubException($"SDP exceeds maximum permitted size of {MaxSdpPayloadBytes} bytes.");
    }

    private static void ValidateIdentity(Guid messageId, int generation)
    {
        if (messageId == Guid.Empty || generation < 0)
            throw new HubException("invalid RTC message identity or negotiation generation");
    }

    private void EnsureEnabled()
    {
        if (!_options.Value.V2Enabled) throw new HubException("voice_calls_v2_disabled");
    }

    private Task RelayAsync<T>(Guid targetDevice, string eventName, T envelope) =>
        _connections.SendAsync(_db, _clock, targetDevice,
            group => Clients.Group(group).SendAsync(eventName, envelope, Context.ConnectionAborted),
            Context.ConnectionAborted);

    private async Task<CallSession> LoadCallForSignalingAsync(Guid callId, Guid userId, Guid deviceSessionId)
    {
        var call = await _db.CallSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == callId, Context.ConnectionAborted);

        if (call is null || (call.CallerUserId != userId && call.ReceiverUserId != userId))
            throw new HubException("Call session not found.");

        if (call.ProtocolVersion != 2)
            throw new HubException("upgrade_required: this hub serves v2 device-bound calls only.");

        // Only the two winning devices may exchange media: the caller's bound device
        // and the receiver device that won the atomic answer claim.
        var isCaller = call.CallerUserId == userId;
        var ownedDevice = isCaller ? call.CallerDeviceSessionId : call.ReceiverDeviceSessionId;
        if (ownedDevice != deviceSessionId)
            throw new HubException("forbidden: this device does not own the media side of the call.");

        if (!CallStatePolicy.CanSignalWebRtc(call.Status))
            throw new HubException($"Cannot relay signaling in call status '{call.Status}'.");

        return call;
    }

    private static (Guid TargetUserId, Guid TargetDevice) ResolvePeer(CallSession call, Guid senderUserId, Guid senderDevice)
    {
        var isCaller = call.CallerUserId == senderUserId;
        var targetUserId = isCaller ? call.ReceiverUserId : call.CallerUserId;
        var targetDevice = isCaller
            ? call.ReceiverDeviceSessionId
                ?? throw new HubException("peer device not yet claimed; the receiver must answer first")
            : call.CallerDeviceSessionId
                ?? throw new HubException("caller device missing; call state is inconsistent");
        return (targetUserId, targetDevice);
    }

    public static string GroupForDevice(Guid deviceSessionId) => $"voice-device:{deviceSessionId:D}";

    public override async Task OnConnectedAsync()
    {
        EnsureEnabled();
        // JWT-authenticated but media-inert until BindDeviceSession succeeds.
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
