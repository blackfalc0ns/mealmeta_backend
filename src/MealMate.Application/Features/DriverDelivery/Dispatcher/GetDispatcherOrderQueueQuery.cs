using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherOrderQueueQuery(
    Guid? RestaurantId = null,
    string? Filter = null,
    int Page = 1,
    int PageSize = 25) : IQuery<DispatcherOrderQueueDto>;

public sealed class GetDispatcherOrderQueueQueryHandler : IQueryHandler<GetDispatcherOrderQueueQuery, DispatcherOrderQueueDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IDispatchRecommendationEngine _recommendationEngine;

    public GetDispatcherOrderQueueQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IDispatchRecommendationEngine recommendationEngine)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _recommendationEngine = recommendationEngine;
    }

    public async Task<Result<DispatcherOrderQueueDto>> Handle(
        GetDispatcherOrderQueueQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(request.RestaurantId, cancellationToken);
        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == restaurantId, cancellationToken);

        var restaurantNameAr = restaurant?.TradeNameAr ?? restaurant?.LegalNameAr ?? "مطعم MealMate الكويت";
        var restaurantNameEn = restaurant?.TradeNameEn ?? restaurant?.LegalNameEn ?? "MealMate Restaurant Kuwait";
        var restaurantName = _messages.Localize(restaurantNameAr, restaurantNameEn);
        var roleLabel = _messages.GetMessage(MessageCodes.Dispatcher.RoleLabel, "Dispatcher");
        var isAr = _messages.IsArabic();

        var restaurantContext = new RestaurantContextDto(
            Id: restaurantId,
            Name: restaurantName,
            NameAr: restaurantNameAr,
            NameEn: restaurantNameEn,
            Role: roleLabel);

        // Fetch Trips and Stops for the restaurant
        var trips = await _db.Trips
            .AsNoTracking()
            .Include(t => t.Stops)
            .Include(t => t.DriverAssignments)
            .Where(t => t.RestaurantId == restaurantId && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var allStops = trips.SelectMany(t => t.Stops).ToList();

        var assignedDayIds = allStops
            .Where(s => s.SubscriptionDayId.HasValue)
            .Select(s => s.SubscriptionDayId!.Value)
            .ToHashSet();

        // Only include boxes that have exited kitchen preparation, have their invoices/labels issued,
        // and are marked ReadyForPickup (ready for delivery dispatcher assignment).
        var readyDailyOrderDayIds = await _db.RestaurantDailyOrders
            .AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId && o.Status == OrderStatus.ReadyForPickup)
            .Select(o => o.SubscriptionDayId)
            .ToHashSetAsync(cancellationToken);

        var unassignedDays = await _db.SubscriptionDays
            .AsNoTracking()
            .Include(d => d.MealSelections)
            .Where(d => d.RestaurantId == restaurantId
                && (readyDailyOrderDayIds.Contains(d.Id) || d.Status == SubscriptionDayStatus.ConfirmedByRestaurant || d.Status == SubscriptionDayStatus.Preparing)
                && !assignedDayIds.Contains(d.Id))
            .OrderBy(d => d.ServiceDate)
            .ThenBy(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var unassignedDayIds = unassignedDays.Select(d => d.Id).ToList();
        var dailyOrders = unassignedDayIds.Count > 0
            ? await _db.RestaurantDailyOrders
                .AsNoTracking()
                .Where(o => unassignedDayIds.Contains(o.SubscriptionDayId))
                .ToDictionaryAsync(o => o.SubscriptionDayId, cancellationToken)
            : new Dictionary<Guid, RestaurantDailyOrder>();

        var normPage = request.Page < 1 ? 1 : request.Page;
        var normPageSize = request.PageSize switch
        {
            < 1 => 25,
            > 100 => 100,
            _ => request.PageSize
        };

        int pendingCount;
        int assignedCount;
        int inDeliveryCount;
        int issuesCount;
        int totalCount;

        var boxes = new List<DispatcherQueueBoxItemDto>();
        int totalFilteredCount;
        int totalPages;

        if (allStops.Count > 0 || unassignedDays.Count > 0)
        {
            pendingCount = allStops.Count(s => s.Status == TripStopStatus.Pending) + unassignedDays.Count;
            assignedCount = trips.Count(t => t.Status == TripStatus.Assigned);
            inDeliveryCount = allStops.Count(s => s.Status == TripStopStatus.PickedUp);
            issuesCount = allStops.Count(s => s.Status == TripStopStatus.Failed);
            totalCount = pendingCount + assignedCount + inDeliveryCount + issuesCount;

            var rawItems = new List<RawQueueItem>();
            foreach (var day in unassignedDays.OrderByDescending(d => d.CreatedAtUtc))
            {
                rawItems.Add(new DayQueueItem(day, "Pending"));
            }

            foreach (var stop in allStops.OrderBy(s => s.SequenceNumber))
            {
                var status = stop.Status switch
                {
                    TripStopStatus.Pending => "Pending",
                    TripStopStatus.PickedUp => "InDelivery",
                    TripStopStatus.Failed => "Issue",
                    _ => "Assigned"
                };
                rawItems.Add(new StopQueueItem(stop, status));
            }

            var filteredRawItems = ApplyRawFilter(rawItems, request.Filter);
            totalFilteredCount = filteredRawItems.Count;
            totalPages = normPageSize == 0 ? 1 : (int)Math.Ceiling(totalFilteredCount / (double)normPageSize);
            if (totalPages < 1) totalPages = 1;

            var pagedRawItems = filteredRawItems
                .Skip((normPage - 1) * normPageSize)
                .Take(normPageSize)
                .ToList();

            var badgeNew = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNew, "جديد");
            var badgeUrgent = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeUrgent, "عاجل");
            var badgeHigh = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeHighPriority, "أولوية عالية");
            var badgeNormal = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNormal, "عادي");

            var restLat = restaurant?.Latitude ?? 29.3375m;
            var restLng = restaurant?.Longitude ?? 48.0280m;

            var activeDriverIds = allStops
                .SelectMany(s => s.Trip.DriverAssignments.Where(da => da.Status == "Active").Select(da => da.DriverProfileId))
                .Distinct()
                .ToList();

            var activeDriversMap = activeDriverIds.Count > 0
                ? await _db.DriverProfiles
                    .AsNoTracking()
                    .Where(d => activeDriverIds.Contains(d.Id))
                    .ToDictionaryAsync(d => d.Id, cancellationToken)
                : new Dictionary<Guid, MealMate.Domain.IdentityAccess.DriverProfile>();

            var dayAddressIds = unassignedDays.Where(d => d.DeliveryAddressId.HasValue).Select(d => d.DeliveryAddressId!.Value).Distinct().ToList();
            var dayAddressMap = dayAddressIds.Count > 0
                ? await _db.CustomerAddresses
                    .AsNoTracking()
                    .Where(a => dayAddressIds.Contains(a.Id))
                    .ToDictionaryAsync(a => a.Id, cancellationToken)
                : new Dictionary<Guid, MealMate.Domain.IdentityAccess.CustomerAddress>();

            foreach (var item in pagedRawItems)
            {
                if (item is StopQueueItem stopItem)
                {
                    var stop = stopItem.Stop;
                    var priority = DeterminePriority(stop);
                    var priorityText = priority switch
                    {
                        "Urgent" => badgeUrgent,
                        "HighPriority" => badgeHigh,
                        "New" => badgeNew,
                        _ => badgeNormal
                    };

                    var mealCount = stop.BoxCount * 2;
                    var mealsLabel = _messages.GetMessage(MessageCodes.DispatcherOrders.MealsCountFormat, $"{mealCount} وجبات", mealCount);

                    var stopLat = stop.Latitude ?? restLat;
                    var stopLng = stop.Longitude ?? restLng;
                    var distKm = Math.Round(KuwaitGeoBounds.HaversineKm(restLat, restLng, stopLat, stopLng), 1);

                    var suggestion = await _recommendationEngine.RecommendDriverForBoxAsync(
                        restaurantId, stop.ZoneNameAr, stopLat, stopLng, priority, cancellationToken);

                    var activeAssign = stop.Trip?.DriverAssignments?.FirstOrDefault(da => da.Status == "Active");
                    Guid? assignedDriverId = activeAssign?.DriverProfileId;
                    string? assignedDriverName = null;
                    if (assignedDriverId.HasValue && activeDriversMap.TryGetValue(assignedDriverId.Value, out var aDriver))
                    {
                        assignedDriverName = _messages.Localize(aDriver.FullNameAr, aDriver.FullNameEn);
                    }

                    boxes.Add(new DispatcherQueueBoxItemDto(
                        BoxId: stop.Id,
                        BoxCode: stop.OrderCode ?? $"#BX-{Math.Abs(stop.Id.GetHashCode()) % 9000 + 1000}",
                        ZoneName: isAr ? $"منطقة {stop.ZoneNameAr}" : $"{stop.ZoneNameEn} Zone",
                        MealCount: mealCount,
                        MealCountLabel: mealsLabel,
                        DeliveryTimeWindow: string.IsNullOrWhiteSpace(stop.SlotLabel) ? "09:30-10:30 ص" : stop.SlotLabel,
                        DistanceKm: distKm,
                        DistanceText: $"{distKm:F1} كم",
                        Status: stopItem.Status,
                        Priority: priority,
                        PriorityBadgeText: priorityText,
                        Suggestion: suggestion,
                        AssignedDriverId: assignedDriverId,
                        AssignedDriverName: assignedDriverName));
                }
                else if (item is DayQueueItem dayItem)
                {
                    var day = dayItem.Day;
                    dailyOrders.TryGetValue(day.Id, out var dailyOrder);
                    var isReady = dailyOrder?.Status == OrderStatus.ReadyForPickup;
                    var priority = (isReady || day.Status == SubscriptionDayStatus.Preparing)
                        ? "Urgent"
                        : (day.CreatedAtUtc >= DateTime.UtcNow.AddMinutes(-30) ? "New" : "Normal");

                    var priorityText = priority switch
                    {
                        "Urgent" => badgeUrgent,
                        "HighPriority" => badgeHigh,
                        "New" => badgeNew,
                        _ => badgeNormal
                    };

                    var mealCount = day.MealSelections.Count > 0
                        ? day.MealSelections.Count
                        : (dailyOrder != null && dailyOrder.BoxCount > 0 ? dailyOrder.BoxCount * 2 : 2);

                    var mealsLabel = _messages.GetMessage(MessageCodes.DispatcherOrders.MealsCountFormat, $"{mealCount} وجبات", mealCount);
                    var zoneName = !string.IsNullOrWhiteSpace(day.DeliveryAddressLine)
                        ? (day.DeliveryAddressLine.StartsWith("منطقة") ? day.DeliveryAddressLine : $"منطقة {day.DeliveryAddressLine}")
                        : "منطقة العاصمة";

                    decimal dayLat = restLat;
                    decimal dayLng = restLng;
                    if (day.DeliveryAddressId.HasValue && dayAddressMap.TryGetValue(day.DeliveryAddressId.Value, out var dAddr) && dAddr.Latitude.HasValue && dAddr.Longitude.HasValue)
                    {
                        dayLat = dAddr.Latitude.Value;
                        dayLng = dAddr.Longitude.Value;
                    }

                    var distKm = Math.Round(KuwaitGeoBounds.HaversineKm(restLat, restLng, dayLat, dayLng), 1);
                    var suggestion = await _recommendationEngine.RecommendDriverForBoxAsync(
                        restaurantId, zoneName, dayLat, dayLng, priority, cancellationToken);

                    boxes.Add(new DispatcherQueueBoxItemDto(
                        BoxId: day.Id,
                        BoxCode: $"#BX-{Math.Abs(day.Id.GetHashCode()) % 9000 + 1000}",
                        ZoneName: zoneName,
                        MealCount: mealCount,
                        MealCountLabel: mealsLabel,
                        DeliveryTimeWindow: "09:30-10:30 ص",
                        DistanceKm: distKm,
                        DistanceText: $"{distKm:F1} كم",
                        Status: "Pending",
                        Priority: priority,
                        PriorityBadgeText: priorityText,
                        Suggestion: suggestion,
                        AssignedDriverId: null,
                        AssignedDriverName: null));
                }
            }
        }
        else
        {
            pendingCount = 0;
            assignedCount = 0;
            inDeliveryCount = 0;
            issuesCount = 0;
            totalCount = 0;
            totalFilteredCount = 0;
            totalPages = 1;
        }

        var counts = new DispatcherQueueCountsDto(
            TotalCount: totalCount,
            PendingCount: pendingCount,
            AssignedCount: assignedCount,
            InDeliveryCount: inDeliveryCount,
            IssuesCount: issuesCount);

        return Result.Success(new DispatcherOrderQueueDto(
            Restaurant: restaurantContext,
            Counts: counts,
            Boxes: boxes,
            Page: normPage,
            PageSize: normPageSize,
            TotalBoxes: totalFilteredCount,
            TotalPages: totalPages,
            HasNextPage: normPage < totalPages));
    }

    private static string DeterminePriority(TripStop stop)
    {
        if (stop.SequenceNumber == 1) return "Urgent";
        if (stop.BoxCount >= 3) return "HighPriority";
        if (stop.CreatedAtUtc >= DateTime.UtcNow.AddMinutes(-30)) return "New";
        return "Normal";
    }

    private static IReadOnlyList<DispatcherQueueBoxItemDto> ApplyFilter(
        List<DispatcherQueueBoxItemDto> boxes,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return boxes;
        }

        return filter.ToLowerInvariant() switch
        {
            "pending" or "بانتظار الإسناد" => boxes.Where(b => b.Status == "Pending").ToList(),
            "assigned" or "تم الإسناد" => boxes.Where(b => b.Status == "Assigned").ToList(),
            "indelivery" or "in_delivery" or "قيد التوصيل" => boxes.Where(b => b.Status == "InDelivery").ToList(),
            "issues" or "issue" or "مشاكل" => boxes.Where(b => b.Status == "Issue").ToList(),
            _ => boxes
        };
    }

    private async Task<Guid> ResolveRestaurantIdAsync(Guid? requestedRestaurantId, CancellationToken cancellationToken)
    {
        if (requestedRestaurantId.HasValue && requestedRestaurantId.Value != Guid.Empty)
        {
            return requestedRestaurantId.Value;
        }

        if (_currentUser.RestaurantId.HasValue && _currentUser.RestaurantId.Value != Guid.Empty)
        {
            return _currentUser.RestaurantId.Value;
        }

        if (_currentUser.UserId.HasValue)
        {
            var deliveryManager = await _db.DeliveryManagerRegistrations
                .AsNoTracking()
                .Where(d => d.ResultingUserId == _currentUser.UserId.Value)
                .Select(d => d.RestaurantId)
                .FirstOrDefaultAsync(cancellationToken);

            if (deliveryManager != Guid.Empty)
            {
                return deliveryManager;
            }

            var restaurantProfile = await _db.RestaurantProfiles
                .AsNoTracking()
                .Where(r => r.UserId == _currentUser.UserId.Value)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (restaurantProfile != Guid.Empty)
            {
                return restaurantProfile;
            }
        }

        var firstRestaurantId = await _db.RestaurantProfiles
            .AsNoTracking()
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return firstRestaurantId != Guid.Empty ? firstRestaurantId : Guid.NewGuid();
    }

    private abstract record RawQueueItem(string Status);
    private sealed record StopQueueItem(TripStop Stop, string Status) : RawQueueItem(Status);
    private sealed record DayQueueItem(SubscriptionDay Day, string Status) : RawQueueItem(Status);

    private static List<RawQueueItem> ApplyRawFilter(List<RawQueueItem> items, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return items;
        }

        return filter.ToLowerInvariant() switch
        {
            "pending" or "بانتظار الإسناد" => items.Where(b => b.Status == "Pending").ToList(),
            "assigned" or "تم الإسناد" => items.Where(b => b.Status == "Assigned").ToList(),
            "indelivery" or "in_delivery" or "قيد التوصيل" => items.Where(b => b.Status == "InDelivery").ToList(),
            "issues" or "issue" or "مشاكل" => items.Where(b => b.Status == "Issue").ToList(),
            _ => items
        };
    }
}
