using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.MenuCycles;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/admin/menu-plans")]
public sealed class AdminMenuPlansController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> List(CancellationToken ct)
        => HandleResult(await Sender.Send(new GetAdminMenuCyclesQuery(), ct));

    [HttpGet("{planId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Get(Guid planId, CancellationToken ct)
        => HandleResult(await Sender.Send(new GetAdminMenuCycleQuery(planId), ct));

    [HttpGet("{planId:guid}/preview")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Preview(Guid planId, [FromQuery] DateOnly? from, [FromQuery] int days = 14, CancellationToken ct = default)
        => HandleResult(await Sender.Send(new PreviewAdminMenuCycleQuery(planId, from ?? DateOnly.FromDateTime(DateTime.UtcNow), days), ct));

    [HttpPost("{planId:guid}/approve")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> Approve(Guid planId, [FromBody] MenuCycleReviewRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new ReviewMenuCycleCommand(planId, VerificationStatus.Approved, body.Notes), ct));

    [HttpPost("{planId:guid}/reject")]
    [HasPermission(PermissionCodes.RestaurantMealsReject)]
    public async Task<IActionResult> Reject(Guid planId, [FromBody] MenuCycleReviewRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new ReviewMenuCycleCommand(planId, VerificationStatus.Rejected, body.Notes), ct));

    [HttpPost("{planId:guid}/request-changes")]
    [HasPermission(PermissionCodes.RestaurantMealsReject)]
    public async Task<IActionResult> RequestChanges(Guid planId, [FromBody] MenuCycleReviewRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new ReviewMenuCycleCommand(planId, VerificationStatus.NeedsChanges, body.Notes), ct));

    [HttpPost("{planId:guid}/pause")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> Pause(Guid planId, [FromBody] MenuCycleReviewRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new SetMenuCyclePausedCommand(planId, true, body.Notes), ct));

    [HttpPost("{planId:guid}/resume")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> Resume(Guid planId, [FromBody] MenuCycleReviewRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new SetMenuCyclePausedCommand(planId, false, body.Notes), ct));

    [HttpGet("program-slot-requirements")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Requirements(CancellationToken ct)
        => HandleResult(await Sender.Send(new GetProgramSlotRequirementsQuery(), ct));

    [HttpPut("program-slot-requirements/{programId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> SetRequirements(Guid programId, [FromBody] ProgramSlotRequirementsRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new SetProgramSlotRequirementsCommand(programId, body.Slots), ct));
}
