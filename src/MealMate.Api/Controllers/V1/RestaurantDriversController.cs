using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/drivers")]
[Authorize]
public sealed class RestaurantDriversController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var targetRestaurantId = restaurantId ?? Guid.Empty;
        var result = await Sender.Send(new ListRestaurantDriversQuery(targetRestaurantId, isActive), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetRestaurantDriverDetailQuery(id), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRestaurantDriverCommand body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(body, cancellationToken);
        return HandleCreated(result, "/api/v1/restaurants/drivers");
    }

    [HttpGet("registrations")]
    public async Task<IActionResult> ListRegistrations(
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] Domain.Enums.VerificationStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await Sender.Send(new GetRestaurantDriverRegistrationsQuery(restaurantId, status, page, pageSize), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("registrations/{id:guid}/decision")]
    public async Task<IActionResult> MakeDecision(
        Guid id,
        [FromBody] RestaurantDriverDecisionRequest body,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new RestaurantDriverDecisionCommand(id, body.Action, body.Reason), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record RestaurantDriverDecisionRequest(string Action, string Reason);
