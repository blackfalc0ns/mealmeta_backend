using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MealMate.Api.Controllers.V1;

[AllowAnonymous]
[Route("api/v1/webhooks/myfatoorah")]
public sealed class MyFatoorahWebhookController : ApiControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IEmailSecretProtector _protector;
    private readonly IMyFatoorahPaymentService _paymentService;

    public MyFatoorahWebhookController(
        IAppDbContext db,
        IEmailSecretProtector protector,
        IMyFatoorahPaymentService paymentService)
    {
        _db = db;
        _protector = protector;
        _paymentService = paymentService;
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        var config = await _db.MyFatoorahGatewayConfigs
            .FirstOrDefaultAsync(c => c.GatewayName == "MyFatoorah", cancellationToken);

        if (config == null || string.IsNullOrWhiteSpace(config.EncryptedWebhookSecret))
            return Unauthorized("MyFatoorah webhook secret is not configured");

        var sigHeader = Request.Headers["MyFatoorah-Signature"].ToString();
        string webhookSecret;
        try
        {
            webhookSecret = _protector.Decrypt(config.EncryptedWebhookSecret);
        }
        catch
        {
            return Unauthorized("MyFatoorah webhook secret is invalid");
        }

        var isSignatureValid = _paymentService.VerifyWebhookSignature(
            payload.GetRawText(),
            sigHeader,
            webhookSecret
        );

        if (!isSignatureValid) return Unauthorized("Invalid webhook signature");
        var model = JsonSerializer.Deserialize<MyFatoorahWebhookPayload>(payload.GetRawText());
        if (model?.Data == null) return BadRequest("Invalid payload format");

        if (model.Event == "PaymentStatusChanged" && model.Data.TransactionStatus == "SUCCESS")
        {
            if (Guid.TryParse(model.Data.CustomerReference, out var subId))
            {
                var confirmCmd = new ConfirmSubscriptionPaymentCommand(
                    subId,
                    model.Data.PaymentId
                );
                await Sender.Send(confirmCmd, cancellationToken);
            }
        }
        else if (model.Event == "PaymentStatusChanged" && Guid.TryParse(model.Data.CustomerReference, out var failedSubscriptionId))
        {
            // A failed/cancelled gateway attempt must immediately release its temporary
            // capacity rather than waiting for the TTL sweep.
            var holds = await _db.CapacityHolds
                .Where(x => x.SubscriptionId == failedSubscriptionId && x.Status == CapacityHoldStatus.PendingPayment)
                .ToListAsync(cancellationToken);
            foreach (var hold in holds)
            {
                hold.Status = CapacityHoldStatus.Released;
                hold.ReleasedAtUtc = DateTime.UtcNow;
            }
            if (holds.Count > 0) await _db.SaveChangesAsync(cancellationToken);
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
