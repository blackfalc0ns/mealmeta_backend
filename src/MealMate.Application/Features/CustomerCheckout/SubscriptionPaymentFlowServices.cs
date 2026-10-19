using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Referrals;
using MealMate.Application.Features.CustomerFinance;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record PaymentProcessingResult(
    bool IsAccepted,
    bool IsDuplicate,
    string? ErrorCode,
    SubscriptionStatus? SubscriptionStatus,
    string FulfillmentStatus);

public sealed record FulfillmentResult(bool IsSuccess, bool IsAlreadyComplete, string? ErrorCode = null);

public interface ISubscriptionPaymentProcessor
{
    Task<PaymentProcessingResult> VerifyAndProcessAsync(
        Guid subscriptionId,
        string paymentId,
        string? observedInvoiceId = null,
        decimal? observedAmount = null,
        string? observedCurrencyCode = null,
        string? observedCustomerReference = null,
        string? payloadHash = null,
        CancellationToken cancellationToken = default);

    Task<PaymentProcessingResult> VerifyAndReleaseFailedAsync(
        Guid subscriptionId,
        string paymentId,
        string? observedInvoiceId = null,
        decimal? observedAmount = null,
        string? observedCustomerReference = null,
        string? payloadHash = null,
        CancellationToken cancellationToken = default);
}

public interface ISubscriptionFulfillmentService
{
    Task<FulfillmentResult> TryFulfillAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
}

public sealed record CapacityExpiryResult(int HoldsExpired, int SubscriptionsExpired);

public interface ICapacityHoldExpiryService
{
    Task<CapacityExpiryResult> ExpireDueAsync(CancellationToken cancellationToken = default);
}

public sealed class SubscriptionPaymentProcessor : ISubscriptionPaymentProcessor
{
    private const string Gateway = "Payzah";
    private readonly IAppDbContext _db;
    private readonly IPaymentGatewayService _payments;
    private readonly ICheckoutTransactionExecutor _transactions;
    private readonly ICapacityConcurrencyLock _capacityLock;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly ISubscriptionFulfillmentService _fulfillment;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerPushNotifier _customerPush;
    private readonly ISubscriptionFlowCouponService _coupons;
    private readonly IReferralRewardService? _referralRewards;
    private readonly IWalletSpendingService _wallets;
    private readonly IApplicationLockManager? _locks;

    public SubscriptionPaymentProcessor(
        IAppDbContext db,
        IPaymentGatewayService payments,
        ICheckoutTransactionExecutor transactions,
        ICapacityConcurrencyLock capacityLock,
        ISubscriptionScheduleService schedule,
        ISubscriptionFulfillmentService fulfillment,
        IDateTimeProvider clock,
        ICustomerPushNotifier? customerPush = null,
        ISubscriptionFlowCouponService? couponService = null,
        IApplicationLockManager? locks = null,
        IReferralRewardService? referralRewards = null,
        IWalletSpendingService? wallets = null)
        => (_db, _payments, _transactions, _capacityLock, _schedule, _fulfillment, _clock, _customerPush, _coupons, _referralRewards, _wallets, _locks)
            = (db, payments, transactions, capacityLock, schedule, fulfillment, clock, customerPush ?? NullCustomerPushNotifier.Instance,
                couponService ?? new SubscriptionFlowCouponService(db, new OfferEligibilityEngine(), clock, locks),
                referralRewards ?? new CustomerReferralRewardsService(db, clock, locks),
                wallets ?? new WalletSpendingService(db, clock, locks), locks);

    public async Task<PaymentProcessingResult> VerifyAndProcessAsync(
        Guid subscriptionId,
        string paymentId,
        string? observedInvoiceId = null,
        decimal? observedAmount = null,
        string? observedCurrencyCode = null,
        string? observedCustomerReference = null,
        string? payloadHash = null,
        CancellationToken cancellationToken = default)
    {
        if (subscriptionId == Guid.Empty || string.IsNullOrWhiteSpace(paymentId))
            return new(false, false, "payment.invalid_reference", null, "NotStarted");

        var verified = await _payments.GetPaymentStatusAsync(paymentId.Trim(), "PaymentId", cancellationToken);
        if (verified is null)
            return new(false, false, "payment.gateway_verification_unavailable", null, "NotStarted");

        var resolvedId = await ResolveCheckoutSubscriptionIdAsync(subscriptionId, paymentId, cancellationToken);
        if (resolvedId is null)
            return new(false, false, "subscription.not_found", null, "NotStarted");
        subscriptionId = resolvedId.Value;

        IReadOnlyList<Guid> siblingIds = [subscriptionId];
        var transition = await _transactions.ExecuteAsync(async transactionCt =>
        {
            await LockFamilyBeforeSubscriptionAsync(subscriptionId, transactionCt);
            await _capacityLock.AcquirePaymentAsync(paymentId, transactionCt);
            await _capacityLock.AcquireSubscriptionAsync(subscriptionId, transactionCt);
            var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, transactionCt);
            if (subscription is null)
                return new PaymentProcessingResult(false, false, "subscription.not_found", null, "NotStarted");

            var previousReceipt = await _db.PaymentWebhookReceipts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Gateway == Gateway && x.PaymentId == paymentId, transactionCt);
            if (previousReceipt is not null)
            {
                if (previousReceipt.SubscriptionId != subscriptionId)
                    return new(false, false, "payment.reference_already_used", subscription.Status, "Rejected");
                // A receipt already held for financial/fulfillment review is terminal for
                // automatic processing. Its historical amount fields may be absent, and
                // a later provider status must never turn it into a successful payment.
                if (previousReceipt.Status is "FinancialReviewRequired" or "FulfillmentReviewRequired")
                    return new(true, true, previousReceipt.LastFulfillmentErrorCode ?? "payment.receipt_requires_review", subscription.Status, "ReviewRequired");
                if (!verified.InvoiceStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
                    && !verified.InvoiceStatus.Equals("Success", StringComparison.OrdinalIgnoreCase))
                    return new(false, false, "payment.not_paid", subscription.Status, "Rejected");
                if (!string.Equals(verified.PaymentId, paymentId, StringComparison.OrdinalIgnoreCase))
                    return new(false, false, "payment.payment_id_mismatch", subscription.Status, "Rejected");
                if (!string.Equals(verified.CustomerReference, previousReceipt.CustomerReference, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(observedCustomerReference) && observedCustomerReference != verified.CustomerReference))
                    return new(false, false, "payment.customer_reference_mismatch", subscription.Status, "Rejected");
                if (verified.InvoiceValue != previousReceipt.Amount || (observedAmount.HasValue && observedAmount != verified.InvoiceValue))
                    return new(false, false, "payment.amount_mismatch", subscription.Status, "Rejected");
                if (!string.Equals(verified.CurrencyCode, previousReceipt.CurrencyCode, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(observedCurrencyCode) && !observedCurrencyCode.Equals(verified.CurrencyCode, StringComparison.OrdinalIgnoreCase)))
                    return new(false, false, "payment.currency_mismatch", subscription.Status, "Rejected");
                if (previousReceipt.Status is not ("PaymentVerified" or "FulfillmentCompleted" or "FulfillmentRetryPending"))
                    return new(false, true, previousReceipt.LastFulfillmentErrorCode ?? "payment.receipt_requires_review", subscription.Status, "ReviewRequired");
                if (subscription.FamilyGroupId is Guid duplicateGroupId)
                {
                    var duplicateBatch = await FamilyBatchPaymentResolver.ResolveAsync(_db, duplicateGroupId, paymentId, transactionCt);
                    if (duplicateBatch is not null) siblingIds = duplicateBatch.SubscriptionIds;
                }
                return new(true, true, null, subscription.Status,
                    subscription.FulfillmentCompletedAtUtc.HasValue ? "Completed" : "Pending");
            }

            var pendingUpgrade = await _db.SubscriptionTierUpgrades
                .FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId
                    && x.Status == VerificationStatus.Submitted
                    && x.PaymentId == paymentId, transactionCt);
            if (pendingUpgrade is not null)
                return await ApplyPaidUpgradeAsync(subscription, pendingUpgrade, verified, paymentId, payloadHash, transactionCt);

            var resolvedBatch = subscription.FamilyGroupId is Guid scopedGroupId
                ? await FamilyBatchPaymentResolver.ResolveAsync(_db, scopedGroupId, paymentId, transactionCt) : null;
            var strictFamily = subscription.FamilyGroupId is Guid strictGroupId
                && await FamilyBatchPaymentResolver.GroupHasAnyBatchAsync(_db, strictGroupId, transactionCt);

            var validationError = await ValidatePaymentAsync(
                subscription, paymentId, verified, observedInvoiceId, observedAmount,
                observedCurrencyCode, observedCustomerReference, transactionCt);
            if (validationError is not null)
            {
                if (strictFamily && validationError is "payment.amount_mismatch" or "payment.currency_mismatch")
                    return await PutBatchInReviewAsync(subscription, resolvedBatch, paymentId, verified, payloadHash,
                        validationError == "payment.amount_mismatch" ? "family.batch_amount_mismatch" : "family.batch_currency_mismatch", transactionCt);
                _db.PaymentWebhookReceipts.Add(NewRejectedReceipt(subscriptionId, paymentId, validationError, payloadHash));
                await _db.SaveChangesAsync(transactionCt);
                return new(false, false, validationError, subscription.Status, "Rejected");
            }

