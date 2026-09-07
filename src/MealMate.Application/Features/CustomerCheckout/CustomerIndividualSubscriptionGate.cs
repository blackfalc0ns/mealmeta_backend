using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record BlockingIndividualSubscription(
    Guid SubscriptionId,
    SubscriptionStatus Status,
    DateOnly? StartDate,
    DateOnly? EndDate);

public interface ICustomerIndividualSubscriptionGate
{
    Task<BlockingIndividualSubscription?> FindBlockingAsync(Guid customerUserId, CancellationToken ct = default);
    Task<bool> HasBlockingAsync(Guid customerUserId, CancellationToken ct = default);
    Task<bool> HasBlockingFamilyGroupAsync(Guid customerUserId, CancellationToken ct = default);
}

public static class CurrentIndividualSubscriptionStatuses
{
    public static readonly SubscriptionStatus[] Blocking =
    [
        SubscriptionStatus.Paid,
        SubscriptionStatus.Scheduled,
        SubscriptionStatus.Active,
        SubscriptionStatus.Frozen,
        SubscriptionStatus.SuspendedForReview,
    ];

    public static bool IsBlocking(SubscriptionStatus status) => Blocking.Contains(status);

    public static bool IsCurrentStatusName(string status) =>
        status is nameof(SubscriptionStatus.Paid)
            or nameof(SubscriptionStatus.Scheduled)
            or nameof(SubscriptionStatus.Active)
            or nameof(SubscriptionStatus.Frozen)
            or nameof(SubscriptionStatus.SuspendedForReview);
}

public sealed class CustomerIndividualSubscriptionGate(IAppDbContext db) : ICustomerIndividualSubscriptionGate
{
    public const string ExistingSubscriptionError = "checkout.existing_subscription";

    public async Task<BlockingIndividualSubscription?> FindBlockingAsync(Guid customerUserId, CancellationToken ct = default)
    {
        if (customerUserId == Guid.Empty) return null;
        return await db.Subscriptions.AsNoTracking()
            .Where(x => x.CustomerUserId == customerUserId
                        && x.FamilyGroupId == null
                        && CurrentIndividualSubscriptionStatuses.Blocking.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new BlockingIndividualSubscription(x.Id, x.Status, x.StartDate, x.EndDate))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Returns true when the customer is the manager of a family group that is in progress
    /// (Draft, CheckoutPending, Active or Frozen). Uses <see cref="ExistingSubscriptionError"/>
    /// so existing readiness/checkout callers stay compatible in Phase 1.
    /// </summary>
    public async Task<bool> HasBlockingFamilyGroupAsync(Guid customerUserId, CancellationToken ct = default)
    {
        if (customerUserId == Guid.Empty) return false;

        return await db.FamilyGroups.AsNoTracking()
            .AnyAsync(x => x.ManagerCustomerUserId == customerUserId
                        && (x.Status == FamilyGroupStatus.Draft
                            || x.Status == FamilyGroupStatus.CheckoutPending
                            || x.Status == FamilyGroupStatus.Active
                            || x.Status == FamilyGroupStatus.Frozen), ct);
    }

    public async Task<bool> HasBlockingAsync(Guid customerUserId, CancellationToken ct = default)
        => await FindBlockingAsync(customerUserId, ct) is not null
           || await HasBlockingFamilyGroupAsync(customerUserId, ct);
}
