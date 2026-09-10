using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.RestaurantOperations.Readiness;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/restaurants")]
public sealed class RestaurantReadinessController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IRestaurantReadinessService _readinessService;

    public RestaurantReadinessController(
        ICurrentUser currentUser,
        IRestaurantReadinessService readinessService)
    {
        _currentUser = currentUser;
        _readinessService = readinessService;
    }

    [HttpGet("readiness")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly? from,
        [FromQuery] int days = 7,
        [FromQuery] Guid? programId = null,
        [FromQuery] Guid? bundleId = null,
        [FromQuery] RestaurantTier? tier = null,
        [FromQuery] Guid? regionId = null,
        CancellationToken ct = default)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();
        return HandleResult(await _readinessService.GetMyReadinessAsync(userId, from, days, programId, bundleId, tier, regionId, ct));
    }
}

[Authorize]
[Route("api/v1/admin/restaurants")]
public sealed class AdminRestaurantReadinessController : ApiControllerBase
{
    private readonly IRestaurantReadinessService _readinessService;

    public AdminRestaurantReadinessController(IRestaurantReadinessService readinessService)
        => _readinessService = readinessService;

    [HttpGet("readiness")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid programId,
        [FromQuery] Guid bundleId,
        [FromQuery] RestaurantTier tier,
        [FromQuery] Guid regionId,
        [FromQuery] DateOnly? from,
        [FromQuery] int days = 7,
        CancellationToken ct = default)
        => HandleResult(await _readinessService.ListAdminReadinessAsync(programId, bundleId, tier, regionId, from, days, ct));

    [HttpGet("{id:guid}/readiness")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> One(
        Guid id,
        [FromQuery] Guid programId,
        [FromQuery] Guid bundleId,
        [FromQuery] RestaurantTier tier,
        [FromQuery] Guid regionId,
        [FromQuery] DateOnly? from,
        [FromQuery] int days = 7,
        CancellationToken ct = default)
        => HandleResult(await _readinessService.GetAdminRestaurantReadinessAsync(id, programId, bundleId, tier, regionId, from, days, ct));

    [HttpPost("/api/v1/admin/subscriptions/meal-snapshots/backfill")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> BackfillSnapshots(CancellationToken ct)
        => HandleResult(await _readinessService.BackfillSnapshotsAsync(ct));
}
