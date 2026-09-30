using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.CollaborativeCampaigns.Enums;
using MealMate.Domain.CustomerFinance;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed class ProviderRefundReconciliationService(IAppDbContext db, IPaymentRefundGateway gateway,
    IApplicationTransactionExecutor transactions, IApplicationLockManager locks, IDateTimeProvider clock)
{
    public async Task<RefundGatewayResult> ReconcileAsync(Guid operationId, CancellationToken ct)
    {
        var result = await gateway.ReconcileAsync(operationId, ct);
        if (!result.IsConfirmed) return result;
        await transactions.ExecuteAsync(async token =>
        {
            var row = await db.RefundOperations.FirstOrDefaultAsync(x => x.Id == operationId, token);
            if (row is null) return;
            await locks.LockRefundInvoiceAsync(row.InvoiceId, token);
            if (db is DbContext context) await context.Entry(row).ReloadAsync(token);
            if (row.State != RefundOperationState.Confirmed || row.LocalAppliedAtUtc is not null) return;
            Guid? campaignId = null; Guid? sessionId = null; var reversal = false;
            const string late = "campaign-late-"; const string cancel = "campaign-cancel-";
            if (row.OperationReference.StartsWith(late) && Guid.TryParse(row.OperationReference[late.Length..], out var transactionId))
            {
                var transaction = await db.CampaignPaymentTransactions.FirstOrDefaultAsync(x => x.Id == transactionId, token);
                if (transaction is null || transaction.Amount != row.Amount || transaction.Currency != row.Currency ||
                    transaction.PaymentProvider != row.PaymentProvider ||
                    (transaction.ProviderPaymentReference != row.PaymentId && transaction.ProviderTransactionId != row.PaymentId)) return;
                if (transaction.Status is not (PaymentTransactionStatus.RefundPending or PaymentTransactionStatus.Refunded)) return;
                var session = await db.CampaignCheckoutSessions.FirstOrDefaultAsync(x => x.Id == transaction.CheckoutSessionId, token);
                if (session is null) return;
                transaction.Status = PaymentTransactionStatus.Refunded; transaction.RefundedAtUtc = clock.UtcNow;
                session.MarkRefunded(); campaignId = transaction.CampaignId; sessionId = transaction.CheckoutSessionId;
            }
            else if (row.OperationReference.StartsWith(cancel) && Guid.TryParse(row.OperationReference[cancel.Length..], out var subscriptionId))
            {
                var subscription = await db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, token);
                if (subscription?.CampaignId is not { } id || subscription.PaidAmount.Amount != row.Amount || subscription.PaidAmount.CurrencyCode != row.Currency) return;
                var source = await db.CampaignPaymentTransactions.FirstOrDefaultAsync(x => x.CampaignId == id &&
                    (x.ProviderPaymentReference == row.PaymentId || x.ProviderTransactionId == subscription.PaymentId), token);
                if (source is null || (source.ProviderTransactionId != subscription.PaymentId && source.ProviderPaymentReference != subscription.PaymentId)) return;
                source.Status = PaymentTransactionStatus.Refunded; source.RefundedAtUtc = clock.UtcNow;
                campaignId = id; sessionId = source.CheckoutSessionId; reversal = true;
            }
            if (campaignId is null) return;
            var exists = await db.CampaignFinancialLedgers.AnyAsync(x => x.JournalId == row.Id, token);
            if (!exists)
            {
                db.CampaignFinancialLedgers.Add(new CampaignFinancialLedger { JournalId = row.Id, CampaignId = campaignId.Value,
                    CheckoutSessionId = sessionId, EntryType = CampaignLedgerEntryType.CustomerRefund, Amount = row.Amount, Currency = row.Currency,
                    Description = "Provider-confirmed campaign refund", CreatedAtUtc = clock.UtcNow });
                if (reversal) db.CampaignFinancialLedgers.Add(new CampaignFinancialLedger { JournalId = row.Id, CampaignId = campaignId.Value,
                    CheckoutSessionId = sessionId, EntryType = CampaignLedgerEntryType.RestaurantSettlementReversal, Amount = row.Amount, Currency = row.Currency,
                    Description = "Settlement reversal following provider-confirmed campaign refund", CreatedAtUtc = clock.UtcNow });
            }
            row.LocalAppliedAtUtc = clock.UtcNow; row.Version++; await db.SaveChangesAsync(token);
        }, ct);
        return result;
    }
}
