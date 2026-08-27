using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.PrepDocuments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/admin/operations/prep-documents")]
public sealed class AdminPrepDocumentsController : ApiControllerBase
{
    [HttpPost("{subscriptionDayId:guid}/reissue")]
    [HasPermission(PermissionCodes.SettingsManage)]
    public async Task<IActionResult> Reissue(
        Guid subscriptionDayId,
        [FromQuery] bool newBarcode = false,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(
            new ReissuePrepDocumentsCommand(subscriptionDayId, newBarcode),
            cancellationToken));
}
