using MealMate.Api.Common;
using MealMate.Api.Realtime;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// Driver-app GPS location ingestion (REST fallback for the DriverHub stream).
/// Lives outside DispatcherDashboardController because that controller requires
/// the DispatcherPolicy (DeliveryManager/Admin) which a driver token can never satisfy.
/// Driver identity is resolved from the JWT only — never from the request body.
/// </summary>
[Route("api/v1/driver")]
[Authorize]
public sealed class DriverTrackingController : ApiControllerBase
{
    private readonly DriverLocationBroadcaster _locationBroadcaster;

    public DriverTrackingController(DriverLocationBroadcaster locationBroadcaster)
    {
        _locationBroadcaster = locationBroadcaster;
    }

    /// <summary>
    /// Records a driver GPS location update and broadcasts it to dispatchers over SignalR.
    /// Returns 409 <c>dispatcher.tracking_not_required</c> when the driver has no active delivery work.
    /// </summary>
    [HttpPost("location")]
    public async Task<IActionResult> UpdateDriverLocation(
        [FromBody] UpdateDriverLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new UpdateDriverLocationCommand(
            DriverId: null,
            TripId: null,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            Heading: request.Heading,
            SpeedKmh: request.SpeedKmh), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        // Broadcast to restaurant dispatcher realtime group
        try
        {
            await _locationBroadcaster.BroadcastAsync(
                result.Value,
                requestDriverId: null,
                requestTripId: null,
                request.Latitude,
                request.Longitude,
                request.Heading,
                request.SpeedKmh,
                cancellationToken);
        }
        catch
        {
            // Non-fatal SignalR broadcast
        }

        return HandleResult(result);
    }
}
