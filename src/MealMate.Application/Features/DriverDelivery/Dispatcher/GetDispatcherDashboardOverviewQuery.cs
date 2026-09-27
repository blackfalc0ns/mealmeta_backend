using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherDashboardOverviewQuery(Guid? RestaurantId = null) : IQuery<DispatcherDashboardOverviewDto>;

public sealed class GetDispatcherDashboardOverviewQueryHandler : IQueryHandler<GetDispatcherDashboardOverviewQuery, DispatcherDashboardOverviewDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherDashboardOverviewQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DispatcherDashboardOverviewDto>> Handle(
        GetDispatcherDashboardOverviewQuery request,
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

        var restaurantContext = new RestaurantContextDto(
            Id: restaurantId,
            Name: restaurantName,
            NameAr: restaurantNameAr,
            NameEn: restaurantNameEn,
            Role: roleLabel);

        // Fetch Trips and Stops for the restaurant
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var trips = await _db.Trips
            .AsNoTracking()
            .Include(t => t.Stops)
            .Include(t => t.DriverAssignments)
            .Where(t => t.RestaurantId == restaurantId && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var allStops = trips.SelectMany(t => t.Stops).ToList();

        // Calculate KPI and Operations metrics
        int deliveredCount;
        int inDeliveryCount;
        int pendingCount;
        int cancelledCount;
        int totalOrders;
        double completionRate;

        if (allStops.Count > 0)
        {
            deliveredCount = allStops.Count(s => s.Status == TripStopStatus.Delivered);
            inDeliveryCount = allStops.Count(s => s.Status == TripStopStatus.PickedUp)
                + trips.Count(t => t.Status == TripStatus.InProgress && t.Stops.All(s => s.Status == TripStopStatus.Pending));
            pendingCount = allStops.Count(s => s.Status == TripStopStatus.Pending)
                + trips.Count(t => t.Status == TripStatus.Planned || t.Status == TripStatus.Draft);
            cancelledCount = allStops.Count(s => s.Status == TripStopStatus.Failed)
                + trips.Count(t => t.Status == TripStatus.Cancelled);

            totalOrders = deliveredCount + inDeliveryCount + pendingCount + cancelledCount;
            completionRate = totalOrders > 0
                ? Math.Round((double)deliveredCount / totalOrders * 100.0, 1)
                : 0.0;
        }
        else
        {
            deliveredCount = 0;
            inDeliveryCount = 0;
            pendingCount = 0;
            cancelledCount = 0;
            totalOrders = 0;
            completionRate = 0.0;
        }

        // Active Issues Detection
        var issues = new List<ActiveIssueItemDto>();
        var delayedStops = allStops
            .Where(s => s.Status == TripStopStatus.PickedUp && s.DeliveredAtUtc == null)
            .Take(2)
            .ToList();

        if (delayedStops.Count > 0)
        {
            foreach (var stop in delayedStops)
            {
                var delayMinutes = 25;
                issues.Add(new ActiveIssueItemDto(
                    Id: $"iss-{stop.Id.ToString()[..8]}",
                    Type: "DeliveryDelay",
                    Severity: "Warning",
                    OrderId: string.IsNullOrWhiteSpace(stop.OrderCode) ? stop.Id.ToString() : stop.OrderCode,
                    DriverId: null,
                    DriverName: stop.CustomerName,
                    Message: _messages.GetMessage(MessageCodes.Dispatcher.IssueDeliveryDelayMessage, $"تأخر تسليم الطلب #{stop.OrderCode ?? stop.Id.ToString()} بمقدار {delayMinutes} دقيقة", stop.OrderCode ?? stop.Id.ToString(), delayMinutes)));
            }
        }

        var activeIssuesCount = issues.Count;
        var summaryAr = activeIssuesCount == 0 ? string.Empty : _messages.GetMessage(MessageCodes.Dispatcher.ActiveIssuesSummary, "تأخير في التوصيل ، سائق متوقف منذ فترة طويلة");
        var summaryEn = activeIssuesCount == 0 ? string.Empty : "Delivery delay, driver idle for too long";
        var activeIssuesContainer = new ActiveIssuesContainerDto(
            Count: activeIssuesCount,
            Summary: _messages.Localize(summaryAr, summaryEn),
            SummaryAr: summaryAr,
            SummaryEn: summaryEn,
            Issues: issues);

        var kpis = new DispatcherKpisDto(
            TotalOrdersToday: totalOrders,
            InDeliveryCount: inDeliveryCount,
            PendingAssignmentCount: pendingCount,
            ActiveIssuesCount: activeIssuesCount);

        var operationsStatus = new DispatcherOperationsStatusDto(
            CompletionRatePercentage: completionRate,
            DeliveredCount: deliveredCount,
            InDeliveryCount: inDeliveryCount,
            PendingAssignmentCount: pendingCount,
            CancelledCount: cancelledCount);

        // Greeting
        var greetingTitle = _messages.GetMessage(MessageCodes.Dispatcher.GreetingTitle, "مرحبًا");
        var greetingSubtitle = activeIssuesCount == 0
            ? _messages.GetMessage(MessageCodes.Dispatcher.GreetingAllUnderControl, "كل شيء تحت السيطرة اليوم 👏")
            : _messages.GetMessage(MessageCodes.Dispatcher.GreetingAttentionNeeded, $"هناك {activeIssuesCount} مشكلة تحتاج إلى انتباهك", activeIssuesCount);

        var greeting = new DispatcherGreetingDto(
            Title: greetingTitle,
            Subtitle: greetingSubtitle);

        // Top Drivers
        var topDrivers = await BuildTopDriversAsync(restaurantId, cancellationToken);

        // Regions Summary
        var regionsSummary = BuildRegionsSummary(allStops);

        return Result.Success(new DispatcherDashboardOverviewDto(
            Restaurant: restaurantContext,
            Greeting: greeting,
            Kpis: kpis,
            OperationsStatus: operationsStatus,
            TopDrivers: topDrivers,
            RegionsSummary: regionsSummary,
            ActiveIssues: activeIssuesContainer));
    }

    private async Task<IReadOnlyList<TopDriverDto>> BuildTopDriversAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var drivers = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => d.RestaurantId == restaurantId || d.RestaurantId == null)
            .Take(5)
            .ToListAsync(cancellationToken);

        var badgeTopRated = _messages.GetMessage(MessageCodes.Dispatcher.BadgeTopRated, "أعلى تقييم");
        var badgeExcellent = _messages.GetMessage(MessageCodes.Dispatcher.BadgeExcellent, "تقييم ممتاز");
        var badgeVeryGood = _messages.GetMessage(MessageCodes.Dispatcher.BadgeVeryGood, "تقييم جيد جداً");

        if (drivers.Count == 0)
        {
            return Array.Empty<TopDriverDto>();
        }

        var driverIds = drivers.Select(d => d.Id).ToList();
        var stats = await _db.DriverPerformanceStats
            .AsNoTracking()
            .Where(s => driverIds.Contains(s.DriverProfileId))
            .ToDictionaryAsync(s => s.DriverProfileId, cancellationToken);

        var list = new List<TopDriverDto>();
        foreach (var driver in drivers)
        {
            stats.TryGetValue(driver.Id, out var stat);
            var rating = stat?.AvgRating ?? 0m;
            var badge = rating >= 4.8m ? badgeTopRated : rating >= 4.0m ? badgeExcellent : badgeVeryGood;
            var deliveredCount = stat?.DeliveredCount ?? 0;
            var name = !string.IsNullOrWhiteSpace(driver.FullNameAr) ? driver.FullNameAr : driver.FullNameEn;

            list.Add(new TopDriverDto(
                DriverId: driver.Id,
                FullName: name,
                AvatarUrl: null,
                Badge: badge,
                Rating: rating,
                CompletedDeliveriesToday: deliveredCount));
        }

        return list.OrderByDescending(d => d.Rating).ThenByDescending(d => d.CompletedDeliveriesToday).Take(3).ToList();
    }

    private static IReadOnlyList<RegionSummaryDto> BuildRegionsSummary(List<TripStop> stops)
    {
        var grouped = stops
            .Where(s => !string.IsNullOrWhiteSpace(s.ZoneNameAr))
            .GroupBy(s => s.ZoneNameAr)
            .Select(g => new RegionSummaryDto(
                RegionId: $"reg-{Math.Abs(g.Key.GetHashCode()) % 1000}",
                RegionName: g.Key,
                OrdersCount: g.Count(),
                Trend: g.Count() >= 20 ? "up" : "down",
                PercentageChange: g.Count() >= 20 ? 12.5 : -4.2))
            .OrderByDescending(r => r.OrdersCount)
            .Take(4)
            .ToList();

        return grouped;
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
}
