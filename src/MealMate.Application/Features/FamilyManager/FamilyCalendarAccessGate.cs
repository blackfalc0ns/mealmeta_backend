using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyManager;

public sealed record FamilyCalendarAccess(
    bool IsManager,
    Guid? FamilyMemberId,
    bool CanEditDailyMeals);

public interface IFamilyCalendarAccessGate
{
    /// <summary>
    /// Authorizes the current customer for a subscription calendar.
    /// Individual: owner only. Family: manager or linked member (writes need EditDailyMeals).
    /// </summary>
    Task<Result<FamilyCalendarAccess>> AuthorizeAsync(
        Subscription subscription,
        bool requireWrite,
        CancellationToken cancellationToken);
}

public sealed class FamilyCalendarAccessGate : IFamilyCalendarAccessGate
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public FamilyCalendarAccessGate(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<FamilyCalendarAccess>> AuthorizeAsync(
        Subscription subscription,
        bool requireWrite,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is null)
            return Error.Forbidden(MessageCodes.Auth.Forbidden);

        if (subscription.FamilyGroupId is null)
        {
            if (subscription.CustomerUserId != _currentUser.UserId)
                return Error.NotFound(MessageCodes.Common.NotFound);
            return Result.Success(new FamilyCalendarAccess(false, null, true));
        }

        var group = await _db.FamilyGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == subscription.FamilyGroupId.Value, cancellationToken);
        if (group is null || group.Status != FamilyGroupStatus.Active)
            return Error.Conflict(MessageCodes.Family.GroupNotActive);

        var member = await _db.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.FamilyGroupId == group.Id && x.MemberSubscriptionId == subscription.Id,
                cancellationToken);
        if (member is null)
            return Error.NotFound(MessageCodes.Family.MemberNotFound);

        var access = await _db.FamilyMemberAccesses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, cancellationToken);
        if (access?.RemovedAtUtc is not null)
            return Error.NotFound(MessageCodes.Family.MemberNotFound);

        var isManager = group.ManagerCustomerUserId == _currentUser.UserId;
        if (isManager)
        {
            if (requireWrite && access is { IsPaused: true })
                return Error.Conflict(MessageCodes.Family.MemberPaused);
            return Result.Success(new FamilyCalendarAccess(true, member.Id, true));
        }

        if (access is null || access.LinkedCustomerUserId != _currentUser.UserId)
            return Error.NotFound(MessageCodes.Common.NotFound);

        if (!requireWrite)
            return Result.Success(new FamilyCalendarAccess(false, member.Id, false));

        if (access.IsPaused)
            return Error.Conflict(MessageCodes.Family.MemberPaused);

        var permissions = await _db.FamilyMemberPermissions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, cancellationToken);
        if (permissions is null || !permissions.EditDailyMeals)
            return Error.Forbidden(MessageCodes.Family.PermissionDenied);

        return Result.Success(new FamilyCalendarAccess(false, member.Id, true));
    }
}
