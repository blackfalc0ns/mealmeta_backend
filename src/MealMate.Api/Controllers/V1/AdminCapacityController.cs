using MealMate.Api.Common;
using MealMate.Application.Features.AdminCapacity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/admin/operations/capacity")]
public sealed class AdminCapacityController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCapacityOverview(
        [FromQuery] DateOnly? serviceDate = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetAdminCapacityOverviewQuery(serviceDate), cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("policy")]
    public async Task<IActionResult> UpdatePlatformPolicy(
        [FromBody] UpdatePlatformPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new UpdatePlatformCapacityPolicyCommand(request), cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("restaurants/{restaurantId:guid}/limit")]
    public async Task<IActionResult> UpdateRestaurantCapacityLimit(
        Guid restaurantId,
        [FromBody] UpdateRestaurantCapacityLimitRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(
            new UpdateRestaurantCapacityLimitCommand(restaurantId, request.NewCapacityLimit, request.Reason),
            cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("restaurants/{restaurantId:guid}/busy")]
    public async Task<IActionResult> ToggleRestaurantCapacityBusy(
        Guid restaurantId,
        [FromBody] ToggleRestaurantCapacityBusyRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(
            new ToggleRestaurantCapacityBusyCommand(restaurantId, request.IsBusy, request.Reason, request.OverrideHours),
            cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("reductions/{id:guid}/approve")]
    public async Task<IActionResult> ApproveCapacityReduction(
        Guid id,
        [FromBody] ReviewCapacityReductionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(
            new ApproveAdminCapacityReductionCommand(id, request?.Notes),
            cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("reductions/{id:guid}/reject")]
    public async Task<IActionResult> RejectCapacityReduction(
        Guid id,
        [FromBody] ReviewCapacityReductionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(
            new RejectAdminCapacityReductionCommand(id, request?.Notes),
            cancellationToken);
        return HandleResult(result);
    }
}
