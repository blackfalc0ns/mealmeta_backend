using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[AllowAnonymous]
[Route("api/v1/webhooks/myfatoorah")]
public sealed class MyFatoorahWebhookController : ApiControllerBase
{
    private readonly IMyFatoorahPaymentService _paymentService;

    public MyFatoorahWebhookController(IMyFatoorahPaymentService paymentService) => _paymentService = paymentService;

    [HttpPost]
    public async Task<IActionResult> HandleWebhook([FromBody] MyFatoorahWebhookPayload payload, CancellationToken cancellationToken)
    {
        var sigHeader = Request.Headers["MyFatoorah-Signature"].ToString();
        var isSignatureValid = _paymentService.VerifyWebhookSignature(
            System.Text.Json.JsonSerializer.Serialize(payload),
            sigHeader,
            "secret_key"
        );

        if (payload.Data == null) return BadRequest("Invalid payload format");

        if (payload.Event == "PaymentStatusChanged" && payload.Data.TransactionStatus == "SUCCESS")
        {
            if (Guid.TryParse(payload.Data.CustomerReference, out var subId))
            {
                var confirmCmd = new ConfirmSubscriptionPaymentCommand(
                    subId,
                    payload.Data.PaymentId
                );
                await Sender.Send(confirmCmd, cancellationToken);
            }
        }

        return Ok(new { Message = "Webhook processed successfully" });
    }
}

public sealed record MyFatoorahWebhookPayload(
    string Event,
    MyFatoorahWebhookData? Data
);

public sealed record MyFatoorahWebhookData(
    string PaymentId,
    string InvoiceId,
    string TransactionStatus,
    decimal Amount,
    string? CustomerReference,
    string? PaymentGateway
);
