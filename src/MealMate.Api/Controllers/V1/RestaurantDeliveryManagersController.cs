using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/delivery-managers")]
[Authorize(Roles = $"{AppRoles.RestaurantOwner},{AppRoles.SuperAdmin},{AppRoles.OperationsAdmin}")]
public sealed class RestaurantDeliveryManagersController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? restaurantId = null, CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new ListDeliveryManagersQuery(restaurantId), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateRestaurantDeliveryManagerRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new RegisterDeliveryManagerCommand(
            body.FullName,
            body.Email,
            body.Phone,
            body.Password,
            body.RestaurantId,
            body.RegionId), cancellationToken);

        return HandleCreated(result, "/api/v1/auth/delivery-managers/login");
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRestaurantDeliveryManagerRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration.RegisterRestaurantDeliveryManagerCommand(
            body.FullName,
            body.Phone,
            body.Email,
            body.NationalId,
            body.NationalIdExpiry,
            body.ContractExpiry,
            body.CoverageType,
            body.CoverageScopeId,
            body.CoverageScopeName,
            body.RestaurantId,
            body.NationalIdFrontStorageKey,
            body.NationalIdBackStorageKey,
            body.ContractStorageKey), cancellationToken);

        return HandleCreated(result, $"/api/v1/restaurants/delivery-managers");
    }

    [HttpPost("{id:guid}/toggle-status")]
    public async Task<IActionResult> ToggleStatus(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new ToggleDeliveryManagerStatusCommand(id), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/resend-invite")]
    public async Task<IActionResult> ResendInvite(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new ResendDeliveryManagerInviteCommand(id), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record RegisterRestaurantDeliveryManagerRequest(
    string FullName,
    string Phone,
    string Email,
    string NationalId,
    DateTime NationalIdExpiry,
    DateTime ContractExpiry,
    MealMate.Domain.Enums.DeliveryManagerCoverageType CoverageType,
    string? CoverageScopeId,
    string? CoverageScopeName,
    Guid? RestaurantId,
    string NationalIdFrontStorageKey,
    string NationalIdBackStorageKey,
    string ContractStorageKey);

public sealed record CreateRestaurantDeliveryManagerRequest(
    string FullName,
    string Email,
    string Phone,
    string? Password,
    Guid? RestaurantId = null,
    string? RegionId = null);
