using MealMate.Api.Common;
using MealMate.Api.Realtime;
using MealMate.Application.Features.DriverDelivery.KitchenCoordination;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Route("api/v1/restaurant/kitchen")]
[Authorize]
public sealed class KitchenDeliveryCoordinationController : ApiControllerBase
{
    private readonly IHubContext<DispatcherHub> _dispatcherHub;
    private readonly IHubContext<DriverHub> _driverHub;

    public KitchenDeliveryCoordinationController(
        IHubContext<DispatcherHub> dispatcherHub,
        IHubContext<DriverHub> driverHub)
    {
        _dispatcherHub = dispatcherHub;
        _driverHub = driverHub;
    }

    public sealed record MarkTripReadyRequest(string? Notes = null);

    public sealed record AcknowledgeReturnRequest(
        string Disposition,
        string? KitchenSupervisorNotes = null);

    /// <summary>
    /// Kitchen supervisor marks a packed meal trip ready for driver pickup.
    /// Broadcasts real-time alert to driver and dispatcher.
    /// </summary>
    [HttpPost("trips/{tripId:guid}/ready-for-pickup")]
    public async Task<IActionResult> MarkTripReady(
        [FromRoute] Guid tripId,
        [FromBody] MarkTripReadyRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new MarkTripReadyForPickupCommand(
            TripId: tripId,
            Notes: request.Notes), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var payload = new
            {
                tripId = result.Value.TripId,
                tripCode = result.Value.TripCode,
                boxesCount = result.Value.BoxesCount,
                driverProfileId = result.Value.DriverProfileId,
                readyAtUtc = result.Value.ReadyAtUtc
            };

            await _dispatcherHub.Clients.All.SendAsync("kitchen-orders-ready", payload, cancellationToken);

            if (result.Value.DriverProfileId.HasValue)
            {
                await _driverHub.Clients.Group(DriverHub.GroupForDriver(result.Value.DriverProfileId.Value))
                    .SendAsync("trip-ready-for-pickup", payload, cancellationToken);
            }
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Kitchen supervisor acknowledges receipt of a returned undelivered box.
    /// Closes accountability and logs food disposition.
    /// </summary>
    [HttpPost("orders/{boxId:guid}/acknowledge-return")]
    public async Task<IActionResult> AcknowledgeReturn(
        [FromRoute] Guid boxId,
        [FromBody] AcknowledgeReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new AcknowledgeReturnedBoxCommand(
            BoxId: boxId,
            Disposition: request.Disposition,
            KitchenSupervisorNotes: request.KitchenSupervisorNotes), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var payload = new
            {
                boxId = result.Value.BoxId,
                boxCode = result.Value.BoxCode,
                disposition = result.Value.Disposition,
                acknowledgedAtUtc = result.Value.AcknowledgedAtUtc
            };

            await _dispatcherHub.Clients.All.SendAsync("kitchen-return-acknowledged", payload, cancellationToken);
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }
}
