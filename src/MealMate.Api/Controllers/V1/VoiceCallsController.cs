using MealMate.Api.Common;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Application.Features.VoiceCalls.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/voice-calls")]
[Route("api/v1/calls")]
[Authorize]
public sealed class VoiceCallsController : ApiControllerBase
{
    /// <summary>
    /// Initiates a new in-app VoIP call session between Customer and Driver on an active delivery stop.
    /// Backend validates the delivery window, participant roles, idempotency, and concurrent call presence.
    /// Protected against spam/abuse via fixed-window rate limiting.
    /// </summary>
    [HttpPost]
    [HttpPost("initiate")]
    [EnableRateLimiting("voice-calls-start")]
    public async Task<IActionResult> InitiateCall(
        [FromBody] InitiateCallRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new InitiateCallCommand(request.TripStopId, request.ClientRequestId);
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Retrieves call session details by ID. Only authorized participants (Caller or Receiver) can view the session.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetCallById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetCallByIdQuery(id), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Retrieves the current ongoing/live call session for the authenticated user (if any).
    /// </summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetActiveCall(
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetActiveCallQuery(), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Answers an incoming ringing call session and transitions it to Accepted.
    /// </summary>
    [HttpPost("{id:guid}/answer")]
    public async Task<IActionResult> AnswerCall(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new AnswerCallCommand(id), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Rejects or declines an incoming call session. Only the receiver may invoke this endpoint.
    /// </summary>
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> RejectCall(
        [FromRoute] Guid id,
        [FromBody] RejectCallRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new RejectCallCommand(id, request?.Reason), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Cancels an outgoing call before it is answered. Only the initiating caller may invoke this endpoint.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelCall(
        [FromRoute] Guid id,
        [FromBody] CancelCallRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new CancelCallCommand(id, request?.Reason), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Ends an active or pending call session and records duration from connection time.
    /// </summary>
    [HttpPost("{id:guid}/end")]
    public async Task<IActionResult> EndCall(
        [FromRoute] Guid id,
        [FromBody] EndCallRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new EndCallCommand(id, request?.Reason), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Retrieves paginated voice call history for a specific delivery stop.
    /// Ordered deterministically by CreatedAtUtc DESC, Id DESC.
    /// </summary>
    [HttpGet("history/{tripStopId:guid}")]
    public async Task<IActionResult> GetCallHistory(
        [FromRoute] Guid tripStopId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetCallHistoryForTripStopQuery(tripStopId, page, pageSize), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Retrieves temporary STUN and TURN credentials for WebRTC peer connection media traversal.
    /// Rate-limited to prevent abuse of HMAC-SHA1 credential generation.
    /// </summary>
    [HttpGet("ice-servers")]
    [EnableRateLimiting("voice-calls-ice")]
    public async Task<IActionResult> GetIceServers(
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetTurnCredentialsQuery(), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record InitiateCallRequest(
    Guid TripStopId,
    Guid ClientRequestId);

public sealed record RejectCallRequest(
    string? Reason = null);

public sealed record CancelCallRequest(
    string? Reason = null);

public sealed record EndCallRequest(
    string? Reason = null);
