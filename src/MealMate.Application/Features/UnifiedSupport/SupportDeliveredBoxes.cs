using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed record SupportDeliveredBoxesQuery(
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    Guid? SubscriptionId = null,
    int PageNumber = 1,
    int PageSize = 20);

public sealed record SupportDeliveredBoxDto(
    Guid SubscriptionId,
    Guid SubscriptionDayId,
    Guid? RestaurantDailyOrderId,
    DateOnly ServiceDate,
    Guid? RestaurantId,
    string? RestaurantNameAr,
    string? RestaurantNameEn,
    int? BoxCount,
    string? OrderCode,
    DateTime? DeliveredAtUtc,
    string RelatedResourceType,
    Guid RelatedResourceId);

public sealed record SupportDeliveredBoxesPageDto(
    IReadOnlyList<SupportDeliveredBoxDto> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed partial class SupportCaseService
{
    public async Task<Result<SupportDeliveredBoxesPageDto>> GetDeliveredBoxesAsync(
        SupportDeliveredBoxesQuery request,
        CancellationToken ct = default)
    {
        var actorId = ActorId;
        if (actorId is null) return Unauthorized();
        if (_currentUser.UserType != UserType.Customer) return Forbidden();
        if (request.PageNumber < 1 || request.PageSize < 1 || request.PageSize > 100
            || (long)request.PageNumber * request.PageSize > int.MaxValue
            || request.FromDate.HasValue && request.ToDate.HasValue && request.FromDate > request.ToDate)
            return Invalid();

        var subscriptions = _db.Subscriptions.AsNoTracking()
            .Where(subscription => subscription.CustomerUserId == actorId.Value && !subscription.IsDeleted);
        if (request.SubscriptionId.HasValue)
            subscriptions = subscriptions.Where(subscription => subscription.Id == request.SubscriptionId.Value);

        var days = _db.SubscriptionDays.AsNoTracking()
            .Where(day => !day.IsDeleted)
            .Join(subscriptions, day => day.SubscriptionId, subscription => subscription.Id,
                (day, subscription) => new { Day = day, Subscription = subscription });
        if (request.FromDate.HasValue)
            days = days.Where(row => row.Day.ServiceDate >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            days = days.Where(row => row.Day.ServiceDate <= request.ToDate.Value);

        var deliveredOrders = _db.RestaurantDailyOrders.AsNoTracking()
            .Where(order => !order.IsDeleted && order.Status == OrderStatus.Delivered);
        var query = from row in days
            from order in deliveredOrders.Where(order => order.SubscriptionDayId == row.Day.Id).DefaultIfEmpty()
            where order != null || row.Day.Status == SubscriptionDayStatus.Delivered
                && !_db.RestaurantDailyOrders.IgnoreQueryFilters().AsNoTracking()
                    .Any(existing => existing.SubscriptionDayId == row.Day.Id)
            select new { row.Subscription, row.Day, Order = order };
        var totalCount = await query.CountAsync(ct);
        var offset = checked((int)((long)(request.PageNumber - 1) * request.PageSize));
        var page = await query
            .OrderByDescending(item => item.Day.ServiceDate)
            .ThenBy(item => item.Order == null ? item.Day.Id : item.Order.Id)
            .Skip(offset)
            .Take(request.PageSize)
            .Select(item => new
            {
                SubscriptionId = item.Subscription.Id,
                DayId = item.Day.Id,
                item.Day.ServiceDate,
                OrderId = item.Order == null ? (Guid?)null : item.Order.Id,
                RestaurantId = item.Order == null ? item.Day.RestaurantId : (Guid?)item.Order.RestaurantId,
                BoxCount = item.Order == null ? (int?)null : item.Order.BoxCount
            })
            .ToListAsync(ct);
        var restaurantIds = page.Select(item => item.RestaurantId)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var profiles = await _db.RestaurantProfiles.AsNoTracking()
            .Where(profile => !profile.IsDeleted && restaurantIds.Contains(profile.Id))
            .Select(profile => new { profile.Id, profile.TradeNameAr, profile.TradeNameEn })
            .ToDictionaryAsync(profile => profile.Id, ct);
        var orderIds = page.Where(item => item.OrderId.HasValue).Select(item => item.OrderId!.Value).ToArray();
        var legacyDayIds = page.Where(item => !item.OrderId.HasValue).Select(item => item.DayId).ToArray();
        var orderStops = await _db.TripStops.AsNoTracking()
            .Where(stop => !stop.IsDeleted && stop.Status == TripStopStatus.Delivered
                && stop.DailyOrderId.HasValue && orderIds.Contains(stop.DailyOrderId.Value))
            .GroupBy(stop => stop.DailyOrderId!.Value)
            .Select(group => group.OrderByDescending(candidate => candidate.DeliveredAtUtc.HasValue)
                .ThenByDescending(candidate => candidate.DeliveredAtUtc)
                .ThenBy(candidate => candidate.Id)
                .Select(candidate => new { Key = group.Key, candidate.OrderCode, candidate.DeliveredAtUtc })
                .First())
            .ToDictionaryAsync(stop => stop.Key, ct);
        var legacyStops = await _db.TripStops.AsNoTracking()
            .Where(stop => !stop.IsDeleted && stop.Status == TripStopStatus.Delivered
                && !stop.DailyOrderId.HasValue && stop.SubscriptionDayId.HasValue
                && legacyDayIds.Contains(stop.SubscriptionDayId.Value))
            .GroupBy(stop => stop.SubscriptionDayId!.Value)
            .Select(group => group.OrderByDescending(candidate => candidate.DeliveredAtUtc.HasValue)
                .ThenByDescending(candidate => candidate.DeliveredAtUtc)
                .ThenBy(candidate => candidate.Id)
                .Select(candidate => new { Key = group.Key, candidate.OrderCode, candidate.DeliveredAtUtc })
                .First())
            .ToDictionaryAsync(stop => stop.Key, ct);
        var items = page.Select(item =>
        {
            var profile = item.RestaurantId.HasValue && profiles.TryGetValue(item.RestaurantId.Value, out var foundProfile)
                ? foundProfile
                : null;
            var stop = item.OrderId.HasValue
                ? orderStops.GetValueOrDefault(item.OrderId.Value)
                : legacyStops.GetValueOrDefault(item.DayId);
            return new SupportDeliveredBoxDto(
                item.SubscriptionId,
                item.DayId,
                item.OrderId,
                item.ServiceDate,
                item.RestaurantId,
                profile?.TradeNameAr,
                profile?.TradeNameEn,
                item.BoxCount,
                stop?.OrderCode,
                stop?.DeliveredAtUtc,
                item.OrderId.HasValue ? "RestaurantDailyOrder" : "SubscriptionDay",
                item.OrderId ?? item.DayId);
        }).ToArray();
        return Result.Success(new SupportDeliveredBoxesPageDto(items, totalCount, request.PageNumber, request.PageSize));
    }
}
