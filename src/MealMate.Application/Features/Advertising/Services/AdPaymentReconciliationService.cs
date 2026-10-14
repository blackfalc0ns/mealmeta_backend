using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Advertising;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MealMate.Application.Features.Advertising.Services;

public sealed record AdRefundReconciliationResult(int Attempted, int Refunded, int StillPending);
public sealed record AdPaymentReconciliationResult(int Attempted, int Finalized, int StillPending);

/// <summary>
/// Reconciles admin-approved stop-settlement refunds. Refund submission only occurs
/// after a recorded approval, and Refunded requires a matching provider status.
/// </summary>
public sealed class AdPaymentReconciliationService(
    IAppDbContext context,
    IPaymentGatewayService paymentGateway,
    IDateTimeProvider clock,
    ILogger<AdPaymentReconciliationService>? logger = null,
    IApplicationTransactionExecutor? transactions = null,
    IApplicationLockManager? locks = null)
{
    public async Task<AdPaymentReconciliationResult> ProcessPendingPaymentsAsync(CancellationToken ct = default)
    {
        // Recover the narrow crash window after Payzah saved its attempt and returned a
        // handle but before the application persisted its auction payment row.
        var orphanAttempts = await context.PaymentGatewayAttempts.AsNoTracking()
            .Where(x => x.Provider == "Payzah" && x.PaymentId != "" && x.CustomerReference.StartsWith("ad-auction:"))
            .OrderBy(x => x.CreatedAtUtc).Take(50).ToListAsync(ct);
        foreach (var attempt in orphanAttempts)
        {
            var parts = attempt.CustomerReference.Split(':');
            if (parts.Length != 3 || !Guid.TryParseExact(parts[1], "N", out var auctionId) ||
                !Guid.TryParseExact(parts[2], "N", out var restaurantId)) continue;
            var handle = "pz-" + attempt.TrackId;
            var existingAttempt = await context.AdPaymentTransactions.FirstOrDefaultAsync(x =>
                x.PaymentProvider == "Payzah" && x.ProviderTransactionId == handle, ct);
            if (existingAttempt is not null) continue;
            var auction = await context.AdAuctions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == auctionId, ct);
            if (auction is null || auction.WinningRestaurantId != restaurantId || auction.WinningAmount != attempt.ExpectedAmount ||
                !string.Equals(auction.CurrencyCode, attempt.ExpectedCurrency, StringComparison.OrdinalIgnoreCase)) continue;
            var reservation = await context.AdPaymentTransactions.FirstOrDefaultAsync(x =>
                x.AdAuctionId == auctionId && x.RestaurantId == restaurantId &&
                (x.Status == "Initializing" || x.Status == "InitializationNeedsManualReview"), ct);
            if (reservation is not null)
            {
                reservation.ProviderTransactionId = handle;
                reservation.Status = "Pending";
            }
            else
                context.AdPaymentTransactions.Add(new AdPaymentTransaction
                {
                    AdAuctionId = auctionId, RestaurantId = restaurantId, PaymentProvider = "Payzah",
                    ProviderTransactionId = handle, Amount = attempt.ExpectedAmount, CurrencyCode = attempt.ExpectedCurrency,
                    Status = "Pending", InitiatedAtUtc = attempt.CreatedAtUtc
                });
            await context.SaveChangesAsync(ct);
        }

        var candidates = await context.AdPaymentTransactions.AsNoTracking()
            .Where(x => x.Status == "Pending" && x.PaymentProvider == "Payzah")
            .OrderBy(x => x.InitiatedAtUtc).Select(x => new { x.Id, x.AdAuctionId }).Take(50).ToListAsync(ct);
        var finalized = 0;
        var stillPending = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            async Task<bool> Reconcile(CancellationToken token)
            {
                if (locks is not null) await locks.LockAdAuctionAsync(candidate.AdAuctionId, token);
                var payment = await context.AdPaymentTransactions.FirstOrDefaultAsync(x => x.Id == candidate.Id && x.Status == "Pending", token);
                if (payment is null) return false;
                var auction = await context.AdAuctions.Include(x => x.Bids)
                    .FirstOrDefaultAsync(x => x.Id == payment.AdAuctionId, token);
                if (auction is null) return false;
                var expectedReference = $"ad-auction:{auction.Id:N}:{payment.RestaurantId:N}";
                var status = await paymentGateway.GetPaymentStatusAsync(payment.ProviderTransactionId, cancellationToken: token);
                if (status is null) return false;

                if (string.Equals(status.InvoiceStatus, "Failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status.InvoiceStatus, "Expired", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status.InvoiceStatus, "Canceled", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status.InvoiceStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    payment.Status = "Failed";
                    payment.CompletedAtUtc = clock.UtcNow;
                    await context.SaveChangesAsync(token);
                    logger?.LogInformation("Ad payment {PaymentHandle} reached terminal failure status {InvoiceStatus}.", payment.ProviderTransactionId, status.InvoiceStatus);
                    return true;
                }

                if (!string.Equals(status.InvoiceStatus, "Paid", StringComparison.OrdinalIgnoreCase) ||
                    status.InvoiceValue != payment.Amount || !string.Equals(status.CurrencyCode, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase) ||
                    status.CustomerReference != expectedReference || status.PaymentId != payment.ProviderTransactionId) return false;

                if (auction.Status != AdAuctionStatus.AwaitingPayment || auction.WinningRestaurantId != payment.RestaurantId ||
                    auction.WinningAmount != payment.Amount || !string.Equals(auction.CurrencyCode, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                {
                    payment.Status = "PaidNeedsManualReview";
                    payment.CompletedAtUtc = clock.UtcNow;
                    if (!await context.AdFinancialLedgerEntries.AnyAsync(x => x.Source == "AdPayment" &&
                            x.Action == "Charge" && x.ProviderTransactionId == payment.ProviderTransactionId, token))
                        context.AdFinancialLedgerEntries.Add(new AdFinancialLedgerEntry
                        {
                            AdAuctionId = auction.Id, RestaurantId = payment.RestaurantId, Source = "AdPayment", Action = "Charge",
                            Amount = payment.Amount, CurrencyCode = payment.CurrencyCode, ProviderTransactionId = payment.ProviderTransactionId,
                            OccurredAtUtc = clock.UtcNow
                        });
                    await context.SaveChangesAsync(token);
                    logger?.LogWarning("Confirmed ad payment {PaymentHandle} no longer matches a payable auction; queued for manual financial review.", payment.ProviderTransactionId);
                    return true;
                }

                payment.Status = "Succeeded";
                payment.CompletedAtUtc = clock.UtcNow;
                auction.Status = AdAuctionStatus.Awarded;
                var placement = await context.AdPlacements.FirstOrDefaultAsync(x => x.AdAuctionId == auction.Id, token);
                if (placement is null)
                    context.AdPlacements.Add(new AdPlacement
                    {
                        AdAuctionId = auction.Id, RestaurantId = payment.RestaurantId, AdSpaceId = auction.AdSpaceId,
                        Status = AdPlacementStatus.AwaitingCreative, DisplayStartsAtUtc = auction.DisplayStartsAtUtc,
                        DisplayEndsAtUtc = auction.DisplayEndsAtUtc
                    });
                foreach (var bid in auction.Bids.Where(x => x.Id == auction.WinningBidId)) bid.IsWinning = true;
                if (!await context.AdFinancialLedgerEntries.AnyAsync(x => x.Source == "AdPayment" &&
                        x.Action == "Charge" && x.ProviderTransactionId == payment.ProviderTransactionId, token))
                    context.AdFinancialLedgerEntries.Add(new AdFinancialLedgerEntry
                    {
                        AdAuctionId = auction.Id, RestaurantId = payment.RestaurantId, Source = "AdPayment", Action = "Charge",
                        Amount = payment.Amount, CurrencyCode = payment.CurrencyCode, ProviderTransactionId = payment.ProviderTransactionId,
                        OccurredAtUtc = clock.UtcNow
                    });
                await context.SaveChangesAsync(token);
                return true;
            }

            bool didFinalize;
            try { didFinalize = transactions is null ? await Reconcile(ct) : await transactions.ExecuteAsync(Reconcile, ct); }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Ad payment attempt {PaymentId} could not be reconciled safely.", candidate.Id);
                didFinalize = false;
            }
            if (didFinalize) finalized++; else stillPending++;
        }
        return new AdPaymentReconciliationResult(candidates.Count, finalized, stillPending);
    }

    public async Task<AdRefundReconciliationResult> ProcessPendingRefundsAsync(CancellationToken ct = default)
    {
        var attempted = 0;
        var refunded = 0;
        var stillPending = 0;

        var pendingPayments = await context.AdPaymentTransactions
            .Where(t => t.Status == "RefundApproved" || t.Status == "RefundSubmitted" || t.Status == "RefundSubmissionStarted")
            .OrderBy(t => t.InitiatedAtUtc)
            .Take(50)
            .ToListAsync(ct);

        foreach (var payment in pendingPayments)
        {
            ct.ThrowIfCancellationRequested();

            var refundEntry = await context.AdFinancialLedgerEntries
                .Where(e => e.AdAuctionId == payment.AdAuctionId &&
                            e.Source == "AdStopSettlement" &&
                            e.Action == "RefundRequest" &&
                            e.RefundReviewStatus == "Approved")
                .OrderBy(e => e.OccurredAtUtc)
                .FirstOrDefaultAsync(ct);

            if (refundEntry is null)
            {
                // Ledger lost its refund intent; keep the payment pending for audit instead of guessing.
                stillPending++;
                continue;
            }

            attempted++;

            if (refundEntry.RefundApprovedAmount != refundEntry.Amount ||
                refundEntry.RefundApprovedPolicyId is null ||
                refundEntry.RefundApprovedPolicyVersion is null)
            {
                stillPending++;
                continue;
            }

            if (payment.Status == "RefundSubmissionStarted" && refundEntry.ProviderRefundId is null)
            {
                // A crash may have happened after the provider received the request but before
                // a receipt was saved. Never resend an ambiguous refund automatically.
                payment.Status = "RefundNeedsManualProviderReview";
                refundEntry.RefundReviewStatus = "ProviderReviewRequired";
                stillPending++;
                continue;
            }

            var externalRefundId = $"ad-refund-{refundEntry.Id:N}";
            try
            {
                if (refundEntry.ProviderRefundId is null)
                {
                    payment.Status = "RefundSubmissionStarted";
                    refundEntry.ExternalRefundIdentifier = externalRefundId;
                    await context.SaveChangesAsync(ct);
                    var receipt = await paymentGateway.MakeRefundAsync(
                        paymentId: payment.ProviderTransactionId,
                        amount: refundEntry.RefundApprovedAmount.Value,
                        currency: refundEntry.CurrencyCode,
                        externalIdentifier: externalRefundId,
                        reason: refundEntry.RefundReviewReason ?? "Advertising placement stop settlement",
                        ct);

                    if (receipt is null || string.IsNullOrWhiteSpace(receipt.RefundId) ||
                        receipt.ExternalIdentifier != externalRefundId || receipt.Amount != refundEntry.Amount)
                    {
                        // Acceptance/unknown response is not a final settlement and must never auto-resubmit.
                        payment.Status = "RefundNeedsManualProviderReview";
                        refundEntry.RefundReviewStatus = "ProviderReviewRequired";
                        stillPending++;
                        logger?.LogWarning(
                            "Ad refund submission for auction {AuctionId} requires manual provider reconciliation.",
                            payment.AdAuctionId);
                        continue;
                    }

                    refundEntry.ProviderRefundId = receipt.RefundId;
                    payment.Status = "RefundSubmitted";
                }

                var paymentAttempt = payment.ProviderTransactionId.StartsWith("pz-", StringComparison.Ordinal)
                    ? await context.PaymentGatewayAttempts.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.Provider == "Payzah" && x.TrackId == payment.ProviderTransactionId.Substring(3), ct)
                    : null;
                var providerStatuses = await paymentGateway.GetRefundStatusAsync(
                    refundEntry.ProviderRefundId, "RefundId", ct);
                var confirmed = providerStatuses.Any(s =>
                    s.RefundId == refundEntry.ProviderRefundId &&
                    s.ExternalIdentifier == externalRefundId &&
                    paymentAttempt?.ProviderReference is not null && s.InvoiceId == paymentAttempt.ProviderReference &&
                    s.RefundAmount == refundEntry.Amount &&
                    string.Equals(s.BaseCurrency, refundEntry.CurrencyCode, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s.Status, "Refunded", StringComparison.OrdinalIgnoreCase));
                if (confirmed)
                {
                    payment.Status = "Refunded";
                    payment.CompletedAtUtc = clock.UtcNow;
                    refunded++;
                }
                else
                {
                    stillPending++;
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another reconciler claimed this refund first. Do not call the provider
                // and do not overwrite the winning worker's persisted state.
                context.AdPaymentTransactions.Entry(payment).State = EntityState.Detached;
                context.AdFinancialLedgerEntries.Entry(refundEntry).State = EntityState.Detached;
                stillPending++;
                continue;
            }
            catch (Exception ex)
            {
                // Unknown provider outcomes must be resolved manually; an automatic retry could double refund.
                payment.Status = "RefundNeedsManualProviderReview";
                refundEntry.RefundReviewStatus = "ProviderReviewRequired";
                stillPending++;
                logger?.LogError(ex,
                    "Ad refund outcome for auction {AuctionId} requires manual provider reconciliation.",
                    payment.AdAuctionId);
            }
        }

        if (attempted > 0)
        {
            try
            {
                await context.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // The winning reconciler changed this payment after this worker's
                // initial read. Keep the durable winner state and never retry provider I/O.
                logger?.LogInformation(ex, "Ad refund reconciliation observed a concurrent claim.");
            }
            logger?.LogInformation(
                "Ad refund reconciliation: {Attempted} attempted, {Refunded} refunded, {Pending} still pending.",
                attempted, refunded, stillPending);
        }

        return new AdRefundReconciliationResult(attempted, refunded, stillPending);
    }
}
