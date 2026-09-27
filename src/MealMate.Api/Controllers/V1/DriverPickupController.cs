using MealMate.Api.Common;
using MealMate.Api.Realtime;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/driver/pickup")]
[Authorize]
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
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDriverPickupPointQuery(), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Confirms driver arrival at the kitchen pickup point (Screen 06.01 CTA).
    /// Instantly alerts the kitchen crew and dispatcher via SignalR that the driver has arrived.
    /// </summary>
    [HttpPost("arrive")]
    public async Task<IActionResult> ArriveAtKitchen(
        [FromBody] ArriveAtKitchenRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new ArriveAtKitchenCommand(
            DriverId: null,
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
                arrivedAtUtc = result.Value.ArrivedAtUtc,
                message = result.Value.Message
            };

            await _dispatcherHub.Clients.All.SendAsync("driver-arrived-at-kitchen", payload, cancellationToken);
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
        [FromQuery] string? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDriverPickupManifestQuery(null, statusFilter), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Pre-validates scanned barcode/QR or manual entry before opening camera (Screen 06.03).
    /// Issues a cryptographically hashed 5-minute scoped token.
    /// </summary>
    [HttpPost("validate-barcode")]
    public async Task<IActionResult> ValidateBarcode(
        [FromBody] ValidatePickupBarcodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new ValidatePickupBarcodeCommand(request.BarcodeValue, request.IsManualEntry), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Uploads condition photo for a pre-validated box (Screen 06.04).
    /// Validates file signature (magic bytes) and binds ownership to the active validation token.
    /// </summary>
    [HttpPost("boxes/{boxId:guid}/condition-photo")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadConditionPhoto(
        [FromRoute] Guid boxId,
        IFormFile file,
        [FromForm] string validationToken,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { code = "PHOTO_REQUIRED", message = "Photo file is required." });
        }

        await using var stream = file.OpenReadStream();
        var result = await Sender.Send(new UploadPickupConditionPhotoCommand(
            BoxId: boxId,
            ValidationToken: validationToken,
            FileName: file.FileName,
            ContentType: file.ContentType,
            ContentStream: stream), cancellationToken);

        return HandleResult(result);
    }

    /// <summary>
    /// Confirms box pickup in an atomic database transaction (Screen 06.04 CTA -> 06.05).
    /// Consumes the validation token, confirms photo ownership, updates box to PickedUp, and records PickupScan.
    /// Requires Idempotency-Key header.
    /// </summary>
    [HttpPost("boxes/{boxId:guid}/confirm")]
    public async Task<IActionResult> ConfirmPickup(
        [FromRoute] Guid boxId,
        [FromBody] ConfirmPickupBoxRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new { code = "IDEMPOTENCY_KEY_REQUIRED", message = "Idempotency-Key header is required." });
        }

        var result = await Sender.Send(new ConfirmPickupBoxCommand(
            BoxId: boxId,
            ValidationToken: request.ValidationToken,
            ConditionPhotoStorageKey: request.ConditionPhotoStorageKey,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            IdempotencyKey: idempotencyKey), cancellationToken);

        if (result.IsSuccess)
        {
            try
            {
                await _dispatcherHub.Clients.All.SendAsync("box-picked-up", result.Value, cancellationToken);
            }
            catch { }
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Retrieves the authoritative pickup summary for a trip (Screen 06.05).
    /// Derives state from persistent database records, allowing resuming at any point.
    /// </summary>
    [HttpGet("/api/v1/driver/trips/{tripId:guid}/pickup-summary")]
    [HttpGet("trips/{tripId:guid}/pickup-summary")]
    public async Task<IActionResult> GetPickupSummary(
        [FromRoute] Guid tripId,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetTripPickupSummaryQuery(tripId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Initiates delivery trip after all boxes are verified and picked up (Screen 06.05 CTA -> 06.06).
    /// Completely independent and idempotent operation; enforces that zero unscanned boxes remain.
    /// </summary>
    [HttpPost("/api/v1/driver/trips/{tripId:guid}/start")]
    [HttpPost("trips/{tripId:guid}/start")]
    public async Task<IActionResult> StartTrip(
        [FromRoute] Guid tripId,
        [FromBody] StartTripRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new { code = "IDEMPOTENCY_KEY_REQUIRED", message = "Idempotency-Key header is required." });
        }

        var result = await Sender.Send(new StartTripCommand(
            TripId: tripId,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            IdempotencyKey: idempotencyKey), cancellationToken);

        if (result.IsSuccess)
        {
            try
            {
                await _dispatcherHub.Clients.All.SendAsync("trip-in-transit", result.Value, cancellationToken);
            }
            catch { }
        }

        return HandleResult(result);
    }
}