            if (strictFamily && (resolvedBatch is null || resolvedBatch.BatchStatus is not
                    (FamilyCheckoutBatchStatus.PaymentPending or FamilyCheckoutBatchStatus.Paid)))
                return await PutBatchInReviewAsync(subscription, resolvedBatch, paymentId, verified, payloadHash,
                    resolvedBatch is null ? "family.batch_unresolved" : "family.batch_not_payable", transactionCt);
            if (resolvedBatch is not null && (!resolvedBatch.SubscriptionIds.Contains(subscription.Id)
                || resolvedBatch.SubscriptionIds.Count == 0))
                return await PutBatchInReviewAsync(subscription, resolvedBatch, paymentId, verified, payloadHash,
                    "family.batch_targets_invalid", transactionCt);

            // Converted family checkout may anchor on an already-paid manager subscription.
            // Still process any PendingPayment siblings for that family group.
            var familyPendingTargets = subscription.FamilyGroupId is Guid pendingFamilyGroupId
                ? await _db.Subscriptions
                    .Where(x => x.FamilyGroupId == pendingFamilyGroupId
                                && x.Status == SubscriptionStatus.PendingPayment
                                && (resolvedBatch == null || resolvedBatch.SubscriptionIds.Contains(x.Id)))
                    .ToListAsync(transactionCt)
                : null;

            if (familyPendingTargets is null || familyPendingTargets.Count == 0)
            {
                if (subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Scheduled)
                    return new(true, true, null, subscription.Status, "Completed");
                if (subscription.Status == SubscriptionStatus.Paid)
                    return new(true, true, null, subscription.Status, "Pending");
                if (subscription.Status != SubscriptionStatus.PendingPayment)
                    return new(false, false, "payment.subscription_state_invalid", subscription.Status, "Rejected");
            }

            var targets = familyPendingTargets is { Count: > 0 }
                ? familyPendingTargets
                : [subscription];
            targets = targets
                .OrderBy(x => x.Id)
                .ToList();
            siblingIds = targets.Select(x => x.Id).ToList();

            if (resolvedBatch?.Purpose == "Renewal")
            {
                var renewalError = await ValidateFamilyRenewalBatchAsync(resolvedBatch, targets, transactionCt);
                if (renewalError is not null)
                    return await PutBatchInReviewAsync(subscription, resolvedBatch, paymentId, verified, payloadHash,
                        renewalError, transactionCt);
            }

            // Acquire every target lock in a stable order, then validate the whole
            // financial unit before changing any sibling's payment state.
            foreach (var target in targets.OrderBy(x => x.Id))
                await _capacityLock.AcquireSubscriptionAsync(target.Id, transactionCt);
            foreach (var target in targets.OrderBy(x => x.Id))
            {
                var error = await ValidateSubscriptionReservationAsync(target, transactionCt);
                if (error is not null && resolvedBatch is not null)
                    return await PutBatchInReviewAsync(subscription, resolvedBatch, paymentId, verified, payloadHash, error, transactionCt);
            }
            targets = targets.OrderByDescending(x => x.Id == subscription.Id).ThenBy(x => x.Id).ToList();

            // Family invoice = one financial unit: at most one webhook receipt per paymentId.
            var receiptWritten = false;
            foreach (var target in targets)
            {
                await _capacityLock.AcquireSubscriptionAsync(target.Id, transactionCt);
                var markError = await MarkSubscriptionPaidAsync(
                    target, paymentId, verified, payloadHash, writeReceipt: !receiptWritten, transactionCt);
                if (markError is not null)
                {
                    if (resolvedBatch is not null)
                        return await PutBatchInReviewAsync(subscription, resolvedBatch, paymentId, verified, payloadHash, markError, transactionCt);
                    return await PutInReviewAsync(
                        target, paymentId, verified, payloadHash, markError,
                        writeReceipt: !receiptWritten, transactionCt);
                }

                receiptWritten = true;
            }

