using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramNutrition;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/admin/programs/nutrition-standards")]
public sealed class AdminProgramNutritionStandardsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.RestaurantMealsView)]
    public async Task<IActionResult> List([FromQuery] Guid? programId, CancellationToken ct)
        => HandleResult(await Sender.Send(new GetProgramNutritionStandardsQuery(programId), ct));

    [HttpPut("{programId:guid}")]
    [HasPermission(PermissionCodes.RestaurantMealsApprove)]
    public async Task<IActionResult> Upsert(
        Guid programId,
        [FromBody] UpsertProgramNutritionStandardsRequest body,
        CancellationToken ct)
        => HandleResult(await Sender.Send(new UpsertProgramNutritionStandardsCommand(programId, body.Standards), ct));
}

public sealed record UpsertProgramNutritionStandardsRequest(
    IReadOnlyList<ProgramNutritionStandardInput> Standards);
