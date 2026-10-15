using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;

namespace MealMate.Application.Features.FamilyManager;

public sealed record FamilyCalendarAccess(bool IsManager, Guid? FamilyMemberId, bool CanEditDailyMeals);

public interface IFamilyCalendarAccessGate
{
    Task<Result<FamilyCalendarAccess>> AuthorizeAsync(Subscription subscription, bool requireWrite,
        CancellationToken cancellationToken);
    Task<Result<FamilyCalendarAccess>> AuthorizeAsync(Subscription subscription, FamilyAction action,
        CancellationToken cancellationToken);
}

public sealed class FamilyCalendarAccessGate : IFamilyCalendarAccessGate
{
    private readonly ICurrentUser _currentUser;
    private readonly IFamilySubscriptionAccess _access;

    public FamilyCalendarAccessGate(IAppDbContext db, ICurrentUser currentUser,
        IFamilySubscriptionAccess? access = null)
    {
        _currentUser = currentUser;
        _access = access ?? new FamilySubscriptionAccessService(db, new FamilyActionPolicy(db));
    }

    public Task<Result<FamilyCalendarAccess>> AuthorizeAsync(Subscription subscription, bool requireWrite,
        CancellationToken cancellationToken)
        => AuthorizeAsync(subscription, requireWrite ? FamilyAction.EditDailyMeals : FamilyAction.ViewCalendar,
            cancellationToken);

    public async Task<Result<FamilyCalendarAccess>> AuthorizeAsync(Subscription subscription, FamilyAction action,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserType == UserType.Admin)
            return new FamilyCalendarAccess(true, null, true);
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is not Guid actorId)
            return Error.Forbidden(MessageCodes.Auth.Forbidden);
        var authorization = await _access.AuthorizeAsync(subscription, actorId, action, cancellationToken);
        if (authorization.IsFailure) return authorization.Error;
        var edit = action == FamilyAction.EditDailyMeals || action == FamilyAction.EditMealsAndDays || action == FamilyAction.UnitExchange
            ? authorization
            : await _access.AuthorizeAsync(subscription, actorId, FamilyAction.EditDailyMeals, cancellationToken);
        var canEdit = edit.IsSuccess;
        if (!canEdit)
            canEdit = (await _access.AuthorizeAsync(subscription, actorId, FamilyAction.EditMealsAndDays, cancellationToken)).IsSuccess
                || (await _access.AuthorizeAsync(subscription, actorId, FamilyAction.UnitExchange, cancellationToken)).IsSuccess;
        return new FamilyCalendarAccess(authorization.Value.IsManager, authorization.Value.MemberId, canEdit);
    }
}
