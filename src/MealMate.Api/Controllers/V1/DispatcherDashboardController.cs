using MealMate.Api.Common;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/dispatcher")]
[AllowAnonymous] // Allow anonymous fallback for dev & testing while supporting JWT authentication
public sealed class DispatcherDashboardController : ApiControllerBase
{
    private readonly IHubContext<DispatcherHub> _dispatcherHub;
    private readonly IGoogleRoutingService _routingService;

    public DispatcherDashboardController(
        IHubContext<DispatcherHub> dispatcherHub,
        IGoogleRoutingService routingService)
    {
        _dispatcherHub = dispatcherHub;
        _routingService = routingService;
    }

    /// <summary>
    /// Returns the comprehensive overview for the Dispatcher / Delivery Manager Dashboard (Screen 04.01).
    /// Includes Restaurant context, Greetings, 4 KPI cards, Operations status breakdown, Top drivers,
    /// Regional summary with trends, and Active operational issues.
    /// </summary>
    [HttpGet("dashboard/overview")]
    public async Task<IActionResult> GetDashboardOverview(
        [FromQuery] Guid? restaurantId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherDashboardOverviewQuery(restaurantId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns real-time coordinates, status pills, speed, heading, and active orders for all drivers.
    /// </summary>
    [HttpGet("drivers/live-locations")]
    public async Task<IActionResult> GetLiveDriverLocations(
        [FromQuery] Guid? restaurantId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherDriverLocationsQuery(restaurantId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Records a driver GPS location update and broadcasts the position update via SignalR to dispatchers.
    /// </summary>
    [HttpPost("drivers/location")]
    [HttpPost("/api/v1/driver/location")]
    public async Task<IActionResult> UpdateDriverLocation(
        [FromBody] UpdateDriverLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new UpdateDriverLocationCommand(
            DriverId: request.DriverId,
            TripId: request.TripId,
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
            await _dispatcherHub.Clients.All.SendAsync("driver-location-updated", new
            {
                driverId = request.DriverId,
                tripId = request.TripId,
                latitude = request.Latitude,
                longitude = request.Longitude,
                heading = request.Heading ?? 0.0,
                speedKmh = request.SpeedKmh ?? 0.0,
                updatedAtUtc = result.Value.RecordedAtUtc
            }, cancellationToken);
        }
        catch
        {
            // Logging or non-fatal SignalR broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Calculate road distance and duration between two GPS coordinates using Google Maps with Haversine fallback.
    /// </summary>
    [HttpPost("routes/calculate")]
    public async Task<IActionResult> CalculateRoute(
        [FromBody] CalculateRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _routingService.CalculateDistanceAndDurationAsync(
            request.OriginLatitude,
            request.OriginLongitude,
            request.DestinationLatitude,
            request.DestinationLongitude,
            cancellationToken);

        return HandleResult(result);
    }

    /// <summary>
    /// Retrieve driving directions polyline and step metrics using Google Directions API with fallback.
    /// </summary>
    [HttpPost("routes/directions")]
    public async Task<IActionResult> GetDirections(
        [FromBody] CalculateRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _routingService.GetDirectionsAsync(
            request.OriginLatitude,
            request.OriginLongitude,
            request.DestinationLatitude,
            request.DestinationLongitude,
            cancellationToken);

        return HandleResult(result);
    }
}

public sealed record CalculateRouteRequest(
    decimal OriginLatitude,
    decimal OriginLongitude,
    decimal DestinationLatitude,
    decimal DestinationLongitude);
