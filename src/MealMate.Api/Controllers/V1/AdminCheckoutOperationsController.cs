using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/checkout")]
public sealed class AdminCheckoutOperationsController : ApiControllerBase
{
    private readonly IAdminCheckoutOperationsService _checkoutOperations;
    private readonly ICurrentUser _currentUser;

    public AdminCheckoutOperationsController(
        IAdminCheckoutOperationsService checkoutOperations,
        ICurrentUser currentUser)
    {
        _checkoutOperations = checkoutOperations;
        _currentUser = currentUser;
    }

    [HttpGet("capacity-holds")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> Holds([FromQuery] CapacityHoldStatus? status, CancellationToken cancellationToken)
        => HandleResult(await _checkoutOperations.GetCapacityHoldsAsync(status, cancellationToken));

    [HttpGet("snapshot-migration-issues")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> SnapshotMigrationIssues([FromQuery] bool unresolvedOnly = true, CancellationToken cancellationToken = default)
        => HandleResult(await _checkoutOperations.GetSnapshotMigrationIssuesAsync(unresolvedOnly, cancellationToken));

    [HttpPost("capacity-holds/expire")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> ExpireHolds(CancellationToken cancellationToken)
        => HandleResult(await _checkoutOperations.ExpireHoldsAsync(cancellationToken));

    [HttpPost("/api/v1/admin/subscriptions/{id:guid}/retry-fulfillment")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> RetryFulfillment(Guid id, CancellationToken cancellationToken)
    {
        var result = await _checkoutOperations.RetryFulfillmentAsync(id, cancellationToken);
        if (result.IsFailure) return HandleResult(result);
        var outcome = result.Value;
        return outcome.Status == "Completed"
            ? Ok(new { subscriptionId = id, status = outcome.Status, outcome.IsAlreadyComplete })
            : Conflict(new { subscriptionId = id, status = outcome.Status, error = outcome.ErrorCode });
    }

    [HttpPost("/api/v1/admin/subscriptions/{id:guid}/days/{date}/reassign-restaurant")]
    [HasPermission(PermissionCodes.SubscriptionsManage)]
    public async Task<IActionResult> Reassign(Guid id, DateOnly date, [FromBody] ReassignRestaurantRequest request, CancellationToken cancellationToken)
    {
        var result = await _checkoutOperations.ReassignRestaurantAsync(
            id,
            date,
            request,
            _currentUser.UserId ?? Guid.Empty,
            _currentUser.UserType ?? UserType.Admin,
            cancellationToken);

        if (result.IsFailure) return HandleResult(result);

        var outcome = result.Value;
        return outcome.ErrorCode is null
            ? Ok(new { id = outcome.DayId, restaurantId = outcome.RestaurantId, resolvedMenuPlanVersionId = outcome.MenuPlanVersionId, status = outcome.Status })
            : BadRequest(new { error = outcome.ErrorCode, reasonCodes = outcome.ReasonCodes });
    }

    [HttpGet("/api/v1/admin/subscriptions/{id:guid}/days/{date}/restaurant-options")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    [ProducesResponseType(typeof(IReadOnlyList<FrozenRestaurantOption>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RestaurantOptions(Guid id, DateOnly date, CancellationToken cancellationToken)
    {
        var result = await _checkoutOperations.GetFrozenRestaurantOptionsAsync(id, date, cancellationToken);
        if (result.IsFailure && result.Error.Code == "subscription_day_not_found")
            return NotFound();
        return HandleResult(result);
    }
}
