using MealMate.Domain.Enums;

namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Abstraction for database-level concurrency locks (row-level and transactional locks)
/// so that Application code does not depend directly on EF or database facades.
/// </summary>
public interface IApplicationLockManager
{
    Task LockSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
    Task LockFamilyBatchAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task LockSubscriptionDaysAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
    Task LockCustomerWalletAsync(Guid payerCustomerUserId, CancellationToken cancellationToken = default);
    Task LockRefundInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Locks the pickup box (TripStop and SubscriptionDay) inside the current transaction.
    /// </summary>
    Task LockPickupBoxAsync(Guid subscriptionDayId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks restaurant capacity for a given restaurant and service date inside the current transaction.
    /// </summary>
    Task LockRestaurantCapacityAsync(Guid restaurantId, DateOnly serviceDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Locks multiple restaurant capacity slots in deterministic, deadlock-free lexicographical order.
    /// </summary>
    Task LockRestaurantCapacityAsync(IEnumerable<(Guid RestaurantId, DateOnly ServiceDate)> keys, CancellationToken cancellationToken = default);

    /// <summary>Restoration stub (2026-10-06): serializes family-group checkout
    /// mutations; implemented with the same advisory-lock mechanism as the other
    /// family locks. The owning feature session should reconcile the shape.</summary>
    Task LockFamilyGroupAsync(Guid familyGroupId, CancellationToken cancellationToken = default);
    /// <summary>Serializes customer credential and family identity mutations. Acquire family locks first.</summary>
    Task LockCustomerIdentityAsync(Guid userId, CancellationToken cancellationToken = default);
    Task LockAdAuctionAsync(Guid auctionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task LockPromoCodeAsync(Guid promoCodeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task LockCampaignPaymentKeyAsync(Guid customerId, string key, CancellationToken cancellationToken = default);
    Task LockCampaignCheckoutSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task LockTripAsync(Guid tripId, CancellationToken cancellationToken = default);
    Task LockSupportHandoverAsync(Guid handoverId, CancellationToken cancellationToken = default);
    Task LockDriverAsync(Guid driverProfileId, CancellationToken cancellationToken = default);
    Task LockSupportHandoverKeyAsync(Guid userId, string idempotencyKey, CancellationToken cancellationToken = default);
    Task LockSupportCaseAsync(Guid caseId, CancellationToken cancellationToken = default);
    Task LockSupportEvidenceUploadAsync(Guid uploadFileRecordId, CancellationToken cancellationToken = default);
    Task LockSupportTimingPolicyCatalogAsync(CancellationToken cancellationToken = default);
    Task LockSupportComplaintAsync(Guid complaintId, CancellationToken cancellationToken = default);
    Task LockFinancialRuleScopeAsync(Guid legalEntityId, string ruleKey, RuleScopeType scopeType, Guid? scopeEntityId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    Task LockLedgerPeriodCatalogAsync(Guid legalEntityId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task LockLedgerPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task LockLedgerPostingIdentityAsync(Guid legalEntityId, string sourceType, Guid sourceId, Guid serviceDayKey,
        int effect, Guid adjustmentKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task LockRestaurantPricingBundleAsync(Guid bundleId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task LockPricingQuoteAsync(Guid quoteId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task LockAcquisitionBudgetAsync(Guid budgetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
