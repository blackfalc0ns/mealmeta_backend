using MealMate.Api.Common;
using MealMate.Application.Features.AdminOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize(Policy = "AdminPolicy")]
[Route("api/v1/admin/orders")]
public sealed class AdminOrdersController : ApiControllerBase
{
    [HttpGet("confirmation-overdue")]
    public async Task<IActionResult> GetConfirmationOverdue(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return HandleResult(await Sender.Send(
            new GetConfirmationOverdueOrdersQuery(pageNumber, pageSize),
            cancellationToken));
    }
}
