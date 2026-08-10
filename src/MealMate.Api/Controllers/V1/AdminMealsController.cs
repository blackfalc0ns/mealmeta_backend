using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.RestaurantMeals;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

public sealed record RejectMealRequest(string Reason);

[Route("api/v1/admin/meals")]
[Authorize]
public sealed class AdminMealsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> GetMeals(
        [FromQuery] Guid? restaurantId,
        [FromQuery] VerificationStatus? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetAdminMealsListQuery(restaurantId, status, search, page, pageSize), cancellationToken));

    [HttpPost("{id:guid}/approve")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ApproveMealCommand(id), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [HasPermission(PermissionCodes.RestaurantMealsReject)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectMealRequest request, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new RejectMealCommand(id, request.Reason), cancellationToken));

    [HttpPost("{id:guid}/archive")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new ArchiveMealCommand(id), cancellationToken));

    [HttpPost("{id:guid}/unarchive")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new UnarchiveMealCommand(id), cancellationToken));
}
