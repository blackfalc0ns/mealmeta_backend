using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed class MyFatoorahWebhookService : IMyFatoorahWebhookService
{
    private readonly IAppDbContext _db;
    private readonly IEmailSecretProtector _protector;
    private readonly IMyFatoorahPaymentService _paymentService;
    private readonly ISubscriptionPaymentProcessor _paymentProcessor;

    public MyFatoorahWebhookService(
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

    public async Task<MyFatoorahWebhookExecutionResult> ProcessWebhookAsync(
        JsonElement payload,
        string? sigHeader,
        CancellationToken cancellationToken = default)
    {
        var config = await _db.MyFatoorahGatewayConfigs
            .FirstOrDefaultAsync(c => c.GatewayName == "MyFatoorah", cancellationToken);

        if (config == null || string.IsNullOrWhiteSpace(config.EncryptedWebhookSecret))
            return new MyFatoorahWebhookExecutionResult(401, "MyFatoorah webhook secret is not configured");

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
            return new MyFatoorahWebhookExecutionResult(400, new { error = MessageCodes.Payment.WebhookPayloadInvalid });

        if (model.Event.Code != 1 || !string.Equals(model.Event.Name, "PAYMENT_STATUS_CHANGED", StringComparison.OrdinalIgnoreCase))
            return new MyFatoorahWebhookExecutionResult(200, new { Message = "Webhook event ignored" });

        var signatureData = BuildPaymentStatusSignature(model.Data);
        if (!_paymentService.VerifyWebhookSignature(signatureData, sigHeader ?? string.Empty, webhookSecret))
            return new MyFatoorahWebhookExecutionResult(401, "Invalid webhook signature");

        var customerReference = !string.IsNullOrWhiteSpace(model.Data.Invoice.UserDefinedField)
            ? model.Data.Invoice.UserDefinedField
            : model.Data.Invoice.ExternalIdentifier;

        if (!Guid.TryParse(customerReference, out var subscriptionId))
            return new MyFatoorahWebhookExecutionResult(400, new { error = MessageCodes.Payment.CustomerReferenceInvalid });

        if (!decimal.TryParse(model.Data.Amount.ValueInBaseCurrency, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return new MyFatoorahWebhookExecutionResult(400, new { error = MessageCodes.Payment.WebhookAmountInvalid });

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
            if (processed.ErrorCode == MessageCodes.Payment.GatewayVerificationUnavailable)
                return new MyFatoorahWebhookExecutionResult(503, new { error = processed.ErrorCode });

            return new MyFatoorahWebhookExecutionResult(400, new { error = processed.ErrorCode });
        }

        return new MyFatoorahWebhookExecutionResult(200, new
        {
            Message = "Webhook processed successfully",
            processed.IsDuplicate,
            processed.SubscriptionStatus,
            processed.FulfillmentStatus,
            processed.ErrorCode
        });
    }

    private static string BuildPaymentStatusSignature(MyFatoorahWebhookV2Data data)
        => $"Invoice.Id={data.Invoice.Id},Invoice.Status={data.Invoice.Status},Transaction.Status={data.Transaction.Status},Transaction.PaymentId={data.Transaction.PaymentId},Invoice.ExternalIdentifier={data.Invoice.ExternalIdentifier ?? string.Empty}";
}
