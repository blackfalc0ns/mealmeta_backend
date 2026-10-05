using MealMate.Api.Common;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/dispatcher")]
[Authorize(Policy = "DispatcherPolicy")]
public sealed class DispatcherDashboardController : ApiControllerBase
{
    private readonly IHubContext<DispatcherHub> _dispatcherHub;
    private readonly IHubContext<DriverHub>? _driverHub;
    private readonly IGoogleRoutingService _routingService;
    private readonly DriverLocationBroadcaster _locationBroadcaster;
    private readonly ILogger<DispatcherDashboardController>? _logger;

    public DispatcherDashboardController(
        IHubContext<DispatcherHub> dispatcherHub,
        IGoogleRoutingService routingService,
        DriverLocationBroadcaster locationBroadcaster,
        IHubContext<DriverHub>? driverHub = null,
        ILogger<DispatcherDashboardController>? logger = null)
    {
        _dispatcherHub = dispatcherHub;
        _routingService = routingService;
        _locationBroadcaster = locationBroadcaster;
        _driverHub = driverHub;
        _logger = logger;
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
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherOrderQueueQuery(restaurantId, filter, page, pageSize), cancellationToken);
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
            var assignmentPayload = new
            {
                boxId = result.Value.BoxId,
                boxCode = result.Value.BoxCode,
                driverId = result.Value.DriverId,
                driverName = result.Value.DriverName,
                assignedAtUtc = result.Value.AssignedAtUtc
            };

            await _dispatcherHub.Clients.All.SendAsync(DispatcherHub.EventBoxAssigned, assignmentPayload, cancellationToken);

