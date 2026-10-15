using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.FamilyManager;

/// <summary>Every family action the manager can grant or an actor can attempt.</summary>
public enum FamilyAction
{
    // Read-only visibility.
    ViewCalendar,
    ViewPlan,
    ViewInvoices,

    // Daily meals.
    SelectRestaurant,
    EditDailyMeals,
    ConfirmMeals,

    // Plan/program/address.
    EditMealsAndDays,
    ChangeProgram,
    EditAddress,
    ChangeDeliveryTime,
    UnitExchange,

    // Account & financial operations.
    ChangePassword,
    RequestFreeze,
    RequestRenewal,
    RequestUpgrade,
    CancelSubscription,
}

public sealed record FamilyActionDecision(bool Allowed, string? ReasonCode, string? RequiredPermission);

public interface IFamilyActionPolicy
{
    Task<FamilyActionDecision> EvaluateAsync(Guid actorUserId, Guid targetMemberId, FamilyAction action, CancellationToken cancellationToken = default);
}

/// <summary>
/// Single shared decision source for display and enforcement: the API rejects
/// with the same Allowed/ReasonCode the UI shows. The manager manages linked
/// members. Detached members retain read-only roster visibility for the manager;
/// mutations are denied. Managed members act through granted keys; detached
/// members have every supported own action and no sibling scope.
/// New keys default to deny for members; pausing/removing access cuts access
/// immediately on the next evaluation.
/// </summary>
public sealed class FamilyActionPolicy : IFamilyActionPolicy
{
    private readonly IAppDbContext _db;

    public FamilyActionPolicy(IAppDbContext db) => _db = db;

    public async Task<FamilyActionDecision> EvaluateAsync(
        Guid actorUserId,
        Guid targetMemberId,
        FamilyAction action,
        CancellationToken cancellationToken = default)
    {
        var member = await _db.FamilyMembers.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.FamilyGroup)
            .FirstOrDefaultAsync(x => x.Id == targetMemberId, cancellationToken);
        if (member is null || member.IsDeleted)
            return new(false, "family.member_not_found", null);

