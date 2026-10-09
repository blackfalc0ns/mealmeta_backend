using MealMate.Application.Common.Models;
using System.Security.Claims;
using MealMate.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V2;

/// <summary>
/// v2 voice-call protocol surface (spec 7.1). Routes are declared here so the
/// contract is discoverable, but each endpoint stays blocked (501, stable error
/// code) until its implementing task lands — a route never returns fabricated
/// success. Enablement is additionally gated by VoiceCalls:V2Enabled.
/// </summary>
[Route("api/v2/voice-calls")]
[Authorize]
public sealed class VoiceCallsV2Controller : ApiControllerBase
{
    private readonly MealMate.Application.Features.VoiceCalls.Options.VoiceCallOptions _options;
    private readonly MealMate.Application.Features.VoiceCalls.Abstractions.IVoiceCallDeviceSessionService _deviceSessions;

    public VoiceCallsV2Controller(
        Microsoft.Extensions.Options.IOptions<MealMate.Application.Features.VoiceCalls.Options.VoiceCallOptions> options,
        MealMate.Application.Features.VoiceCalls.Abstractions.IVoiceCallDeviceSessionService deviceSessions)
    {
        _options = options.Value;
        _deviceSessions = deviceSessions;
    }

    private Guid? CurrentUserId =>
        Guid.TryParse(
                User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.UserId),
                out var id)
            ? id
            : null;

    private IActionResult DeviceProblem() =>
        Problem(statusCode: StatusCodes.Status403Forbidden,
            title: "Device control denied",
            detail: "A valid X-Voice-Device-Session and X-Voice-Device-Proof pair owned by the caller is required.",
            type: "about:blank");

    private IActionResult Disabled() =>
        Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Capability disabled",
            detail: "Voice-calls v2 is not enabled on this deployment.",
            type: "about:blank");

    private bool V2Enabled => _options.V2Enabled;

    /// <summary>
    /// Registers a device-control session for the authenticated owner. The control
    /// proof is returned exactly once and never stored raw.
    /// </summary>
    [HttpPost("devices/register")]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] MealMate.Application.Features.VoiceCalls.Abstractions.VoiceCallDeviceRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        if (!V2Enabled)
        {
            return Disabled();
        }

        if (!CurrentUserId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _deviceSessions.RegisterAsync(CurrentUserId.Value, request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("devices/{deviceId:guid}/revoke")]
    public async Task<IActionResult> RevokeDevice(Guid deviceId, CancellationToken cancellationToken)
    {
        if (!V2Enabled)
        {
            return Disabled();
        }

        if (!CurrentUserId.HasValue)
        {
            return Unauthorized();
        }

        var result = await _deviceSessions.RevokeAsync(CurrentUserId.Value, deviceId, cancellationToken);
        return result.IsSuccess ? NoContent() : HandleResult(result);
    }

    [HttpGet("eligibility/{tripStopId:guid}")]
    public async Task<IActionResult> GetEligibility(Guid tripStopId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        if (!CurrentUserId.HasValue) return Unauthorized();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.Queries.GetDeliveryContactEligibilityQuery(
            tripStopId, CurrentUserId.Value), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// T08: atomic v2 initiation. Device context is resolved from the authenticated
    /// JWT + control headers — never from the body.
    /// </summary>
    [HttpPost("initiate")]
    public async Task<IActionResult> Initiate(
        [FromBody] MealMate.Application.Features.VoiceCalls.V2.Commands.InitiateVoiceCallV2BodyRequest request,
        CancellationToken cancellationToken)
    {
        if (!V2Enabled)
        {
            return Disabled();
        }

        var context = await ResolveDeviceContextAsync(cancellationToken);
        if (context is null)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Device control denied",
                detail: "A valid X-Voice-Device-Session and X-Voice-Device-Proof pair owned by the caller is required.",
                type: "about:blank");
        }

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.InitiateVoiceCallV2Command(
            request.TripStopId,
            request.ClientRequestId,
            context), cancellationToken);
        return HandleResult(result);
    }

    private async Task<MealMate.Application.Features.VoiceCalls.V2.Commands.VoiceCallDeviceContext?> ResolveDeviceContextAsync(CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue)
        {
            return null;
        }

        var sessionHeader = Request.Headers["X-Voice-Device-Session"].FirstOrDefault();
        var proofHeader = Request.Headers["X-Voice-Device-Proof"].FirstOrDefault();
        if (!Guid.TryParse(sessionHeader, out var deviceSessionId) || string.IsNullOrEmpty(proofHeader))
        {
            return null;
        }

        var resolution = await _deviceSessions.ResolveAsync(CurrentUserId.Value, deviceSessionId, proofHeader, cancellationToken);
        return resolution.IsSuccess
            ? new MealMate.Application.Features.VoiceCalls.V2.Commands.VoiceCallDeviceContext(CurrentUserId.Value, deviceSessionId)
            : null;
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive(CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        if (!CurrentUserId.HasValue) return Unauthorized();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.GetActiveVoiceCallV2Query(
            CurrentUserId.Value), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{callId:guid}")]
    public async Task<IActionResult> GetById(Guid callId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        if (!CurrentUserId.HasValue) return Unauthorized();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.GetVoiceCallV2SnapshotQuery(
            callId, CurrentUserId.Value), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Call-screen display context for call participants: driver name, image URL,
    /// localized role, delivery address and the linked order data. Deliberately
    /// excludes phone numbers — those are only released by the delivery-contact
    /// reveal flow after qualified no-answer attempts.
    /// </summary>
    [HttpGet("{callId:guid}/display")]
    public async Task<IActionResult> GetCallDisplay(Guid callId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        if (!CurrentUserId.HasValue) return Unauthorized();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.Queries.VoiceCallV2CallDisplayQuery(
            callId, CurrentUserId.Value), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("history/{tripStopId:guid}")]
    public async Task<IActionResult> GetHistory(
        Guid tripStopId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!V2Enabled) return Disabled();
        if (!CurrentUserId.HasValue) return Unauthorized();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.GetVoiceCallV2HistoryQuery(
            tripStopId, CurrentUserId.Value, page, pageSize), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{callId:guid}/ringing-ack")]
    public async Task<IActionResult> AcknowledgeRinging(Guid callId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        var context = await ResolveDeviceContextAsync(cancellationToken);
        if (context is null) return DeviceProblem();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.AcknowledgeRingingV2Command(
            callId, context), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{callId:guid}/answer")]
    public async Task<IActionResult> Answer(Guid callId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        var context = await ResolveDeviceContextAsync(cancellationToken);
        if (context is null) return DeviceProblem();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.AnswerVoiceCallV2Command(
            callId, context), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{callId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid callId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        var context = await ResolveDeviceContextAsync(cancellationToken);
        if (context is null) return DeviceProblem();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.RejectVoiceCallV2Command(
            callId, context), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{callId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid callId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        var context = await ResolveDeviceContextAsync(cancellationToken);
        if (context is null) return DeviceProblem();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.CancelVoiceCallV2Command(
            callId, context), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{callId:guid}/end")]
    public async Task<IActionResult> End(Guid callId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        var context = await ResolveDeviceContextAsync(cancellationToken);
        if (context is null) return DeviceProblem();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.EndVoiceCallV2Command(
            callId, context), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{callId:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(Guid callId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        var context = await ResolveDeviceContextAsync(cancellationToken);
        if (context is null) return DeviceProblem();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.HeartbeatVoiceCallV2Command(
            callId, context), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{callId:guid}/reports/connecting")]
    public async Task<IActionResult> ReportConnecting(Guid callId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        var context = await ResolveDeviceContextAsync(cancellationToken);
        if (context is null) return DeviceProblem();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.ReportMediaStateV2Command(
            callId, context, Connected: false, IsCallerSide: false), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{callId:guid}/reports/connected")]
    public async Task<IActionResult> ReportConnected(Guid callId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        var context = await ResolveDeviceContextAsync(cancellationToken);
        if (context is null) return DeviceProblem();

        var isCaller = await IsCallerSideAsync(callId, context.UserId, cancellationToken);
        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.ReportMediaStateV2Command(
            callId, context, Connected: true, IsCallerSide: isCaller), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{callId:guid}/ice-servers")]
    public async Task<IActionResult> GetIceServers(Guid callId, CancellationToken cancellationToken)
    {
        if (!V2Enabled) return Disabled();
        var context = await ResolveDeviceContextAsync(cancellationToken);
        if (context is null) return DeviceProblem();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.Queries.GetTurnCredentialsForCallV2Query(
            callId, context.UserId), cancellationToken);
        return HandleResult(result);
    }

    private async Task<bool> IsCallerSideAsync(Guid callId, Guid userId, CancellationToken cancellationToken)
    {
        var snapshot = await Sender.Send(new MealMate.Application.Features.VoiceCalls.V2.Commands.GetVoiceCallV2SnapshotQuery(
            callId, userId), cancellationToken);
        return snapshot.Value?.LocalParticipantRole == "Caller";
    }

    [HttpGet("{callId:guid}/events")]
    public async Task<IActionResult> GetEvents(
        Guid callId,
        [FromQuery] long afterSequence = 0,
        CancellationToken cancellationToken = default)
    {
        if (!V2Enabled) return Disabled();
        if (!CurrentUserId.HasValue) return Unauthorized();

        var result = await Sender.Send(new MealMate.Application.Features.VoiceCalls.Queries.GetVoiceCallV2EventsQuery(
            callId, CurrentUserId.Value, afterSequence), cancellationToken);
        return HandleResult(result);
    }
}