            // Broadcast to the assigned driver's real-time mobile app
            if (_driverHub is not null)
            {
                await _driverHub.Clients.Group(DriverHub.GroupForDriver(result.Value.DriverId))
                    .SendAsync(DriverHub.EventBoxAssigned, assignmentPayload, cancellationToken);
                await _driverHub.Clients.Group(DriverHub.UniversalDriverGroup)
                    .SendAsync(DriverHub.EventBoxAssigned, assignmentPayload, cancellationToken);
            }
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Assigns multiple delivery boxes (up to 25) to a chosen driver in a single atomic trip,
    /// broadcasting SignalR updates and sending high-priority FCM push notification to the driver.
    /// </summary>
    [HttpPost("orders/bulk-assign")]
    public async Task<IActionResult> BulkAssignBoxesToDriver(
        [FromBody] BulkAssignBoxesRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new BulkAssignBoxesCommand(request.BoxIds, request.DriverId, request.Notes), cancellationToken);
        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        try
        {
            var assignmentPayload = new
            {
                tripId = result.Value.TripId,
                tripCode = result.Value.TripCode,
                assignedCount = result.Value.AssignedCount,
                driverId = result.Value.DriverId,
                driverName = result.Value.DriverName,
                assignedBoxIds = result.Value.AssignedBoxIds,
                assignedAtUtc = result.Value.AssignedAtUtc
            };

            await _dispatcherHub.Clients.All.SendAsync("boxes-bulk-assigned", assignmentPayload, cancellationToken);

            if (_driverHub is not null)
            {
                await _driverHub.Clients.Group(DriverHub.GroupForDriver(result.Value.DriverId))
                    .SendAsync("trip-assigned", assignmentPayload, cancellationToken);
                await _driverHub.Clients.Group(DriverHub.UniversalDriverGroup)
                    .SendAsync("trip-assigned", assignmentPayload, cancellationToken);
            }
        }
        catch
        {
            // Non-fatal broadcast
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Returns paginated list of drivers with real-time operational status counts and search/filter support.
    /// </summary>
    [HttpGet("drivers")]
    public async Task<IActionResult> GetDrivers(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherDriversQuery(
            restaurantId, search, status, sortBy, page, pageSize), cancellationToken);
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
    /// Returns the complete profile, vehicle, GPS location, performance, today's stats, and document status for a driver.
    /// </summary>
    [HttpGet("drivers/{driverId:guid}")]
    [HttpGet("drivers/{driverId:guid}/details")]
    public async Task<IActionResult> GetDriverDetails(
        [FromRoute] Guid driverId,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherDriverDetailsQuery(driverId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Updates driver availability (online/offline toggle) with audit trail and SignalR broadcast.
    /// </summary>
    [HttpPatch("drivers/{driverId:guid}/availability")]
    public async Task<IActionResult> UpdateDriverAvailability(
        [FromRoute] Guid driverId,
        [FromBody] UpdateDriverAvailabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new UpdateDispatcherDriverAvailabilityCommand(
            driverId, request.IsAvailable, request.Reason), cancellationToken);

        if (result.IsSuccess)
        {
            try
            {
                var payload = result.Value;
                var restaurantClaim = User?.FindFirst(MealMate.Application.Common.Security.AppClaimTypes.RestaurantId)?.Value;
                if (!string.IsNullOrEmpty(restaurantClaim))
                {
                    await _dispatcherHub.Clients
                        .Group(DispatcherHub.GroupForRestaurant(restaurantClaim))
                        .SendAsync(DispatcherHub.EventDriverAvailabilityUpdated, payload, cancellationToken);
                }

                await _dispatcherHub.Clients
                    .Group(DispatcherHub.UniversalDispatcherGroup)
                    .SendAsync(DispatcherHub.EventDriverAvailabilityUpdated, payload, cancellationToken);
            }
            catch
            {
                // Non-fatal SignalR broadcast
            }
        }

        return HandleResult(result);
    }

    /// <summary>
    /// Activates or deactivates a driver's operational status on behalf of the
    /// delivery supervisor. "Active" opens the driver's single work session after
    /// eligibility checks; "Inactive" closes it and is rejected with 409 while the
    /// driver still holds active deliveries. Broadcasts driver-status-updated to
    /// the driver's own group (from the authenticated identity only) after saving.
    /// </summary>
    [HttpPatch("drivers/{driverId:guid}/shift-status")]
    public async Task<IActionResult> UpdateDriverShiftStatus(
        [FromRoute] Guid driverId,
        [FromBody] UpdateDriverShiftStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new UpdateDispatcherDriverShiftStatusCommand(
            driverId, request.ShiftStatus, request.Reason), cancellationToken);

        if (result.IsSuccess)
        {
            try
            {
                var payload = result.Value;

                // Driver's own group: identity-derived group name, no caller input.
                if (_driverHub is not null)
                {
                    await _driverHub.Clients
                        .Group(DriverHub.GroupForDriver(payload.DriverId))
                        .SendAsync(DriverHub.EventDriverStatusUpdated, payload, cancellationToken);
                }

                // Keep the dispatcher dashboard informed via its existing event name.
                var restaurantClaim = User?.FindFirst(MealMate.Application.Common.Security.AppClaimTypes.RestaurantId)?.Value;
                if (!string.IsNullOrEmpty(restaurantClaim))
                {
                    await _dispatcherHub.Clients
                        .Group(DispatcherHub.GroupForRestaurant(restaurantClaim))
                        .SendAsync(DispatcherHub.EventDriverStatusUpdated, payload, cancellationToken);
                }

                await _dispatcherHub.Clients
                    .Group(DispatcherHub.UniversalDispatcherGroup)
                    .SendAsync(DispatcherHub.EventDriverStatusUpdated, payload, cancellationToken);
            }
            catch (Exception ex)
            {
                // Broadcast failure must never be silent: the driver app recovers
                // missed updates through reconnect + home reload, but the incident
                // still has to show up in logs.
                _logger?.LogError(ex, "SignalR broadcast of driver-status-updated failed for driver {DriverId}", driverId);
            }
        }

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
    /// The driver-app fallback route (POST /api/v1/driver/location) lives in DriverTrackingController —
    /// this controller's DispatcherPolicy rejects driver tokens.
    /// </summary>
    [HttpPost("drivers/location")]
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
            await _locationBroadcaster.BroadcastAsync(
                result.Value,
                request.DriverId,
                request.TripId,
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
    /// Optimizes the stops sequence for a trip geographically using TSP ordering.
    /// </summary>
    [HttpPost("trips/{tripId:guid}/optimize-route")]
    public async Task<IActionResult> OptimizeTripRoute(
        [FromRoute] Guid tripId,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new OptimizeTripRouteCommand(tripId), cancellationToken);
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
        [FromQuery(Name = "driverIds")] string[]? driverIds = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var parsedDriverIds = ParseDriverIds(driverIds);
        var result = await Sender.Send(new GetDispatcherDriverComparisonQuery(restaurantId, period, parsedDriverIds, fromDate, toDate), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns active support issues with counters, area chips, and pagination for DeliveryManager (Screen 10.11 / 04.11).
    /// </summary>
    [HttpGet("support/issues")]
    public async Task<IActionResult> GetSupportIssues(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] string? status = "Open",
        [FromQuery] string? area = null,
        [FromQuery] string? search = null,
        [FromQuery] string? datePreset = null,
        [FromQuery] DateTime? fromDateUtc = null,
        [FromQuery] DateTime? toDateUtc = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherSupportIssuesQuery(
            RestaurantId: restaurantId,
            Status: status,
            Area: area,
            Search: search,
            DatePreset: datePreset,
            FromDateUtc: fromDateUtc,
            ToDateUtc: toDateUtc,
            PageNumber: pageNumber,
            PageSize: pageSize), cancellationToken);

        return HandleResult(result);
    }

    /// <summary>
    /// Returns deep problem details, driver status, attachments, and trip metadata for Screen 10.12.
    /// </summary>
    [HttpGet("support/issues/{issueId:guid}")]
    public async Task<IActionResult> GetProblemDetails(
        [FromRoute] Guid issueId,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherProblemDetailsQuery(issueId), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Returns issue summary, current unavailable driver, and ranked candidate replacement drivers for Screen 10.13.
    /// </summary>
    [HttpGet("support/issues/{issueId:guid}/candidates")]
    public async Task<IActionResult> GetReplacementCandidates(
        [FromRoute] Guid issueId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherReplacementCandidatesQuery(issueId, pageNumber, pageSize), cancellationToken);
        return HandleResult(result);
    }

    /// <summary>
    /// Reassigns an affected box/trip from current driver to a selected replacement driver (Screen 10.13).
    /// </summary>
    [HttpPost("support/issues/{issueId:guid}/reassign")]
    public async Task<IActionResult> ReassignReplacementDriver(
        [FromRoute] Guid issueId,
        [FromBody] ReassignReplacementDriverRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new ReassignReplacementDriverCommand(issueId, request.ReplacementDriverId, request.Notes), cancellationToken);
        if (result.IsSuccess)
        {
            try
            {
                var kpis = await GetLiveKpisAsync(null, cancellationToken);
                var isAr = Messages.IsArabic();

                var statusPayload = new
                {
                    driverId = result.Value.ReplacementDriverId,
                    statusCategory = "InDelivery",
                    statusText = isAr ? "في التوصيل" : "In Delivery",
                    statusColor = "Green",
                    topIndicatorColor = "Green",
                    hasIssue = false,
                    issueDescription = (string?)null,
                    kpis = kpis,
                    timestampUtc = result.Value.ReassignedAtUtc
                };

                var issuePayload = new
                {
                    issueId,
                    driverId = result.Value.ReplacementDriverId,
                    resolved = false,
                    status = "Open",
                    issueDescription = isAr ? "تم تعيين سائق بديل" : "Replacement driver assigned",
                    kpis = kpis,
                    timestampUtc = result.Value.ReassignedAtUtc
                };

                await _dispatcherHub.Clients.All.SendAsync(DispatcherHub.EventDriverStatusUpdated, statusPayload, cancellationToken);
                await _dispatcherHub.Clients.All.SendAsync(DispatcherHub.EventDriverIssueUpdated, issuePayload, cancellationToken);
            }
            catch
            {
                // Non-fatal broadcast
            }
        }
        return HandleResult(result);
    }

    /// <summary>
    /// Marks a support issue as resolved/closed.
    /// </summary>
    [HttpPost("support/issues/{issueId:guid}/resolve")]
    public async Task<IActionResult> ResolveSupportIssue(
        [FromRoute] Guid issueId,
        [FromBody] ResolveSupportIssueRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new ResolveSupportIssueCommand(issueId, request.ResolutionNotes), cancellationToken);
        if (result.IsSuccess)
        {
            try
            {
                var kpis = await GetLiveKpisAsync(null, cancellationToken);
                var isAr = Messages.IsArabic();

                var issuePayload = new
                {
                    issueId,
                    driverId = result.Value.DriverId ?? Guid.Empty,
                    resolved = true,
                    status = "Resolved",
                    issueDescription = isAr ? "تم حل المشكلة" : "Issue resolved",
                    kpis = kpis,
                    timestampUtc = result.Value.ResolvedAtUtc
                };

                await _dispatcherHub.Clients.All.SendAsync(DispatcherHub.EventDriverIssueUpdated, issuePayload, cancellationToken);
            }
            catch
            {
                // Non-fatal broadcast
            }
        }
        return HandleResult(result);
    }

