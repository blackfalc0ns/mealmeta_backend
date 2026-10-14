using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MealMate.Api.Controllers.Restaurant;

public sealed record RespondCampaignInvitationRequest(
    bool Accept,
    int? DedicatedDailyCapacity = null,
    string? DeclineReason = null);

public sealed record UpdateCapacityRequest(
    Guid? RestaurantId,
    int NewDailyCapacity);

[Authorize]
[Route("api/v1/restaurant/campaigns")]
[Route("api/v1/restaurants/campaigns")]
public sealed class RestaurantCollaborativeCampaignsController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<RestaurantCollaborativeCampaignsController> _logger;

    public RestaurantCollaborativeCampaignsController(
        ICurrentUser currentUser,
        ILogger<RestaurantCollaborativeCampaignsController> logger)
    {
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActiveCampaigns([FromQuery] Guid? restaurantId, CancellationToken cancellationToken)
    {
        // Identity comes from the token only; a client-supplied restaurantId is never trusted.
        if (!_currentUser.RestaurantId.HasValue)
        {
            return Forbid();
        }

        if (restaurantId.HasValue && restaurantId.Value != _currentUser.RestaurantId.Value)
        {
            return Forbid();
        }

        var result = await Sender.Send(new GetRestaurantActiveCampaignsQuery(_currentUser.RestaurantId), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("invitations")]
    public async Task<IActionResult> GetInvitations([FromQuery] Guid? restaurantId, CancellationToken cancellationToken)
    {
        if (!_currentUser.RestaurantId.HasValue)
        {
            return Forbid();
        }

        if (restaurantId.HasValue && restaurantId.Value != _currentUser.RestaurantId.Value)
        {
            return Forbid();
        }

        var result = await Sender.Send(new GetRestaurantCampaignInvitationsQuery(_currentUser.RestaurantId), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("invitations/{enrollmentId:guid}/respond")]
    public async Task<IActionResult> RespondToInvitation(
        Guid enrollmentId,
        [FromBody] RespondCampaignInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RespondToCampaignInvitationCommand(
            enrollmentId,
            request.Accept,
            request.DedicatedDailyCapacity,
            request.DeclineReason);

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/reduce-capacity")]
    public async Task<IActionResult> ReduceCapacity(
        Guid id,
        [FromBody] UpdateCapacityRequest request,
        CancellationToken cancellationToken)
    {
        // The acting restaurant is always the token's restaurant.
        var restaurantId = _currentUser.RestaurantId;
        if (!restaurantId.HasValue)
        {
            return Forbid();
        }

        if (request.RestaurantId.HasValue && request.RestaurantId.Value != restaurantId.Value)
        {
            return Forbid();
        }

        var command = new UpdateRestaurantCampaignCapacityCommand(id, restaurantId.Value, request.NewDailyCapacity);
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/request-capacity-change")]
    public async Task<IActionResult> RequestCapacityChange(
        Guid id,
        [FromBody] SubmitCapacityChangeRequestCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserType != UserType.Restaurant || !_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId != command.RestaurantId)
        {
            return Forbid();
        }

        if (id != command.CampaignId)
        {
            command = command with { CampaignId = id };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }
}
