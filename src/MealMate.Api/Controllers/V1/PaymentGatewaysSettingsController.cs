using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Settings;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/settings/payment_gateways/payzah")]
public sealed class PaymentGatewaysSettingsController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.SettingsManage)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetPayzahSettingsQuery(), cancellationToken));

    [HttpPut]
    [HasPermission(PermissionCodes.SettingsManage)]
    public async Task<IActionResult> Update([FromBody] UpdatePayzahSettingsCommand command, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken));

    [HttpPost("test-connection")]
    [HasPermission(PermissionCodes.SettingsManage)]
    public async Task<IActionResult> TestConnection([FromBody] TestPayzahConnectionCommand command, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken));
}