    /// <summary>
    /// Returns the comprehensive Operations Log (Screen 04.10 / 10.10).
    /// Includes isolated status counters, operation cards (completed, failed, reassigned, cancelled),
    /// multi-field search, date preset filtering, and pagination.
    /// </summary>
    [HttpGet("operations/log")]
    [HttpGet("operations-log")]
    public async Task<IActionResult> GetOperationsLog(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? datePreset = null,
        [FromQuery] DateTime? fromDateUtc = null,
        [FromQuery] DateTime? toDateUtc = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetDispatcherOperationsLogQuery(
            RestaurantId: restaurantId,
            Status: status,
            Search: search,
            DatePreset: datePreset,
            FromDateUtc: fromDateUtc,
            ToDateUtc: toDateUtc,
            PageNumber: pageNumber,
            PageSize: pageSize), cancellationToken);

        return HandleResult(result);
    }

    private async Task<LiveMapKpisDto> GetLiveKpisAsync(Guid? restaurantId, CancellationToken cancellationToken)
    {
        var kpisResult = await Sender.Send(new GetDispatcherLiveMonitoringQuery(restaurantId, null), cancellationToken);
        return kpisResult.IsSuccess
            ? kpisResult.Value.Kpis
            : new LiveMapKpisDto(0, 0, 0, 0);
    }

    private static List<Guid>? ParseDriverIds(string[]? raw)
    {
        if (raw == null || raw.Length == 0) return null;
        var list = new List<Guid>();
        foreach (var item in raw)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            var parts = item.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (Guid.TryParse(part, out var g))
                {
                    list.Add(g);
                }
            }
        }
        return list.Count > 0 ? list : null;
    }
}

public sealed record CalculateRouteRequest(
    decimal OriginLatitude,
    decimal OriginLongitude,
    decimal DestinationLatitude,
    decimal DestinationLongitude);

public sealed record UpdateDriverAvailabilityRequest(
    bool IsAvailable,
    string? Reason = null);

public sealed record UpdateDriverShiftStatusRequest(
    string ShiftStatus,
    string? Reason = null);