            if (resolvedBatch is not null)
            {
                var batch = await _db.FamilyCheckoutBatches.SingleAsync(x => x.Id == resolvedBatch.BatchId, transactionCt);
                batch.Status = FamilyCheckoutBatchStatus.Paid; batch.PaidAtUtc = _clock.UtcNow;
                batch.GatewayInvoiceId = verified.InvoiceId; batch.Version++;
                var attempt = await _db.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.BatchId == batch.Id
                    && x.GatewayPaymentId == paymentId, transactionCt);
                attempt.VerifiedAtUtc = _clock.UtcNow; attempt.GatewayInvoiceId = verified.InvoiceId;
                attempt.ResultCode = "Paid"; attempt.Version++;
                if (batch.Purpose == "Renewal" && batch.RenewalRequestId is Guid renewalRequestId)
                {
                    var line = await _db.FamilyCheckoutBatchLines.SingleAsync(x => x.BatchId == batch.Id
                        && x.CheckoutSelected && x.RenewalRequestId == renewalRequestId, transactionCt);
                    var renewal = await _db.SubscriptionRenewals.SingleAsync(x => x.Id == renewalRequestId
                        && x.BatchId == batch.Id && x.PreviousSubscriptionId == line.PreviousSubscriptionId, transactionCt);
                    renewal.NewSubscriptionId = line.SubscriptionId;
                    renewal.Status = "Completed";
                    renewal.CompletedAtUtc = _clock.UtcNow;
                    renewal.BatchVersion = batch.Version;
                }
            }
            if (subscription.FamilyGroupId is Guid paidFamilyGroupId)
            {
                var group = await _db.FamilyGroups
                    .Include(x => x.Members)
                    .FirstAsync(x => x.Id == paidFamilyGroupId, transactionCt);
                foreach (var member in group.Members)
                {
                    var memberSub = targets.FirstOrDefault(x => x.FamilyMemberId == member.Id);
                    if (memberSub is not null)
                        member.MemberSubscriptionId = memberSub.Id;
                }

                var discountAmount = resolvedBatch is null ? targets.Sum(x => x.PaidAmount.Amount) - group.QuotedFinalPrice
                    : await _db.FamilyCheckoutBatches.Where(x => x.Id == resolvedBatch.BatchId).Select(x => x.FamilyDiscountAmount).SingleAsync(transactionCt);
                if (discountAmount > 0m)
                {
                    var anchorId = group.PaymentAnchorSubscriptionId ?? subscription.Id;
                    _db.DiscountsAndReferralLogs.Add(new DiscountsAndReferralLog
                    {
                        SubscriptionId = anchorId,
                        DiscountType = "FamilyDiscount",
                        Amount = new MealMate.Domain.ValueObjects.Money(discountAmount, group.CurrencyCode),
                        FundingMode = CampaignFundingMode.MealMateFull,
                        AppliedAtUtc = _clock.UtcNow,
                        MetadataJson = $"{{\"familyGroupId\":\"{group.Id}\"}}",
                    });
                }

                group.Status = FamilyGroupStatus.Active;
                group.PaidAtUtc ??= _clock.UtcNow;
                if (resolvedBatch is null || group.PaymentAnchorSubscriptionId == subscription.Id)
                {
                    group.PaymentId = paymentId; group.PaymentInvoiceId = verified.InvoiceId;
                }
            }

            await _db.SaveChangesAsync(transactionCt);
            return new(true, false, null, SubscriptionStatus.Paid, "Pending");
        }, cancellationToken);

        if (!transition.IsAccepted) return transition;

        if (!transition.IsDuplicate && transition.FulfillmentStatus == "Pending")
        {
            foreach (var siblingId in siblingIds)
            {
                var customerUserId = await _db.Subscriptions.AsNoTracking()
                    .Where(x => x.Id == siblingId)
                    .Select(x => x.CustomerUserId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (customerUserId != Guid.Empty)
                    await _customerPush.NotifySubscriptionPaymentConfirmedAsync(customerUserId, siblingId, cancellationToken);
            }
        }

        // Upgrade payments and already-active shortcuts finish inside the transaction.
        if (transition.FulfillmentStatus is "Completed" or "ReviewRequired" or "Released")
            return transition;

        string? fulfillmentError = null;
        var allFulfilled = true;
        foreach (var siblingId in siblingIds)
        {
            var fulfillment = await _fulfillment.TryFulfillAsync(siblingId, cancellationToken);
            if (!fulfillment.IsSuccess)
            {
                allFulfilled = false;
                fulfillmentError ??= fulfillment.ErrorCode;
            }
        }

        var currentStatus = await _db.Subscriptions.AsNoTracking()
            .Where(x => x.Id == subscriptionId)
            .Select(x => (SubscriptionStatus?)x.Status)
            .FirstOrDefaultAsync(cancellationToken);
        return new(true, transition.IsDuplicate, fulfillmentError, currentStatus,
            allFulfilled ? "Completed" : "PendingRetry");
    }

    public async Task<PaymentProcessingResult> VerifyAndReleaseFailedAsync(
        Guid subscriptionId,
        string paymentId,
        string? observedInvoiceId = null,
        decimal? observedAmount = null,
        string? observedCustomerReference = null,
        string? payloadHash = null,
        CancellationToken cancellationToken = default)
    {
        var verified = await _payments.GetPaymentStatusAsync(paymentId, "PaymentId", cancellationToken);
        if (verified is null)
            return new(false, false, "payment.gateway_verification_unavailable", null, "NotStarted");
        if (!verified.InvoiceStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase)
            && !verified.InvoiceStatus.Equals("Expired", StringComparison.OrdinalIgnoreCase)
            && !verified.InvoiceStatus.Equals("Canceled", StringComparison.OrdinalIgnoreCase)
            && !verified.InvoiceStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
            return new(false, false, "payment.gateway_status_conflict", null, "NotStarted");
        if (!string.IsNullOrWhiteSpace(observedInvoiceId) && observedInvoiceId != verified.InvoiceId)
            return new(false, false, "payment.invoice_id_mismatch", null, "NotStarted");
        if (observedAmount.HasValue && observedAmount.Value != verified.InvoiceValue)
            return new(false, false, "payment.webhook_amount_mismatch", null, "NotStarted");

        var resolvedId = await ResolveCheckoutSubscriptionIdAsync(subscriptionId, paymentId, cancellationToken);
        if (resolvedId is null)
            return new(false, false, "subscription.not_found", null, "NotStarted");
        subscriptionId = resolvedId.Value;

        return await _transactions.ExecuteAsync<PaymentProcessingResult>(async ct =>
        {
            await LockFamilyBeforeSubscriptionAsync(subscriptionId, ct);
            await _capacityLock.AcquirePaymentAsync(paymentId, ct);
            await _capacityLock.AcquireSubscriptionAsync(subscriptionId, ct);
            var existing = await _db.PaymentWebhookReceipts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Gateway == Gateway && x.PaymentId == paymentId, ct);
            if (existing is not null)
                return existing.SubscriptionId == subscriptionId && existing.Status == "PaymentFailureVerified"
                    ? new(true, true, null, null, "Released")
                    : new(false, true, "payment.receipt_requires_review", null, "ReviewRequired");
            var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
            if (subscription is null) return new(false, false, "subscription.not_found", null, "NotStarted");

            if (!Guid.TryParse(verified.CustomerReference, out var verifiedReference)
                || (!string.IsNullOrWhiteSpace(observedCustomerReference)
                    && observedCustomerReference != verified.CustomerReference))
                return new(false, false, "payment.customer_reference_mismatch", null, "NotStarted");
            var failedBatch = subscription.FamilyGroupId is Guid failedGroupId
                ? await FamilyBatchPaymentResolver.ResolveAsync(_db, failedGroupId, paymentId, ct) : null;
            if (subscription.FamilyGroupId is Guid batchGroupId
                && await FamilyBatchPaymentResolver.GroupHasAnyBatchAsync(_db, batchGroupId, ct)
                && (failedBatch is null || failedBatch.BatchStatus is not (FamilyCheckoutBatchStatus.PaymentPending or FamilyCheckoutBatchStatus.Expired or FamilyCheckoutBatchStatus.Failed)))
                return new(false, false, "payment.stale_attempt_review_required", subscription.Status, "ReviewRequired");
            if (!string.Equals(verified.PaymentId, paymentId, StringComparison.OrdinalIgnoreCase))
                return new(false, false, "payment.payment_id_mismatch", subscription.Status, "Rejected");
            var failedUpgrade = await _db.SubscriptionTierUpgrades.AsNoTracking().FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId
                && x.Status == VerificationStatus.Submitted && x.PaymentId == paymentId, ct);
            var expectedAmount = failedUpgrade?.QuotedTotalDue.Amount ?? failedBatch?.FinalPrice ?? subscription.PaidAmount.Amount;
            var expectedCurrency = failedUpgrade?.QuotedTotalDue.CurrencyCode ?? failedBatch?.CurrencyCode ?? subscription.PaidAmount.CurrencyCode;
            if (verified.InvoiceValue != expectedAmount || !string.Equals(verified.CurrencyCode, expectedCurrency, StringComparison.OrdinalIgnoreCase))
                return new(false, false, "payment.amount_mismatch", subscription.Status, "Rejected");
            if (!await IsCurrentAttemptAsync(subscription, paymentId, ct))
                return new(false, false, "payment.stale_attempt_review_required", subscription.Status, "ReviewRequired");
            var isUpgrade = await _db.SubscriptionTierUpgrades.AnyAsync(x => x.SubscriptionId == subscriptionId && x.Status == VerificationStatus.Submitted && x.PaymentId == paymentId, ct);
            var expectedReference = isUpgrade ? subscription.Id : subscription.FamilyGroupId ?? subscription.Id;
            if (verifiedReference != expectedReference)
                return new(false, false, "payment.customer_reference_mismatch", null, "NotStarted");

            var pendingUpgrade = await _db.SubscriptionTierUpgrades
                .FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId
                    && x.Status == VerificationStatus.Submitted
                    && x.PaymentId == paymentId, ct);
            if (pendingUpgrade is not null)
            {
                pendingUpgrade.PaymentId ??= paymentId;
                pendingUpgrade.Status = VerificationStatus.Rejected;
                _db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
                {
                    Gateway = Gateway,
                    EventKey = paymentId,
                    SubscriptionId = subscriptionId,
                    Status = "PaymentFailureVerified",
                    PayloadHash = payloadHash,
                    PaymentId = paymentId,
                    InvoiceId = verified.InvoiceId,
                    Amount = verified.InvoiceValue,
                    CurrencyCode = verified.CurrencyCode,
                    CustomerReference = verified.CustomerReference,
                    VerifiedAtUtc = _clock.UtcNow,
                    ProcessedAtUtc = _clock.UtcNow
                });
                await _db.SaveChangesAsync(ct);
                return new(true, false, null, subscription.Status, "Released");
            }

            var targets = subscription.FamilyGroupId is Guid familyGroupId
                ? await _db.Subscriptions.Where(x => x.FamilyGroupId == familyGroupId
                    && (failedBatch == null || failedBatch.SubscriptionIds.Contains(x.Id))).OrderBy(x => x.Id).ToListAsync(ct)
                : [subscription];

            foreach (var target in targets)
            {
                await _capacityLock.AcquireSubscriptionAsync(target.Id, ct);
                var holds = await _db.CapacityHolds.Where(x => x.SubscriptionId == target.Id).ToListAsync(ct);
                if (holds.Count > 0)
                    await _capacityLock.AcquireAsync(holds[0].RestaurantId, holds.Select(x => x.ServiceDate).ToList(), ct);
                foreach (var hold in holds.Where(x => x.Status == CapacityHoldStatus.PendingPayment))
                {
                    hold.Status = CapacityHoldStatus.Released;
                    hold.ReleasedAtUtc = _clock.UtcNow;
                }

                // A verified payment failure also releases the coupon hold so the customer's
                // per-offer usage limit is not consumed by an abandoned checkout. Only the
                // pending→failed transition loses the hold; paid siblings keep their final use.
                if (target.Status != SubscriptionStatus.PendingPayment)
                    continue;
                await _coupons.ReleaseCheckoutRedemptionAsync(target.Id, ct);
                await _wallets.ReleaseCheckoutHoldAsync(target.Id, target.CustomerUserId, ct);

                var oldStatus = target.Status;
                target.Status = SubscriptionStatus.PaymentFailed;
                target.LastOperationAtUtc = _clock.UtcNow;
                _db.SubscriptionOperations.Add(new SubscriptionOperation
                {
                    SubscriptionId = target.Id,
                    OperationType = "PAYMENT_FAILED",
                    PreviousStatus = oldStatus,
                    NewStatus = target.Status,
                    Reason = $"Payzah status verified as {verified.InvoiceStatus}.",
                    PerformedByUserId = target.CustomerUserId,
                    PerformedByUserType = UserType.Customer,
                    TimestampUtc = _clock.UtcNow
                });
            }

            if (failedBatch is not null)
            {
                var batch = await _db.FamilyCheckoutBatches.SingleAsync(x => x.Id == failedBatch.BatchId, ct);
                batch.Status = FamilyCheckoutBatchStatus.Failed; batch.Version++;
                var attempt = await _db.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.BatchId == batch.Id && x.GatewayPaymentId == paymentId, ct);
                attempt.ResultCode = "Failed"; attempt.VerifiedAtUtc = _clock.UtcNow;
                attempt.GatewayInvoiceId = verified.InvoiceId; attempt.Version++;
            }
            if (subscription.FamilyGroupId is Guid failedFamilyGroupId)
            {
                var group = await _db.FamilyGroups.FirstAsync(x => x.Id == failedFamilyGroupId, ct);
                if (failedBatch is null || group.PaymentAnchorSubscriptionId == subscription.Id)
                {
                group.Status = group.PaidAtUtc.HasValue
                    ? FamilyGroupStatus.Active
                    : FamilyGroupStatus.Draft;
                group.PaymentAnchorSubscriptionId = group.PaidAtUtc.HasValue
                    ? group.PaymentAnchorSubscriptionId
                    : null;
                group.PaymentId = group.PaidAtUtc.HasValue ? group.PaymentId : null;
                group.PaymentInvoiceId = group.PaidAtUtc.HasValue ? group.PaymentInvoiceId : null;
                }
            }

            _db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
            {
                Gateway = Gateway,
                EventKey = paymentId,
                SubscriptionId = subscriptionId,
                Status = "PaymentFailureVerified",
                PayloadHash = payloadHash,
                PaymentId = paymentId,
                InvoiceId = verified.InvoiceId,
                Amount = verified.InvoiceValue,
                CurrencyCode = verified.CurrencyCode,
                CustomerReference = verified.CustomerReference,
                VerifiedAtUtc = _clock.UtcNow,
                ProcessedAtUtc = _clock.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return new(true, false, null, subscription.Status, "Released");
        }, cancellationToken);
    }

    private async Task<Guid?> ResolveCheckoutSubscriptionIdAsync(Guid id, string paymentId, CancellationToken cancellationToken)
    {
        var requested = await _db.Subscriptions.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.FamilyGroupId }).FirstOrDefaultAsync(cancellationToken);
        var familyId = requested?.FamilyGroupId ?? id;
        var batch = await FamilyBatchPaymentResolver.ResolveAsync(_db, familyId, paymentId, cancellationToken);
        if (batch is not null)
            return await _db.FamilyCheckoutBatches.AsNoTracking().Where(x => x.Id == batch.BatchId)
                .Select(x => x.PaymentAnchorSubscriptionId).SingleAsync(cancellationToken);
        if (requested is not null) return requested.Id;
        return await _db.FamilyGroups.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.PaymentAnchorSubscriptionId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<PaymentProcessingResult> ApplyPaidUpgradeAsync(
        Subscription subscription,
        SubscriptionTierUpgrade upgrade,
        PaymentGatewayStatusResponse verified,
        string paymentId,
        string? payloadHash,
        CancellationToken ct)
    {
        if (!verified.InvoiceStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
            && !verified.InvoiceStatus.Equals("Success", StringComparison.OrdinalIgnoreCase))
            return new(false, false, "payment.not_paid", subscription.Status, "Rejected");

        if (!Guid.TryParse(verified.CustomerReference, out var reference) || reference != subscription.Id)
            return new(false, false, "payment.customer_reference_mismatch", subscription.Status, "Rejected");

        var expected = upgrade.QuotedTotalDue.Amount;
        if (Math.Abs(verified.InvoiceValue - expected) > 0.001m)
        {
            _db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
            {
                Gateway = Gateway,
                EventKey = paymentId,
                SubscriptionId = subscription.Id,
                Status = "VerificationRejected",
                PayloadHash = payloadHash,
                PaymentId = paymentId,
                InvoiceId = verified.InvoiceId,
                Amount = verified.InvoiceValue,
                CurrencyCode = verified.CurrencyCode,
                CustomerReference = verified.CustomerReference,
                VerifiedAtUtc = _clock.UtcNow,
                ProcessedAtUtc = _clock.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return new(false, false, "payment.amount_mismatch", subscription.Status, "Rejected");
        }

        if (!verified.CurrencyCode.Equals(upgrade.QuotedTotalDue.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            return new(false, false, "payment.currency_mismatch", subscription.Status, "Rejected");

        upgrade.VerifiedPaidAmount = verified.InvoiceValue;
        upgrade.VerifiedCurrencyCode = verified.CurrencyCode;
        upgrade.VerifiedInvoiceId = verified.InvoiceId;
        upgrade.VerifiedPaidAtUtc = _clock.UtcNow;
        if (subscription.FamilyGroupId.HasValue)
        {
            var familyReviewCode = await ValidateFamilyUpgradeEntitlementAsync(subscription, upgrade, ct);
            if (familyReviewCode is not null)
            {
                _db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt { Gateway = Gateway, EventKey = paymentId,
                    SubscriptionId = subscription.Id, Status = "FinancialReviewRequired", PaymentId = paymentId,
                    InvoiceId = verified.InvoiceId, Amount = verified.InvoiceValue, CurrencyCode = verified.CurrencyCode,
                    CustomerReference = verified.CustomerReference, VerifiedAtUtc = _clock.UtcNow, ProcessedAtUtc = _clock.UtcNow,
                    LastFulfillmentErrorCode = familyReviewCode });
                await _db.SaveChangesAsync(ct);
                return new(false, false, familyReviewCode, subscription.Status, "ReviewRequired");
            }
        }
        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
        {
            _db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt { Gateway = Gateway, EventKey = paymentId,
                SubscriptionId = subscription.Id, Status = "FinancialReviewRequired", PaymentId = paymentId,
                InvoiceId = verified.InvoiceId, Amount = verified.InvoiceValue, CurrencyCode = verified.CurrencyCode,
                CustomerReference = verified.CustomerReference, VerifiedAtUtc = _clock.UtcNow, ProcessedAtUtc = _clock.UtcNow,
                LastFulfillmentErrorCode = "upgrade.service_terminated_review_required" });
            await _db.SaveChangesAsync(ct);
            return new(false, false, "upgrade.service_terminated_review_required", subscription.Status, "ReviewRequired");
        }
        var oldTier = subscription.SelectedTier;
        upgrade.PaymentId = paymentId;
        upgrade.Status = VerificationStatus.Approved;
        upgrade.EffectiveAtUtc = _clock.UtcNow;
        subscription.SelectedTier = upgrade.ToTier;
        subscription.LastOperationAtUtc = _clock.UtcNow;

        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = subscription.Id,
            OperationType = "UPGRADE_PAID",
            PreviousStatus = subscription.Status,
            NewStatus = subscription.Status,
            Reason = $"Tier upgraded from {oldTier} to {upgrade.ToTier} after payment {paymentId}",
            PerformedByUserId = subscription.CustomerUserId,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow
        });

        _db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
        {
            Gateway = Gateway,
            EventKey = paymentId,
            SubscriptionId = subscription.Id,
            Status = "PaymentVerified",
            PayloadHash = payloadHash,
            PaymentId = paymentId,
            InvoiceId = verified.InvoiceId,
            Amount = verified.InvoiceValue,
            CurrencyCode = verified.CurrencyCode,
            CustomerReference = verified.CustomerReference,
            VerifiedAtUtc = _clock.UtcNow,
            ProcessedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return new(true, false, null, subscription.Status, "Completed");
    }

    private async Task<string?> ValidateFamilyUpgradeEntitlementAsync(
        Subscription subscription, SubscriptionTierUpgrade upgrade, CancellationToken ct)
    {
        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled)
            || upgrade.FromTier != subscription.SelectedTier || subscription.FamilyMemberId is not Guid memberId)
            return "upgrade.service_changed_review_required";
        var member = await _db.FamilyMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (member is null || member.IsDeleted
            || await _db.FamilyMemberAccesses.AsNoTracking().AnyAsync(x => x.FamilyMemberId == memberId && x.RemovedAtUtc != null, ct))
            return "upgrade.service_changed_review_required";

        List<DateOnly>? captured;
        try { captured = JsonSerializer.Deserialize<List<DateOnly>>(upgrade.EffectiveServiceDatesJson ?? "null"); }
        catch (JsonException) { return "family.financial_review_required"; }
        if (captured is null || captured.Count == 0 || captured.Count != upgrade.QuotedRemainingDays
            || captured.Distinct().Count() != captured.Count)
            return "family.financial_review_required";
        var current = await SubscriptionTierUpgradeQuoteBuilder.ResolveFamilyUpgradeFundedDatesAsync(_db, subscription,
            _clock.TodayUtc, ct);
        return captured.OrderBy(x => x).SequenceEqual(current) ? null : "upgrade.service_changed_review_required";
    }

    private async Task<string?> ValidateFamilyRenewalBatchAsync(FamilyBatchPaymentResolution batch,
        IReadOnlyList<Subscription> targets, CancellationToken ct)
    {
        if (batch.RenewalRequestId is not Guid renewalId)
            return "family.renewal_intent_changed";
        var lines = await _db.FamilyCheckoutBatchLines.AsNoTracking().Where(x => x.BatchId == batch.BatchId
            && x.CheckoutSelected).ToListAsync(ct);
        if (lines.Count != 1 || lines[0].RenewalRequestId != renewalId
            || lines[0].PreviousSubscriptionId is not Guid previousId || lines[0].SubscriptionId is not Guid nextId
            || targets.Count != 1 || targets[0].Id != nextId)
            return "family.renewal_intent_changed";
        var batchGroupId = await _db.FamilyCheckoutBatches.AsNoTracking().Where(x => x.Id == batch.BatchId)
            .Select(x => x.FamilyGroupId).SingleOrDefaultAsync(ct);
        var renewal = await _db.SubscriptionRenewals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == renewalId
            && x.BatchId == batch.BatchId && x.PreviousSubscriptionId == previousId
            && x.FamilyGroupId == batchGroupId && x.FamilyMemberId == lines[0].FamilyMemberId, ct);
        var previous = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == previousId, ct);
        var next = targets[0];
        if (renewal is null || renewal.Status != "PaymentPending" || renewal.NewSubscriptionId.HasValue
            || previous is null || previous.FamilyGroupId != next.FamilyGroupId
            || previous.FamilyMemberId != next.FamilyMemberId || next.FamilyGroupId != batchGroupId
            || next.FamilyMemberId != lines[0].FamilyMemberId || next.Status != SubscriptionStatus.PendingPayment
            || next.StartDate <= previous.EndDate || renewal.RequestedStartDate != next.StartDate
            || renewal.RequestedAddressId != lines[0].DeliveryAddressId
            || next.ProgramId != previous.ProgramId || next.BundleId != previous.BundleId
            || next.SelectedTier != previous.SelectedTier || next.DurationDays != previous.DurationDays
            || next.IncludeFriday != previous.IncludeFriday)
            return "family.renewal_intent_changed";
        var member = await _db.FamilyMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == next.FamilyMemberId
            && x.FamilyGroupId == next.FamilyGroupId && !x.IsDeleted, ct);
        if (member is null || member.MemberSubscriptionId != previous.Id)
            return "family.renewal_pointer_changed";
        var renewablePreviousStates = new[] { SubscriptionStatus.Paid, SubscriptionStatus.Active,
            SubscriptionStatus.Scheduled, SubscriptionStatus.Frozen, SubscriptionStatus.SuspendedForReview,
            SubscriptionStatus.Completed, SubscriptionStatus.Expired };
        var previousHasPaidProof = previous.PaymentVerifiedAtUtc.HasValue
            || await _db.FamilyCheckoutBatchLines.AsNoTracking().AnyAsync(x => x.SubscriptionId == previous.Id
                && x.CheckoutSelected && _db.FamilyCheckoutBatches.Any(b => b.Id == x.BatchId
                    && b.Status == FamilyCheckoutBatchStatus.Paid), ct);
        if (!renewablePreviousStates.Contains(previous.Status) || !previousHasPaidProof)
            return "family.renewal_intent_changed";
        var otherLiveStatuses = new[] { SubscriptionStatus.Paid, SubscriptionStatus.Active, SubscriptionStatus.Scheduled,
            SubscriptionStatus.Frozen, SubscriptionStatus.SuspendedForReview, SubscriptionStatus.CancellationPending };
        if (await _db.Subscriptions.AsNoTracking().AnyAsync(x => x.FamilyGroupId == next.FamilyGroupId
            && x.FamilyMemberId == next.FamilyMemberId && x.Id != previous.Id && x.Id != next.Id
            && otherLiveStatuses.Contains(x.Status) && x.StartDate <= next.EndDate && x.EndDate >= next.StartDate, ct))
            return "family.renewal_dates_overlap";
        return null;
    }

    private async Task<string?> MarkSubscriptionPaidAsync(
        Subscription subscription,
        string paymentId,
        PaymentGatewayStatusResponse verified,
        string? payloadHash,
        bool writeReceipt,
        CancellationToken transactionCt)
    {
        var expectedDates = _schedule.GetServiceDates(subscription.StartDate, subscription.DurationDays, subscription.IncludeFriday);
        var poolRestaurant = await _db.SubscriptionRestaurantSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubscriptionId == subscription.Id && x.IsInitiallySelected, transactionCt);
        if (poolRestaurant is null)
            return "payment.restaurant_pool_missing";

        await _capacityLock.AcquireAsync(poolRestaurant.RestaurantId, expectedDates, transactionCt);
        var holds = await _db.CapacityHolds
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(transactionCt);
        var poolDays = await _db.SubscriptionRestaurantDaySnapshots.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.RestaurantId == poolRestaurant.RestaurantId)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(transactionCt);

        var holdError = ValidatePendingHolds(expectedDates, poolRestaurant.RestaurantId, holds, poolDays, _clock.UtcNow);
        if (holdError is not null)
            return holdError;

        var oldStatus = subscription.Status;
        subscription.Status = SubscriptionStatus.Paid;
        await _coupons.CommitCheckoutRedemptionAsync(subscription.Id, transactionCt);
        // The wallet hold becomes a real balance reduction only when the payment is verified;
        // the conversion is idempotent (a Debit row is never converted twice).
        await _wallets.ConvertHoldToDebitAsync(subscription.Id, subscription.CustomerUserId, transactionCt);
        // The referrer's reward is credited when the referred customer's subscription is paid;
        // attribution rows are the source of truth and the call is idempotent (retries included).
        await _referralRewards!.CompleteForPaidSubscriptionAsync(subscription, transactionCt);
        // Family siblings share one Payzah payment. Subscriptions.PaymentId is unique,
        // so only the receipt-owning row (the payment anchor) stores the gateway id.
        if (writeReceipt)
            subscription.PaymentId = paymentId;
        subscription.PaymentInvoiceId = verified.InvoiceId;
        subscription.PaymentVerifiedAtUtc = _clock.UtcNow;
        subscription.LastOperationAtUtc = _clock.UtcNow;
        foreach (var hold in holds)
        {
            hold.Status = CapacityHoldStatus.PaymentConfirmed;
            hold.ConfirmedAtUtc = _clock.UtcNow;
        }

        if (writeReceipt)
        {
            _db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
            {
                Gateway = Gateway,
                EventKey = paymentId,
                SubscriptionId = subscription.Id,
                Status = "PaymentVerified",
                PayloadHash = payloadHash,
                PaymentId = paymentId,
                InvoiceId = verified.InvoiceId,
                Amount = verified.InvoiceValue,
                CurrencyCode = verified.CurrencyCode.ToUpperInvariant(),
                CustomerReference = verified.CustomerReference,
                VerifiedAtUtc = _clock.UtcNow,
                ProcessedAtUtc = _clock.UtcNow
            });
        }

        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = subscription.Id,
            OperationType = "PAYMENT_VERIFIED",
            PreviousStatus = oldStatus,
            NewStatus = SubscriptionStatus.Paid,
            Reason = $"Payzah payment {paymentId} verified; fulfillment is pending.",
            PerformedByUserId = subscription.CustomerUserId,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow
        });
        return null;
    }

    private async Task<string?> ValidatePaymentAsync(
        Subscription subscription,
        string paymentId,
        PaymentGatewayStatusResponse payment,
        string? observedInvoiceId,
        decimal? observedAmount,
        string? observedCurrencyCode,
        string? observedCustomerReference,
        CancellationToken cancellationToken)
    {
        if (!payment.InvoiceStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
            && !payment.InvoiceStatus.Equals("Success", StringComparison.OrdinalIgnoreCase))
            return "payment.not_paid";
        if (string.IsNullOrWhiteSpace(payment.PaymentId)
            || !payment.PaymentId.Equals(paymentId, StringComparison.OrdinalIgnoreCase))
            return "payment.payment_id_mismatch";
        if (!string.IsNullOrWhiteSpace(observedInvoiceId)
            && !observedInvoiceId.Equals(payment.InvoiceId, StringComparison.OrdinalIgnoreCase))
            return "payment.invoice_id_mismatch";
        if (observedAmount.HasValue && observedAmount.Value != payment.InvoiceValue)
            return "payment.webhook_amount_mismatch";
        if (!string.IsNullOrWhiteSpace(observedCurrencyCode)
            && !observedCurrencyCode.Equals(payment.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            return "payment.webhook_currency_mismatch";
        if (!string.IsNullOrWhiteSpace(observedCustomerReference)
            && !observedCustomerReference.Equals(payment.CustomerReference, StringComparison.OrdinalIgnoreCase))
            return "payment.webhook_customer_reference_mismatch";
        if (!Guid.TryParse(payment.CustomerReference, out var reference))
            return "payment.customer_reference_mismatch";

        if (subscription.FamilyGroupId is Guid familyGroupId)
        {
            if (reference != familyGroupId) return "payment.customer_reference_mismatch";
            var batch = await FamilyBatchPaymentResolver.ResolveAsync(_db, familyGroupId, paymentId, cancellationToken);
            if (batch is not null)
            {
                if (payment.InvoiceValue != batch.FinalPrice) return "payment.amount_mismatch";
                if (!payment.CurrencyCode.Equals(batch.CurrencyCode, StringComparison.OrdinalIgnoreCase)) return "payment.currency_mismatch";
                return null;
            }
            if (await FamilyBatchPaymentResolver.GroupHasAnyBatchAsync(_db, familyGroupId, cancellationToken))
                return null; // verified paid identity/reference; unresolved batch is recorded for review below.
            var group = await _db.FamilyGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == familyGroupId, cancellationToken);
            if (group is null) return "payment.family_group_missing";
            if (group.PaymentId != paymentId) return "payment.stale_attempt_review_required";
            if (payment.InvoiceValue != group.QuotedFinalPrice) return "payment.amount_mismatch";
            if (!payment.CurrencyCode.Equals(group.CurrencyCode, StringComparison.OrdinalIgnoreCase)) return "payment.currency_mismatch";
            return null;
        }

        if (!await IsCurrentAttemptAsync(subscription, paymentId, cancellationToken)) return "payment.stale_attempt_review_required";
        if (reference != subscription.Id)
            return "payment.customer_reference_mismatch";
        if (payment.InvoiceValue != subscription.PaidAmount.Amount)
            return "payment.amount_mismatch";
        if (!payment.CurrencyCode.Equals(subscription.PaidAmount.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            return "payment.currency_mismatch";
        return null;
    }

    private async Task<bool> IsCurrentAttemptAsync(Subscription subscription, string paymentId, CancellationToken ct)
    {
        if (await _db.SubscriptionTierUpgrades.AnyAsync(x => x.SubscriptionId == subscription.Id && x.Status == VerificationStatus.Submitted && x.PaymentId == paymentId, ct)) return true;
        if (subscription.FamilyGroupId is Guid groupId)
        {
            if (await FamilyBatchPaymentResolver.GroupHasAnyBatchAsync(_db, groupId, ct))
                return await FamilyBatchPaymentResolver.ResolveAsync(_db, groupId, paymentId, ct) is not null;
            return await _db.FamilyGroups.AnyAsync(x => x.Id == groupId && x.PaymentId == paymentId, ct);
        }
        return subscription.PaymentId == paymentId;
    }

    private async Task<string?> ValidateSubscriptionReservationAsync(Subscription subscription, CancellationToken transactionCt)
    {
        var expectedDates = _schedule.GetServiceDates(subscription.StartDate, subscription.DurationDays, subscription.IncludeFriday);
        var poolRestaurant = await _db.SubscriptionRestaurantSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubscriptionId == subscription.Id && x.IsInitiallySelected, transactionCt);
        if (poolRestaurant is null)
            return "payment.restaurant_pool_missing";

        await _capacityLock.AcquireAsync(poolRestaurant.RestaurantId, expectedDates, transactionCt);
        var holds = await _db.CapacityHolds
            .Where(x => x.SubscriptionId == subscription.Id)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(transactionCt);
        var poolDays = await _db.SubscriptionRestaurantDaySnapshots.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.RestaurantId == poolRestaurant.RestaurantId)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(transactionCt);

        var holdError = ValidatePendingHolds(expectedDates, poolRestaurant.RestaurantId, holds, poolDays, _clock.UtcNow);
        if (holdError is not null)
            return holdError;

        return null;
    }

    private async Task LockFamilyBeforeSubscriptionAsync(Guid subscriptionId, CancellationToken ct)
    {
        var groupId = await _db.Subscriptions.AsNoTracking().Where(x => x.Id == subscriptionId)
            .Select(x => x.FamilyGroupId).SingleOrDefaultAsync(ct);
        if (groupId is Guid id)
        {
            if (_locks is null && _db is DbContext context && context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
                throw new InvalidOperationException("Family financial processing requires the application lock manager.");
            if (_locks is not null)
            {
                await _locks.LockFamilyGroupAsync(id, ct);
                var batchIds = await _db.FamilyCheckoutBatchLines.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId && x.Batch.FamilyGroupId == id)
                    .Select(x => x.BatchId).Distinct().ToListAsync(ct);
                foreach (var batchId in batchIds.OrderBy(x => x)) await _locks.LockFamilyBatchAsync(batchId, ct);
            }
        }
    }

    private async Task<PaymentProcessingResult> PutBatchInReviewAsync(Subscription subscription,
        FamilyBatchPaymentResolution? resolution, string paymentId, PaymentGatewayStatusResponse payment,
        string? payloadHash, string error, CancellationToken ct)
    {
        if (resolution is not null)
        {
            var batch = await _db.FamilyCheckoutBatches.SingleAsync(x => x.Id == resolution.BatchId, ct);
            batch.Status = FamilyCheckoutBatchStatus.ReviewRequired; batch.Version++;
            if (batch.Purpose == "Renewal" && batch.RenewalRequestId is Guid renewalId)
            {
                var renewal = await _db.SubscriptionRenewals.SingleOrDefaultAsync(x => x.Id == renewalId
                    && x.BatchId == batch.Id, ct);
                if (renewal is not null) renewal.Status = "ReviewRequired";
            }
        }
        var receipt = await _db.PaymentWebhookReceipts.FirstOrDefaultAsync(x => x.Gateway == Gateway && x.PaymentId == paymentId, ct);
        if (receipt is null)
        {
            receipt = new PaymentWebhookReceipt { Gateway = Gateway, EventKey = paymentId, SubscriptionId = subscription.Id,
                PaymentId = paymentId, InvoiceId = payment.InvoiceId, Amount = payment.InvoiceValue, CurrencyCode = payment.CurrencyCode,
                CustomerReference = payment.CustomerReference, VerifiedAtUtc = _clock.UtcNow, ProcessedAtUtc = _clock.UtcNow,
                PayloadHash = payloadHash };
            _db.PaymentWebhookReceipts.Add(receipt);
        }
        receipt.Status = "FinancialReviewRequired"; receipt.LastFulfillmentErrorCode = error;
        await _db.SaveChangesAsync(ct);
        return new(true, false, error, subscription.Status, "ReviewRequired");
    }

    private static string? ValidatePendingHolds(
        IReadOnlyList<DateOnly> expectedDates,
        Guid restaurantId,
        IReadOnlyList<MealMate.Domain.RestaurantOperations.CapacityHold> holds,
        IReadOnlyList<SubscriptionRestaurantDaySnapshot> poolDays,
        DateTime now)
    {
        if (holds.Count != expectedDates.Count || poolDays.Count != expectedDates.Count)
            return "payment.hold_or_snapshot_count_mismatch";
        if (holds.Any(x => x.Status != CapacityHoldStatus.PendingPayment || x.RestaurantId != restaurantId || x.Quantity != 1))
            return "payment.capacity_hold_mismatch";
        if (holds.Any(x => x.ExpiresAtUtc <= now)) return "payment.capacity_hold_expired";
        foreach (var date in expectedDates)
        {
            var hold = holds.SingleOrDefault(x => x.ServiceDate == date);
            var snapshot = poolDays.SingleOrDefault(x => x.ServiceDate == date);
            if (hold is null || snapshot is null || hold.MenuPlanVersionId != snapshot.MenuPlanVersionId)
                return "payment.capacity_hold_snapshot_mismatch";
        }
        return null;
    }

    private async Task<PaymentProcessingResult> PutInReviewAsync(
        Subscription subscription,
        string paymentId,
        PaymentGatewayStatusResponse payment,
        string? payloadHash,
        string errorCode,
        bool writeReceipt,
        CancellationToken ct)
    {
        var oldStatus = subscription.Status;
        subscription.Status = SubscriptionStatus.SuspendedForReview;
        if (writeReceipt)
            subscription.PaymentId = paymentId;
        subscription.PaymentInvoiceId = payment.InvoiceId;
        subscription.PaymentVerifiedAtUtc = _clock.UtcNow;
        subscription.LastFulfillmentErrorCode = errorCode;
        subscription.LastOperationAtUtc = _clock.UtcNow;

        if (writeReceipt)
        {
            _db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
            {
                Gateway = Gateway,
                EventKey = paymentId,
                SubscriptionId = subscription.Id,
                Status = "FinancialReviewRequired",
                PayloadHash = payloadHash,
                PaymentId = paymentId,
                InvoiceId = payment.InvoiceId,
                Amount = payment.InvoiceValue,
                CurrencyCode = payment.CurrencyCode.ToUpperInvariant(),
                CustomerReference = payment.CustomerReference,
                VerifiedAtUtc = _clock.UtcNow,
                ProcessedAtUtc = _clock.UtcNow,
                LastFulfillmentErrorCode = errorCode
            });
        }
        else
        {
            // Receipt already recorded for this family invoice; annotate without a second row.
            var existing = await _db.PaymentWebhookReceipts
                .FirstOrDefaultAsync(x => x.Gateway == Gateway && x.PaymentId == paymentId, ct);
            if (existing is not null)
            {
                existing.LastFulfillmentErrorCode = errorCode;
                if (existing.Status == "PaymentVerified")
                    existing.Status = "FinancialReviewRequired";
            }
        }

        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = subscription.Id,
            OperationType = "PAYMENT_REQUIRES_REVIEW",
            PreviousStatus = oldStatus,
            NewStatus = SubscriptionStatus.SuspendedForReview,
            Reason = errorCode,
            PerformedByUserId = subscription.CustomerUserId,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return new(true, false, errorCode, subscription.Status, "ReviewRequired");
    }

    private PaymentWebhookReceipt NewRejectedReceipt(Guid subscriptionId, string paymentId, string errorCode, string? payloadHash)
        => new()
        {
            Gateway = Gateway,
            EventKey = $"rejected:{subscriptionId:N}:{paymentId}:{Guid.NewGuid():N}",
            SubscriptionId = subscriptionId,
            Status = "VerificationRejected",
            PayloadHash = payloadHash,
            ProcessedAtUtc = _clock.UtcNow,
            LastFulfillmentErrorCode = errorCode
        };
}

