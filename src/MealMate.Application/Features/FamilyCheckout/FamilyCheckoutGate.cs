using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyCheckout;

public static class FamilyCheckoutGate
{
    public static (bool CanCheckout, string? BlockReason) Evaluate(
        FamilyGroup group,
        IReadOnlyList<FamilyMember> members,
        FamilyQuoteResult quote,
        IReadOnlySet<Guid>? payableMembersWithAllergies = null)
    {
        if (group.Status != FamilyGroupStatus.Draft && group.Status != FamilyGroupStatus.Active)
            return (false, "family.not_draft");

        var quotedIds = quote.Lines.Select(x => x.MemberId).ToHashSet();
        var payable = PayableMembers(members).Where(m => quotedIds.Contains(m.Id)).ToList();
        if (quote.Lines.Count == 0)
            return (false, "family.checkout_no_selection");

        if (quote.EffectiveFamilyPackageId is null
            || quote.EffectiveFamilyPackageMinMembers is null
            || quote.EffectiveFamilyPackageMaxMembers is null)
            return (false, "family.package_required");

        if (quote.ParticipantCount < quote.EffectiveFamilyPackageMinMembers.Value)
            return (false, "family.min_members");

        if (quote.ParticipantCount > quote.EffectiveFamilyPackageMaxMembers.Value)
            return (false, "family.max_members");

        if (quote.FinalPrice <= 0m)
            return (false, "family.invalid_amount");

        if (payableMembersWithAllergies is not null
            && payable.Any(m => !payableMembersWithAllergies.Contains(m.Id)))
            return (false, "family.allergies_required");

        return (true, null);
    }

    public static List<FamilyMember> PayableMembers(IReadOnlyList<FamilyMember> members)
        => members
            .Where(m => FamilyMemberPlanRules.HasPricablePlan(m) && m.MemberSubscriptionId is null)
            .ToList();

    public static async Task<HashSet<Guid>> ActiveAccessMemberIdsAsync(IAppDbContext db, FamilyGroup group, CancellationToken ct)
    {
        var ids = group.Members.Select(x => x.Id).ToHashSet();
        var removed = await db.FamilyMemberAccesses.AsNoTracking()
            .Where(x => ids.Contains(x.FamilyMemberId) && x.RemovedAtUtc != null)
            .Select(x => x.FamilyMemberId).ToListAsync(ct);
        ids.ExceptWith(removed);
        return ids;
    }
}

public static class FamilyCheckoutAllergyLookup
{
    public static async Task<HashSet<Guid>> CompletedMemberIdsAsync(
        IAppDbContext db,
        FamilyGroup group,
        IReadOnlyList<FamilyMember> payableMembers,
        CancellationToken cancellationToken)
    {
        if (payableMembers.Count == 0)
            return [];

        var memberIds = payableMembers.Select(m => m.Id).ToList();
        var accesses = await db.FamilyMemberAccesses.AsNoTracking()
            .Where(a => memberIds.Contains(a.FamilyMemberId) && a.RemovedAtUtc == null)
            .Select(a => new { a.FamilyMemberId, a.LinkedCustomerUserId })
            .ToListAsync(cancellationToken);

        var accessByMember = accesses
            .GroupBy(a => a.FamilyMemberId)
            .ToDictionary(g => g.Key, g => g.First());
        var pairs = new List<(Guid MemberId, Guid UserId)>();
        foreach (var member in payableMembers)
        {
            if (accessByMember.TryGetValue(member.Id, out var access) && access.LinkedCustomerUserId is Guid linked)
                pairs.Add((member.Id, linked));
            else if (member.Role == FamilyMemberRole.Manager)
                pairs.Add((member.Id, group.ManagerCustomerUserId));
        }

        var userIds = pairs.Select(p => p.UserId).Distinct().ToList();
        if (userIds.Count == 0)
            return [];

        var completedUsers = (await db.CustomerProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId) && p.AllergiesCompletedAtUtc != null)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken)).ToHashSet();

        return pairs.Where(p => completedUsers.Contains(p.UserId)).Select(p => p.MemberId).ToHashSet();
    }
}
