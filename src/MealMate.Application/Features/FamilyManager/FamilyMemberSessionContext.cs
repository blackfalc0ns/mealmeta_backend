using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyManager;

public sealed record FamilyMemberSessionContextDto(
    Guid GroupId,
    bool IsManager,
    Guid? MemberId,
    Guid? MemberSubscriptionId,
    IReadOnlyList<FamilyCapabilityDto> Capabilities,
    bool IsDetachedFromFamilyManagement = false,
    FamilyMemberSetupStatus? SetupStatus = null,
    bool HasSeparateAccount = false,
    bool IsPaused = false,
    bool RequiresPasswordSetup = false,
    string? AccessStatus = null);

public sealed record GetMyFamilySessionContextQuery : IQuery<IReadOnlyList<FamilyMemberSessionContextDto>>;

/// <summary>Entry context for the authenticated customer, with decisions from the enforcement policy.</summary>
public sealed class GetMyFamilySessionContextHandler(IAppDbContext db, ICurrentUser currentUser, IFamilyActionPolicy policy, IDateTimeProvider clock)
    : IQueryHandler<GetMyFamilySessionContextQuery, IReadOnlyList<FamilyMemberSessionContextDto>>
{
    public async Task<Result<IReadOnlyList<FamilyMemberSessionContextDto>>> Handle(
        GetMyFamilySessionContextQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserType != UserType.Customer || currentUser.UserId is not Guid actorId)
            return Error.Forbidden(MessageCodes.Auth.Forbidden);

        var managed = await db.FamilyGroups.AsNoTracking()
            .Where(x => x.ManagerCustomerUserId == actorId && !x.IsDeleted && x.Status != FamilyGroupStatus.Cancelled)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        var memberships = await db.FamilyMemberAccesses.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.FamilyMember).ThenInclude(x => x.FamilyGroup)
            .Where(x => x.LinkedCustomerUserId == actorId && x.RemovedAtUtc == null && !x.IsDeleted
                && !x.FamilyMember.IsDeleted
                && ((!x.FamilyMember.FamilyGroup.IsDeleted && x.FamilyMember.FamilyGroup.Status != FamilyGroupStatus.Cancelled)
                    || x.DetachedFromFamilyManagement))
            .OrderBy(x => x.FamilyMember.FamilyGroupId).ThenBy(x => x.FamilyMemberId).ToListAsync(cancellationToken);
        var result = new List<FamilyMemberSessionContextDto>();
        foreach (var group in managed)
        {
            var manager = await db.FamilyMembers.AsNoTracking()
                .Where(x => x.FamilyGroupId == group.Id && x.Role == FamilyMemberRole.Manager && !x.IsDeleted)
                .Select(x => new { x.Id, x.SetupStatus }).FirstOrDefaultAsync(cancellationToken);
            result.Add(new(group.Id, true, manager?.Id, null, [], false, manager?.SetupStatus,
                AccessStatus: "Manager"));
        }
        foreach (var access in memberships.Where(x => managed.All(g => g.Id != x.FamilyMember.FamilyGroupId)))
        {
            var capabilities = new List<FamilyCapabilityDto>();
            var isUsable = !access.IsPaused && access.HasSeparateAccount && !access.RequiresPasswordSetup;
            foreach (var action in Enum.GetValues<FamilyAction>())
            {
                var decision = await policy.EvaluateAsync(actorId, access.FamilyMemberId, action, cancellationToken);
                capabilities.Add(new(action.ToString(), decision.Allowed, decision.ReasonCode, decision.RequiredPermission));
            }
            var member = access.FamilyMember;
            var canAccessSubscription = capabilities.Any(x => x.Allowed && x.Action != nameof(FamilyAction.ChangePassword));
            Guid? subscriptionId = null;
            if (isUsable && canAccessSubscription)
            {
                var projections = await new FamilyServiceSubscriptionResolver(db).ResolveAsync(
                    [member.Id], new Dictionary<Guid, Guid?> { [member.Id] = member.MemberSubscriptionId },
                    BusinessCalendarDate.KuwaitToday(clock.UtcNow), cancellationToken);
                var projection = projections.GetValueOrDefault(member.Id);
                subscriptionId = projection?.CurrentId ?? projection?.UpcomingId;
            }
            result.Add(new(member.FamilyGroupId, false, member.Id, subscriptionId, capabilities,
                access.DetachedFromFamilyManagement, member.SetupStatus, access.HasSeparateAccount,
                access.IsPaused, access.RequiresPasswordSetup,
                access.DetachedFromFamilyManagement ? "Detached" : access.RequiresPasswordSetup ? "SetupRequired"
                    : access.IsPaused ? "Paused"
                    : access.HasSeparateAccount ? "Enabled" : "Disabled"));
        }
        return Result.Success<IReadOnlyList<FamilyMemberSessionContextDto>>(result);
    }
}
