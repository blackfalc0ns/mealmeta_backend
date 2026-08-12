using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurant_bundle_pricing")]
public sealed class RestaurantBundlePricingController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.ProgramsView)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] Guid? programId = null,
        [FromQuery] Guid? bundleId = null,
        [FromQuery] PriceLockStatus? lockStatus = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetRestaurantPricingsListQuery(restaurantId, programId, bundleId, lockStatus, page, pageSize), cancellationToken));

    [HttpGet("tier-averages")]
    [HasPermission(PermissionCodes.ProgramsView)]
    public async Task<IActionResult> GetTierAverages(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetSubscriptionBundleTierPricingsQuery(), cancellationToken));

    /// <summary>MM-PRC-AMD-01: pending reference/supply price change requests.</summary>
    [HttpGet("pending-changes")]
    [HasPermission(PermissionCodes.ProgramsView)]
    public async Task<IActionResult> GetPendingChanges(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetPendingRestaurantPricingChangesQuery(), cancellationToken));

    [HttpGet("lookup")]
    [HasPermission(PermissionCodes.ProgramsView)]
    public async Task<IActionResult> GetByLookup(
        [FromQuery] Guid restaurantId,
        [FromQuery] Guid programId,
        [FromQuery] Guid bundleId,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetRestaurantPricingQuery(restaurantId, programId, bundleId), cancellationToken));

    [HttpPost]
    [HasPermission(PermissionCodes.ProgramsManage)]
    public async Task<IActionResult> SetPricing([FromBody] SetRestaurantPricingCommand command, CancellationToken cancellationToken)
        => HandleCreated(await Sender.Send(command, cancellationToken), "/api/v1/restaurant_bundle_pricing");

    [HttpPost("{id:guid}/lock")]
    [HasPermission(PermissionCodes.ProgramsApprove)]
    public async Task<IActionResult> LockPricing(Guid id, [FromBody] LockPricingRequest? request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new LockRestaurantPricingCommand(id, request?.TargetStatus ?? PriceLockStatus.Locked), cancellationToken));
}

public sealed record LockPricingRequest(PriceLockStatus TargetStatus);
