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
}
