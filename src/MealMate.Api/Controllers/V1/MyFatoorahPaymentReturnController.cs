using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[AllowAnonymous]
[Route("api/v1/payments/myfatoorah")]
public sealed class MyFatoorahPaymentReturnController : ControllerBase
{
    private readonly IMyFatoorahPaymentReturnService _returnService;

    public MyFatoorahPaymentReturnController(IMyFatoorahPaymentReturnService returnService)
    {
        _returnService = returnService;
    }

    /// <summary>
    /// Legacy constructor preserved for test backwards compatibility.
    /// </summary>
    public MyFatoorahPaymentReturnController(
        IMyFatoorahPaymentService payments,
        ISubscriptionPaymentProcessor paymentProcessor)
        : this(new MyFatoorahPaymentReturnService(payments, paymentProcessor))
    {
    }

    [HttpGet("return")]
    public async Task<IActionResult> Return([FromQuery] string? to, [FromQuery] string? paymentId)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        var html = await _returnService.ProcessReturnAndBuildHtmlAsync(
            to,
            paymentId,
            Request?.Query["Id"].ToString(),
            Request?.Query["InvoiceId"].ToString(),
            cancellationToken);

        return Content(html, "text/html; charset=utf-8");
    }
}
