using MealMate.Api.Common;
using MealMate.Application.Features.Home;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Route("api/v1/customer/home")]
[Authorize]
public sealed class CustomerHomeController : ApiControllerBase
{
    /// <summary>
    /// Returns the customer home-screen payload. <c>subscription</c> is null
    /// unless the authenticated customer currently has an active subscription.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetCustomerHomeQuery(), cancellationToken));
}
