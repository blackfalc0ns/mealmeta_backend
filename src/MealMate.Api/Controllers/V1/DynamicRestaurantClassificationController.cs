using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/dynamic_restaurant_classification")]
public sealed class DynamicRestaurantClassificationController : ApiControllerBase
{
    [HttpPost("run")]
    [HasPermission(PermissionCodes.ProgramsApprove)]
    public async Task<IActionResult> RunClassification([FromBody] RunClassificationCommand command, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken));

    [HttpGet("latest")]
    [HasPermission(PermissionCodes.ProgramsView)]
    public async Task<IActionResult> GetLatestRun(
        [FromQuery] Guid programId,
        [FromQuery] Guid bundleId,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetLatestClassificationRunQuery(programId, bundleId), cancellationToken));
}
