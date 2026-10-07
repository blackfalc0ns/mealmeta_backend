using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.V1;

[AllowAnonymous]
[Route("api/v1/payments/payzah")]
public sealed class PayzahPaymentReturnController : ControllerBase
{
    private readonly IPayzahPaymentReturnService _returnService;

    [ActivatorUtilitiesConstructor]
    public PayzahPaymentReturnController(IPayzahPaymentReturnService returnService)
    {
        _returnService = returnService;
    }

    /// <summary>
    /// Legacy constructor preserved for test backwards compatibility.
    /// </summary>
    public PayzahPaymentReturnController(
        IPaymentGatewayService payments,
        ISubscriptionPaymentProcessor paymentProcessor)
        : this(new PayzahPaymentReturnService(payments, paymentProcessor))
    {
    }

    [HttpGet("return")]
    [HttpPost("return")]
    public async Task<IActionResult> Return([FromQuery] string? to, [FromQuery] string? paymentId)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        var html = await _returnService.ProcessReturnAndBuildHtmlAsync(
            to,
            paymentId,
            Request?.Query["trackId"].ToString(),
            Request?.Query["InvoiceId"].ToString(),
            cancellationToken);

        return Content(html, "text/html; charset=utf-8");
    }
}
