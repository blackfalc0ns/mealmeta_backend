using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record GetDriverHomeScreenQuery(Guid? DriverId = null) : IQuery<DriverHomeScreenResponse>;

public sealed record GetDriverShiftSummaryQuery(Guid? DriverId = null, Guid? SessionId = null) : IQuery<DriverShiftSummaryResponse>;

public sealed class DriverHomeScreenQueryHandlers
    : IQueryHandler<GetDriverHomeScreenQuery, DriverHomeScreenResponse>,
      IQueryHandler<GetDriverShiftSummaryQuery, DriverShiftSummaryResponse>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public DriverHomeScreenQueryHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DriverHomeScreenResponse>> Handle(
        GetDriverHomeScreenQuery request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق.");
        }

        var isAr = _messages.IsArabic();
        var now = DateTime.UtcNow;

        var driverName = isAr ? driver.FullNameAr : driver.FullNameEn;
        if (string.IsNullOrWhiteSpace(driverName)) driverName = driver.FullNameAr ?? driver.FullNameEn ?? (isAr ? "سائق MealMate" : "MealMate Driver");

        var driverCode = !string.IsNullOrWhiteSpace(driver.NationalId)
            ? $"DRV-{Math.Abs(driver.NationalId.GetHashCode()) % 9000 + 1000}"
            : $"DRV-{driver.Id.ToString("N")[..6].ToUpperInvariant()}";

        var activeSession = await _db.DriverWorkSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);

        var isAvailable = driver.IsAvailable;
        var shiftStatus = activeSession is not null ? "Active" : (isAvailable ? "Active" : "Offline");

        var currentStatusText = isAvailable
            ? (isAr ? "خارج التوصيل (توصيل الطلب)" : "Out for delivery (Delivering order)")
            : (isAr ? "غير متاح للعمل" : "Offline for work");

        // Current delivery task & next location text from real active stop
        DriverCurrentDeliveryTaskDto? currentTask = null;
        string? nextLocationText = null;

        if (isAvailable)
        {
            var activeStop = await (
                from stop in _db.TripStops
                join da in _db.DriverAssignments on stop.TripId equals da.TripId
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
        }

        // Today's summary computed from real driver trip stops
        var startOfDayUtc = DateTime.UtcNow.Date;
        var endOfDayUtc = startOfDayUtc.AddDays(1);

        var todayStops = await (
            from stop in _db.TripStops
            join da in _db.DriverAssignments on stop.TripId equals da.TripId
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

        var todaySummary = new DriverTodaySummaryDto(
            TotalOrders: totalOrders,
            DeliveredOrders: deliveredOrders,
            InDeliveryOrders: inDeliveryOrders,
            IncompleteOrders: incompleteOrders
        );

        var onTimeRate = totalOrders > 0 ? Math.Round((double)deliveredOrders / totalOrders * 100, 1) : 0.0;
        var distanceKm = activeSession?.TotalDistanceKm > 0 ? activeSession.TotalDistanceKm : 0.0;

        var dailyPerformance = new DriverDailyPerformanceDto(
            OnTimeRatePercentage: onTimeRate,
            DistanceCoveredKm: distanceKm,
            AverageDeliveryMinutes: 0.0
        );

        var targetOrders = activeSession?.DailyTargetOrders ?? (totalOrders > 0 ? totalOrders : 0);
        var completedOrders = activeSession?.CompletedOrdersCount ?? deliveredOrders;
        var remainingOrders = Math.Max(0, targetOrders - completedOrders);
        var progressPct = targetOrders > 0 ? Math.Round((double)completedOrders / targetOrders * 100.0, 1) : 0.0;

        var targetProgress = new DriverTargetProgressDto(
            CompletedCount: completedOrders,
            TargetCount: targetOrders,
            RemainingCount: remainingOrders,
            ProgressPercentage: progressPct,
            PerformanceLevel: progressPct >= 80 ? (isAr ? "ممتاز" : "Excellent") : (isAr ? "جيد" : "Good"),
            SummaryText: isAr ? $"أتممت {completedOrders} من إجمالي {targetOrders} طلبات" : $"Completed {completedOrders} of {targetOrders} orders",
            RemainingText: isAr ? $"تبقى {remainingOrders} طلبات لإتمام الهدف" : $"{remainingOrders} orders remaining"
        );

        DriverActiveSessionDto? activeSessionDto = null;
        if (activeSession is not null)
        {
            var duration = now - activeSession.StartedAtUtc;
            var h = (int)duration.TotalHours;
            var m = duration.Minutes;
            var durationText = isAr ? $"{h} س {m} د" : $"{h}h {m}m";

            activeSessionDto = new DriverActiveSessionDto(
                SessionId: activeSession.Id,
                StartedAtUtc: activeSession.StartedAtUtc,
                WorkDurationText: durationText
            );
        }

        var unreadNotificationsCount = await _db.DriverNotifications
            .CountAsync(n => n.DriverProfileId == driver.Id && !n.IsRead, cancellationToken);

        var response = new DriverHomeScreenResponse(
            DriverId: driver.Id,
            DriverName: driverName,
            DriverCode: driverCode,
            ProfileImageUrl: driver.ProfileImageStorageKey,
            ShiftStatus: shiftStatus,
            IsAvailable: isAvailable,
            CurrentStatusText: currentStatusText,
            NextLocationText: nextLocationText,
            TargetProgress: targetProgress,
            CurrentDeliveryTask: currentTask,
            TodaySummary: todaySummary,
            DailyPerformance: dailyPerformance,
            ActiveSession: activeSessionDto,
            UnreadNotificationsCount: unreadNotificationsCount
        );

        return Result.Success(response);
    }

    public async Task<Result<DriverShiftSummaryResponse>> Handle(
        GetDriverShiftSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق.");
        }

        var isAr = _messages.IsArabic();
        var now = DateTime.UtcNow;

        var driverName = isAr ? driver.FullNameAr : driver.FullNameEn;
        if (string.IsNullOrWhiteSpace(driverName)) driverName = driver.FullNameAr ?? driver.FullNameEn ?? (isAr ? "سائق MealMate" : "MealMate Driver");

        var driverCode = !string.IsNullOrWhiteSpace(driver.NationalId)
            ? $"DRV-{Math.Abs(driver.NationalId.GetHashCode()) % 9000 + 1000}"
            : $"DRV-{driver.Id.ToString("N")[..6].ToUpperInvariant()}";

        var session = request.SessionId.HasValue
            ? await _db.DriverWorkSessions.FirstOrDefaultAsync(s => s.Id == request.SessionId.Value, cancellationToken)
            : await _db.DriverWorkSessions.OrderByDescending(s => s.StartedAtUtc).FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id, cancellationToken);

        var startedAt = session?.StartedAtUtc ?? now.AddHours(-1);
        var endedAt = session?.EndedAtUtc ?? now;
        var duration = endedAt >= startedAt ? endedAt - startedAt : TimeSpan.Zero;

        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;
        var workingHoursText = isAr ? $"{hours}:{minutes:D2} ساعات" : $"{hours}:{minutes:D2} hrs";

        var deliveredCount = session?.CompletedOrdersCount ?? 0;
        var distanceKm = session?.TotalDistanceKm ?? 0.0;

        var culture = isAr ? new System.Globalization.CultureInfo("ar-KW") : System.Globalization.CultureInfo.InvariantCulture;
        var dateText = (session?.StartedAtUtc ?? now).ToString("dddd، d MMMM yyyy", culture);
        var workPeriodText = isAr
            ? $"فترة العمل: {startedAt:hh:mm tt} - {endedAt:hh:mm tt} ({hours} س {minutes} د)"
            : $"Work Period: {startedAt:hh:mm tt} - {endedAt:hh:mm tt} ({hours}h {minutes}m)";

        var response = new DriverShiftSummaryResponse(
            SessionId: session?.Id ?? Guid.NewGuid(),
            DriverName: driverName,
            DriverCode: driverCode,
            ZoneName: isAr ? "الكويت" : "Kuwait",
            DateText: dateText,
            WorkPeriodText: workPeriodText,
            WorkingHoursText: workingHoursText,
            TotalOrdersDelivered: deliveredCount,
            TotalDistanceKm: distanceKm,
            CelebrationTitle: isAr ? "أحسنت العمل اليوم!" : "Great work today!",
            CelebrationMessage: isAr
                ? "أنت تقدم تجربة ممتازة وتصنع الفرق كل يوم"
                : "You deliver an excellent experience and make a difference every day",
            MotivationalBadge: isAr
                ? "استمر بنفس التميز! أنت من أفضل السائقين لدينا، نتطلع لرؤيتك غداً 💜"
                : "Keep up the great work! You're one of our top drivers, looking forward to seeing you tomorrow 💜"
        );

        return Result.Success(response);
    }

    private Task<DriverProfile?> ResolveDriverAsync(Guid? driverId, CancellationToken cancellationToken)
        => DriverResolutionHelper.ResolveDriverAsync(_db, _currentUser, driverId, cancellationToken);
}
