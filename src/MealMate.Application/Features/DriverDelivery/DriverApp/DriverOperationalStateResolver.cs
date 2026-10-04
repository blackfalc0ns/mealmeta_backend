using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record DriverOperationalState(
    string ShiftStatus,
    bool IsAvailable,
    string CurrentStatusText);

/// <summary>
/// Single source of the driver's displayed operational status. Operational status is
/// owned by the delivery supervisor: it is Active only while an active
/// <see cref="DriverWorkSession"/> exists, never derived from availability.
/// GET home and the shift-status broadcast must produce identical values, so every
/// producer must resolve through this type.
/// </summary>
public static class DriverOperationalStateResolver
{
    public const string StatusActive = "Active";
    public const string StatusInactive = "Inactive";

    public static DriverOperationalState Resolve(DriverProfile driver, DriverWorkSession? activeSession, bool isArabic)
    {
        // The stored availability flag is reported as-is; it is never used to
        // upgrade an Inactive driver and legacy data is never silently fixed here.
        if (activeSession is null)
        {
            return new DriverOperationalState(StatusInactive, driver.IsAvailable, isArabic ? "غير متصل" : "Offline");
        }

        var statusText = driver.IsAvailable
            ? (isArabic ? "متصل ومتاح لاستلام الطلبات" : "Online and available for new orders")
            : (isArabic ? "خارج التوصيل (توصيل الطلب)" : "Out for delivery (Delivering order)");

        return new DriverOperationalState(StatusActive, driver.IsAvailable, statusText);
    }
}

/// <summary>
/// Builds the driver home screen payload from real sources. Shared by the GET home
/// query and the shift commands so no second path can reintroduce an Offline
/// status or gate the current delivery task behind availability.
/// </summary>
public static class DriverHomeScreenBuilder
{
    public static async Task<DriverHomeScreenResponse> BuildAsync(
        IAppDbContext db,
        ILocalizedMessageProvider messages,
        DriverProfile driver,
        CancellationToken cancellationToken)
    {
        var isAr = messages.IsArabic();
        var now = DateTime.UtcNow;

        var driverName = isAr ? driver.FullNameAr : driver.FullNameEn;
        if (string.IsNullOrWhiteSpace(driverName)) driverName = driver.FullNameAr ?? driver.FullNameEn ?? (isAr ? "سائق MealMate" : "MealMate Driver");

        var driverCode = !string.IsNullOrWhiteSpace(driver.NationalId)
            ? $"DRV-{Math.Abs(driver.NationalId.GetHashCode()) % 9000 + 1000}"
            : $"DRV-{driver.Id.ToString("N")[..6].ToUpperInvariant()}";

        var activeSession = await db.DriverWorkSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);

        var state = DriverOperationalStateResolver.Resolve(driver, activeSession, isAr);
        var isActive = state.ShiftStatus == DriverOperationalStateResolver.StatusActive;

        DriverCurrentDeliveryTaskDto? currentTask = null;
        string? nextLocationText = null;
        DriverTargetProgressDto? targetProgress = null;
        DriverTodaySummaryDto? todaySummary = null;
        DriverDailyPerformanceDto? dailyPerformance = null;
        DriverActiveSessionDto? activeSessionDto = null;
        var unreadNotificationsCount = 0;

