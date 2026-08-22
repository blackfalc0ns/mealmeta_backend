using MealMate.Api.Common;
using MealMate.Application.Features.IdentityAccess.RestaurantRegistration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/restaurants/settings")]
[Authorize]
public sealed class RestaurantSettingsController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetRestaurantSettingsQuery(), cancellationToken));

    [HttpPut("draft")]
    public async Task<IActionResult> SaveDraft(
        [FromBody] RestaurantSettingsDraftPayload body,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SaveRestaurantSettingsDraftCommand(body), cancellationToken));

    [HttpPost("draft/submit")]
    public async Task<IActionResult> SubmitDraft(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new SubmitRestaurantSettingsDraftCommand(), cancellationToken));
}

public sealed record SettingsChangeDecisionRequest(string? Reason);
