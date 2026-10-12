using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyManager;

public sealed record FamilyServiceSubscriptionProjection(Guid? CurrentId, Subscription? Current,
    Guid? UpcomingId, DateOnly? UpcomingStartDate);

/// <summary>Resolves funded service from persisted dates and days; the group payment anchor is financial history.</summary>
public sealed class FamilyServiceSubscriptionResolver(IAppDbContext db)
{
    public async Task<Dictionary<Guid, FamilyServiceSubscriptionProjection>> ResolveAsync(
        IReadOnlyCollection<Guid> memberIds, IReadOnlyDictionary<Guid, Guid?> pointers, DateOnly today,
        CancellationToken ct = default)
    {
        if (memberIds.Count == 0) return [];
        var memberOwners = await db.FamilyMembers.AsNoTracking().Where(x => memberIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x => new { x.Id, x.FamilyGroupId }).ToDictionaryAsync(x => x.Id, x => x.FamilyGroupId, ct);
        var liveStatuses = new[] { SubscriptionStatus.Paid, SubscriptionStatus.Active, SubscriptionStatus.Scheduled,
            SubscriptionStatus.Frozen, SubscriptionStatus.SuspendedForReview, SubscriptionStatus.CancellationPending };
        var serviceDays = await db.SubscriptionDays.AsNoTracking()
            .Where(day => day.ServiceDate >= today && day.Status != SubscriptionDayStatus.Cancelled
                && db.Subscriptions.Any(subscription => subscription.Id == day.SubscriptionId
                    && subscription.FamilyMemberId.HasValue && memberIds.Contains(subscription.FamilyMemberId.Value)
                    && liveStatuses.Contains(subscription.Status)))
            .OrderBy(day => day.ServiceDate)
            .Select(day => new { day.SubscriptionId, day.ServiceDate, day.Status }).ToListAsync(ct);
        var pointerIds = pointers.Values.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var subscriptionIds = serviceDays.Select(x => x.SubscriptionId).Concat(pointerIds).Distinct().ToArray();
        var subscriptions = await db.Subscriptions.AsNoTracking().Include(x => x.Program)
            .Where(x => subscriptionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var paidBatchSubscriptionIds = await db.FamilyCheckoutBatchLines.AsNoTracking()
            .Where(x => subscriptionIds.Contains(x.SubscriptionId ?? Guid.Empty) && x.Batch.Status == FamilyCheckoutBatchStatus.Paid)
            .Select(x => x.SubscriptionId!.Value).Distinct().ToListAsync(ct);
        var fundedIds = subscriptions.Values.Where(x => x.PaymentVerifiedAtUtc.HasValue || paidBatchSubscriptionIds.Contains(x.Id))
            .Select(x => x.Id).ToHashSet();
        var result = new Dictionary<Guid, FamilyServiceSubscriptionProjection>();
        foreach (var memberId in memberIds)
        {
            var rows = serviceDays.Where(day => subscriptions.TryGetValue(day.SubscriptionId, out var sub)
                && sub.FamilyMemberId == memberId && memberOwners.TryGetValue(memberId, out var ownerGroupId)
                && sub.FamilyGroupId == ownerGroupId && fundedIds.Contains(day.SubscriptionId)).ToList();
            var currentRow = rows.Where(x => x.ServiceDate == today).OrderBy(x => x.SubscriptionId).FirstOrDefault();
            var pointer = pointers.GetValueOrDefault(memberId) is Guid pointerId && subscriptions.TryGetValue(pointerId, out var found)
                && memberOwners.TryGetValue(memberId, out var pointerOwnerGroupId)
                && found.FamilyGroupId == pointerOwnerGroupId
                && (found.FamilyMemberId is null || found.FamilyMemberId == memberId)
                ? found : null;
            var intervalRow = rows.Where(x => subscriptions.TryGetValue(x.SubscriptionId, out var sub)
                    && sub.StartDate <= today && sub.EndDate >= today)
                .OrderBy(x => subscriptions[x.SubscriptionId].EndDate).ThenBy(x => x.SubscriptionId).FirstOrDefault();
            var pointerCurrent = pointer is not null && pointer.StartDate <= today && pointer.EndDate >= today
                && liveStatuses.Contains(pointer.Status) && fundedIds.Contains(pointer.Id);
            var currentId = currentRow?.SubscriptionId ?? intervalRow?.SubscriptionId ?? (pointerCurrent ? pointer!.Id : (Guid?)null);
            var upcomingRow = rows.Where(x => x.ServiceDate > today && x.SubscriptionId != currentId)
                .OrderBy(x => x.ServiceDate).ThenBy(x => x.SubscriptionId).FirstOrDefault();
            var pointerUpcoming = pointer is not null && pointer.Id != currentId && pointer.StartDate > today
                && liveStatuses.Contains(pointer.Status) && fundedIds.Contains(pointer.Id);
            result[memberId] = new FamilyServiceSubscriptionProjection(currentId,
                currentId is Guid id && subscriptions.TryGetValue(id, out var current) ? current : null,
                upcomingRow?.SubscriptionId ?? (pointerUpcoming ? pointer!.Id : null),
                upcomingRow?.ServiceDate ?? (pointerUpcoming ? pointer!.StartDate : null));
        }
        return result;
    }
}
