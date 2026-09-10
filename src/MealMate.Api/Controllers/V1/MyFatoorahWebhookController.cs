using System.Text.Json;
using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.V1;

[AllowAnonymous]
[Route("api/v1/webhooks/myfatoorah")]
public sealed class MyFatoorahWebhookController : ApiControllerBase
{
    private readonly IMyFatoorahWebhookService _webhookService;

    [ActivatorUtilitiesConstructor]
    public MyFatoorahWebhookController(IMyFatoorahWebhookService webhookService)
    {
        _webhookService = webhookService;
    }

    /// <summary>
    /// Legacy constructor preserved for backward-compatibility with tests.
    /// </summary>
    public MyFatoorahWebhookController(
        IAppDbContext db,
        IEmailSecretProtector protector,
        IMyFatoorahPaymentService paymentService,
        ISubscriptionPaymentProcessor paymentProcessor)
        : this(new MyFatoorahWebhookService(db, protector, paymentService, paymentProcessor))
    {
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        var sigHeader = Request.Headers["MyFatoorah-Signature"].ToString();
        var result = await _webhookService.ProcessWebhookAsync(payload, sigHeader, cancellationToken);
        return result.StatusCode switch
        {
            StatusCodes.Status200OK => Ok(result.Response),
            StatusCodes.Status400BadRequest => BadRequest(result.Response),
            StatusCodes.Status401Unauthorized => Unauthorized(result.Response),
            _ => StatusCode(result.StatusCode, result.Response)
        };
    }
}
