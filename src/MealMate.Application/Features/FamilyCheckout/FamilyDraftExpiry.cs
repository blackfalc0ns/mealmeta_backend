using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public static class FamilyDraftExpiry
{
    public const int TtlMinutes = 15;
    public const string SessionEndedMessageAr = "تم انتهاء جلسة الاشتراك";
    private static readonly DateTime LifetimeFloorUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly SubscriptionStatus[] LivePaymentStatuses =
    [
        SubscriptionStatus.PendingPayment,
        SubscriptionStatus.Paid,
        SubscriptionStatus.Scheduled,
        SubscriptionStatus.Active,
        SubscriptionStatus.Frozen,
        SubscriptionStatus.SuspendedForReview,
    ];

    public static DateTime StartedAtUtc(FamilyGroup group)
        => group.ModifiedAtUtc ?? group.CreatedAtUtc;

    public static DateTime ExpiresAtUtc(FamilyGroup group)
        => StartedAtUtc(group).AddMinutes(TtlMinutes);

    public static DateTime? LookbackUtc(DateTime now)
        => now < LifetimeFloorUtc ? null : now.AddMinutes(-TtlMinutes);

    public static bool IsExpired(FamilyGroup group, DateTime now)
    {
        var started = StartedAtUtc(group);
        return started >= LifetimeFloorUtc && started <= now.AddMinutes(-TtlMinutes);
    }

    public static void Touch(FamilyGroup group, DateTime now)
    {
        if (group.Status == FamilyGroupStatus.Draft)
            group.ModifiedAtUtc = now;
    }

    public static async Task<int> ExpireDueAsync(
        IAppDbContext db,
        DateTime now,
        CancellationToken cancellationToken,
        Guid? managerId = null, bool groupLockHeld = false)
    {
        // Readers without a family transaction must not race a freeze. The expiry
        // worker and draft handlers execute this under the same family row lock.
        if (!groupLockHeld && db is DbContext context && context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory") return 0;
        if (now < LifetimeFloorUtc) return 0;
        var cutoff = now.AddMinutes(-TtlMinutes);
        var batches = await db.FamilyCheckoutBatches
            .Where(x => x.Status == FamilyCheckoutBatchStatus.Draft && x.CreatedAtUtc <= cutoff
                && (!managerId.HasValue || x.PayerCustomerUserId == managerId.Value)).ToListAsync(cancellationToken);
        foreach (var batch in batches) { batch.Status = FamilyCheckoutBatchStatus.Expired; batch.Version++; }
        if (batches.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return batches.Count;
    }

    /// <summary>
    /// CheckoutPending must survive the payment page. Remove it only after
    /// holds/unpaid siblings are gone so invoices and subscriptions stay intact.
    /// </summary>
    public static async Task<int> RemoveOrphanCheckoutPendingAsync(
        IAppDbContext db,
        DateTime now,
        CancellationToken cancellationToken,
        Guid? managerId = null)
    {
        var query = db.FamilyGroups
            .Include(x => x.Members)
            .Where(x => x.Status == FamilyGroupStatus.CheckoutPending);
        if (managerId is Guid id)
            query = query.Where(x => x.ManagerCustomerUserId == id);

        var groups = await query.ToListAsync(cancellationToken);
        var removed = 0;
        foreach (var group in groups)
        {
            var hasLivePayment = await db.Subscriptions.AnyAsync(
                x => x.FamilyGroupId == group.Id && LivePaymentStatuses.Contains(x.Status),
                cancellationToken);
            if (hasLivePayment)
                continue;

            if (await db.FamilyCheckoutBatches.AnyAsync(x => x.FamilyGroupId == group.Id
                && (x.Status == FamilyCheckoutBatchStatus.PaymentPending || x.Status == FamilyCheckoutBatchStatus.ReviewRequired), cancellationToken)) continue;
            group.Status = group.PaidAtUtc.HasValue ? FamilyGroupStatus.Active : FamilyGroupStatus.Draft;
            removed++;
        }

        if (removed > 0)
            await db.SaveChangesAsync(cancellationToken);

        return removed;
    }

    public static async Task RemoveAsync(
        IAppDbContext db,
        FamilyGroup group,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var drafts = await db.FamilyCheckoutBatches.Where(x => x.FamilyGroupId == group.Id
            && x.Status == FamilyCheckoutBatchStatus.Draft).ToListAsync(cancellationToken);
        foreach (var batch in drafts) { batch.Status = FamilyCheckoutBatchStatus.Cancelled; batch.Version++; }
        Touch(group, now);
    }
}
