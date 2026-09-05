using System;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/delivery-managers/registrations")]
[Authorize(Roles = $"{AppRoles.SuperAdmin},{AppRoles.OperationsAdmin}")]
public sealed class AdminDeliveryManagerRegistrationsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] VerificationStatus? status = null,
        [FromQuery] Guid? restaurantId = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListDeliveryManagerRegistrationsQuery(status, restaurantId), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetDeliveryManagerRegistrationByIdQuery(id), cancellationToken));

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] DecisionRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminDeliveryManagerDecisionCommand(id, "approve", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] DecisionRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminDeliveryManagerDecisionCommand(id, "reject", body.Reason), cancellationToken));

    [HttpPost("{id:guid}/request-changes")]
    public async Task<IActionResult> RequestChanges(Guid id, [FromBody] DecisionRequest body, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminDeliveryManagerDecisionCommand(id, "request-changes", body.Reason), cancellationToken));
}
