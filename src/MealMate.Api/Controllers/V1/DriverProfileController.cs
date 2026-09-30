using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/driver/profile")]
[Authorize(Roles = AppRoles.Driver)]
public sealed class DriverProfileController : ApiControllerBase
{
    /// <summary>Screen 09.02: authenticated driver's profile, vehicle, documents and operational summary.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(DriverProfileResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetDriverProfileQuery(), cancellationToken));
}
