using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
namespace MealMate.Application.Features.CustomerCheckout;

public sealed class PayzahWebhookService(IAppDbContext db,
    IPaymentGatewayService payments, ISubscriptionPaymentProcessor processor) : IPayzahWebhookService
{
    public async Task<PayzahWebhookExecutionResult> ProcessWebhookAsync(JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        // Payzah documents callback fields, not an HMAC signature. Only bound server verification is authoritative.
        if (!payload.TryGetProperty("trackId", out var track) || track.ValueKind != JsonValueKind.String)
            return new(400, new { error = "payment.callback_track_required" });
        var attempt = await db.PaymentGatewayAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == "Payzah" && x.TrackId == track.GetString(), cancellationToken);
        if (attempt is null || string.IsNullOrWhiteSpace(attempt.PaymentId)) return new(400, new { error = "payment.unbound_attempt" });
        var verified = await payments.GetPaymentStatusAsync("pz-" + attempt.TrackId, "PaymentId", cancellationToken);
        if (verified is null || !Guid.TryParse(attempt.CustomerReference, out var reference)) return new(400, new { error = "payment.verification_failed" });
        var isPaid = string.Equals(verified.InvoiceStatus, "Paid", StringComparison.OrdinalIgnoreCase)
            || string.Equals(verified.InvoiceStatus, "Success", StringComparison.OrdinalIgnoreCase);
        var isFailed = string.Equals(verified.InvoiceStatus, "Failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(verified.InvoiceStatus, "Expired", StringComparison.OrdinalIgnoreCase)
            || string.Equals(verified.InvoiceStatus, "Canceled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(verified.InvoiceStatus, "Cancelled", StringComparison.OrdinalIgnoreCase);

        var result = isPaid
            ? await processor.VerifyAndProcessAsync(reference, "pz-" + attempt.TrackId, cancellationToken: cancellationToken)
            : isFailed
                ? await processor.VerifyAndReleaseFailedAsync(reference, "pz-" + attempt.TrackId, cancellationToken: cancellationToken)
                : null;
        return new(200, new { verified = result?.IsAccepted ?? false, status = verified.InvoiceStatus });
    }
}
