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
    /// Returns the box queue awaiting assignment (Screen 04.03).
    /// Includes the 4 status counters and boxes with smart driver recommendations.
    /// </summary>
    [HttpGet("orders/queue")]
    public async Task<IActionResult> GetOrderQueue(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] string? filter = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherOrderQueueQuery(restaurantId, filter), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns recommended and candidate drivers for a specific delivery box to power the assignment bottom sheet.
    /// </summary>
    [HttpGet("orders/{boxId:guid}/candidates")]
    public async Task<IActionResult> GetBoxCandidates(
        Guid boxId,
        [FromQuery] Guid? restaurantId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetBoxCandidateDriversQuery(boxId, restaurantId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns the comprehensive assignment screen data (Screen 10.02) including box details,
    /// top recommendation driver card, and the full ranked candidate driver list.
    /// </summary>
    [HttpGet("orders/{boxId:guid}/assignment-details")]
    public async Task<IActionResult> GetAssignmentDetails(
        Guid boxId,
        [FromQuery] Guid? restaurantId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetBoxAssignmentDetailsQuery(boxId, restaurantId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns meal items, customer details, and delivery notes for the "View Box" (عرض البوكس) modal.
    /// </summary>
    [HttpGet("orders/{boxId:guid}/summary")]
    public async Task<IActionResult> GetBoxSummary(
        Guid boxId,
        [FromQuery] Guid? restaurantId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetBoxContentSummaryQuery(boxId, restaurantId), cancellationToken);
        return HandleResult(result);
    }


    /// <summary>
    /// Assigns a delivery box to a chosen driver and broadcasts the update via SignalR to all active dispatchers.
    /// </summary>
    [HttpPost("orders/{boxId:guid}/assign")]
    public async Task<IActionResult> AssignBoxToDriver(
        Guid boxId,
        [FromBody] AssignBoxRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new AssignBoxToDriverCommand(boxId, request.DriverId, request.Notes), cancellationToken);
        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            await _dispatcherHub.Clients.All.SendAsync("box-assigned", new
            {
                boxId = result.Value.BoxId,
                boxCode = result.Value.BoxCode,
                driverId = result.Value.DriverId,
                driverName = result.Value.DriverName,
                assignedAtUtc = result.Value.AssignedAtUtc
            }, cancellationToken);
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Returns the drivers roster list (Screen 10.03 / 04.05) filtered by area or all,
    /// with operational metrics (active orders, completed today, distance) and selection state.
    /// </summary>
    [HttpGet("drivers/roster")]
    public async Task<IActionResult> GetDriverRoster(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] string? view = "ByArea",
        [FromQuery] string? area = null,
        [FromQuery] Guid? boxId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherDriverRosterQuery(
            restaurantId, view, area, boxId, status, search, sortBy), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns live monitoring dashboard for Screen 10.06: 4 live KPIs, map markers, and bottom carousel driver cards.
    /// </summary>
    [HttpGet("drivers/live-monitoring")]
    public async Task<IActionResult> GetLiveMonitoring(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherLiveMonitoringQuery(restaurantId, status), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns the complete profile, performance KPIs, and daily summary for a specific driver (Screen 10.07).
    /// </summary>
    [HttpGet("drivers/{driverId:guid}/details")]
    public async Task<IActionResult> GetDriverDetails(
        [FromRoute] Guid driverId,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherDriverDetailsQuery(driverId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns current active assigned boxes for a specific driver (Screen 10.07 active boxes list).
    /// </summary>
    [HttpGet("drivers/{driverId:guid}/active-boxes")]
    public async Task<IActionResult> GetDriverActiveBoxes(
        [FromRoute] Guid driverId,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherDriverActiveBoxesQuery(driverId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns real-time GPS location and mini-map destination for a specific driver (Screen 10.07 mini-map).
    /// </summary>
    [HttpGet("drivers/{driverId:guid}/current-location")]
    public async Task<IActionResult> GetDriverCurrentLocation(
        [FromRoute] Guid driverId,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherDriverCurrentLocationQuery(driverId), cancellationToken);
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

    /// <summary>
    /// Returns the complete vertical timeline stepper, assigned driver quick bar, and operational details for box tracking (Screen 10.08).
    /// </summary>
    [HttpGet("orders/{boxId:guid}/tracking")]
    public async Task<IActionResult> GetBoxTracking(
        [FromRoute] Guid boxId,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherBoxTrackingQuery(boxId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Records an operational issue on a delivery box and appends an audit log entry (Screen 10.08).
    /// </summary>
    [HttpPost("orders/{boxId:guid}/issues")]
    public async Task<IActionResult> ReportBoxIssue(
        [FromRoute] Guid boxId,
        [FromBody] ReportBoxIssueRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new ReportBoxIssueCommand(
            BoxId: boxId,
            IssueType: request.IssueType,
            Description: request.Description,
            Severity: request.Severity), cancellationToken);

        return HandleResult(result);
    }

    /// <summary>
    /// Returns the comprehensive driver performance overview for Screen 10.09 (Overview tab):
    /// 5 top KPIs, Performance distribution donut breakdown, Top 3 rated drivers podium, and Drivers performance table.
    /// </summary>
    [HttpGet("performance/overview")]
    public async Task<IActionResult> GetPerformanceOverview(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] string? period = "Last7Days",
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherPerformanceOverviewQuery(restaurantId, period, fromDate, toDate), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns side-by-side comparative metrics for drivers for Screen 10.09 (Comparison tab):
    /// Delivered, On-Time %, Avg Delay, Rating, Failed deliveries, and Distance covered.
    /// </summary>
    [HttpGet("performance/comparison")]
    public async Task<IActionResult> GetDriverComparison(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] string? period = "Last7Days",
        [FromQuery] Guid[]? driverIds = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherDriverComparisonQuery(restaurantId, period, driverIds, fromDate, toDate), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record CalculateRouteRequest(
    decimal OriginLatitude,
    decimal OriginLongitude,
    decimal DestinationLatitude,
    decimal DestinationLongitude);
