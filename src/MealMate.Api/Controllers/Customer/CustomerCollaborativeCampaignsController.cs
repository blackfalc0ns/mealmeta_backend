using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Queries;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.Customer;

[Route("api/v1/customer/campaigns")]
public sealed class CustomerCollaborativeCampaignsController : ApiControllerBase
{
    private ICurrentUser? _currentUser;

    [ActivatorUtilitiesConstructor]
    public CustomerCollaborativeCampaignsController(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public CustomerCollaborativeCampaignsController() { }

    private ICurrentUser CurrentUser => _currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    [AllowAnonymous]
    [HttpPost("price-preview")]
    public async Task<IActionResult> GetPricePreview([FromBody] GetCampaignPricePreviewQuery query, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(query, cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpPost("checkout-session")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateCampaignCheckoutSessionCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpPost("subscriptions/{id:guid}/cancel")]
    public async Task<IActionResult> CancelSubscription(Guid id, [FromBody] CancelCampaignSubscriptionCommand command, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        if (id != command.SubscriptionId)
        {
            command = command with { SubscriptionId = id };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }
}