        var group = member.FamilyGroup;
        var targetAccess = await _db.FamilyMemberAccesses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, cancellationToken);
        var isDetachedOwner = targetAccess?.DetachedFromFamilyManagement == true
            && targetAccess.LinkedCustomerUserId == actorUserId && targetAccess.RemovedAtUtc == null;
        if ((group.IsDeleted || group.Status == FamilyGroupStatus.Cancelled) && !isDetachedOwner)
            return new(false, "family.not_active", null);

        var isManager = group.ManagerCustomerUserId == actorUserId;

        // R07: the actor is identified by their own access row INSIDE this group —
        // the same rule the calendar gate applies — so a sibling with family-scope
        // read grants gets consistent decisions, not a spurious not_member.
        FamilyMemberAccess? actorAccess = null;
        if (!isManager)
        {
            actorAccess = await _db.FamilyMemberAccesses.AsNoTracking()
                .Where(x => x.LinkedCustomerUserId == actorUserId
                         && x.RemovedAtUtc == null
                         && _db.FamilyMembers.IgnoreQueryFilters().Any(m => m.Id == x.FamilyMemberId
                             && m.FamilyGroupId == group.Id && !m.IsDeleted))
                .OrderByDescending(x => x.FamilyMemberId == member.Id)
                .ThenBy(x => x.DetachedFromFamilyManagement)
                .FirstOrDefaultAsync(cancellationToken);
            if (actorAccess is null)
                return new(false, "family.not_member", PermissionKey(action));
            if (action == FamilyAction.ChangePassword && actorAccess.FamilyMemberId == member.Id
                && !actorAccess.RequiresPasswordSetup)
                return new(true, null, PermissionKey(action));
            if (actorAccess.IsPaused)
                return new(false, "family.access_paused", PermissionKey(action));
            // R07/B08: a disabled independent account loses family data access —
            // capabilities must report the same denial the gate enforces.
            if (!actorAccess.HasSeparateAccount)
                return new(false, "family.access_disabled", PermissionKey(action));
            if (actorAccess.RequiresPasswordSetup)
                return new(false, "family.password_setup_required", PermissionKey(action));
        }

        if (!isManager && await _db.FamilyMemberAccesses.AsNoTracking()
            .AnyAsync(x => x.FamilyMemberId == member.Id && x.RemovedAtUtc != null, cancellationToken))
            return new(false, "family.member_not_found", PermissionKey(action));

        if (isManager && targetAccess?.DetachedFromFamilyManagement == true)
        {
            if (action is FamilyAction.ViewCalendar or FamilyAction.ViewPlan or FamilyAction.ViewInvoices)
                return new(true, null, PermissionKey(action));
            return new(false, "family.member_detached", PermissionKey(action));
        }

        if (action is FamilyAction.ChangeProgram or FamilyAction.ChangeDeliveryTime)
            return new(false, "family.action_not_available", PermissionKey(action));

        var isOwnRow = actorAccess is not null && actorAccess.FamilyMemberId == member.Id;

        // Family-scope visibility: a sibling reading another member's calendar/
        // plan/invoices needs the actor's matching family-scope grant.
        if (!isManager && !isOwnRow)
        {
            if (actorAccess!.DetachedFromFamilyManagement || targetAccess?.DetachedFromFamilyManagement == true)
                return new(false, "family.member_detached", PermissionKey(action));
            var actorPermissions = await _db.FamilyMemberPermissions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.FamilyMemberId == actorAccess!.FamilyMemberId, cancellationToken);
            var familyScopeGranted = action switch
            {
                FamilyAction.ViewCalendar => actorPermissions?.ViewFamilyCalendars == true,
                FamilyAction.ViewPlan => actorPermissions?.ViewFamilyPlans == true,
                FamilyAction.ViewInvoices => actorPermissions?.ViewFamilyInvoices == true,
                _ => false,
            };
            return familyScopeGranted
                ? new(true, null, PermissionKey(action))
                : new(false, "family.permission_required", PermissionKey(action));
        }

        if (isManager)
        {
            // Pausing or removing linked access does not block manager actions.
            // Detached targets were denied above except for read-only projections.
            return new(true, null, PermissionKey(action));
        }

        // Detachment turns the linked login into an independent customer for
        // every supported action on their own subscription. Older detached
        // rows may still have the pre-detach manager-grant values.
        if (isOwnRow && actorAccess!.DetachedFromFamilyManagement)
            return new(true, null, PermissionKey(action));

        // Credential ownership is independent of family data grants.
        if (isOwnRow && action == FamilyAction.ChangePassword)
            return new(true, null, PermissionKey(action));

        // Members always retain read access to their own plan. Invoice access,
        // calendar access and every write action remain separately permissioned.
        if (isOwnRow && action == FamilyAction.ViewPlan && member.Role == FamilyMemberRole.Member)
            return new(true, null, PermissionKey(action));

        // Linked member acting on their own row (pause/disable checked above for
        // the actor's own access row).
        var permissions = await _db.FamilyMemberPermissions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FamilyMemberId == member.Id, cancellationToken);
        if (permissions is null)
            return new(false, "family.permission_required", PermissionKey(action));

        // Legacy rows may have writes enabled without their required read grants.
        var calendarWrite = permissions.SelectRestaurant || permissions.EditDailyMeals || permissions.EditMealsAndDays
            || permissions.UnitExchange || permissions.ConfirmMeals || permissions.EditAddress;
        if (action == FamilyAction.ViewCalendar && calendarWrite)
            return new(true, null, PermissionKey(action));
        if (action == FamilyAction.ViewPlan && (permissions.ViewCalendar || calendarWrite
                || permissions.EditAddress || permissions.RequestFreeze || permissions.RequestRenewal
                || permissions.RequestUpgrade || permissions.CancelSubscription))
            return new(true, null, PermissionKey(action));

        var key = PermissionKey(action);
        if (key is not null && IsGranted(permissions, key))
            return new(true, null, key);
        return new(false, "family.permission_required", key);
    }

    private static string? PermissionKey(FamilyAction action) => action switch
    {
        FamilyAction.ViewCalendar => nameof(FamilyMemberPermissions.ViewCalendar),
        FamilyAction.ViewPlan => nameof(FamilyMemberPermissions.ViewProgram),
        FamilyAction.ViewInvoices => nameof(FamilyMemberPermissions.ViewInvoices),
        FamilyAction.SelectRestaurant => nameof(FamilyMemberPermissions.SelectRestaurant),
        FamilyAction.EditDailyMeals => nameof(FamilyMemberPermissions.EditDailyMeals),
        FamilyAction.ConfirmMeals => nameof(FamilyMemberPermissions.ConfirmMeals),
        FamilyAction.EditMealsAndDays => nameof(FamilyMemberPermissions.EditMealsAndDays),
        FamilyAction.ChangeProgram => nameof(FamilyMemberPermissions.ChangeProgram),
        FamilyAction.EditAddress => nameof(FamilyMemberPermissions.EditAddress),
        FamilyAction.ChangeDeliveryTime => nameof(FamilyMemberPermissions.ChangeDeliveryTime),
        FamilyAction.UnitExchange => nameof(FamilyMemberPermissions.UnitExchange),
        FamilyAction.ChangePassword => nameof(FamilyMemberPermissions.ChangePassword),
        FamilyAction.RequestFreeze => nameof(FamilyMemberPermissions.RequestFreeze),
        FamilyAction.RequestRenewal => nameof(FamilyMemberPermissions.RequestRenewal),
        FamilyAction.RequestUpgrade => nameof(FamilyMemberPermissions.RequestUpgrade),
        FamilyAction.CancelSubscription => nameof(FamilyMemberPermissions.CancelSubscription),
        _ => null,
    };

    private static bool IsGranted(FamilyMemberPermissions permissions, string key)
        => key switch
        {
            nameof(FamilyMemberPermissions.ViewCalendar) => permissions.ViewCalendar,
            nameof(FamilyMemberPermissions.ViewProgram) => permissions.ViewProgram,
            nameof(FamilyMemberPermissions.ViewInvoices) => permissions.ViewInvoices,
            nameof(FamilyMemberPermissions.SelectRestaurant) => permissions.SelectRestaurant,
            nameof(FamilyMemberPermissions.EditDailyMeals) => permissions.EditDailyMeals,
            nameof(FamilyMemberPermissions.ConfirmMeals) => permissions.ConfirmMeals,
            nameof(FamilyMemberPermissions.EditMealsAndDays) => permissions.EditMealsAndDays,
            nameof(FamilyMemberPermissions.ChangeProgram) => permissions.ChangeProgram,
            nameof(FamilyMemberPermissions.EditAddress) => permissions.EditAddress,
            nameof(FamilyMemberPermissions.ChangeDeliveryTime) => permissions.ChangeDeliveryTime,
            nameof(FamilyMemberPermissions.UnitExchange) => permissions.UnitExchange,
            nameof(FamilyMemberPermissions.ChangePassword) => permissions.ChangePassword,
            nameof(FamilyMemberPermissions.RequestFreeze) => permissions.RequestFreeze,
            nameof(FamilyMemberPermissions.RequestRenewal) => permissions.RequestRenewal,
            nameof(FamilyMemberPermissions.RequestUpgrade) => permissions.RequestUpgrade,
            nameof(FamilyMemberPermissions.CancelSubscription) => permissions.CancelSubscription,
            _ => false,
        };
}

