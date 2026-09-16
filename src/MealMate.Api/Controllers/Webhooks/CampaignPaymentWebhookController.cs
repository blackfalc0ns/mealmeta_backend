using MealMate.Api.Common;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
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

        // If secret is configured, enforce strict verification
        if (!string.IsNullOrEmpty(expectedSecret) && incomingSecret != expectedSecret)
        {
            return Unauthorized();
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }
}
