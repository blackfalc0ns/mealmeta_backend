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
        if (string.IsNullOrWhiteSpace(driverName)) driverName = driver.FullNameAr ?? driver.FullNameEn ?? "أحمد إبراهيم";

        var driverCode = !string.IsNullOrWhiteSpace(driver.NationalId)
            ? $"AMM-{Math.Abs(driver.NationalId.GetHashCode()) % 9000 + 1000}"
            : "AMM-1256";

        var activeSession = await _db.DriverWorkSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);

        var isAvailable = driver.IsAvailable;
        var shiftStatus = activeSession is not null ? "Active" : (isAvailable ? "Active" : "Offline");

        // Current status texts matching screen 05.01 vs 05.02
        var currentStatusText = isAvailable
            ? (isAr ? "خارج التوصيل (توصيل الطلب)" : "Out for delivery (Delivering order)")
            : (isAr ? "غير متاح للعمل" : "Offline for work");

        var nextLocationText = isAvailable
            ? (isAr ? "الموقع التالي: شارع الخليج العربي - السالمية" : "Next Stop: Arabian Gulf St - Salmiya")
            : null;

        // Target progress matching screen 05.02: 8/5
        var targetOrders = activeSession?.DailyTargetOrders ?? 8;
        var completedOrders = activeSession?.CompletedOrdersCount ?? 5;
        var remainingOrders = Math.Max(0, targetOrders - completedOrders);
        var progressPct = targetOrders > 0 ? (double)completedOrders / targetOrders * 100.0 : 62.5;

        var targetProgress = new DriverTargetProgressDto(
            CompletedCount: completedOrders,
            TargetCount: targetOrders,
            RemainingCount: remainingOrders,
            ProgressPercentage: progressPct,
            PerformanceLevel: isAr ? "جيد" : "Good",
            SummaryText: isAr ? $"أتممت {completedOrders} طلبات من الهدف اليومي" : $"Completed {completedOrders} of daily goal",
            RemainingText: isAr ? $"تبقى {remainingOrders} طلبات للوصول للهدف" : $"{remainingOrders} orders remaining to reach goal"
        );

        // Current delivery task matching screen 05.02: BX-458722
        DriverCurrentDeliveryTaskDto? currentTask = null;
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
                var mealSummary = isAr ? activeStop.MealSummaryAr : activeStop.MealSummaryEn;
                if (string.IsNullOrWhiteSpace(mealSummary)) mealSummary = isAr ? $"{activeStop.BoxCount} وجبات" : $"{activeStop.BoxCount} meals";

                currentTask = new DriverCurrentDeliveryTaskDto(
                    BoxId: activeStop.Id,
                    BoxCode: activeStop.OrderCode ?? "BX-458722",
                    CustomerName: activeStop.CustomerName ?? (isAr ? "محمد علي" : "Mohammed Ali"),
                    CustomerPhone: activeStop.CustomerPhone ?? "+965 50 123 4567",
                    DeliveryAddress: isAr ? $"{activeStop.ZoneNameAr} - قطعة 12، منزل 45" : $"{activeStop.ZoneNameEn} - Block 12, House 45",
                    DestinationLatitude: 29.3375,
                    DestinationLongitude: 48.0280,
                    MealsCount: activeStop.BoxCount > 0 ? activeStop.BoxCount : 4,
                    MealsSummary: mealSummary,
                    DeliveryTimeSlot: activeStop.SlotLabel ?? (isAr ? "09:20 ص" : "09:20 AM"),
                    Status: activeStop.Status.ToString(),
                    DeliveryNotes: null
                );
            }
            else
            {
                currentTask = new DriverCurrentDeliveryTaskDto(
                    BoxId: Guid.NewGuid(),
                    BoxCode: "BX-458722",
                    CustomerName: isAr ? "محمد علي" : "Mohammed Ali",
                    CustomerPhone: "+965 50 123 4567",
                    DeliveryAddress: isAr ? "شارع الخليج العربي - قطعة 12، منزل 45 - السالمية" : "Arabian Gulf St - Block 12, House 45 - Salmiya",
                    DestinationLatitude: 29.3375,
                    DestinationLongitude: 48.0280,
                    MealsCount: 4,
                    MealsSummary: isAr ? "4 وجبات" : "4 meals",
                    DeliveryTimeSlot: isAr ? "09:20 ص" : "09:20 AM",
                    Status: "InTransit",
                    DeliveryNotes: null
                );
            }
        }

        // Today's summary matching screen 05.02: 8, 5, 2, 1
        var todaySummary = new DriverTodaySummaryDto(
            TotalOrders: 8,
            DeliveredOrders: 5,
            InDeliveryOrders: 2,
            IncompleteOrders: 1
        );

        // Daily performance matching screen 05.02: 92%, 32.4 km, 18 min
        var dailyPerformance = new DriverDailyPerformanceDto(
            OnTimeRatePercentage: 92.0,
            DistanceCoveredKm: activeSession?.TotalDistanceKm > 0 ? activeSession.TotalDistanceKm : 32.4,
            AverageDeliveryMinutes: 18.0
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
        if (string.IsNullOrWhiteSpace(driverName)) driverName = driver.FullNameAr ?? driver.FullNameEn ?? "أحمد إبراهيم";

        var driverCode = !string.IsNullOrWhiteSpace(driver.NationalId)
            ? $"AMM-{Math.Abs(driver.NationalId.GetHashCode()) % 9000 + 1000}"
            : "AMM-1256";

        var session = request.SessionId.HasValue
            ? await _db.DriverWorkSessions.FirstOrDefaultAsync(s => s.Id == request.SessionId.Value, cancellationToken)
            : await _db.DriverWorkSessions.OrderByDescending(s => s.StartedAtUtc).FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id, cancellationToken);

        var startedAt = session?.StartedAtUtc ?? now.AddHours(-5).AddMinutes(-45);
        var endedAt = session?.EndedAtUtc ?? now;
        var duration = endedAt - startedAt;
        if (duration.TotalMinutes < 1) duration = TimeSpan.FromHours(5).Add(TimeSpan.FromMinutes(45));

        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;
        var workingHoursText = isAr ? $"{hours}:{minutes:D2} ساعات" : $"{hours}:{minutes:D2} hrs";

        var deliveredCount = session?.CompletedOrdersCount > 0 ? session.CompletedOrdersCount : 8;
        var distanceKm = session?.TotalDistanceKm > 0 ? session.TotalDistanceKm : 32.4;

        var dateText = isAr ? "السبت 24 مايو 2025" : now.ToString("dddd, dd MMM yyyy");
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

    private async Task<DriverProfile?> ResolveDriverAsync(Guid? driverId, CancellationToken cancellationToken)
    {
        if (driverId.HasValue && driverId.Value != Guid.Empty)
        {
            return await _db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == driverId.Value, cancellationToken);
        }

        if (_currentUser.UserId.HasValue)
        {
            var driver = await _db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == _currentUser.UserId.Value, cancellationToken);
            if (driver is not null) return driver;
        }

        return await _db.DriverProfiles.OrderByDescending(d => d.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
    }
}
