using MealMate.Api.Common;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[ApiController]
[Route("api/v1/restaurants/catalog")]
[AllowAnonymous]
public sealed class RestaurantCatalogController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var programs = await Sender.Send(
            new GetProgramsListQuery(ProgramStatus.Active, null, 1, 100),
            cancellationToken);
        var bundles = await Sender.Send(
            new GetBundlesListQuery(null, ProgramStatus.Active, null, 1, 100),
            cancellationToken);

        if (programs.IsFailure) return HandleResult(programs);
        if (bundles.IsFailure) return HandleResult(bundles);

        return Ok(new
        {
            programs = programs.Value.Items.Where(item => item.IsActive),
            bundles = bundles.Value.Items.Where(item => item.IsActive),
        });
    }
}
