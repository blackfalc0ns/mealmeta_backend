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
        Guid? managerId = null)
    {
        var query = db.FamilyGroups
            .Include(x => x.Members)
            .Where(x => x.Status == FamilyGroupStatus.Draft);
        if (managerId is Guid id)
            query = query.Where(x => x.ManagerCustomerUserId == id);

        var groups = await query.ToListAsync(cancellationToken);
        var expired = groups.Where(x => IsExpired(x, now)).ToList();
        foreach (var group in expired)
            await RemoveAsync(db, group, now, cancellationToken);

        if (expired.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return expired.Count;
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

            await RemoveAsync(db, group, now, cancellationToken);
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
        var linked = await db.Subscriptions
            .Where(x => x.FamilyGroupId == group.Id)
            .ToListAsync(cancellationToken);
        foreach (var sub in linked)
        {
            if (sub.Status is SubscriptionStatus.Draft
                or SubscriptionStatus.PendingPayment
                or SubscriptionStatus.PaymentFailed)
            {
                sub.Status = SubscriptionStatus.Cancelled;
                sub.LastOperationAtUtc = now;
            }

            sub.FamilyGroupId = null;
            sub.FamilyMemberId = null;
        }

        foreach (var member in group.Members.ToList())
        {
            member.MemberSubscriptionId = null;
            db.FamilyMembers.Remove(member);
        }

        db.FamilyGroups.Remove(group);
    }
}
