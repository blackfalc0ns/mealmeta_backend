using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace MealMate.Api.Controllers.V1;

[AllowAnonymous]
[Route("api/v1/webhooks/myfatoorah")]
public sealed class MyFatoorahWebhookController : ApiControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IEmailSecretProtector _protector;
    private readonly IMyFatoorahPaymentService _paymentService;
    private readonly ISubscriptionPaymentProcessor _paymentProcessor;

    public MyFatoorahWebhookController(
        IAppDbContext db,
        IEmailSecretProtector protector,
        IMyFatoorahPaymentService paymentService,
        ISubscriptionPaymentProcessor paymentProcessor)
    {
        _db = db;
        _protector = protector;
        _paymentService = paymentService;
        _paymentProcessor = paymentProcessor;
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
            // Compatibility for the old settings screen; the next save encrypts it.
            webhookSecret = config.EncryptedWebhookSecret;
        }
        var model = JsonSerializer.Deserialize<MyFatoorahWebhookV2Payload>(payload.GetRawText(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (model?.Event is null || model.Data?.Invoice is null || model.Data.Transaction is null || model.Data.Amount is null)
            return BadRequest(new { error = "payment.webhook_v2_payload_invalid" });
        if (model.Event.Code != 1 || !string.Equals(model.Event.Name, "PAYMENT_STATUS_CHANGED", StringComparison.OrdinalIgnoreCase))
            return Ok(new { Message = "Webhook event ignored" });

        var signatureData = BuildPaymentStatusSignature(model.Data);
        if (!_paymentService.VerifyWebhookSignature(signatureData, sigHeader, webhookSecret))
            return Unauthorized("Invalid webhook signature");

        var customerReference = !string.IsNullOrWhiteSpace(model.Data.Invoice.UserDefinedField)
            ? model.Data.Invoice.UserDefinedField
            : model.Data.Invoice.ExternalIdentifier;
        if (!Guid.TryParse(customerReference, out var subscriptionId))
            return BadRequest(new { error = "payment.customer_reference_invalid" });
        if (!decimal.TryParse(model.Data.Amount.ValueInBaseCurrency, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return BadRequest(new { error = "payment.webhook_amount_invalid" });

        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.GetRawText()))).ToLowerInvariant();
        PaymentProcessingResult processed;
        if (string.Equals(model.Data.Transaction.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            processed = await _paymentProcessor.VerifyAndProcessAsync(
                subscriptionId,
                model.Data.Transaction.PaymentId,
                model.Data.Invoice.Id,
                amount,
                model.Data.Amount.BaseCurrency,
                customerReference,
                payloadHash: payloadHash,
                cancellationToken: cancellationToken);
        }
        else
        {
            processed = await _paymentProcessor.VerifyAndReleaseFailedAsync(
                subscriptionId,
                model.Data.Transaction.PaymentId,
                model.Data.Invoice.Id,
                amount,
                customerReference,
                payloadHash,
                cancellationToken);
        }

        if (!processed.IsAccepted)
        {
            if (processed.ErrorCode == "payment.gateway_verification_unavailable")
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = processed.ErrorCode });
            return BadRequest(new { error = processed.ErrorCode });
        }
        return Ok(new { Message = "Webhook processed successfully", processed.IsDuplicate, processed.SubscriptionStatus, processed.FulfillmentStatus, processed.ErrorCode });
    }

    private static string BuildPaymentStatusSignature(MyFatoorahWebhookV2Data data)
        => $"Invoice.Id={data.Invoice.Id},Invoice.Status={data.Invoice.Status},Transaction.Status={data.Transaction.Status},Transaction.PaymentId={data.Transaction.PaymentId},Invoice.ExternalIdentifier={data.Invoice.ExternalIdentifier ?? string.Empty}";
}

public sealed record MyFatoorahWebhookV2Payload(MyFatoorahWebhookV2Event? Event, MyFatoorahWebhookV2Data? Data);
public sealed record MyFatoorahWebhookV2Event(int Code, string Name, string? CountryIsoCode, DateTime? CreationDate, string? Reference);
public sealed record MyFatoorahWebhookV2Data(MyFatoorahWebhookV2Invoice Invoice, MyFatoorahWebhookV2Transaction Transaction, MyFatoorahWebhookV2Amount Amount);
public sealed record MyFatoorahWebhookV2Invoice(string Id, string Status, string? UserDefinedField, string? ExternalIdentifier);
public sealed record MyFatoorahWebhookV2Transaction(string Status, string PaymentId);
public sealed record MyFatoorahWebhookV2Amount(string BaseCurrency, string ValueInBaseCurrency);
