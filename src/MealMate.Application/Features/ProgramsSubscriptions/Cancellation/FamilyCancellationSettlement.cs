using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Domain.CalendarMeals;
using MealMate.Application.Features.ProgramsSubscriptions.Freeze;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Cancellation;

public interface IFamilyCancellationSettlement
{
    Task<Result<bool>> RemoveAsync(Guid memberId, Guid actor, CancellationToken ct);
    Task<Result<CustomerCancellationQuoteDto>> QuoteAsync(Guid subscriptionId, Guid actor, UserType actorType, CancellationToken ct);
    Task<Result<CustomerCancellationResultDto>> CancelAsync(Guid subscriptionId, Guid actor, UserType actorType,
        string? reason, bool forceImmediate, CancellationToken ct, bool removeMember = false);
}

/// <summary>One durable family cancellation identity across customer, administrator and roster removal.</summary>
public sealed class FamilyCancellationSettlement(IAppDbContext db, IDateTimeProvider clock,
    IApplicationTransactionExecutor transactions, IApplicationLockManager locks, IFamilySubscriptionAccess access,
    ILocalizedMessageProvider messages, IOperationalPolicyService policies) : IFamilyCancellationSettlement
{
    private const string Kind = "FamilyCancellation";
    private const string ReviewCode = "family.financial_review_required";
    private sealed record Contribution(Guid SourceId, Guid Payer, decimal Paid, string Currency, IReadOnlyList<DateOnly> Dates);
    private sealed record Computation(CustomerCancellationQuoteDto Quote, List<SubscriptionDay> Days,
        IReadOnlyList<FamilyRefundBeneficiaryDto> Beneficiaries, Guid? BatchLineId);

    public async Task<Result<bool>> RemoveAsync(Guid memberId, Guid actor, CancellationToken ct)
    {
        var routing = await db.FamilyMembers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == memberId, ct);
        if (routing is null) return Error.NotFound("family.member_not_found");
        Guid? target = null;
        var result = await transactions.ExecuteResultAsync<bool>(async token => {
            await locks.LockFamilyGroupAsync(routing.FamilyGroupId, token);
            var member = await db.FamilyMembers.FirstAsync(x => x.Id == memberId, token);
            var group = await db.FamilyGroups.FirstAsync(x => x.Id == member.FamilyGroupId, token);
            if (group.ManagerCustomerUserId != actor) return Error.Forbidden("family.manager_only_action");
            if (member.Role == FamilyMemberRole.Manager) return Error.Conflict("family.cannot_remove_manager");
            if (await db.FamilyCheckoutBatchLines.AnyAsync(x => x.FamilyMemberId == memberId && x.CheckoutSelected
                && (x.Batch.Status == FamilyCheckoutBatchStatus.PaymentPending || x.Batch.Status == FamilyCheckoutBatchStatus.ReviewRequired), token))
                return Error.Conflict("family.payment_in_flight");
            var liveStatuses = new[] { SubscriptionStatus.Paid, SubscriptionStatus.Active, SubscriptionStatus.Scheduled,
                SubscriptionStatus.Frozen, SubscriptionStatus.SuspendedForReview, SubscriptionStatus.CancellationPending };
            var fundedSubscriptions = await db.Subscriptions.AsNoTracking().Where(x => x.FamilyGroupId == group.Id
                    && x.FamilyMemberId == memberId && liveStatuses.Contains(x.Status)
                    && (x.PaymentVerifiedAtUtc.HasValue || db.FamilyCheckoutBatchLines.Any(line => line.SubscriptionId == x.Id
                        && line.Batch.Status == FamilyCheckoutBatchStatus.Paid)))
                .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(token);
            if (fundedSubscriptions.Count > 1) return Error.Conflict("family.member_live_service_requires_settlement");
            target = fundedSubscriptions.SingleOrDefault();
            if (target == Guid.Empty) target = member.MemberSubscriptionId;
            if (target.HasValue)
            {
                var cancellation = await CancelAsync(target.Value, actor, UserType.Customer, "Roster removal", false, token, true);
                if (cancellation.IsFailure) return cancellation.Error;
            }
            else
            {
                var memberAccess = await db.FamilyMemberAccesses.FirstOrDefaultAsync(x => x.FamilyMemberId == memberId, token);
                if (memberAccess is null) { memberAccess = new FamilyMemberAccess { FamilyMemberId = memberId }; db.FamilyMemberAccesses.Add(memberAccess); }
                memberAccess.RemovedAtUtc ??= clock.UtcNow; memberAccess.IsPaused = true; memberAccess.PausedAtUtc ??= clock.UtcNow;
                await db.SaveChangesAsync(token);
            }
            return true;
        }, ct);
        if (result.IsFailure && result.Error.Code == ReviewCode && target.HasValue)
            await PersistReviewAsync(target.Value, routing.FamilyGroupId, actor, ct);
        return result;
    }

    public async Task<Result<CustomerCancellationQuoteDto>> QuoteAsync(Guid subscriptionId, Guid actor, UserType actorType, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null) return Error.NotFound("common.not_found");
        var auth = await AuthorizeAsync(subscription, actor, actorType, ct);
        if (auth.IsFailure) return auth.Error;
        var calculation = await CalculateAsync(subscription, false, ct);
        if (calculation.IsFailure) return calculation.Error;
        return calculation.Value.Quote;
    }

    public async Task<Result<CustomerCancellationResultDto>> CancelAsync(Guid subscriptionId, Guid actor, UserType actorType,
        string? reason, bool forceImmediate, CancellationToken ct, bool removeMember = false)
    {
        var routing = await db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (routing?.FamilyGroupId is not Guid groupId) return Error.NotFound("common.not_found");
        var result = await transactions.ExecuteResultAsync<CustomerCancellationResultDto>(async token =>
        {
            await locks.LockFamilyGroupAsync(groupId, token);
            var batchIds = await db.FamilyCheckoutBatchLines.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId)
                .Select(x => x.BatchId).Distinct().ToListAsync(token);
            foreach (var id in batchIds.OrderBy(x => x)) await locks.LockFamilyBatchAsync(id, token);
            await locks.LockSubscriptionAsync(subscriptionId, token);
            await locks.LockSubscriptionDaysAsync(subscriptionId, token);
            var subscription = await db.Subscriptions.FirstAsync(x => x.Id == subscriptionId, token);
            if (subscription.FamilyGroupId != groupId) return Error.Conflict(ReviewCode);
            if (removeMember && !await db.FamilyMembers.AnyAsync(x => x.Id == subscription.FamilyMemberId && x.MemberSubscriptionId == subscriptionId, token))
                return Error.Conflict("family.subscription_changed");
            var auth = await AuthorizeAsync(subscription, actor, actorType, token);
            if (auth.IsFailure) return auth.Error;
            var existing = await db.SubscriptionCancellations.FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId && x.SettlementKind == Kind, token);
            if (existing?.ResultMetadataJson is { } stored)
            {
                if (removeMember) await RevokeAsync(subscription, token);
                await db.SaveChangesAsync(token);
                return Render(JsonSerializer.Deserialize<CustomerCancellationResultDto>(stored)!);
            }
            if (subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.CancellationPending)
                return Error.Conflict(ReviewCode); // Legacy cancellation must be reconciled, never paid again.
            var calculated = await CalculateAsync(subscription, forceImmediate, token);
            if (calculated.IsFailure) return calculated.Error;
            var details = calculated.Value;
            var quote = details.Quote;
            if (!quote.RefundToCashWallet && quote.NetRefundAmount > 0)
                return Error.Conflict("family.provider_refund_required");
            var settlement = existing ?? new SubscriptionCancellation { SubscriptionId = subscriptionId, CustomerUserId = subscription.CustomerUserId,
                SettlementKind = Kind, FamilyGroupId = groupId, RequestedAtUtc = clock.UtcNow };
            settlement.ActorUserId = actor; settlement.BatchId = quote.BatchId; settlement.BatchLineId = details.BatchLineId;
            settlement.PayerCustomerUserId = quote.PayerCustomerUserId; settlement.CountedDays = quote.DeliveredDays + quote.LockedKitchenDays;
            settlement.UnusedDays = quote.RefundableDays;
            if (existing is null) db.SubscriptionCancellations.Add(settlement);
            var dayIds = details.Days.Select(x => x.Id).ToList();
            var capacityKeys = details.Days.Where(x => x.RestaurantId.HasValue).Select(x => (x.RestaurantId!.Value, x.ServiceDate));
            await locks.LockRestaurantCapacityAsync(capacityKeys, token);
            var beneficiaries = new List<FamilyRefundBeneficiaryDto>();
            foreach (var beneficiary in details.Beneficiaries.OrderBy(x => x.PayerCustomerUserId))
            {
                if (beneficiary.RefundAmount <= 0) { beneficiaries.Add(beneficiary); continue; }
                await locks.LockCustomerWalletAsync(beneficiary.PayerCustomerUserId, token);
                var wallet = await db.CustomerCashWallets.FirstOrDefaultAsync(x => x.CustomerUserId == beneficiary.PayerCustomerUserId, token);
                if (wallet is not null && (!wallet.IsActive || wallet.Balance.CurrencyCode != beneficiary.CurrencyCode))
                    return Error.Conflict(ReviewCode);
                if (wallet is null)
                {
                    wallet = new CustomerCashWallet { CustomerUserId = beneficiary.PayerCustomerUserId,
                        Balance = new Money(0m, beneficiary.CurrencyCode), IsActive = true };
                    db.CustomerCashWallets.Add(wallet);
                }
                wallet.Balance = wallet.Balance.Add(new Money(beneficiary.RefundAmount, beneficiary.CurrencyCode));
                var transaction = new WalletTransaction { WalletId = wallet.Id, WalletType = WalletType.Cash,
                    CustomerUserId = beneficiary.PayerCustomerUserId, Type = WalletTransactionType.Refund,
                    Amount = new Money(beneficiary.RefundAmount, beneficiary.CurrencyCode), BalanceAfter = wallet.Balance,
                    ReferenceType = "FamilyCancellationRefund", ReferenceId = settlement.Id, OccurredAtUtc = clock.UtcNow };
                db.WalletTransactions.Add(transaction);
                beneficiaries.Add(beneficiary with { WalletTransactionId = transaction.Id });
            }
            foreach (var day in details.Days) { day.Status = SubscriptionDayStatus.Cancelled; day.ModifiedAtUtc = clock.UtcNow; }
            var cancelledDates = details.Days.Select(x => x.ServiceDate).ToList();
            var holds = await db.CapacityHolds.Where(x => x.SubscriptionId == subscriptionId
                && x.Status != CapacityHoldStatus.Released && x.Status != CapacityHoldStatus.Expired
                && (quote.LockedKitchenDays == 0 && details.Days.Count == 0 || cancelledDates.Contains(x.ServiceDate))).ToListAsync(token);
            foreach (var hold in holds) { hold.Status = CapacityHoldStatus.Released; hold.ReleasedAtUtc = clock.UtcNow; }
            db.DailyMealSelections.RemoveRange(await db.DailyMealSelections.IgnoreQueryFilters().Where(x => dayIds.Contains(x.SubscriptionDayId)).ToListAsync(token));
            db.DailyRestaurantSelections.RemoveRange(await db.DailyRestaurantSelections.Where(x => dayIds.Contains(x.SubscriptionDayId)).ToListAsync(token));
            var previous = subscription.Status;
            // A verified future payment can temporarily have no materialized service days while
            // fulfillment is retryable. Settling the refundable amount must not turn that
            // still-funded preparation state into a terminal cancellation; the next fulfillment
            // attempt needs the Paid state and its immutable payment evidence.
            var paidPreparingWithoutDays = details.Days.Count == 0
                && subscription.Status == SubscriptionStatus.Paid
                && subscription.StartDate > BusinessCalendarDate.KuwaitToday(clock.UtcNow)
                && !subscription.FulfillmentCompletedAtUtc.HasValue;
            subscription.Status = paidPreparingWithoutDays
                ? SubscriptionStatus.Paid
                : quote.LockedKitchenDays > 0 ? SubscriptionStatus.CancellationPending : SubscriptionStatus.Cancelled;
            subscription.LastOperationAtUtc = clock.UtcNow;
            settlement.Status = "Settled"; settlement.CompletedAtUtc = clock.UtcNow;
            settlement.ContributionRefundsJson = JsonSerializer.Serialize(beneficiaries);
            db.RefundCalculations.Add(new RefundCalculation { CancellationId = settlement.Id,
                RefundEligiblePaid = new Money(beneficiaries.Sum(x => x.ConfirmedPaidAmount), quote.CurrencyCode),
                UnusedCashValue = new Money(quote.RefundAmount, quote.CurrencyCode), CancellationFee = new Money(quote.CancellationFeeAmount, quote.CurrencyCode),
                FinalCashRefund = new Money(quote.NetRefundAmount, quote.CurrencyCode), CalculatedAtUtc = clock.UtcNow,
                SnapshotJson = JsonSerializer.Serialize(new { quote.BatchId, details.BatchLineId, beneficiaries }) });
            db.SubscriptionOperations.Add(new SubscriptionOperation { SubscriptionId = subscriptionId, OperationType = "FAMILY_CANCELLATION_SETTLED",
                PreviousStatus = previous, NewStatus = subscription.Status, Reason = reason?.Trim(), PerformedByUserId = actor,
                PerformedByUserType = actorType, TimestampUtc = clock.UtcNow, MetadataJson = JsonSerializer.Serialize(new { settlement.Id, beneficiaries }) });
            if (removeMember) await RevokeAsync(subscription, token);
            var response = new CustomerCancellationResultDto(settlement.Id, subscriptionId, subscription.Status.ToString(), "",
                quote.TotalServiceDays, quote.DeliveredDays, quote.LockedKitchenDays, quote.RefundableDays, quote.DailyBoxPrice,
                quote.RefundAmount, quote.CurrencyCode, quote.EffectiveCancellationDate, "", clock.UtcNow,
                quote.CancellationFeeAmount, quote.NetRefundAmount, beneficiaries.Any(x => x.WalletTransactionId.HasValue),
                MessageCode: "family.cancellation_settled", PayerCustomerUserId: quote.PayerCustomerUserId,
                SettlementId: settlement.Id, BatchId: quote.BatchId, Beneficiaries: beneficiaries);
            // Persist semantic fields and numbers, never cached translated sentences.
            settlement.ResultMetadataJson = JsonSerializer.Serialize(response);
            await db.SaveChangesAsync(token);
            return Render(response);
        }, ct);
        if (result.IsFailure && result.Error.Code == ReviewCode && !transactions.HasActiveTransaction)
            await PersistReviewAsync(subscriptionId, groupId, actor, ct);
        return result;
    }

    private async Task<Result<FamilySubscriptionAccess>> AuthorizeAsync(Subscription subscription, Guid actor, UserType actorType, CancellationToken ct)
        => actorType == UserType.Admin ? new FamilySubscriptionAccess(false, subscription.FamilyMemberId)
            : await access.AuthorizeAsync(subscription, actor, FamilyAction.CancelSubscription, ct);

    private async Task RevokeAsync(Subscription subscription, CancellationToken ct)
    {
        var memberAccess = await db.FamilyMemberAccesses.FirstOrDefaultAsync(x => x.FamilyMemberId == subscription.FamilyMemberId, ct);
        if (memberAccess is null) { memberAccess = new FamilyMemberAccess { FamilyMemberId = subscription.FamilyMemberId!.Value }; db.FamilyMemberAccesses.Add(memberAccess); }
        memberAccess.RemovedAtUtc ??= clock.UtcNow; memberAccess.IsPaused = true; memberAccess.PausedAtUtc ??= clock.UtcNow;
    }

    private async Task PersistReviewAsync(Guid subscriptionId, Guid groupId, Guid actor, CancellationToken ct)
        => await transactions.ExecuteAsync(async token => {
            await locks.LockFamilyGroupAsync(groupId, token); await locks.LockSubscriptionAsync(subscriptionId, token);
            var record = await db.SubscriptionCancellations.FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId && x.SettlementKind == Kind, token);
            if (record is null) {
                var owner = await db.Subscriptions.Where(x => x.Id == subscriptionId).Select(x => x.CustomerUserId).SingleAsync(token);
                db.SubscriptionCancellations.Add(new SubscriptionCancellation { SubscriptionId = subscriptionId, CustomerUserId = owner,
                    FamilyGroupId = groupId, ActorUserId = actor, SettlementKind = Kind, Status = "ReviewRequired", RequestedAtUtc = clock.UtcNow });
            }
            await db.SaveChangesAsync(token);
        }, ct);

    private async Task<Result<Computation>> CalculateAsync(Subscription subscription, bool forceImmediate, CancellationToken ct)
    {
        var days = await db.SubscriptionDays.Where(x => x.SubscriptionId == subscription.Id).OrderBy(x => x.ServiceDate).ToListAsync(ct);
        var freezeIds = await db.SubscriptionFreezeRequests.AsNoTracking()
            .Where(x => x.SubscriptionId == subscription.Id && x.Status == VerificationStatus.Approved)
            .Select(x => x.Id).ToListAsync(ct);
        var freezeKeys = freezeIds.Select(x => FreezeAppliedOutcome.Prefix + x).ToList();
        var freezeRows = freezeKeys.Count == 0 ? [] : await db.SystemSettings.AsNoTracking().Where(x => freezeKeys.Contains(x.Key))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Key).Select(x => new { x.Key, x.ValueJson }).ToListAsync(ct);
        var hasMissingFreezeOutcome = freezeRows.Select(x => x.Key).Distinct().Count() != freezeIds.Count;
        var freezeOutcomes = new List<FreezeAppliedOutcome>();
        foreach (var outcomeRow in freezeRows)
        {
            FreezeAppliedOutcome? outcome;
            try { outcome = JsonSerializer.Deserialize<FreezeAppliedOutcome>(outcomeRow.ValueJson); }
            catch (JsonException) { return Error.Conflict(ReviewCode); }
            if (outcome is null || outcome.FrozenDates is null || outcome.ReplacementDates is null
                || outcome.FrozenDates.Distinct().Count() != outcome.FrozenDates.Count
                || outcome.ReplacementDates.Distinct().Count() != outcome.ReplacementDates.Count
                || outcome.ReplacementDates.Count > 0 && outcome.FrozenDates.Count != outcome.ReplacementDates.Count)
                return Error.Conflict(ReviewCode);
            freezeOutcomes.Add(outcome);
        }
        var compensatedOriginals = freezeOutcomes.Where(x => x.ReplacementDates.Count > 0)
            .SelectMany(x => x.FrozenDates).ToHashSet();
        var entitlementDays = days.Where(x => !compensatedOriginals.Contains(x.ServiceDate)).ToList();
        var policy = await OperationalPolicyRules.ReadAsync(db, ct);
        var printed = (await db.MealInvoices.AsNoTracking().Where(x => x.SubscriptionDayId.HasValue && days.Select(d => d.Id).Contains(x.SubscriptionDayId.Value))
            .Select(x => x.SubscriptionDayId!.Value).ToListAsync(ct)).ToHashSet();
        var today = DateOnly.FromDateTime(clock.UtcNow.AddHours(3));
        bool Mutable(SubscriptionDay d) => !compensatedOriginals.Contains(d.ServiceDate) && !printed.Contains(d.Id) && d.LockAtUtc is null
            && d.Status is SubscriptionDayStatus.Open or SubscriptionDayStatus.CustomerSelected or SubscriptionDayStatus.AutoSelected
                or SubscriptionDayStatus.Frozen or SubscriptionDayStatus.ReplacementRequired
            && (forceImmediate || clock.UtcNow < (d.SelectionDeadlineUtc ?? policies.CalculateSelectionDeadlineUtc(d.ServiceDate, policy)));
        var refundable = days.Where(Mutable).ToList();
        var protectedDays = entitlementDays.Where(x => x.Status != SubscriptionDayStatus.Delivered && x.Status != SubscriptionDayStatus.Cancelled && !Mutable(x)).ToList();
        var plannedDates = days.Count > 0 ? entitlementDays.Select(x => x.ServiceDate).Distinct().OrderBy(x => x).ToList()
            : PlanDates(subscription.StartDate, subscription.DurationDays, subscription.IncludeFriday);
        if (plannedDates.Count == 0) return Error.Conflict(ReviewCode);
        var contributions = new List<Contribution>(); Guid? batchId = null; Guid? lineId = null;
        var unpaidStatus = subscription.Status is SubscriptionStatus.Draft or SubscriptionStatus.PendingPayment or SubscriptionStatus.PaymentFailed;
        var allLines = await db.FamilyCheckoutBatchLines.AsNoTrackingWithIdentityResolution().Include(x => x.Batch).ThenInclude(x => x.Lines)
            .Where(x => x.SubscriptionId == subscription.Id).ToListAsync(ct);
        var lines = allLines.Where(x => x.Batch.Status == FamilyCheckoutBatchStatus.Paid).ToList();
        var hasVerifiedPaymentEvidence = false;
        if (unpaidStatus && lines.Count == 0 && allLines.Count > 0)
        {
            var batchIds = allLines.Select(x => x.BatchId).Distinct().ToList();
            var paymentIds = allLines.Select(x => x.Batch.GatewayPaymentId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            hasVerifiedPaymentEvidence = await db.FamilyCheckoutPaymentAttempts.AsNoTracking().AnyAsync(x => batchIds.Contains(x.BatchId)
                && x.VerifiedAtUtc.HasValue && x.ResultCode == "Paid", ct)
                || await db.PaymentWebhookReceipts.AsNoTracking().AnyAsync(x => paymentIds.Contains(x.PaymentId)
                    && x.VerifiedAtUtc.HasValue && (x.Status == "PaymentVerified" || x.Status == "FulfillmentCompleted" || x.Status == "FulfillmentRetryPending"), ct);
        }
        var unpaid = unpaidStatus && lines.Count == 0 && !hasVerifiedPaymentEvidence;
        if (!unpaid)
        {
            if (hasMissingFreezeOutcome) return Error.Conflict(ReviewCode);
            if (lines.Count != 1) return Error.Conflict(ReviewCode);
            var line = lines[0]; var batch = line.Batch;
            if (batch.PayerCustomerUserId == Guid.Empty || batch.FamilyGroupId != subscription.FamilyGroupId || line.FamilyMemberId != subscription.FamilyMemberId
                || !batch.PaidAtUtc.HasValue || line.NetPaidAmount < 0 || line.CurrencyCode != batch.CurrencyCode
                || batch.Lines.Sum(x => x.NetPaidAmount) != batch.FinalPrice
                || batch.Lines.Any(x => x.CurrencyCode != batch.CurrencyCode || x.NetPaidAmount != x.PriceBeforeFamilyDiscount - x.FamilyDiscountAllocated))
                return Error.Conflict(ReviewCode);
            var attempt = await db.FamilyCheckoutPaymentAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.BatchId == batch.Id
                && x.GatewayPaymentId == batch.GatewayPaymentId && x.VerifiedAtUtc.HasValue && x.ResultCode == "Paid", ct);
            var receipt = await db.PaymentWebhookReceipts.AsNoTracking().FirstOrDefaultAsync(x => x.PaymentId == batch.GatewayPaymentId
                && x.VerifiedAtUtc.HasValue && (x.Status == "PaymentVerified" || x.Status == "FulfillmentCompleted" || x.Status == "FulfillmentRetryPending"), ct);
            if (attempt is null || receipt is null || receipt.Amount != batch.FinalPrice || receipt.CurrencyCode != batch.CurrencyCode
                || receipt.InvoiceId != batch.GatewayInvoiceId || receipt.CustomerReference != batch.FamilyGroupId.ToString())
                return Error.Conflict(ReviewCode);
            if (days.Count == 0)
            {
                var hasFulfillmentOperation = await db.SubscriptionOperations.AsNoTracking().AnyAsync(x =>
                    x.SubscriptionId == subscription.Id && x.OperationType == "FULFILLMENT_COMPLETED", ct);
                var hasFulfilledReceipt = await db.PaymentWebhookReceipts.AsNoTracking().AnyAsync(x =>
                    x.PaymentId == batch.GatewayPaymentId
                    && (x.Status == "FulfillmentCompleted" || x.FulfillmentCompletedAtUtc.HasValue), ct);
                if (subscription.Status != SubscriptionStatus.Paid
                    || subscription.StartDate <= BusinessCalendarDate.KuwaitToday(clock.UtcNow)
                    || subscription.FulfillmentCompletedAtUtc.HasValue
                    || subscription.PaidAmount.Amount != line.NetPaidAmount
                    || subscription.PaidAmount.CurrencyCode != line.CurrencyCode
                    || attempt.ResultCode != "Paid"
                    || receipt.Status is not ("PaymentVerified" or "FulfillmentRetryPending")
                    || receipt.Status == "FulfillmentRetryPending"
                        && (receipt.FulfillmentAttemptCount != subscription.FulfillmentAttemptCount
                            || receipt.LastFulfillmentAttemptUtc != subscription.LastFulfillmentAttemptUtc
                            || !string.Equals(receipt.LastFulfillmentErrorCode, subscription.LastFulfillmentErrorCode, StringComparison.Ordinal))
                    || receipt.FulfillmentCompletedAtUtc.HasValue
                    || hasFulfillmentOperation || hasFulfilledReceipt)
                    return Error.Conflict(ReviewCode);
            }
            batchId = batch.Id; lineId = line.Id;
            contributions.Add(new Contribution(line.Id, batch.PayerCustomerUserId, line.NetPaidAmount, line.CurrencyCode, plannedDates));
            var upgrades = await db.SubscriptionTierUpgrades.AsNoTracking().Where(x => x.SubscriptionId == subscription.Id
                && x.Status == VerificationStatus.Approved).ToListAsync(ct);
            foreach (var upgrade in upgrades)
            {
                var zeroDueProven = upgrade.PaymentCreationState == "NotRequired"
                    && upgrade.PriceDifference.Amount <= 0m && upgrade.QuotedTotalDue.Amount <= 0m
                    && string.IsNullOrWhiteSpace(upgrade.PaymentId) && !upgrade.VerifiedPaidAmount.HasValue
                    && !upgrade.VerifiedPaidAtUtc.HasValue && string.IsNullOrWhiteSpace(upgrade.VerifiedInvoiceId)
                    && string.IsNullOrWhiteSpace(upgrade.VerifiedCurrencyCode);
                if (zeroDueProven) continue;

                var expectedUpgradeAmount = upgrade.QuotedTotalDue.Amount > 0m
                    ? upgrade.QuotedTotalDue.Amount
                    : upgrade.PriceDifference.Amount;
                if (expectedUpgradeAmount <= 0m
                    || (upgrade.QuotedTotalDue.Amount > 0m && upgrade.PriceDifference.Amount > 0m
                        && upgrade.QuotedTotalDue.Amount != upgrade.PriceDifference.Amount))
                    return Error.Conflict(ReviewCode);
                if (!upgrade.PayerCustomerUserId.HasValue || upgrade.PayerCustomerUserId == Guid.Empty || !upgrade.VerifiedPaidAtUtc.HasValue
                    || !upgrade.VerifiedPaidAmount.HasValue || upgrade.VerifiedPaidAmount <= 0
                    || upgrade.VerifiedPaidAmount != expectedUpgradeAmount || upgrade.VerifiedCurrencyCode != line.CurrencyCode
                    || upgrade.EffectiveServiceDatesJson is null) return Error.Conflict(ReviewCode);
                List<DateOnly>? dates;
                try { dates = JsonSerializer.Deserialize<List<DateOnly>>(upgrade.EffectiveServiceDatesJson); }
                catch (JsonException) { return Error.Conflict(ReviewCode); }
                if (dates is null || dates.Count == 0 || dates.Count != upgrade.QuotedRemainingDays
                    || dates.Distinct().Count() != dates.Count) return Error.Conflict(ReviewCode);
                var fundedDates = dates.ToHashSet();
                foreach (var outcome in freezeOutcomes.Where(x => x.ReplacementDates.Count > 0))
                {
                    for (var index = 0; index < outcome.FrozenDates.Count; index++)
                    {
                        var original = outcome.FrozenDates[index];
                        if (!fundedDates.Remove(original)) continue;
                        if (!fundedDates.Add(outcome.ReplacementDates[index])) return Error.Conflict(ReviewCode);
                    }
                }
                dates = fundedDates.OrderBy(x => x).ToList();
                if (dates.Count != upgrade.QuotedRemainingDays) return Error.Conflict(ReviewCode);
                var upgradeReceipt = await db.PaymentWebhookReceipts.AsNoTracking().FirstOrDefaultAsync(x => x.PaymentId == upgrade.PaymentId
                    && x.Status == "PaymentVerified" && x.VerifiedAtUtc.HasValue, ct);
                if (upgradeReceipt?.Amount != upgrade.VerifiedPaidAmount || upgradeReceipt.CurrencyCode != upgrade.VerifiedCurrencyCode
                    || upgradeReceipt.InvoiceId != upgrade.VerifiedInvoiceId) return Error.Conflict(ReviewCode);
                contributions.Add(new Contribution(upgrade.Id, upgrade.PayerCustomerUserId.Value, upgrade.VerifiedPaidAmount.Value, upgrade.VerifiedCurrencyCode!, dates));
            }
            // Existing old credits have no contribution allocation; require reconciliation rather than guess which payer they reduced.
            if (await db.WalletTransactions.AnyAsync(x => x.ReferenceType == "SubscriptionCancellation" && x.ReferenceId == subscription.Id, ct))
                return Error.Conflict(ReviewCode);
        }
        var currency = contributions.FirstOrDefault()?.Currency ?? subscription.PaidAmount.CurrencyCode;
        int digits; try { digits = FamilyPaymentAllocationService.CurrencyMinorDigits(currency); } catch (ArgumentException) { return Error.Conflict(ReviewCode); }
        decimal Round(decimal value) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
        var eligibleDates = days.Count == 0 ? plannedDates.ToHashSet() : refundable.Select(x => x.ServiceDate).ToHashSet();
        var portions = contributions.Select(x => new { Contribution = x, Gross = Math.Min(x.Paid, Round(x.Paid * x.Dates.Count(eligibleDates.Contains) / x.Dates.Count)) }).ToList();
        var gross = portions.Sum(x => x.Gross);
        var fee = forceImmediate ? 0m : policy.Cancellation.FeeType switch {
            CancellationFeeType.Percentage => Round(gross * policy.Cancellation.FeeValue / 100m),
            CancellationFeeType.FixedAmount => Round(Math.Min(gross, policy.Cancellation.FeeValue)), _ => 0m };
        fee = Math.Min(gross, fee);
        var grouped = portions.GroupBy(x => x.Contribution.Payer).OrderBy(x => x.Key).ToList();
        IReadOnlyDictionary<Guid, decimal> feeShares;
        try
        {
            feeShares = FamilyCancellationFeeAllocationService.Allocate(
                grouped.ToDictionary(x => x.Key, x => x.Sum(y => y.Gross)), fee, digits);
        }
        catch (ArgumentException) { return Error.Conflict(ReviewCode); }
        var beneficiaries = grouped.Select(payer => new FamilyRefundBeneficiaryDto(
            payer.Key,
            payer.Sum(x => x.Contribution.Paid),
            payer.Sum(x => x.Gross) - feeShares[payer.Key],
            currency)).ToList();
        if (feeShares.Values.Sum() != fee || beneficiaries.Sum(x => x.RefundAmount) != gross - fee
            || beneficiaries.Any(x => x.RefundAmount < 0m
                || x.RefundAmount > grouped.Single(p => p.Key == x.PayerCustomerUserId).Sum(y => y.Gross)
                || x.RefundAmount * (decimal)Math.Pow(10, digits) != decimal.Truncate(x.RefundAmount * (decimal)Math.Pow(10, digits))))
            return Error.Conflict(ReviewCode);
        var quote = new CustomerCancellationQuoteDto(subscription.Id, plannedDates.Count, days.Count(x => x.Status == SubscriptionDayStatus.Delivered),
            protectedDays.Count, eligibleDates.Count, contributions.Count == 0 ? 0m : contributions[0].Paid / plannedDates.Count,
            gross, currency, protectedDays.Count > 0 ? protectedDays.Max(x => x.ServiceDate).AddDays(1) : today,
            messages.GetArabicMessage("family.cancellation_quote"), fee, gross - fee, policy.Cancellation.RefundToCashWallet,
            messages.GetEnglishMessage("family.cancellation_quote"), "family.cancellation_quote",
            beneficiaries.Count == 1 ? beneficiaries[0].PayerCustomerUserId : null, batchId, beneficiaries);
        return new Computation(quote, refundable, beneficiaries, lineId);
    }

    internal static List<DateOnly> PlanDates(DateOnly start, int count, bool includeFriday)
    {
        var dates = new List<DateOnly>();
        for (var date = start; dates.Count < count; date = date.AddDays(1))
            if (includeFriday || date.DayOfWeek != DayOfWeek.Friday) dates.Add(date);
        return dates;
    }

    private CustomerCancellationResultDto Render(CustomerCancellationResultDto result) => result with {
        StatusNameAr = messages.GetArabicMessage(result.Status == "Cancelled" ? "family.subscription_cancelled" : "family.subscription_cancellation_pending"),
        MessageAr = messages.GetArabicMessage(result.MessageCode!), MessageEn = messages.GetEnglishMessage(result.MessageCode!) };
}
