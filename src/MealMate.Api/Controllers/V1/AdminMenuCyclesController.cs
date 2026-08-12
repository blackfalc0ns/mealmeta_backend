using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.MenuCycles;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/admin/menu-cycles")]
public sealed class AdminMenuCyclesController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> List(CancellationToken ct)
        => HandleResult(await Sender.Send(new GetAdminMenuCyclesQuery(), ct));

    [HttpGet("{cycleId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Get(Guid cycleId, CancellationToken ct)
        => HandleResult(await Sender.Send(new GetAdminMenuCycleQuery(cycleId), ct));

    [HttpGet("{cycleId:guid}/preview")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Preview(Guid cycleId, [FromQuery] DateOnly? from, [FromQuery] int days = 14, CancellationToken ct = default)
        => HandleResult(await Sender.Send(new PreviewAdminMenuCycleQuery(cycleId, from ?? DateOnly.FromDateTime(DateTime.UtcNow), days), ct));

    [HttpPost("{cycleId:guid}/future-version")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> FutureVersion(Guid cycleId, [FromBody] FutureVersionRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new CreateAdminFutureMenuCycleVersionCommand(cycleId, body.EffectiveFrom), ct));

    [HttpPost("{cycleId:guid}/approve")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> Approve(Guid cycleId, [FromBody] MenuCycleReviewRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new ReviewMenuCycleCommand(cycleId, VerificationStatus.Approved, body.Notes), ct));

    [HttpPost("{cycleId:guid}/reject")]
    [HasPermission(PermissionCodes.RestaurantMealsReject)]
    public async Task<IActionResult> Reject(Guid cycleId, [FromBody] MenuCycleReviewRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new ReviewMenuCycleCommand(cycleId, VerificationStatus.Rejected, body.Notes), ct));

    [HttpPost("{cycleId:guid}/request-changes")]
    [HasPermission(PermissionCodes.RestaurantMealsReject)]
    public async Task<IActionResult> RequestChanges(Guid cycleId, [FromBody] MenuCycleReviewRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new ReviewMenuCycleCommand(cycleId, VerificationStatus.NeedsChanges, body.Notes), ct));

    [HttpPost("{cycleId:guid}/pause")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> Pause(Guid cycleId, [FromBody] MenuCycleReviewRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new SetMenuCyclePausedCommand(cycleId, true, body.Notes), ct));

    [HttpPost("{cycleId:guid}/resume")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> Resume(Guid cycleId, [FromBody] MenuCycleReviewRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new SetMenuCyclePausedCommand(cycleId, false, body.Notes), ct));

    [HttpGet("program-slot-requirements")]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> Requirements(CancellationToken ct)
        => HandleResult(await Sender.Send(new GetProgramSlotRequirementsQuery(), ct));

    [HttpPut("program-slot-requirements/{programId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> SetRequirements(Guid programId, [FromBody] ProgramSlotRequirementsRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new SetProgramSlotRequirementsCommand(programId, body.Slots), ct));
}

public sealed record MenuCycleReviewRequest(string? Notes);
public sealed record ProgramSlotRequirementsRequest(IReadOnlyList<string> Slots);