public sealed class SubscriptionFulfillmentService : ISubscriptionFulfillmentService
{
    private readonly int _maxAutomaticAttempts;
    private readonly IAppDbContext _db;
    private readonly ICheckoutTransactionExecutor _transactions;
    private readonly ICapacityConcurrencyLock _capacityLock;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerPushNotifier _customerPush;

    public SubscriptionFulfillmentService(
        IAppDbContext db,
        ICheckoutTransactionExecutor transactions,
        ICapacityConcurrencyLock capacityLock,
        ISubscriptionScheduleService schedule,
        IDateTimeProvider clock,
        ICustomerPushNotifier? customerPush = null,
        Microsoft.Extensions.Options.IOptions<FulfillmentOptions>? options = null)
        => (_db, _transactions, _capacityLock, _schedule, _clock, _customerPush, _maxAutomaticAttempts)
            = (db, transactions, capacityLock, schedule, clock, customerPush ?? NullCustomerPushNotifier.Instance, options?.Value?.MaxAutomaticAttempts > 0 ? options.Value.MaxAutomaticAttempts : 5);

    public Task<FulfillmentResult> TryFulfillAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
        => _transactions.ExecuteAsync(ct => TryFulfillInTransactionAsync(subscriptionId, ct), cancellationToken);

    private async Task<FulfillmentResult> TryFulfillInTransactionAsync(Guid subscriptionId, CancellationToken ct)
    {
        await _capacityLock.AcquireSubscriptionAsync(subscriptionId, ct);
        var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null) return new(false, false, "subscription.not_found");
        if (subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.Scheduled
            && subscription.FulfillmentCompletedAtUtc.HasValue)
            return new(true, true);
        if (subscription.Status != SubscriptionStatus.Paid)
            return new(false, false, "fulfillment.subscription_not_paid");

