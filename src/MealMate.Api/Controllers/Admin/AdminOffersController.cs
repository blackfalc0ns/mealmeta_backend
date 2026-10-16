using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Offers.Commands;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Domain.Offers.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.Admin;

[Authorize]
[Route("api/v1/admin/offers")]
public sealed class AdminOffersController : ApiControllerBase
{
    [HttpPost]
    [HasPermission(PermissionCodes.OffersManage)]
    public async Task<IActionResult> Create([FromBody] CreateOfferDraftCommand command, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken);
        return HandleCreated(result, $"api/v1/admin/offers/{result.Value?.Id}");
    }

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.OffersManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOfferDraftCommand command, CancellationToken cancellationToken)
    {
        if (id != command.OfferId)
        {
            command = command with { OfferId = id };
        }
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/transition")]
    [HasPermission(PermissionCodes.OffersManage)]
    public async Task<IActionResult> Transition(Guid id, [FromBody] TransitionOfferStatusCommand command, CancellationToken cancellationToken)
    {
        if (id != command.OfferId)
        {
            command = command with { OfferId = id };
        }
        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet]
    [HasPermission(PermissionCodes.OffersView)]
    public async Task<IActionResult> List([FromQuery] OfferStatus? status, [FromQuery] PageRequest? page, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetOffersListQuery(status, page, search), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.OffersView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetOfferByIdQuery(id), cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}/redemptions")]
    [HasPermission(PermissionCodes.OffersView)]
    public async Task<IActionResult> GetRedemptions(Guid id, [FromQuery] PageRequest? page, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetOfferRedemptionsQuery(id, page), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("preview")]
    [HasPermission(PermissionCodes.OffersView)]
    public async Task<IActionResult> Preview([FromBody] EvaluateOfferDiscountQuery query, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(query, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/preview")]
    [HasPermission(PermissionCodes.OffersView)]
    public async Task<IActionResult> PreviewOffer(Guid id, [FromBody] EvaluateOfferDiscountQuery query, CancellationToken cancellationToken)
    {
        query = query with { OfferId = id };
        var result = await Sender.Send(query, cancellationToken);
        return HandleResult(result);
    }
}
