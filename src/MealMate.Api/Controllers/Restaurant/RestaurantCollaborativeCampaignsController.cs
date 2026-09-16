using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.Restaurant;

[Authorize]
[Route("api/v1/restaurant/campaigns")]
public sealed class RestaurantCollaborativeCampaignsController : ApiControllerBase
{
    private ICurrentUser? _currentUser;

    public RestaurantCollaborativeCampaignsController() { }

    public RestaurantCollaborativeCampaignsController(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    private ICurrentUser CurrentUser => _currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    [HttpPost("{id:guid}/request-capacity-change")]
    public async Task<IActionResult> RequestCapacityChange(Guid id, [FromBody] SubmitCapacityChangeRequestCommand command, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Restaurant || CurrentUser.RestaurantId != command.RestaurantId)
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