        var expectedDates = _schedule.GetServiceDates(subscription.StartDate, subscription.DurationDays, subscription.IncludeFriday);
        var selected = await _db.SubscriptionRestaurantSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubscriptionId == subscriptionId && x.IsInitiallySelected, ct);
        if (selected is null)
            return await FailAsync(subscription, "fulfillment.selected_restaurant_snapshot_missing", ct);

        await _capacityLock.AcquireAsync(selected.RestaurantId, expectedDates, ct);
        var holds = await _db.CapacityHolds
            .Where(x => x.SubscriptionId == subscriptionId)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(ct);
        var sources = await _db.SubscriptionRestaurantDaySnapshots.AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId && x.RestaurantId == selected.RestaurantId)
            .OrderBy(x => x.ServiceDate)
            .ToListAsync(ct);
        if (holds.Count != expectedDates.Count || sources.Count != expectedDates.Count
            || holds.Any(x => x.Status != CapacityHoldStatus.PaymentConfirmed || x.Quantity != 1 || x.RestaurantId != selected.RestaurantId))
            return await FailAsync(subscription, "fulfillment.hold_or_snapshot_mismatch", ct);

        foreach (var date in expectedDates)
        {
            var hold = holds.SingleOrDefault(x => x.ServiceDate == date);
            var source = sources.SingleOrDefault(x => x.ServiceDate == date);
            if (hold is null || source is null || hold.MenuPlanVersionId != source.MenuPlanVersionId
                || !FrozenSnapshotIntegrity.HasUsablePayload(source.SlotRequirementsJson, source.MealsJson, source.ContentHash))
                return await FailAsync(subscription, "fulfillment.day_snapshot_missing", ct);
        }

        var existingDays = await _db.SubscriptionDays.Where(x => x.SubscriptionId == subscriptionId).ToListAsync(ct);
        // Days stay without a customer-chosen restaurant until POST .../days/{date}/restaurant.
        // Capacity holds still use the checkout "initially selected" restaurant; that is not a day selection.
        if (existingDays.Any(x => !expectedDates.Contains(x.ServiceDate)
            || (x.RestaurantId is null
                && sources.Single(s => s.ServiceDate == x.ServiceDate).MenuPlanVersionId != x.ResolvedMenuPlanVersionId)))
            return await FailAsync(subscription, "fulfillment.existing_day_conflict", ct);

        var paymentVerifiedAtUtc = subscription.PaymentVerifiedAtUtc;
        for (var dayIndex = 0; dayIndex < expectedDates.Count; dayIndex++)
        {
            var date = expectedDates[dayIndex];
            var source = sources.Single(x => x.ServiceDate == date);
            var day = existingDays.SingleOrDefault(x => x.ServiceDate == date);
            if (day is null)
            {
                day = new SubscriptionDay
                {
                    SubscriptionId = subscriptionId,
                    RestaurantId = null,
                    ResolvedMenuPlanVersionId = source.MenuPlanVersionId,
                    ServiceDate = date,
                    Status = SubscriptionDayStatus.Open,
                    // First service day uses the approved payment+window exception; later days the
                    // standard cutoff. ConfirmedAtUtc stays null: payment/hold confirmation is not
                    // a day-selection confirmation and must not block calendar edits.
                    SelectionDeadlineUtc = dayIndex == 0 && paymentVerifiedAtUtc is { } verifiedAt
                        ? _schedule.GetFirstDaySelectionDeadlineUtc(date, verifiedAt)
                        : _schedule.GetSelectionDeadlineUtc(date)
                };
                _db.SubscriptionDays.Add(day);
                existingDays.Add(day);
            }

            var finalSnapshot = await _db.SubscriptionDayMealSnapshots
                .SingleOrDefaultAsync(x => x.SubscriptionDayId == day.Id, ct);
            if (finalSnapshot is null)
            {
                // Freeze the checkout hold restaurant's menu for the day; do not treat it as the customer's pick.
                _db.SubscriptionDayMealSnapshots.Add(new SubscriptionDayMealSnapshot
                {
                    SubscriptionId = subscriptionId,
                    SubscriptionDayId = day.Id,
                    ServiceDate = date,
                    RestaurantId = selected.RestaurantId,
                    ProgramId = subscription.ProgramId,
                    MenuPlanVersionId = source.MenuPlanVersionId,
                    RequiredSlotsJson = source.SlotRequirementsJson,
                    MealsJson = source.MealsJson,
                    CapturedAtUtc = _clock.UtcNow
                });
            }
            else if (day.RestaurantId is null
                || day.RestaurantId != selected.RestaurantId)
            {
                // Unselected day, or customer already chose a (possibly different) restaurant — leave snapshot alone.
            }
            else if (finalSnapshot.RestaurantId != selected.RestaurantId
                || finalSnapshot.MenuPlanVersionId != source.MenuPlanVersionId
                || finalSnapshot.MealsJson != source.MealsJson
                || finalSnapshot.RequiredSlotsJson != source.SlotRequirementsJson)
            {
                return await FailAsync(subscription, "fulfillment.immutable_snapshot_conflict", ct);
            }
        }

        foreach (var hold in holds)
        {
            hold.Status = CapacityHoldStatus.Confirmed;
            hold.ConfirmedAtUtc = _clock.UtcNow;
        }

        var oldStatus = subscription.Status;
        subscription.Status = subscription.StartDate > KuwaitToday(_clock.UtcNow)
            ? SubscriptionStatus.Scheduled
            : SubscriptionStatus.Active;
        subscription.FulfillmentAttemptCount++;
        subscription.LastFulfillmentAttemptUtc = _clock.UtcNow;
        subscription.LastFulfillmentErrorCode = null;
        subscription.FulfillmentCompletedAtUtc = _clock.UtcNow;
        subscription.LastOperationAtUtc = _clock.UtcNow;
        var receipt = await _db.PaymentWebhookReceipts
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId && x.PaymentId == subscription.PaymentId, ct);
        if (receipt is not null)
        {
            receipt.Status = "FulfillmentCompleted";
            receipt.FulfillmentAttemptCount = subscription.FulfillmentAttemptCount;
            receipt.LastFulfillmentAttemptUtc = _clock.UtcNow;
            receipt.LastFulfillmentErrorCode = null;
            receipt.FulfillmentCompletedAtUtc = _clock.UtcNow;
        }
        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = subscriptionId,
            OperationType = "FULFILLMENT_COMPLETED",
            PreviousStatus = oldStatus,
            NewStatus = subscription.Status,
            Reason = "All subscription days and immutable meal snapshots were created from the frozen restaurant pool.",
            PerformedByUserId = subscription.CustomerUserId,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        await _customerPush.NotifySubscriptionPaymentConfirmedAsync(
            subscription.CustomerUserId,
            subscriptionId,
            ct);

        return new(true, false);
    }

    private async Task<FulfillmentResult> FailAsync(Subscription subscription, string errorCode, CancellationToken ct)
    {
        subscription.FulfillmentAttemptCount++;
        subscription.LastFulfillmentAttemptUtc = _clock.UtcNow;
        subscription.LastFulfillmentErrorCode = errorCode;
        subscription.LastOperationAtUtc = _clock.UtcNow;
        if (subscription.FulfillmentAttemptCount >= _maxAutomaticAttempts)
            subscription.Status = SubscriptionStatus.SuspendedForReview;
        var receipt = await _db.PaymentWebhookReceipts
            .FirstOrDefaultAsync(x => x.SubscriptionId == subscription.Id && x.PaymentId == subscription.PaymentId, ct);
        if (receipt is not null)
        {
            receipt.Status = subscription.Status == SubscriptionStatus.SuspendedForReview
                ? "FulfillmentReviewRequired"
                : "FulfillmentRetryPending";
            receipt.FulfillmentAttemptCount = subscription.FulfillmentAttemptCount;
            receipt.LastFulfillmentAttemptUtc = _clock.UtcNow;
            receipt.LastFulfillmentErrorCode = errorCode;
        }
        _db.SubscriptionOperations.Add(new SubscriptionOperation
        {
            SubscriptionId = subscription.Id,
            OperationType = subscription.Status == SubscriptionStatus.SuspendedForReview
                ? "FULFILLMENT_REVIEW_REQUIRED"
                : "FULFILLMENT_RETRY_SCHEDULED",
            PreviousStatus = SubscriptionStatus.Paid,
            NewStatus = subscription.Status,
            Reason = errorCode,
            PerformedByUserId = subscription.CustomerUserId,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return new(false, false, errorCode);
    }

    private static DateOnly KuwaitToday(DateTime utcNow)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Arab Standard Time" : "Asia/Kuwait");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone));
    }
}

