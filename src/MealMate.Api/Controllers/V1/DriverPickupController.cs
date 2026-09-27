using MealMate.Api.Common;
using MealMate.Api.Realtime;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/driver/pickup")]
[AllowAnonymous]
public sealed class DriverPickupController : ApiControllerBase
{
    private readonly IHubContext<DispatcherHub> _dispatcherHub;
    private readonly IHubContext<DriverHub> _driverHub;

    public DriverPickupController(
        IHubContext<DispatcherHub> dispatcherHub,
        IHubContext<DriverHub> driverHub)
    {
        _dispatcherHub = dispatcherHub;
        _driverHub = driverHub;
    }

    /// <summary>
    /// Returns pickup point and kitchen navigation details for Screen 06.01.
    /// Includes kitchen address, route distance (4.5 km), estimated arrival (10 mins), and trip summary.
    /// </summary>
    [HttpGet("point")]
    public async Task<IActionResult> GetPickupPoint(
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDriverPickupPointQuery(driverId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Confirms driver arrival at the kitchen pickup point (Screen 06.01 CTA).
    /// Instantly alerts the kitchen crew and dispatcher via SignalR that the driver has arrived.
    /// </summary>
    [HttpPost("arrive")]
    public async Task<IActionResult> ArriveAtKitchen(
        [FromBody] ArriveAtKitchenRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new ArriveAtKitchenCommand(
            DriverId: driverId,
            Latitude: request.Latitude,
            Longitude: request.Longitude), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var payload = new
            {
                tripId = result.Value.TripId,
                driverId,
                arrivedAtUtc = result.Value.ArrivedAtUtc,
                message = result.Value.Message
            };

            await _dispatcherHub.Clients.All.SendAsync("driver-arrived-at-kitchen", payload, cancellationToken);
            if (driverId.HasValue)
            {
                await _driverHub.Clients.Group(DriverHub.GroupForDriver(driverId.Value))
                    .SendAsync("driver-arrived-at-kitchen", payload, cancellationToken);
            }
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Returns assigned boxes manifest with scanning progress (0/32 or 8/8) for Screen 06.02, 06.05, 06.06.
    /// Supports status filtering: PendingScan, Scanned, All.
    /// </summary>
    [HttpGet("manifest")]
    public async Task<IActionResult> GetPickupManifest(
        [FromQuery] Guid? driverId = null,
        [FromQuery] string? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDriverPickupManifestQuery(driverId, statusFilter), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Pre-validates scanned barcode/QR or manual entry before opening camera (Screen 06.03).
    /// Enforces cross-driver anti-fraud checks; returns error 06.13 if box belongs to another driver.
    /// </summary>
    [HttpPost("validate-barcode")]
    public async Task<IActionResult> ValidateBarcode(
        [FromBody] ValidatePickupBarcodeRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new LegacyValidatePickupBarcodeCommand(
            BarcodeValue: request.BarcodeValue,
            DriverId: driverId,
            IsManualEntry: request.IsManualEntry), cancellationToken);

        return HandleResult(result);
    }

    /// <summary>
    /// Launches the delivery trip after scanning all boxes (Screen 06.05 CTA -> 06.06 / 05.02).
    /// Verifies all assigned boxes are verified, sets trip InProgress, and alerts kitchen to clear pickup area.
    /// </summary>
    [HttpPost("start-trip")]
    public async Task<IActionResult> StartTripDelivery(
        [FromBody] StartTripDeliveryRequest request,
        [FromQuery] Guid? driverId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new StartTripDeliveryCommand(
            DriverId: driverId,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            ForceStartWithUnscannedBoxes: request.ForceStartWithUnscannedBoxes,
            UnscannedReason: request.UnscannedReason), cancellationToken);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var payload = new
            {
                tripId = result.Value.TripId,
                driverId,
                status = "InProgress",
                dispatchedBoxesCount = result.Value.DispatchedBoxesCount,
                startedAtUtc = result.Value.StartedAtUtc
            };

            await _dispatcherHub.Clients.All.SendAsync("trip-in-transit", payload, cancellationToken);
            if (driverId.HasValue)
            {
                await _driverHub.Clients.Group(DriverHub.GroupForDriver(driverId.Value))
                    .SendAsync("trip-in-transit", payload, cancellationToken);
            }
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }
}