/// <summary>
/// Returns the current actor's decision for every family action on one member:
/// the UI renders capabilities from exactly the same policy the API enforces.
/// </summary>
public sealed class GetMyFamilyCapabilitiesHandler(IAppDbContext db, ICurrentUser currentUser, IFamilyActionPolicy? sharedPolicy = null)
    : IQueryHandler<GetMyFamilyCapabilitiesQuery, IReadOnlyList<FamilyCapabilityDto>>
{
    private static readonly FamilyAction[] AllActions =
    [
        FamilyAction.ViewCalendar, FamilyAction.ViewPlan, FamilyAction.ViewInvoices,
        FamilyAction.SelectRestaurant, FamilyAction.EditDailyMeals, FamilyAction.ConfirmMeals,
        FamilyAction.EditMealsAndDays, FamilyAction.ChangeProgram, FamilyAction.EditAddress,
        FamilyAction.ChangeDeliveryTime, FamilyAction.UnitExchange, FamilyAction.ChangePassword,
        FamilyAction.RequestFreeze, FamilyAction.RequestRenewal, FamilyAction.RequestUpgrade,
        FamilyAction.CancelSubscription,
    ];

    public async Task<Result<IReadOnlyList<FamilyCapabilityDto>>> Handle(
        GetMyFamilyCapabilitiesQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserType != UserType.Customer || currentUser.UserId is not Guid actorId)
            return Error.Forbidden(MealMate.Application.Common.Localization.MessageCodes.Auth.Forbidden);

        var groupId = await db.FamilyMembers.AsNoTracking().Where(x => x.Id == request.MemberId)
            .Select(x => (Guid?)x.FamilyGroupId).FirstOrDefaultAsync(cancellationToken);
        if (groupId is null) return Error.NotFound("family.member_not_found");
        var isManager = await db.FamilyGroups.AsNoTracking()
            .AnyAsync(x => x.Id == groupId && x.ManagerCustomerUserId == actorId, cancellationToken);
        var hasAccess = isManager || await db.FamilyMemberAccesses.AsNoTracking()
            .AnyAsync(x => x.LinkedCustomerUserId == actorId && x.RemovedAtUtc == null
                && x.HasSeparateAccount && !x.IsPaused
                && db.FamilyMembers.Any(m => m.Id == x.FamilyMemberId && m.FamilyGroupId == groupId), cancellationToken);
        if (!hasAccess) return Error.Forbidden("family.not_member");
        var policy = sharedPolicy ?? new FamilyActionPolicy(db);
        var result = new List<FamilyCapabilityDto>(AllActions.Length);
        foreach (var action in AllActions)
        {
            var decision = await policy.EvaluateAsync(actorId, request.MemberId, action, cancellationToken);
            result.Add(new FamilyCapabilityDto(action.ToString(), decision.Allowed, decision.ReasonCode, decision.RequiredPermission));
        }
        return result;
    }
}
