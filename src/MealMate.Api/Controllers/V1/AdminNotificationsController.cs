using MealMate.Api.Common;
using MealMate.Application.Features.AdminNotifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/admin/notifications")]
[Authorize]
public sealed class AdminNotificationsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] int limit = 30,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] bool? unreadOnly = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetAdminNotificationsQuery(limit, page, pageSize, unreadOnly), cancellationToken));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(
        Guid id,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new MarkAdminNotificationReadCommand(id), cancellationToken));

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllAsRead(
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new MarkAllAdminNotificationsReadCommand(), cancellationToken));
}