public sealed class CapacityHoldExpiryService : ICapacityHoldExpiryService
{
    private readonly IAppDbContext _db;
    private readonly ICheckoutTransactionExecutor _transactions;
    private readonly ICapacityConcurrencyLock _capacityLock;
    private readonly IDateTimeProvider _clock;
    private readonly ISubscriptionFlowCouponService? _coupons;
    private readonly IApplicationLockManager? _familyLocks;

    public CapacityHoldExpiryService(
        IAppDbContext db,
        ICheckoutTransactionExecutor transactions,
        ICapacityConcurrencyLock capacityLock,
        IDateTimeProvider clock,
        ISubscriptionFlowCouponService? couponService = null, IApplicationLockManager? familyLocks = null)
        => (_db, _transactions, _capacityLock, _clock, _coupons, _familyLocks)
            = (db, transactions, capacityLock, clock, couponService, familyLocks);

    public async Task<CapacityExpiryResult> ExpireDueAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var unpaidStatuses = new[]
        {
            SubscriptionStatus.Draft,
            SubscriptionStatus.PendingPayment,
            SubscriptionStatus.PaymentFailed
        };
        var holdDueIds = await _db.CapacityHolds.AsNoTracking()
            .Where(x => x.Status == CapacityHoldStatus.PendingPayment && x.ExpiresAtUtc <= now)
            .Select(x => x.SubscriptionId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var ageCutoff = now.AddMinutes(-15);
        var agedIds = await _db.Subscriptions.AsNoTracking()
            .Where(x => unpaidStatuses.Contains(x.Status)
                && (x.LastOperationAtUtc ?? x.CreatedAtUtc) <= ageCutoff)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        var candidates = holdDueIds.Concat(agedIds).Distinct().Take(500).ToList();
        var holdsExpired = 0;
        var subscriptionsExpired = 0;
        foreach (var subscriptionId in candidates)
        {
            var result = await _transactions.ExecuteAsync(async ct =>
            {
                var groupId = await _db.Subscriptions.AsNoTracking().Where(x => x.Id == subscriptionId)
                    .Select(x => x.FamilyGroupId).FirstOrDefaultAsync(ct);
                if (groupId is Guid familyId)
                {
                    if (_familyLocks is null && _db is DbContext context && context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
                        throw new InvalidOperationException("Family expiry requires the application lock manager.");
                    if (_familyLocks is not null) await _familyLocks.LockFamilyGroupAsync(familyId, ct);
                }
                await _capacityLock.AcquireSubscriptionAsync(subscriptionId, ct);
                var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
                if (subscription is null || !unpaidStatuses.Contains(subscription.Status))
                    return new CapacityExpiryResult(0, 0);
                var holds = await _db.CapacityHolds.Where(x => x.SubscriptionId == subscriptionId).ToListAsync(ct);
                if (holds.Count > 0)
                    await _capacityLock.AcquireAsync(holds[0].RestaurantId, holds.Select(x => x.ServiceDate).ToList(), ct);
                holds = await _db.CapacityHolds.Where(x => x.SubscriptionId == subscriptionId).ToListAsync(ct);
                if (holds.Any(x => x.Status == CapacityHoldStatus.PendingPayment && x.ExpiresAtUtc > now))
                    return new CapacityExpiryResult(0, 0);
                var dueHolds = holds.Where(x => x.Status == CapacityHoldStatus.PendingPayment && x.ExpiresAtUtc <= now).ToList();
                var ageElapsed = (subscription.LastOperationAtUtc ?? subscription.CreatedAtUtc) <= ageCutoff;
                if (dueHolds.Count == 0 && !ageElapsed)
                    return new CapacityExpiryResult(0, 0);

                foreach (var hold in dueHolds)
                {
                    hold.Status = CapacityHoldStatus.Released;
                    hold.ReleasedAtUtc = now;
                }

                if (_coupons is not null)
                    await _coupons.ReleaseCheckoutRedemptionAsync(subscriptionId, ct);

                var oldStatus = subscription.Status;
                subscription.Status = SubscriptionStatus.Cancelled;
                subscription.LastOperationAtUtc = now;
                _db.SubscriptionOperations.Add(new SubscriptionOperation
                {
                    SubscriptionId = subscriptionId,
                    OperationType = "CHECKOUT_HOLD_EXPIRED",
                    PreviousStatus = oldStatus,
                    NewStatus = SubscriptionStatus.Cancelled,
                    Reason = "Uncompleted checkout expired after the 15-minute reservation window.",
                    PerformedByUserId = Guid.Empty,
                    PerformedByUserType = UserType.Admin,
                    TimestampUtc = now
                });
                if (subscription.FamilyGroupId is Guid expiredGroupId)
                {
                    var batch = await _db.FamilyCheckoutBatches.Include(x => x.PaymentAttempts)
                        .Where(x => x.FamilyGroupId == expiredGroupId && x.Status == FamilyCheckoutBatchStatus.PaymentPending
                            && x.Lines.Any(l => l.SubscriptionId == subscriptionId)).SingleOrDefaultAsync(ct);
                    if (batch is not null)
                    {
                        batch.Status = batch.PaymentAttempts.Any(x => x.ResultCode is "Initializing" or "InitializationUncertain")
                            ? FamilyCheckoutBatchStatus.ReviewRequired : FamilyCheckoutBatchStatus.Expired;
                        batch.Version++;
                        var group = await _db.FamilyGroups.SingleAsync(x => x.Id == expiredGroupId, ct);
                        if (group.PaymentAnchorSubscriptionId == batch.PaymentAnchorSubscriptionId
                            && batch.Status != FamilyCheckoutBatchStatus.ReviewRequired)
                        {
                            group.Status = group.PaidAtUtc.HasValue ? FamilyGroupStatus.Active : FamilyGroupStatus.Draft;
                            group.PaymentAnchorSubscriptionId = null; group.PaymentId = null; group.PaymentInvoiceId = null;
                        }
                    }
                }
                await _db.SaveChangesAsync(ct);
                return new CapacityExpiryResult(dueHolds.Count, 1);
            }, cancellationToken);
            holdsExpired += result.HoldsExpired;
            subscriptionsExpired += result.SubscriptionsExpired;
        }

        var managers = await _db.FamilyGroups.AsNoTracking()
            .Where(x => x.Status == FamilyGroupStatus.Draft || x.Status == FamilyGroupStatus.CheckoutPending)
            .Select(x => new { x.Id, x.ManagerCustomerUserId }).ToListAsync(cancellationToken);
        foreach (var group in managers)
        {
            await _transactions.ExecuteAsync(async ct =>
            {
                if (_familyLocks is null && _db is DbContext context && context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
                    throw new InvalidOperationException("Family expiry requires the application lock manager.");
                if (_familyLocks is not null) await _familyLocks.LockFamilyGroupAsync(group.Id, ct);
                await FamilyDraftExpiry.ExpireDueAsync(_db, now, ct, group.ManagerCustomerUserId, groupLockHeld: true);
                await FamilyDraftExpiry.RemoveOrphanCheckoutPendingAsync(_db, now, ct, group.ManagerCustomerUserId);
                return true;
            }, cancellationToken);
        }
        return new(holdsExpired, subscriptionsExpired);
    }
}
