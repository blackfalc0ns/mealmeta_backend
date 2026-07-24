using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Audit;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/audit-logs")]
public sealed class AuditLogsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.AuditView)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? action = null,
        [FromQuery] string? entityType = null,
        [FromQuery] Guid? actorUserId = null,
        [FromQuery] Guid? entityId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListAuditLogsQuery(page, pageSize, action, entityType, actorUserId, entityId, fromUtc, toUtc), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.AuditView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetAuditLogQuery(id), cancellationToken));

    [HttpGet("entities/{entityType}/{entityId:guid}")]
    [HasPermission(PermissionCodes.AuditView)]
    public async Task<IActionResult> ListForEntity(string entityType, Guid entityId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new ListEntityAuditLogsQuery(entityType, entityId, page, pageSize), cancellationToken));
}
