using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/review")]
[Authorize]
public sealed class RestaurantReviewController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetRestaurantReviewCenterQuery(), cancellationToken));

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetRestaurantReviewNotificationsQuery(), cancellationToken));

    [HttpPost("notifications/{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new MarkRestaurantReviewNotificationReadCommand(id), cancellationToken));

    [HttpPost("notifications/mark-all-read")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new MarkAllRestaurantReviewNotificationsReadCommand(), cancellationToken));

    [HttpPut("sections/{section}/draft")]
    public async Task<IActionResult> SaveDraft(string section, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SaveRestaurantReviewSectionDraftCommand(section), cancellationToken));

    [HttpPost("sections/{section}/submit")]
    public async Task<IActionResult> SubmitSection(string section, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SubmitRestaurantReviewSectionCommand(section), cancellationToken));
}

[Route("api/v1/admin/restaurants/reviews")]
public sealed class AdminRestaurantReviewController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? section = null, [FromQuery] VerificationStatus? status = null, [FromQuery] Guid? restaurantId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new AdminRestaurantReviewListQuery(section, status, restaurantId, page, pageSize), cancellationToken));

    [HttpPost("{id:guid}/decision")]
    public async Task<IActionResult> Decision(Guid id, [FromBody] AdminReviewDecisionRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new AdminReviewItemDecisionCommand(id, request.Action, request.Reason), cancellationToken));
}

public sealed record AdminReviewDecisionRequest(string Action, string? Reason);
