using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/family-groups")]
public sealed class AdminFamilyGroupsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> List(
        [FromQuery] FamilyGroupStatus? status = null,
        [FromQuery] string? q = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetAdminFamilyGroupsListQuery(status, q, page, pageSize), cancellationToken));

    [HttpGet("stats")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> Stats(CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetAdminFamilyGroupStatsQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAdminFamilyGroupByIdQuery(id), cancellationToken));

    [HttpGet("{id:guid}/history")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> History(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] int evidencePage = 1, [FromQuery] int evidencePageSize = 10,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetAdminFamilyGroupHistoryQuery(id, page, pageSize,
            evidencePage, evidencePageSize), cancellationToken));

    [HttpGet("{id:guid}/cancellations")]
    [HasPermission(PermissionCodes.SubscriptionsView)]
    public async Task<IActionResult> Cancellations(Guid id, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetAdminFamilyCancellationHistoryQuery(id, page, pageSize), cancellationToken));
}
