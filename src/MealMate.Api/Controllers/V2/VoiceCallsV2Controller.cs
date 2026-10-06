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

    private IActionResult NotYetImplemented(string capability) =>
        Problem(statusCode: StatusCodes.Status501NotImplemented,
            title: "Not implemented",
            detail: $"Voice-calls v2 capability '{capability}' is declared but not implemented yet.",
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
    public IActionResult GetEligibility(Guid tripStopId) => V2Enabled
        ? NotYetImplemented("eligibility")
        : Disabled();

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
    public IActionResult GetActive() => V2Enabled
        ? NotYetImplemented("active")
        : Disabled();

    [HttpGet("{callId:guid}")]
    public IActionResult GetById(Guid callId) => V2Enabled
        ? NotYetImplemented("call.byId")
        : Disabled();

    [HttpGet("history/{tripStopId:guid}")]
    public IActionResult GetHistory(Guid tripStopId) => V2Enabled
        ? NotYetImplemented("history")
        : Disabled();

    [HttpPost("{callId:guid}/ringing-ack")]
    public IActionResult AcknowledgeRinging(Guid callId) => V2Enabled
        ? NotYetImplemented("ringing-ack")
        : Disabled();

    [HttpPost("{callId:guid}/answer")]
    public IActionResult Answer(Guid callId) => V2Enabled
        ? NotYetImplemented("answer")
        : Disabled();

    [HttpPost("{callId:guid}/reject")]
    public IActionResult Reject(Guid callId) => V2Enabled
        ? NotYetImplemented("reject")
        : Disabled();

    [HttpPost("{callId:guid}/cancel")]
    public IActionResult Cancel(Guid callId) => V2Enabled
        ? NotYetImplemented("cancel")
        : Disabled();

    [HttpPost("{callId:guid}/end")]
    public IActionResult End(Guid callId) => V2Enabled
        ? NotYetImplemented("end")
        : Disabled();

    [HttpPost("{callId:guid}/heartbeat")]
    public IActionResult Heartbeat(Guid callId) => V2Enabled
        ? NotYetImplemented("heartbeat")
        : Disabled();

    [HttpGet("{callId:guid}/ice-servers")]
    public IActionResult GetIceServers(Guid callId) => V2Enabled
        ? NotYetImplemented("ice-servers")
        : Disabled();

    [HttpGet("{callId:guid}/events")]
    public IActionResult GetEvents(Guid callId) => V2Enabled
        ? NotYetImplemented("events")
        : Disabled();
}