        if (isActive)
        {
            // Current delivery task & next location from the real active stop —
            // deliberately not conditioned on availability, so a busy driver
            // keeps seeing their order.
            var activeStop = await (
                from stop in db.TripStops
                join da in db.DriverAssignments on stop.TripId equals da.TripId
                where da.DriverProfileId == driver.Id
                      && da.Status == "Active"
                      && (stop.Status == TripStopStatus.PickedUp || stop.Status == TripStopStatus.Pending)
                orderby stop.SequenceNumber ascending
                select stop
            ).FirstOrDefaultAsync(cancellationToken);

            if (activeStop is not null)
            {
                var nextZone = isAr ? activeStop.ZoneNameAr : activeStop.ZoneNameEn;
                nextLocationText = !string.IsNullOrWhiteSpace(nextZone)
                    ? (isAr ? $"الموقع التالي: {nextZone}" : $"Next Stop: {nextZone}")
                    : null;

                var mealSummary = isAr ? activeStop.MealSummaryAr : activeStop.MealSummaryEn;
                if (string.IsNullOrWhiteSpace(mealSummary)) mealSummary = isAr ? $"{activeStop.BoxCount} وجبات" : $"{activeStop.BoxCount} meals";

                currentTask = new DriverCurrentDeliveryTaskDto(
                    BoxId: activeStop.Id,
                    BoxCode: activeStop.OrderCode ?? $"BX-{activeStop.Id.ToString()[..8]}",
                    CustomerName: activeStop.CustomerName ?? string.Empty,
                    CustomerPhone: activeStop.CustomerPhone ?? string.Empty,
                    DeliveryAddress: isAr ? (activeStop.ZoneNameAr ?? string.Empty) : (activeStop.ZoneNameEn ?? string.Empty),
                    DestinationLatitude: (double?)activeStop.Latitude,
                    DestinationLongitude: (double?)activeStop.Longitude,
                    MealsCount: activeStop.BoxCount > 0 ? activeStop.BoxCount : 1,
                    MealsSummary: mealSummary,
                    DeliveryTimeSlot: activeStop.SlotLabel ?? string.Empty,
                    Status: activeStop.Status.ToString(),
                    DeliveryNotes: null
                );
            }

            // Today's summary computed from real driver trip stops.
            var startOfDayUtc = DateTime.UtcNow.Date;
            var endOfDayUtc = startOfDayUtc.AddDays(1);

            var todayStops = await (
                from stop in db.TripStops
                join da in db.DriverAssignments on stop.TripId equals da.TripId
                where da.DriverProfileId == driver.Id
                      && stop.CreatedAtUtc >= startOfDayUtc
                      && stop.CreatedAtUtc < endOfDayUtc
                select stop.Status
            ).ToListAsync(cancellationToken);

            var totalOrders = todayStops.Count;
            var deliveredOrders = todayStops.Count(s => s == TripStopStatus.Delivered);
            var inDeliveryOrders = todayStops.Count(s => s == TripStopStatus.PickedUp);
            var incompleteOrders = todayStops.Count(s => s == TripStopStatus.Failed || s == TripStopStatus.ReturnedToKitchen);

            if (totalOrders == 0 && activeSession != null && (activeSession.DailyTargetOrders > 0 || activeSession.CompletedOrdersCount > 0))
            {
                totalOrders = activeSession.DailyTargetOrders;
                deliveredOrders = activeSession.CompletedOrdersCount;
                inDeliveryOrders = Math.Max(0, totalOrders - deliveredOrders);
            }

            todaySummary = new DriverTodaySummaryDto(
                TotalOrders: totalOrders,
                DeliveredOrders: deliveredOrders,
                InDeliveryOrders: inDeliveryOrders,
                IncompleteOrders: incompleteOrders);

            var onTimeRate = totalOrders > 0 ? Math.Round((double)deliveredOrders / totalOrders * 100, 1) : 0.0;
            var distanceKm = activeSession?.TotalDistanceKm > 0 ? activeSession.TotalDistanceKm : 0.0;

            dailyPerformance = new DriverDailyPerformanceDto(
                OnTimeRatePercentage: onTimeRate,
                DistanceCoveredKm: distanceKm,
                AverageDeliveryMinutes: 0.0);

            var targetOrders = activeSession?.DailyTargetOrders ?? (totalOrders > 0 ? totalOrders : 0);
            var completedOrders = activeSession?.CompletedOrdersCount ?? deliveredOrders;
            var remainingOrders = Math.Max(0, targetOrders - completedOrders);
            var progressPct = targetOrders > 0 ? Math.Round((double)completedOrders / targetOrders * 100.0, 1) : 0.0;

            targetProgress = new DriverTargetProgressDto(
                CompletedCount: completedOrders,
                TargetCount: targetOrders,
                RemainingCount: remainingOrders,
                ProgressPercentage: progressPct,
                PerformanceLevel: progressPct >= 80 ? (isAr ? "ممتاز" : "Excellent") : (isAr ? "جيد" : "Good"),
                SummaryText: isAr ? $"أتممت {completedOrders} من إجمالي {targetOrders} طلبات" : $"Completed {completedOrders} of {targetOrders} orders",
                RemainingText: isAr ? $"تبقى {remainingOrders} طلبات لإتمام الهدف" : $"{remainingOrders} orders remaining");

            if (activeSession is not null)
            {
                var duration = now - activeSession.StartedAtUtc;
                var h = (int)duration.TotalHours;
                var m = duration.Minutes;
                var durationText = isAr ? $"{h} س {m} د" : $"{h}h {m}m";

                activeSessionDto = new DriverActiveSessionDto(
                    SessionId: activeSession.Id,
                    StartedAtUtc: activeSession.StartedAtUtc,
                    WorkDurationText: durationText);
            }

            unreadNotificationsCount = await db.DriverNotifications
                .CountAsync(n => n.DriverProfileId == driver.Id && !n.IsRead, cancellationToken);
        }

        return new DriverHomeScreenResponse(
            DriverId: driver.Id,
            DriverName: driverName,
            DriverCode: driverCode,
            ProfileImageUrl: driver.ProfileImageStorageKey,
            ShiftStatus: state.ShiftStatus,
            IsAvailable: state.IsAvailable,
            CurrentStatusText: state.CurrentStatusText,
            NextLocationText: nextLocationText,
            TargetProgress: targetProgress,
            CurrentDeliveryTask: currentTask,
            TodaySummary: todaySummary,
            DailyPerformance: dailyPerformance,
            ActiveSession: activeSessionDto,
            // Fixed contract: the Inactive screen always reports zero unread
            // notifications (approved product decision); the real count returns
            // as soon as the driver is activated.
            UnreadNotificationsCount: unreadNotificationsCount);
    }
}
