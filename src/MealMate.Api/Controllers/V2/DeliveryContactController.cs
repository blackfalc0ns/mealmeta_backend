using MealMate.Api.Common;
using MealMate.Application.Features.VoiceCalls.V2.Commands;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V2;

/// <summary>
/// F14 delivery-contact workflow surface (T14/T15): case state, hold, resume and the
/// conditional phone reveal. Identity comes from the JWT; the reveal additionally
/// requires a bound device-control session. No endpoint can fabricate qualifying
/// evidence — increments derive only from persisted terminal calls.
/// </summary>
[Route("api/v2/delivery-contact")]
[Authorize]
public sealed class DeliveryContactController : ApiControllerBase
{
    private Guid? CurrentUserId =>
        Guid.TryParse(
                User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User?.FindFirstValue(MealMate.Application.Common.Security.AppClaimTypes.UserId),
                out var id)
            ? id
            : null;

    [HttpGet("{tripStopId:guid}/case")]
    public async Task<IActionResult> GetCase(Guid tripStopId, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();

        var result = await Sender.Send(new GetDeliveryContactCaseV2Query(tripStopId, CurrentUserId.Value), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{tripStopId:guid}/hold")]
    public async Task<IActionResult> Hold(
        Guid tripStopId,
        [FromBody] DeliveryContactHoldRequest request,
        CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();

        var result = await Sender.Send(new CreateDeliveryHoldV2Command(
            tripStopId, CurrentUserId.Value, request.Notes, request.FirstAttemptCallId), cancellationToken);

        if (result.IsSuccess)
        {
            Response.Headers.CacheControl = "no-store";
        }

        return HandleResult(result);
    }

    [HttpPost("{tripStopId:guid}/resume")]
    public async Task<IActionResult> Resume(Guid tripStopId, CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();

        var result = await Sender.Send(new ResumeDeliveryHoldV2Command(
            tripStopId, CurrentUserId.Value), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{tripStopId:guid}/reveal-phone")]
    public async Task<IActionResult> RevealPhone(
        Guid tripStopId,
        [FromBody] DeliveryContactRevealRequest request,
        CancellationToken cancellationToken)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();

        var result = await Sender.Send(new RevealCustomerPhoneV2Command(
            tripStopId,
            CurrentUserId.Value,
            request.ClientRequestId,
            request.Reason,
            request.AcknowledgedPrivacyWarning), cancellationToken);

        if (result.IsSuccess)
        {
            // The revealed number is transient: no-store on every layer of caching.
            Response.Headers.CacheControl = "no-store";
        }

        return HandleResult(result);
    }
}

public sealed record DeliveryContactHoldRequest(string Notes, Guid? FirstAttemptCallId = null);

public sealed record DeliveryContactRevealRequest(
    Guid ClientRequestId,
    string Reason,
    bool AcknowledgedPrivacyWarning);
