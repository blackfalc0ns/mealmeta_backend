using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.AdminCoverage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/admin/coverage")]
public sealed class AdminCoverageController : ApiControllerBase
{
    [HttpGet("snapshot")]
    [HasPermission(PermissionCodes.RestaurantRegView)]
    public async Task<IActionResult> Snapshot(
        [FromQuery] string countryCode,
        [FromQuery] Guid? governorateId,
        [FromQuery] Guid? restaurantId,
        [FromQuery] DateOnly serviceDate,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdminCoverageSnapshotQuery(
            countryCode, governorateId, restaurantId, serviceDate, search), cancellationToken));

    [HttpGet("trips")]
    [HasPermission(PermissionCodes.AdminCoverageOperationsView)]
    public async Task<IActionResult> Trips(
        [FromQuery] string countryCode,
        [FromQuery] DateOnly serviceDate,
        [FromQuery] Guid? governorateId,
        [FromQuery] Guid? restaurantId,
        [FromQuery] string? search,
        [FromQuery] string? tripStatus,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetAdminOperationsTripsQuery(
            countryCode, serviceDate, governorateId, restaurantId, search, tripStatus, page, pageSize), cancellationToken));

    [HttpGet("trips/{tripId:guid}")]
    [HasPermission(PermissionCodes.AdminCoverageOperationsView)]
    public async Task<IActionResult> TripDetail(
        Guid tripId,
        [FromQuery] string countryCode,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdminOperationsTripDetailQuery(countryCode, tripId), cancellationToken));

    [HttpGet("trips/{tripId:guid}/planned-route")]
    [HasPermission(PermissionCodes.AdminCoverageOperationsView)]
    public async Task<IActionResult> PlannedTripRoute(
        Guid tripId,
        [FromQuery] string countryCode,
        [FromQuery] string? originMode = "dispatch",
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetAdminTripPlannedRouteQuery(countryCode, tripId, originMode ?? "dispatch"), cancellationToken));

    [HttpGet("driver-locations")]
    [HasPermission(PermissionCodes.AdminCoverageOperationsView)]
    public async Task<IActionResult> DriverLocations(
        [FromQuery] string countryCode,
        [FromQuery] DateOnly serviceDate,
        [FromQuery] Guid? governorateId,
        [FromQuery] Guid? restaurantId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdminDriverMapLocationsQuery(
            countryCode, serviceDate, governorateId, restaurantId, search), cancellationToken));
}
