using MealMate.Domain.CalendarMeals;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed partial class SupportCaseService
{
    private async Task<ResolvedSupportResource> ResolveCustomerResourceAsync(
        SupportCaseChannel channel,
        Guid requesterId,
        string? resourceType,
        Guid? resourceId,
        CancellationToken ct)
    {
        if (resourceType is null && resourceId is null) return new(true, true, null, null);
        if (channel != SupportCaseChannel.Customer || resourceType is null || resourceId is null || resourceId == Guid.Empty)
            return new(false, false, null, null);

        if (resourceType == "SubscriptionDay")
        {
            var day = await _db.SubscriptionDays.AsNoTracking()
                .Where(x => x.Id == resourceId.Value)
                .Join(_db.Subscriptions.AsNoTracking().Where(s => s.CustomerUserId == requesterId),
                    d => d.SubscriptionId, s => s.Id, (d, _) => d)
                .Select(x => new { x.RestaurantId })
                .SingleOrDefaultAsync(ct);
            return day is null
                ? new(true, false, null, resourceType)
                : new(true, true, day.RestaurantId, resourceType);
        }

        if (resourceType == "RestaurantDailyOrder")
        {
            var order = await _db.RestaurantDailyOrders.AsNoTracking()
                .Where(x => x.Id == resourceId.Value)
                .Join(_db.SubscriptionDays.AsNoTracking(), order => order.SubscriptionDayId, day => day.Id,
                    (order, day) => new { order.RestaurantId, day.SubscriptionId })
                .Join(_db.Subscriptions.AsNoTracking().Where(s => s.CustomerUserId == requesterId),
                    row => row.SubscriptionId, subscription => subscription.Id,
                    (row, _) => (Guid?)row.RestaurantId)
                .SingleOrDefaultAsync(ct);
            return order is not null
                ? new(true, true, order, resourceType)
                : new(true, false, null, resourceType);
        }

        return new(false, false, null, null);
    }

    private readonly record struct ResolvedSupportResource(bool Supported, bool Found, Guid? RestaurantId, string? ResourceType);
}
