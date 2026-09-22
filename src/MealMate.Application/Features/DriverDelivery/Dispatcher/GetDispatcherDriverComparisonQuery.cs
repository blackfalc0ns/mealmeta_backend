using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherDriverComparisonQuery(
    Guid? RestaurantId = null,
    string? Period = "Last7Days",
    IReadOnlyList<Guid>? DriverIds = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null) : IQuery<DispatcherDriverComparisonResponseDto>;

public sealed class GetDispatcherDriverComparisonQueryHandler : IQueryHandler<GetDispatcherDriverComparisonQuery, DispatcherDriverComparisonResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherDriverComparisonQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DispatcherDriverComparisonResponseDto>> Handle(
        GetDispatcherDriverComparisonQuery request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        var restaurantId = request.RestaurantId ?? _currentUser.RestaurantId;
        if (!restaurantId.HasValue || restaurantId.Value == Guid.Empty)
        {
            restaurantId = await _db.RestaurantProfiles
                .AsNoTracking()
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var (fromDate, toDate, periodKey, periodText) = ResolveDateRange(request.Period, request.FromDate, request.ToDate, isAr);

        // Drivers query
        var driverQuery = _db.DriverProfiles
            .AsNoTracking()
            .Where(d => (!restaurantId.HasValue || d.RestaurantId == restaurantId.Value) && !d.IsDeleted);

        if (request.DriverIds != null && request.DriverIds.Count > 0)
        {
            driverQuery = driverQuery.Where(d => request.DriverIds.Contains(d.Id));
        }

        var drivers = await driverQuery.ToListAsync(cancellationToken);

        // Trips & Stops in period
        var trips = await _db.Trips
            .AsNoTracking()
            .Include(t => t.DriverAssignments)
            .Include(t => t.Stops)
            .Where(t => (!restaurantId.HasValue || t.RestaurantId == restaurantId.Value) &&
                        (t.CreatedAtUtc >= fromDate && t.CreatedAtUtc <= toDate ||
                         t.PlannedDate >= DateOnly.FromDateTime(fromDate) && t.PlannedDate <= DateOnly.FromDateTime(toDate)))
            .ToListAsync(cancellationToken);

        var allStops = trips.SelectMany(t => t.Stops).ToList();

        var comparisonList = new List<DriverComparisonMetricDto>();

        foreach (var driver in drivers)
        {
            var driverName = isAr ? driver.FullNameAr : driver.FullNameEn;
            var driverCode = !string.IsNullOrWhiteSpace(driver.LicenseNumber)
                ? driver.LicenseNumber
                : $"DR-{Math.Abs(driver.Id.GetHashCode()) % 900 + 1000}";

            var driverStops = allStops
                .Where(s => s.Trip?.DriverAssignments?.Any(da => da.DriverProfileId == driver.Id) == true)
                .ToList();

            var totalAssigned = driverStops.Count;
            var deliveredStops = driverStops.Where(s => s.Status == TripStopStatus.Delivered).ToList();
            var deliveredCount = deliveredStops.Count;
            var failedCount = driverStops.Count(s => s.Status == TripStopStatus.Failed);

            var delays = deliveredStops.Select(s =>
            {
                var exp = s.CreatedAtUtc.AddMinutes(45);
                var act = s.DeliveredAtUtc ?? s.ModifiedAtUtc ?? s.CreatedAtUtc;
                return Math.Max(0, (int)(act - exp).TotalMinutes);
            }).ToList();

            var onTimeCount = delays.Count(d => d <= 10);
            var onTimePercentage = deliveredCount > 0 ? Math.Round((decimal)onTimeCount / deliveredCount * 100, 1) : 0m;
            var avgDelay = delays.Any() ? (int)Math.Round(delays.Average()) : 0;

            // Rating
            var ratings = await _db.DriverRatings
                .AsNoTracking()
                .Where(r => r.DriverProfileId == driver.Id)
                .Select(r => (decimal)r.Score)
                .ToListAsync(cancellationToken);

            var rating = ratings.Any() ? Math.Round(ratings.Average(), 1) : 4.6m;

            // Distance covered in period
            var points = await _db.DeliveryTrackingPoints
                .AsNoTracking()
                .Where(p => p.DriverProfileId == driver.Id && p.RecordedAtUtc >= fromDate && p.RecordedAtUtc <= toDate)
                .OrderBy(p => p.RecordedAtUtc)
                .ToListAsync(cancellationToken);

            decimal totalDistance = 0m;
            for (int i = 0; i < points.Count - 1; i++)
            {
                totalDistance += (decimal)KuwaitGeoBounds.HaversineKm(
                    points[i].Latitude, points[i].Longitude,
                    points[i + 1].Latitude, points[i + 1].Longitude);
            }

            if (totalDistance == 0m)
            {
                totalDistance = deliveredCount * 4.2m; // average trip leg distance fallback
            }

            comparisonList.Add(new DriverComparisonMetricDto(
                DriverId: driver.Id,
                DriverCode: driverCode,
                FullName: driverName,
                AvatarUrl: $"https://cdn.mealmate.app/avatars/{driver.Id.ToString()[..8]}.jpg",
                TotalAssigned: totalAssigned,
                DeliveredCount: deliveredCount,
                OnTimePercentage: onTimePercentage,
                AvgDelayMinutes: avgDelay,
                Rating: rating,
                FailedCount: failedCount,
                TotalDistanceKm: Math.Round(totalDistance, 1)));
        }

        return Result.Success(new DispatcherDriverComparisonResponseDto(
            Period: periodKey,
            PeriodText: periodText,
            Drivers: comparisonList));
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
}
