using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

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
    private const string Gateway = "MyFatoorah";
    private readonly IAppDbContext _db;
    private readonly IMyFatoorahPaymentService _payments;
    private readonly ICheckoutTransactionExecutor _transactions;
    private readonly ICapacityConcurrencyLock _capacityLock;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly ISubscriptionFulfillmentService _fulfillment;
    private readonly IDateTimeProvider _clock;
    private readonly ICustomerPushNotifier _customerPush;

    public SubscriptionPaymentProcessor(
        IAppDbContext db,
        IMyFatoorahPaymentService payments,
        ICheckoutTransactionExecutor transactions,
        ICapacityConcurrencyLock capacityLock,
        ISubscriptionScheduleService schedule,
        ISubscriptionFulfillmentService fulfillment,
        IDateTimeProvider clock,
        ICustomerPushNotifier? customerPush = null)
        => (_db, _payments, _transactions, _capacityLock, _schedule, _fulfillment, _clock, _customerPush)
            = (db, payments, transactions, capacityLock, schedule, fulfillment, clock, customerPush ?? NullCustomerPushNotifier.Instance);

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

        var resolvedId = await ResolveCheckoutSubscriptionIdAsync(subscriptionId, cancellationToken);
        if (resolvedId is null)
            return new(false, false, "subscription.not_found", null, "NotStarted");
        subscriptionId = resolvedId.Value;

        IReadOnlyList<Guid> siblingIds = [subscriptionId];
        var transition = await _transactions.ExecuteAsync(async transactionCt =>
        {
            await _capacityLock.AcquirePaymentAsync(paymentId, transactionCt);
            await _capacityLock.AcquireSubscriptionAsync(subscriptionId, transactionCt);
            var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, transactionCt);
            if (subscription is null)
                return new PaymentProcessingResult(false, false, "subscription.not_found", null, "NotStarted");

            var previousReceipt = await _db.PaymentWebhookReceipts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Gateway == Gateway && x.PaymentId == paymentId, transactionCt);
            if (previousReceipt is not null)
            {
                if (previousReceipt.SubscriptionId != subscriptionId
                    && (subscription.FamilyGroupId is null
                        || previousReceipt.SubscriptionId != (await _db.FamilyGroups.AsNoTracking()
                            .Where(x => x.Id == subscription.FamilyGroupId)
                            .Select(x => x.PaymentAnchorSubscriptionId)
                            .FirstOrDefaultAsync(transactionCt))))
                    return new(false, false, "payment.reference_already_used", subscription.Status, "Rejected");
                return new(true, true, null, subscription.Status,
                    subscription.FulfillmentCompletedAtUtc.HasValue ? "Completed" : "Pending");
            }

            var pendingUpgrade = await _db.SubscriptionTierUpgrades
                .FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId
                    && x.Status == VerificationStatus.Submitted
                    && (x.PaymentId == paymentId || string.IsNullOrEmpty(x.PaymentId)), transactionCt);
            if (pendingUpgrade is not null)
                return await ApplyPaidUpgradeAsync(subscription, pendingUpgrade, verified, paymentId, payloadHash, transactionCt);

            var validationError = await ValidatePaymentAsync(
                subscription, paymentId, verified, observedInvoiceId, observedAmount,
                observedCurrencyCode, observedCustomerReference, transactionCt);
            if (validationError is not null)
            {
                _db.PaymentWebhookReceipts.Add(NewRejectedReceipt(subscriptionId, paymentId, validationError, payloadHash));
                await _db.SaveChangesAsync(transactionCt);
                return new(false, false, validationError, subscription.Status, "Rejected");
            }

            // Converted family checkout may anchor on an already-paid manager subscription.
            // Still process any PendingPayment siblings for that family group.
            var familyPendingTargets = subscription.FamilyGroupId is Guid pendingFamilyGroupId
                ? await _db.Subscriptions
                    .Where(x => x.FamilyGroupId == pendingFamilyGroupId
                                && x.Status == SubscriptionStatus.PendingPayment)
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
                .OrderByDescending(x => x.Id == subscription.Id)
                .ThenBy(x => x.CreatedAtUtc)
                .ToList();
            siblingIds = targets.Select(x => x.Id).ToList();

            // Family invoice = one financial unit: at most one webhook receipt per paymentId.
            var receiptWritten = false;
            foreach (var target in targets)
            {
                await _capacityLock.AcquireSubscriptionAsync(target.Id, transactionCt);
                var markError = await MarkSubscriptionPaidAsync(
                    target, paymentId, verified, payloadHash, writeReceipt: !receiptWritten, transactionCt);
                if (markError is not null)
                {
                    return await PutInReviewAsync(
                        target, paymentId, verified, payloadHash, markError,
                        writeReceipt: !receiptWritten, transactionCt);
                }

                receiptWritten = true;
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

                var discountAmount = targets.Sum(x => x.PaidAmount.Amount) - group.QuotedFinalPrice;
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
                group.PaidAtUtc = _clock.UtcNow;
                group.PaymentId = paymentId;
                group.PaymentInvoiceId = verified.InvoiceId;
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
        if (transition.FulfillmentStatus == "Completed")
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
        if (verified.InvoiceStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
            || verified.InvoiceStatus.Equals("Success", StringComparison.OrdinalIgnoreCase))
            return new(false, false, "payment.gateway_status_conflict", null, "NotStarted");
        if (!string.IsNullOrWhiteSpace(observedInvoiceId) && observedInvoiceId != verified.InvoiceId)
            return new(false, false, "payment.invoice_id_mismatch", null, "NotStarted");
        if (observedAmount.HasValue && observedAmount.Value != verified.InvoiceValue)
            return new(false, false, "payment.webhook_amount_mismatch", null, "NotStarted");

        var resolvedId = await ResolveCheckoutSubscriptionIdAsync(subscriptionId, cancellationToken);
        if (resolvedId is null)
            return new(false, false, "subscription.not_found", null, "NotStarted");
        subscriptionId = resolvedId.Value;

        return await _transactions.ExecuteAsync<PaymentProcessingResult>(async ct =>
        {
            await _capacityLock.AcquirePaymentAsync(paymentId, ct);
            await _capacityLock.AcquireSubscriptionAsync(subscriptionId, ct);
            var existing = await _db.PaymentWebhookReceipts.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Gateway == Gateway && x.PaymentId == paymentId, ct);
            if (existing is not null)
                return new(existing.SubscriptionId == subscriptionId, true,
                    existing.SubscriptionId == subscriptionId ? null : "payment.reference_already_used", null, "Released");
            var subscription = await _db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
            if (subscription is null) return new(false, false, "subscription.not_found", null, "NotStarted");

            if (!Guid.TryParse(verified.CustomerReference, out var verifiedReference)
                || (!string.IsNullOrWhiteSpace(observedCustomerReference)
                    && observedCustomerReference != verified.CustomerReference))
                return new(false, false, "payment.customer_reference_mismatch", null, "NotStarted");
            var expectedReference = subscription.FamilyGroupId ?? subscription.Id;
            if (verifiedReference != expectedReference)
                return new(false, false, "payment.customer_reference_mismatch", null, "NotStarted");

            var pendingUpgrade = await _db.SubscriptionTierUpgrades
                .FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId
                    && x.Status == VerificationStatus.Submitted
                    && (x.PaymentId == paymentId || string.IsNullOrEmpty(x.PaymentId)), ct);
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
                ? await _db.Subscriptions.Where(x => x.FamilyGroupId == familyGroupId).ToListAsync(ct)
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

                if (target.Status != SubscriptionStatus.PendingPayment)
                    continue;

                var oldStatus = target.Status;
                target.Status = SubscriptionStatus.PaymentFailed;
                target.LastOperationAtUtc = _clock.UtcNow;
                _db.SubscriptionOperations.Add(new SubscriptionOperation
                {
                    SubscriptionId = target.Id,
                    OperationType = "PAYMENT_FAILED",
                    PreviousStatus = oldStatus,
                    NewStatus = target.Status,
                    Reason = $"MyFatoorah status verified as {verified.InvoiceStatus}.",
                    PerformedByUserId = target.CustomerUserId,
                    PerformedByUserType = UserType.Customer,
                    TimestampUtc = _clock.UtcNow
                });
            }

            if (subscription.FamilyGroupId is Guid failedFamilyGroupId)
            {
                var group = await _db.FamilyGroups.FirstAsync(x => x.Id == failedFamilyGroupId, ct);
                group.Status = group.PaidAtUtc.HasValue
                    ? FamilyGroupStatus.Active
                    : FamilyGroupStatus.Draft;
                group.PaymentAnchorSubscriptionId = group.PaidAtUtc.HasValue
                    ? group.PaymentAnchorSubscriptionId
                    : null;
                group.PaymentId = group.PaidAtUtc.HasValue ? group.PaymentId : null;
                group.PaymentInvoiceId = group.PaidAtUtc.HasValue ? group.PaymentInvoiceId : null;
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

    private async Task<Guid?> ResolveCheckoutSubscriptionIdAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await _db.Subscriptions.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken))
            return id;

        return await _db.FamilyGroups.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.PaymentAnchorSubscriptionId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<PaymentProcessingResult> ApplyPaidUpgradeAsync(
        Subscription subscription,
        SubscriptionTierUpgrade upgrade,
        MyFatoorahPaymentStatusResponse verified,
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

        if (!verified.CurrencyCode.Equals(upgrade.QuotedTotalDue.CurrencyCode, StringComparison.OrdinalIgnoreCase)
            && !verified.CurrencyCode.Equals(subscription.PaidAmount.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            return new(false, false, "payment.currency_mismatch", subscription.Status, "Rejected");

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

    private async Task<string?> MarkSubscriptionPaidAsync(
        Subscription subscription,
        string paymentId,
        MyFatoorahPaymentStatusResponse verified,
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
            Reason = $"MyFatoorah payment {paymentId} verified; fulfillment is pending.",
            PerformedByUserId = subscription.CustomerUserId,
            PerformedByUserType = UserType.Customer,
            TimestampUtc = _clock.UtcNow
        });
        return null;
    }

    private async Task<string?> ValidatePaymentAsync(
        Subscription subscription,
        string paymentId,
        MyFatoorahPaymentStatusResponse payment,
        string? observedInvoiceId,
        decimal? observedAmount,
        string? observedCurrencyCode,
        string? observedCustomerReference,
        CancellationToken cancellationToken)
    {
        if (!payment.InvoiceStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
            && !payment.InvoiceStatus.Equals("Success", StringComparison.OrdinalIgnoreCase))
            return "payment.not_paid";
        if (!string.IsNullOrWhiteSpace(payment.PaymentId)
            && !payment.PaymentId.Equals(paymentId, StringComparison.OrdinalIgnoreCase))
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
            var group = await _db.FamilyGroups.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == familyGroupId, cancellationToken);
            if (group is null)
                return "payment.family_group_missing";
            if (reference != familyGroupId)
                return "payment.customer_reference_mismatch";
            if (payment.InvoiceValue != group.QuotedFinalPrice)
                return "payment.amount_mismatch";
            if (!payment.CurrencyCode.Equals(group.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                return "payment.currency_mismatch";
            return null;
        }

        if (reference != subscription.Id)
            return "payment.customer_reference_mismatch";
        if (payment.InvoiceValue != subscription.PaidAmount.Amount)
            return "payment.amount_mismatch";
        if (!payment.CurrencyCode.Equals(subscription.PaidAmount.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            return "payment.currency_mismatch";
        return null;
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
        MyFatoorahPaymentStatusResponse payment,
        string? payloadHash,
        string errorCode,
        bool writeReceipt,
        CancellationToken ct)
    {
        var oldStatus = subscription.Status;
        subscription.Status = SubscriptionStatus.SuspendedForReview;
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
    private const int MaxAutomaticAttempts = 5;
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
        ICustomerPushNotifier? customerPush = null)
        => (_db, _transactions, _capacityLock, _schedule, _clock, _customerPush)
            = (db, transactions, capacityLock, schedule, clock, customerPush ?? NullCustomerPushNotifier.Instance);

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

        foreach (var date in expectedDates)
        {
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
                    SelectionDeadlineUtc = _schedule.GetSelectionDeadlineUtc(date),
                    ConfirmedAtUtc = _clock.UtcNow
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
        if (subscription.FulfillmentAttemptCount >= MaxAutomaticAttempts)
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

    public CapacityHoldExpiryService(IAppDbContext db, ICheckoutTransactionExecutor transactions, ICapacityConcurrencyLock capacityLock, IDateTimeProvider clock)
        => (_db, _transactions, _capacityLock, _clock) = (db, transactions, capacityLock, clock);

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
                await _db.SaveChangesAsync(ct);
                return new CapacityExpiryResult(dueHolds.Count, 1);
            }, cancellationToken);
            holdsExpired += result.HoldsExpired;
            subscriptionsExpired += result.SubscriptionsExpired;
        }
        return new(holdsExpired, subscriptionsExpired);
    }
}

