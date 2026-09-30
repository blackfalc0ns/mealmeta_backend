using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CollaborativeCampaigns.Services;
using MealMate.Domain.CustomerFinance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace MealMate.Infrastructure.Services;

public sealed class CampaignPaymentRefundGateway(IAppDbContext db, IMyFatoorahPaymentService payments,
    IApplicationTransactionExecutor transactions, IApplicationLockManager locks, IDateTimeProvider clock,
    ILogger<CampaignPaymentRefundGateway> logger) : IPaymentRefundGateway
{
    public async Task<bool> IssueRefundAsync(string paymentProvider, string providerTransactionId, decimal amount,
        string currency, string reason, CancellationToken cancellationToken = default)
    {
        var reference = "legacy-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paymentProvider + ":" + providerTransactionId)));
        return (await RequestRefundAsync(paymentProvider, providerTransactionId, amount, currency, reason, reference, cancellationToken)).IsConfirmed;
    }

    public async Task<RefundGatewayResult> RequestRefundAsync(string paymentProvider, string paymentId, decimal amount,
        string currency, string reason, string operationReference, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(paymentProvider, "MyFatoorah", StringComparison.OrdinalIgnoreCase))
            return new(RefundGatewayState.NeedsReview, Detail: "Unsupported payment provider; no refund was sent.");
        if (string.IsNullOrWhiteSpace(paymentId) || paymentId.Length > 150 || amount <= 0 || decimal.Round(amount, 3) != amount ||
            string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || string.IsNullOrWhiteSpace(operationReference) || operationReference.Length > 100 ||
            string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
            return new(RefundGatewayState.Rejected, Detail: "PaymentId, valid amount/currency, reason and operation reference are required.");

        var existing = await db.RefundOperations.AsNoTracking().FirstOrDefaultAsync(x => x.OperationReference == operationReference, cancellationToken);
        if (existing is not null)
        {
            if (existing.PaymentId != paymentId || existing.Amount != amount || !existing.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
                return new(RefundGatewayState.Rejected, Detail: "The operation reference belongs to a different refund request.");
            return await ReconcileAsync(existing.Id, cancellationToken);
        }
        var verified = await payments.GetPaymentStatusAsync(paymentId, "PaymentId", cancellationToken);
        if (verified is null || !verified.InvoiceStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase) || verified.PaymentId != paymentId ||
            !verified.CurrencyCode.Equals(currency, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(verified.InvoiceId))
            return new(RefundGatewayState.NeedsReview, Detail: "The original successful payment, invoice and currency could not be verified.");

        var sourceTransaction = transactions.HasActiveTransaction;
        var operation = await transactions.ExecuteAsync(async ct =>
        {
            await locks.LockRefundInvoiceAsync(verified.InvoiceId, ct);
            var duplicate = await db.RefundOperations.FirstOrDefaultAsync(x => x.OperationReference == operationReference, ct);
            if (duplicate is not null) return duplicate;
            var reserved = await db.RefundOperations.Where(x => x.PaymentProvider == "MyFatoorah" && x.InvoiceId == verified.InvoiceId && x.State != RefundOperationState.Rejected)
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            if (amount > verified.InvoiceValue - reserved) return null;
            var row = new RefundOperation { OperationReference = operationReference, PaymentId = paymentId, InvoiceId = verified.InvoiceId,
                Amount = amount, Currency = currency.ToUpperInvariant(), Reason = reason.Trim(), CreatedAtUtc = clock.UtcNow };
            db.RefundOperations.Add(row); await db.SaveChangesAsync(ct); return row;
        }, cancellationToken);
        if (operation is null) return new(RefundGatewayState.Rejected, Detail: "Refund exceeds the payment amount not already reserved or refunded.");
        if (operation.PaymentId != paymentId || operation.Amount != amount || !operation.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
            return new(RefundGatewayState.Rejected, Detail: "The operation reference was concurrently reserved by a different refund request.");
        if (sourceTransaction) return new(RefundGatewayState.Pending, Detail: "Refund queued. Submit only after the source transaction has committed.");
        return await ReconcileAsync(operation.Id, cancellationToken);
    }

    public async Task<RefundGatewayResult> ReconcileAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var operation = await db.RefundOperations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == operationId, cancellationToken);
        if (operation is null) return new(RefundGatewayState.Rejected, Detail: "Refund operation was not found.");
        if (operation.State is RefundOperationState.Confirmed or RefundOperationState.Rejected) return Outcome(operation);
        if (transactions.HasActiveTransaction) return new(RefundGatewayState.Pending, operation.ProviderRefundReference, "Refund reconciliation is deferred until the source transaction commits.");
        if (operation.State == RefundOperationState.Queued)
        {
            var claimed = await transactions.ExecuteAsync(async ct =>
            {
                await locks.LockRefundInvoiceAsync(operation.InvoiceId, ct);
                var row = await db.RefundOperations.FirstAsync(x => x.Id == operationId, ct);
                if (db is DbContext context) await context.Entry(row).ReloadAsync(ct);
                if (row.State != RefundOperationState.Queued) return false;
                row.State = RefundOperationState.Submitting; row.SubmittedAtUtc = clock.UtcNow; row.Version++;
                await db.SaveChangesAsync(ct); return true;
            }, cancellationToken);
            if (!claimed) return new(RefundGatewayState.Pending, Detail: "Another worker already claimed this refund.");
            // Submitting is committed before contacting the provider. Never automatically repeat a submission.
            try
            {
                var receipt = await payments.MakeRefundAsync(operation.PaymentId, operation.Amount, operation.Currency, operation.OperationReference, operation.Reason, cancellationToken);
                var row = await db.RefundOperations.FirstAsync(x => x.Id == operationId, cancellationToken);
                row.State = receipt is null ? RefundOperationState.NeedsReview : RefundOperationState.Pending;
                row.ProviderRefundId = receipt?.RefundId; row.ProviderRefundReference = receipt?.RefundReference;
                row.Detail = receipt is null ? "Provider did not supply a valid receipt. Reconcile before any retry." : "Provider accepted the request; funds are not yet confirmed returned.";
                row.Version++; await db.SaveChangesAsync(cancellationToken);
                return Outcome(row);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Refund submission outcome is uncertain ({ExceptionType}); reconciliation required. No automatic resend.", ex.GetType().Name);
                return new(RefundGatewayState.NeedsReview, Detail: "Submission outcome is uncertain. Reconcile the existing operation; do not resend.");
            }
        }
        var statuses = await payments.GetRefundStatusAsync(operation.ProviderRefundId ?? operation.InvoiceId,
            operation.ProviderRefundId is null ? "InvoiceId" : "RefundId", cancellationToken);
        var matches = statuses.Where(x => !string.IsNullOrWhiteSpace(x.RefundId) && !string.IsNullOrWhiteSpace(x.RefundReference) && x.ExternalIdentifier == operation.OperationReference && x.InvoiceId == operation.InvoiceId &&
            x.Amount == operation.Amount && x.BaseCurrency.Equals(operation.Currency, StringComparison.OrdinalIgnoreCase) &&
            (operation.ProviderRefundId is null || x.RefundId == operation.ProviderRefundId) &&
            (operation.ProviderRefundReference is null || x.RefundReference == operation.ProviderRefundReference)).ToList();
        var status = matches.Count == 1 ? matches[0] : null;
        if (status is null) return new(RefundGatewayState.NeedsReview, operation.ProviderRefundReference, "No exactly matching provider refund status was found. The request has not been resent.");
        var confirmed = status.Status.Equals("Refunded", StringComparison.OrdinalIgnoreCase) && status.RefundAmount == operation.Amount;
        var rejected = status.Status.Equals("Canceled", StringComparison.OrdinalIgnoreCase) || status.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase);
        await transactions.ExecuteAsync(async ct =>
        {
            await locks.LockRefundInvoiceAsync(operation.InvoiceId, ct);
            var row = await db.RefundOperations.FirstAsync(x => x.Id == operationId, ct);
            if (db is DbContext context) await context.Entry(row).ReloadAsync(ct);
            if (row.State == RefundOperationState.Confirmed) return;
            row.ProviderRefundId = status.RefundId; row.ProviderRefundReference = status.RefundReference;
            row.State = confirmed ? RefundOperationState.Confirmed : rejected ? RefundOperationState.Rejected : RefundOperationState.Pending;
            row.ReconciledAtUtc = clock.UtcNow; row.ConfirmedAtUtc = confirmed ? clock.UtcNow : null;
            row.Detail = confirmed ? "Provider confirmed funds returned." : rejected ? "Provider canceled or rejected the refund." : "Provider refund is still pending.";
            if (confirmed && row.LocalAppliedAtUtc is null && row.OperationReference.StartsWith("campaign-late-", StringComparison.Ordinal) &&
                Guid.TryParse(row.OperationReference["campaign-late-".Length..], out var transactionId))
            {
                var transaction = await db.CampaignPaymentTransactions.FirstOrDefaultAsync(x => x.Id == transactionId, ct);
                if (transaction is not null && transaction.Status == MealMate.Domain.CollaborativeCampaigns.Enums.PaymentTransactionStatus.RefundPending)
                {
                    var session = await db.CampaignCheckoutSessions.FirstOrDefaultAsync(x => x.Id == transaction.CheckoutSessionId, ct);
                    if (session?.Status == MealMate.Domain.CollaborativeCampaigns.Enums.CampaignCheckoutSessionStatus.RefundPending)
                        session.MarkRefunded();
                    transaction.Status = MealMate.Domain.CollaborativeCampaigns.Enums.PaymentTransactionStatus.Refunded;
                    transaction.RefundedAtUtc = clock.UtcNow;
                    row.LocalAppliedAtUtc = clock.UtcNow;
                }
            }
            row.Version++; await db.SaveChangesAsync(ct);
        }, cancellationToken);
        return Outcome(await db.RefundOperations.AsNoTracking().FirstAsync(x => x.Id == operationId, cancellationToken));
    }

    private static RefundGatewayResult Outcome(RefundOperation row) => new(row.State switch
    { RefundOperationState.Confirmed => RefundGatewayState.Confirmed, RefundOperationState.Rejected => RefundGatewayState.Rejected,
      RefundOperationState.NeedsReview or RefundOperationState.Submitting => RefundGatewayState.NeedsReview, _ => RefundGatewayState.Pending }, row.ProviderRefundReference, row.Detail);
}
