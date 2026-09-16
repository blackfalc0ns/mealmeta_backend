using MealMate.Api.Common;
using MealMate.Application.Features.DriverDelivery.Trips;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/delivery/trips")]
[Route("api/restaurant/delivery/trips")]
[AllowAnonymous]
public sealed class RestaurantDeliveryTripsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] string? filter = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(
            new GetRestaurantTripsQuery(restaurantId ?? Guid.Empty, filter, search),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetRestaurantTripDetailQuery(id), cancellationToken);
        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        if (result.Value is null)
        {
            return NotFound();
        }

        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateTripRequest request,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new CreateRestaurantTripCommand(
                request.RestaurantId ?? Guid.Empty,
                request.DriverId,
                request.Stops,
                request.ShiftLabel,
                request.Notes),
            cancellationToken);

        return HandleCreated(result, $"/api/v1/restaurants/delivery/trips/{result.Value}");
    }

    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateTripStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new UpdateTripStatusCommand(id, request.Status),
            cancellationToken);

        return HandleResult(result);
    }

    [HttpPut("{id:guid}/stops/{stopId:guid}/status")]
    public async Task<IActionResult> UpdateStopStatus(
        Guid id,
        Guid stopId,
        [FromBody] UpdateTripStopStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(
            new UpdateTripStopStatusCommand(id, stopId, request.Status, request.FailureReason),
            cancellationToken);

        return HandleResult(result);
    }
}

public sealed record CreateTripRequest(
    Guid? RestaurantId,
    Guid DriverId,
    IReadOnlyList<CreateTripStopInput> Stops,
    string? ShiftLabel = null,
    string? Notes = null);

public sealed record UpdateTripStatusRequest(string Status);

public sealed record UpdateTripStopStatusRequest(string Status, string? FailureReason = null);
