using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyCheckout;
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

public sealed class CustomerIndividualSubscriptionGate(
    IAppDbContext db,
    IDateTimeProvider? clock = null) : ICustomerIndividualSubscriptionGate
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
    /// Returns true when the customer is the manager of a family group that has live blocking state:
    /// - Active or Frozen: has at least one subscription in CurrentIndividualSubscriptionStatuses.Blocking.
    /// - CheckoutPending: has a pending subscription with an unexpired payment hold.
    /// - Draft: has an unexpired draft according to FamilyDraftExpiry.
    /// Archived, completed, cancelled, or expired groups/subscriptions do not block individual subscriptions.
    /// </summary>
    public async Task<bool> HasBlockingFamilyGroupAsync(Guid customerUserId, CancellationToken ct = default)
    {
        if (customerUserId == Guid.Empty) return false;

        var groups = await db.FamilyGroups.AsNoTracking()
            .Where(x => x.ManagerCustomerUserId == customerUserId
                        && (x.Status == FamilyGroupStatus.Draft
                            || x.Status == FamilyGroupStatus.CheckoutPending
                            || x.Status == FamilyGroupStatus.Active
                            || x.Status == FamilyGroupStatus.Frozen))
            .ToListAsync(ct);

        if (groups.Count == 0) return false;

        var now = clock?.UtcNow ?? DateTime.UtcNow;
        var groupIds = groups.Select(g => g.Id).ToList();

        var candidateSubscriptions = await db.Subscriptions.AsNoTracking()
            .Where(s => s.FamilyGroupId != null
                        && groupIds.Contains(s.FamilyGroupId.Value)
                        && (CurrentIndividualSubscriptionStatuses.Blocking.Contains(s.Status)
                            || s.Status == SubscriptionStatus.PendingPayment))
            .Select(s => new { s.Id, s.FamilyGroupId, s.Status })
            .ToListAsync(ct);

        var pendingSubscriptionIds = candidateSubscriptions
            .Where(s => s.Status == SubscriptionStatus.PendingPayment)
            .Select(s => s.Id)
            .ToList();
        var subscriptionsWithLivePendingHolds = pendingSubscriptionIds.Count == 0
            ? new HashSet<Guid>()
            : (await db.CapacityHolds.AsNoTracking()
                .Where(h => pendingSubscriptionIds.Contains(h.SubscriptionId)
                            && h.Status == CapacityHoldStatus.PendingPayment
                            && h.ExpiresAtUtc > now)
                .Select(h => h.SubscriptionId)
                .Distinct()
                .ToListAsync(ct))
                .ToHashSet();

        foreach (var group in groups)
        {
            if (group.Status == FamilyGroupStatus.Active || group.Status == FamilyGroupStatus.Frozen)
            {
                var hasBlockingSub = candidateSubscriptions.Any(s =>
                    s.FamilyGroupId == group.Id && CurrentIndividualSubscriptionStatuses.IsBlocking(s.Status));
                if (hasBlockingSub)
                    return true;
            }
            else if (group.Status == FamilyGroupStatus.CheckoutPending)
            {
                var hasLivePendingPayment = candidateSubscriptions.Any(s =>
                    s.FamilyGroupId == group.Id
                    && s.Status == SubscriptionStatus.PendingPayment
                    && subscriptionsWithLivePendingHolds.Contains(s.Id));
                if (hasLivePendingPayment)
                    return true;
            }
            else if (group.Status == FamilyGroupStatus.Draft)
            {
                if (!FamilyDraftExpiry.IsExpired(group, now))
                    return true;
            }
        }

        return false;
    }

    public async Task<bool> HasBlockingAsync(Guid customerUserId, CancellationToken ct = default)
        => await FindBlockingAsync(customerUserId, ct) is not null
           || await HasBlockingFamilyGroupAsync(customerUserId, ct);
}
