using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherPerformanceOverviewQuery(
    Guid? RestaurantId = null,
    string? Period = "Last7Days",
    DateTime? FromDate = null,
    DateTime? ToDate = null) : IQuery<DispatcherPerformanceOverviewResponseDto>;

public sealed class GetDispatcherPerformanceOverviewQueryHandler : IQueryHandler<GetDispatcherPerformanceOverviewQuery, DispatcherPerformanceOverviewResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherPerformanceOverviewQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DispatcherPerformanceOverviewResponseDto>> Handle(
        GetDispatcherPerformanceOverviewQuery request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        // 1. Resolve RestaurantId
        var restaurantId = request.RestaurantId ?? _currentUser.RestaurantId;
        if (!restaurantId.HasValue || restaurantId.Value == Guid.Empty)
        {
            restaurantId = await _db.RestaurantProfiles
                .AsNoTracking()
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // 2. Resolve Date Range
        var (fromDate, toDate, periodKey, periodText) = ResolveDateRange(request.Period, request.FromDate, request.ToDate, isAr);
        var dateRangeText = FormatDateRange(fromDate, toDate, isAr);

        // 3. Query Trips and Stops in range
        var trips = await _db.Trips
            .AsNoTracking()
            .Include(t => t.DriverAssignments)
            .Include(t => t.Stops)
            .Where(t => (!restaurantId.HasValue || t.RestaurantId == restaurantId.Value) &&
                        (t.CreatedAtUtc >= fromDate && t.CreatedAtUtc <= toDate ||
                         t.PlannedDate >= DateOnly.FromDateTime(fromDate) && t.PlannedDate <= DateOnly.FromDateTime(toDate)))
            .ToListAsync(cancellationToken);

        var allStops = trips.SelectMany(t => t.Stops).ToList();

        // Fallback: If trips list is empty, search TripStops directly
        if (allStops.Count == 0)
        {
            allStops = await _db.TripStops
                .AsNoTracking()
                .Include(s => s.Trip)
                    .ThenInclude(t => t.DriverAssignments)
                .Where(s => s.CreatedAtUtc >= fromDate && s.CreatedAtUtc <= toDate ||
                            (s.DeliveredAtUtc.HasValue && s.DeliveredAtUtc.Value >= fromDate && s.DeliveredAtUtc.Value <= toDate))
                .ToListAsync(cancellationToken);
        }

        // 4. Calculate Top KPIs
        var totalBoxes = allStops.Count;
        var deliveredStops = allStops.Where(s => s.Status == TripStopStatus.Delivered).ToList();
        var deliveredCount = deliveredStops.Count;
        var deliveredPercentage = totalBoxes > 0 ? Math.Round((decimal)deliveredCount / totalBoxes * 100, 1) : 0m;

        var failedStops = allStops.Where(s => s.Status == TripStopStatus.Failed).ToList();
        var failedCount = failedStops.Count;
        var failedPercentage = totalBoxes > 0 ? Math.Round((decimal)failedCount / totalBoxes * 100, 1) : 0m;

        // Calculate delays for delivered stops
        var delays = new List<int>();
        int onTimeCount = 0;
        int delayedCount = 0;

        foreach (var stop in deliveredStops)
        {
            var expectedTime = stop.CreatedAtUtc.AddMinutes(45);
            var deliveredTime = stop.DeliveredAtUtc ?? stop.ModifiedAtUtc ?? stop.CreatedAtUtc;
            var diff = (int)(deliveredTime - expectedTime).TotalMinutes;
            var delay = Math.Max(0, diff);
            delays.Add(delay);

            if (delay <= 10)
            {
                onTimeCount++;
            }
            else
            {
                delayedCount++;
            }
        }

        var avgDelayMinutes = delays.Any() ? (int)Math.Round(delays.Average()) : 0;
        var cancelledCount = allStops.Count(s => s.Trip != null && s.Trip.Status == TripStatus.Cancelled);

        // 5. Performance Distribution (Donut Segments)
        var segments = new List<PerformanceDistributionSegmentDto>
        {
            new(
                Key: "OnTime",
                Title: isAr ? "تم في الوقت" : "On Time",
                Count: onTimeCount,
                Percentage: totalBoxes > 0 ? Math.Round((decimal)onTimeCount / totalBoxes * 100, 1) : 0m,
                ColorHex: "#10B981"),
            new(
                Key: "Delayed",
                Title: isAr ? "متأخر" : "Delayed",
                Count: delayedCount,
                Percentage: totalBoxes > 0 ? Math.Round((decimal)delayedCount / totalBoxes * 100, 1) : 0m,
                ColorHex: "#F59E0B"),
            new(
                Key: "Failed",
                Title: isAr ? "فشل التسليم" : "Failed Delivery",
                Count: failedCount,
                Percentage: totalBoxes > 0 ? Math.Round((decimal)failedCount / totalBoxes * 100, 1) : 0m,
                ColorHex: "#EF4444"),
            new(
                Key: "Cancelled",
                Title: isAr ? "ملغي" : "Cancelled",
                Count: cancelledCount,
                Percentage: totalBoxes > 0 ? Math.Round((decimal)cancelledCount / totalBoxes * 100, 1) : 0m,
                ColorHex: "#9CA3AF")
        };

        var distribution = new PerformanceDistributionDto(totalBoxes, segments);

        // 6. Query Drivers and compute their performance table
        var drivers = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => (!restaurantId.HasValue || d.RestaurantId == restaurantId.Value) && !d.IsDeleted)
            .ToListAsync(cancellationToken);

        var driverRows = new List<DriverPerformanceRowDto>();

        foreach (var driver in drivers)
        {
            var driverName = isAr ? driver.FullNameAr : driver.FullNameEn;
            var driverCode = !string.IsNullOrWhiteSpace(driver.LicenseNumber)
                ? driver.LicenseNumber
                : $"DR-{Math.Abs(driver.Id.GetHashCode()) % 900 + 1000}";

            // Find stops assigned to this driver
            var driverStops = allStops
                .Where(s => s.Trip?.DriverAssignments?.Any(da => da.DriverProfileId == driver.Id) == true)
                .ToList();

            var driverDeliveredStops = driverStops.Where(s => s.Status == TripStopStatus.Delivered).ToList();
            var dDelivered = driverDeliveredStops.Count;
            var dTotal = driverStops.Count;
            var dDeliveredRate = dTotal > 0 ? Math.Round((decimal)dDelivered / dTotal * 100, 1) : 0m;

            var dFailed = driverStops.Count(s => s.Status == TripStopStatus.Failed);
            var dFailedRate = dTotal > 0 ? Math.Round((decimal)dFailed / dTotal * 100, 1) : 0m;

            // Driver average delay
            var driverDelays = driverDeliveredStops.Select(s =>
            {
                var exp = s.CreatedAtUtc.AddMinutes(45);
                var act = s.DeliveredAtUtc ?? s.ModifiedAtUtc ?? s.CreatedAtUtc;
                return Math.Max(0, (int)(act - exp).TotalMinutes);
            }).ToList();

            var dAvgDelay = driverDelays.Any() ? (int)Math.Round(driverDelays.Average()) : 0;
            var delayColor = dAvgDelay <= 10 ? "Green" : (dAvgDelay <= 20 ? "Orange" : "Red");

            // Driver Rating
            var ratings = await _db.DriverRatings
                .AsNoTracking()
                .Where(r => r.DriverProfileId == driver.Id)
                .Select(r => (decimal)r.Score)
                .ToListAsync(cancellationToken);

            var ratingStat = await _db.DriverPerformanceStats
                .AsNoTracking()
                .Where(s => s.DriverProfileId == driver.Id)
                .OrderByDescending(s => s.PeriodEnd)
                .FirstOrDefaultAsync(cancellationToken);

            var dRating = ratings.Any()
                ? Math.Round(ratings.Average(), 1)
                : (ratingStat?.AvgRating ?? (driver.IsAvailable ? 4.8m : 4.5m));

            // Status dot
            var status = driver.IsAvailable ? "Available" : "Busy";
            var statusDot = driver.IsAvailable ? "Green" : "Orange";

            driverRows.Add(new DriverPerformanceRowDto(
                DriverId: driver.Id,
                DriverCode: driverCode,
                FullName: driverName,
                AvatarUrl: $"https://cdn.mealmate.app/avatars/{driver.Id.ToString()[..8]}.jpg",
                Status: status,
                StatusDotColor: statusDot,
                DeliveredCount: dDelivered,
                DeliveredRate: dDeliveredRate,
                DeliveredRateText: $"{dDeliveredRate}%",
                AvgDelayMinutes: dAvgDelay,
                AvgDelayText: isAr ? $"{dAvgDelay} د" : $"{dAvgDelay}m",
                AvgDelayColor: delayColor,
                FailedCount: dFailed,
                FailedRate: dFailedRate,
                FailedRateText: $"{dFailedRate}%",
                Rating: dRating));
        }

        // 7. Overall rating across all drivers
        var overallRating = driverRows.Any() ? Math.Round(driverRows.Average(r => r.Rating), 1) : 4.6m;

        var kpis = new PerformanceKpisDto(
            TotalBoxes: totalBoxes,
            DeliveredCount: deliveredCount,
            DeliveredPercentage: deliveredPercentage,
            AvgDelayMinutes: avgDelayMinutes,
            OverallRating: overallRating,
            FailedCount: failedCount,
            FailedPercentage: failedPercentage);

        // 8. Top Rated Drivers Podium (Top 3)
        var top3Sorted = driverRows
            .OrderByDescending(r => r.Rating)
            .ThenByDescending(r => r.DeliveredCount)
            .Take(3)
            .ToList();

        var topDrivers = new List<TopRatedDriverDto>();
        for (int i = 0; i < top3Sorted.Count; i++)
        {
            var rank = i + 1;
            topDrivers.Add(new TopRatedDriverDto(
                Rank: rank,
                DriverId: top3Sorted[i].DriverId,
                DriverCode: top3Sorted[i].DriverCode,
                FullName: top3Sorted[i].FullName,
                Rating: top3Sorted[i].Rating,
                AvatarUrl: top3Sorted[i].AvatarUrl,
                IsHighlighted: rank == 1));
        }

        return Result.Success(new DispatcherPerformanceOverviewResponseDto(
            Period: periodKey,
            PeriodText: periodText,
            DateRangeText: dateRangeText,
            Kpis: kpis,
            Distribution: distribution,
            TopDrivers: topDrivers,
            DriversTable: driverRows));
    }

    private static (DateTime from, DateTime to, string key, string text) ResolveDateRange(
        string? period,
        DateTime? customFrom,
        DateTime? customTo,
        bool isAr)
    {
        var now = DateTime.UtcNow;

        if (customFrom.HasValue && customTo.HasValue)
        {
            return (customFrom.Value, customTo.Value, "Custom", isAr ? "فترة مخصصة" : "Custom Period");
        }

        return (period?.ToLowerInvariant()) switch
        {
            "today" => (now.Date, now, "Today", isAr ? "اليوم" : "Today"),
            "thisweek" => (now.Date.AddDays(-(int)now.DayOfWeek), now, "ThisWeek", isAr ? "هذا الأسبوع" : "This Week"),
            "thismonth" => (new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), now, "ThisMonth", isAr ? "هذا الشهر" : "This Month"),
            "last30days" => (now.Date.AddDays(-30), now, "Last30Days", isAr ? "آخر 30 يوم" : "Last 30 Days"),
            _ => (now.Date.AddDays(-7), now, "Last7Days", isAr ? "آخر 7 أيام" : "Last 7 Days")
        };
    }

    private static string FormatDateRange(DateTime fromUtc, DateTime toUtc, bool isAr)
    {
        // Kuwait is UTC+3
        var fromLocal = fromUtc.AddHours(3);
        var toLocal = toUtc.AddHours(3);

        string[] arMonths = { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
        string[] enMonths = { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        if (isAr)
        {
            return $"{fromLocal.Day} {arMonths[fromLocal.Month]} - {toLocal.Day} {arMonths[toLocal.Month]} {toLocal.Year}";
        }

        return $"{fromLocal.Day} {enMonths[fromLocal.Month]} - {toLocal.Day} {enMonths[toLocal.Month]} {toLocal.Year}";
    }
}
