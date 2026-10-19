using MealMate.Api.Common;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.Webhooks;

[AllowAnonymous]
[Route("api/v1/webhooks/campaign-payment")]
public sealed class CampaignPaymentWebhookController : ApiControllerBase
{
    private IConfiguration? _config;
    private ICampaignPaymentService? _payments;

    [ActivatorUtilitiesConstructor]
    public CampaignPaymentWebhookController(IConfiguration config, ICampaignPaymentService payments)
    {
        _config = config;
        _payments = payments;
    }

    public CampaignPaymentWebhookController() { }

    public CampaignPaymentWebhookController(IConfiguration config)
    {
        _config = config;
    }

    private IConfiguration Config => _config ??= HttpContext.RequestServices.GetRequiredService<IConfiguration>();

    [HttpPost("callback")]
    public async Task<IActionResult> ProcessCallback([FromBody] ProcessPaymentCallbackCommand command, CancellationToken cancellationToken)
    {
        var expectedSecret = Config["PaymentGateways:CampaignWebhookSecret"];
        var incomingSecret = Request.Headers["X-Webhook-Secret"].ToString();

        // Fail closed: an unconfigured webhook secret disables the callback instead of trusting anonymous calls.
        if (string.IsNullOrEmpty(expectedSecret))
        {
            return StatusCode(503);
        }

        if (incomingSecret != expectedSecret)
        {
            return Unauthorized();
        }

        var payments = _payments ?? HttpContext.RequestServices.GetService<ICampaignPaymentService>();
        var result = payments is null
            ? await Sender.Send(command, cancellationToken)
            : await payments.ProcessCallbackAsync(command, cancellationToken);
        return HandleResult(result);
    }
}
