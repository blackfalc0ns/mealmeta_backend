using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using MealMate.Application.Features.FamilyManager;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Realtime;

public sealed class SignalRCustomerDeliveryChangeNotifier(
    IAppDbContext db,
    IFamilySubscriptionAccess familyAccess,
    IHubContext<CustomerDeliveryHub> hub)
{
    public async Task NotifyAsync(CustomerDeliveryChangedV1 change, CancellationToken cancellationToken)
    {
        var subscriptionId = await db.SubscriptionDays.AsNoTracking()
            .Where(day => day.Id == change.SubscriptionDayId)
            .Select(day => (Guid?)day.SubscriptionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (subscriptionId is null)
            return;

        var subscription = await db.Subscriptions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == subscriptionId.Value, cancellationToken);
        if (subscription is null)
            return;

        var candidateRecipients = new HashSet<Guid> { subscription.CustomerUserId };
        if (subscription.FamilyGroupId is Guid familyGroupId)
        {
            var managerUserId = await db.FamilyGroups.AsNoTracking()
                .Where(group => group.Id == familyGroupId)
                .Select(group => (Guid?)group.ManagerCustomerUserId)
                .SingleOrDefaultAsync(cancellationToken);
            if (managerUserId.HasValue)
                candidateRecipients.Add(managerUserId.Value);

            var linkedUserIds = await (
                from access in db.FamilyMemberAccesses.AsNoTracking()
                join member in db.FamilyMembers.IgnoreQueryFilters().AsNoTracking()
                    on access.FamilyMemberId equals member.Id
                where member.FamilyGroupId == familyGroupId
                      && !member.IsDeleted
                      && access.RemovedAtUtc == null
                      && access.LinkedCustomerUserId.HasValue
                select access.LinkedCustomerUserId.GetValueOrDefault())
                .ToListAsync(cancellationToken);
            candidateRecipients.UnionWith(linkedUserIds);
        }

        var activeRecipients = await db.GetActiveAccountUserIdsAsync(candidateRecipients, cancellationToken);
        foreach (var recipientUserId in activeRecipients)
        {
            var access = await familyAccess.AuthorizeAsync(
                subscription,
                recipientUserId,
                FamilyAction.ViewCalendar,
                cancellationToken);
            if (access.IsSuccess)
            {
                await hub.Clients.Group(CustomerDeliveryHub.GroupForUser(recipientUserId))
                    .SendAsync(CustomerDeliveryHub.EventCustomerDeliveryChanged, change, cancellationToken);
            }
        }
    }
}
