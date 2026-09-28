using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record StartDriverShiftCommand(
    Guid? DriverId = null,
    double? Latitude = null,
    double? Longitude = null,
    string? ShiftLabel = null,
    string? Notes = null) : ICommand<DriverHomeScreenResponse>;

public sealed record EndDriverShiftCommand(
    Guid? DriverId = null,
    double? Latitude = null,
    double? Longitude = null,
    string? Notes = null,
    bool ForceEndWithPendingReturns = false) : ICommand<DriverShiftSummaryResponse>;

public sealed class DriverShiftCommandHandlers
    : ICommandHandler<StartDriverShiftCommand, DriverHomeScreenResponse>,
      ICommandHandler<EndDriverShiftCommand, DriverShiftSummaryResponse>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public DriverShiftCommandHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DriverHomeScreenResponse>> Handle(
        StartDriverShiftCommand request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق.");
        }

        var isAr = _messages.IsArabic();
        var now = DateTime.UtcNow;

        // Check if there is already an active session
        var activeSession = await _db.DriverWorkSessions
            .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);

        if (activeSession is null)
        {
            activeSession = new DriverWorkSession
            {
                DriverProfileId = driver.Id,
                RestaurantId = driver.RestaurantId,
                StartedAtUtc = now,
                StartLatitude = request.Latitude,
                StartLongitude = request.Longitude,
                Status = "Active",
                ShiftLabel = request.ShiftLabel ?? (isAr ? "الوردية الصباحية" : "Morning Shift"),
                DailyTargetOrders = 8,
                Notes = request.Notes
            };

            _db.DriverWorkSessions.Add(activeSession);
        }

        driver.IsAvailable = true;
        await _db.SaveChangesAsync(cancellationToken);

        // Build current screen state
        var response = await BuildDriverHomeScreenResponseAsync(driver.Id, cancellationToken);
        return Result.Success(response);
    }

    public async Task<Result<DriverShiftSummaryResponse>> Handle(
        EndDriverShiftCommand request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق.");
        }

        var isAr = _messages.IsArabic();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        // Check active trips / pending deliveries
        var pendingStopsCount = await (
            from stop in _db.TripStops
            join da in _db.DriverAssignments on stop.TripId equals da.TripId
            where da.DriverProfileId == driver.Id
                  && da.Status == "Active"
                  && (stop.Status == TripStopStatus.Pending || stop.Status == TripStopStatus.PickedUp)
            select stop.Id
        ).CountAsync(cancellationToken);

        if (pendingStopsCount > 0 && !request.ForceEndWithPendingReturns)
        {
            var warningMessage = isAr
                ? $"لا يمكن إنهاء الوردية لوجود {pendingStopsCount} طلبات قيد التوصيل لم يتم إتمامها بعد. يرجى تسليمها أو إعادتها لمسؤول التوصيل."
                : $"Cannot end shift while {pendingStopsCount} deliveries are still active. Please complete or return them first.";

            return Error.Validation("Shift.PendingOrdersRemaining", warningMessage);
        }

        // Close active session
        var activeSession = await _db.DriverWorkSessions
            .FirstOrDefaultAsync(s => s.DriverProfileId == driver.Id && s.Status == "Active", cancellationToken);

        var startedAt = activeSession?.StartedAtUtc ?? now;
        var duration = now >= startedAt ? now - startedAt : TimeSpan.Zero;

        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;
        var workingHoursText = isAr ? $"{hours}:{minutes:D2} ساعات" : $"{hours}:{minutes:D2} hrs";

        var deliveredCount = await (
            from stop in _db.TripStops
            join da in _db.DriverAssignments on stop.TripId equals da.TripId
            where da.DriverProfileId == driver.Id
                  && stop.Status == TripStopStatus.Delivered
                  && stop.Trip.PlannedDate == today
            select stop.Id
        ).CountAsync(cancellationToken);

        if (deliveredCount == 0 && activeSession?.CompletedOrdersCount > 0)
        {
            deliveredCount = activeSession.CompletedOrdersCount;
        }

        var distanceKm = activeSession?.TotalDistanceKm ?? 0.0;

        if (activeSession is not null)
        {
            activeSession.Status = "Ended";
            activeSession.EndedAtUtc = now;
            activeSession.EndLatitude = request.Latitude;
            activeSession.EndLongitude = request.Longitude;
            activeSession.TotalDistanceKm = distanceKm;
            activeSession.CompletedOrdersCount = deliveredCount;
            activeSession.Notes = request.Notes ?? activeSession.Notes;
        }

        driver.IsAvailable = false;
        await _db.SaveChangesAsync(cancellationToken);

        var driverName = isAr ? driver.FullNameAr : driver.FullNameEn;
        if (string.IsNullOrWhiteSpace(driverName)) driverName = driver.FullNameAr ?? driver.FullNameEn ?? (isAr ? "سائق MealMate" : "MealMate Driver");

        var driverCode = !string.IsNullOrWhiteSpace(driver.NationalId)
            ? $"DRV-{Math.Abs(driver.NationalId.GetHashCode()) % 9000 + 1000}"
            : $"DRV-{driver.Id.ToString("N")[..6].ToUpperInvariant()}";

        var culture = isAr ? new System.Globalization.CultureInfo("ar-KW") : System.Globalization.CultureInfo.InvariantCulture;
        var dateText = now.ToString("dddd، d MMMM yyyy", culture);
        var workPeriodText = isAr
            ? $"فترة العمل: {startedAt:hh:mm tt} - {now:hh:mm tt} ({hours} س {minutes} د)"
            : $"Work Period: {startedAt:hh:mm tt} - {now:hh:mm tt} ({hours}h {minutes}m)";

        var response = new DriverShiftSummaryResponse(
            SessionId: activeSession?.Id ?? Guid.NewGuid(),
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

    private async Task<DriverHomeScreenResponse> BuildDriverHomeScreenResponseAsync(Guid driverId, CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        var driver = await _db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(d => d.Id == driverId, cancellationToken);
        var driverName = isAr ? driver?.FullNameAr : driver?.FullNameEn;
        if (string.IsNullOrWhiteSpace(driverName)) driverName = driver?.FullNameAr ?? driver?.FullNameEn ?? (isAr ? "سائق MealMate" : "MealMate Driver");

        var driverCode = !string.IsNullOrWhiteSpace(driver?.NationalId)
            ? $"DRV-{Math.Abs(driver.NationalId.GetHashCode()) % 9000 + 1000}"
            : (driver != null ? $"DRV-{driver.Id.ToString("N")[..6].ToUpperInvariant()}" : string.Empty);

        var activeSession = await _db.DriverWorkSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DriverProfileId == driverId && s.Status == "Active", cancellationToken);

        var shiftStatus = activeSession is not null ? "Active" : (driver?.IsAvailable == true ? "Active" : "Offline");
        var isAvailable = driver?.IsAvailable ?? false;

        // Current status texts
        var currentStatusText = isAvailable
            ? (isAr ? "خارج التوصيل (توصيل الطلب)" : "Out for delivery (Delivering order)")
            : (isAr ? "غير متاح للعمل" : "Offline for work");

        // Current delivery task & next location text
        DriverCurrentDeliveryTaskDto? currentTask = null;
        string? nextLocationText = null;

        var activeStop = await (
            from stop in _db.TripStops
            join da in _db.DriverAssignments on stop.TripId equals da.TripId
            where da.DriverProfileId == driverId
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
        }

        if (activeStop is not null)
        {
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

        // Today's summary computed from real driver trip stops
        var startOfDayUtc = DateTime.UtcNow.Date;
        var endOfDayUtc = startOfDayUtc.AddDays(1);

        var todayStops = await (
            from stop in _db.TripStops
            join da in _db.DriverAssignments on stop.TripId equals da.TripId
            where da.DriverProfileId == driverId
                  && stop.CreatedAtUtc >= startOfDayUtc
                  && stop.CreatedAtUtc < endOfDayUtc
            select stop.Status
        ).ToListAsync(cancellationToken);

        var totalOrders = todayStops.Count;
        var deliveredOrders = todayStops.Count(s => s == TripStopStatus.Delivered);
        var inDeliveryOrders = todayStops.Count(s => s == TripStopStatus.PickedUp);
        var incompleteOrders = todayStops.Count(s => s == TripStopStatus.Failed || s == TripStopStatus.ReturnedToKitchen);

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

        // Active session info
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
            .CountAsync(n => n.DriverProfileId == driverId && !n.IsRead, cancellationToken);

        return new DriverHomeScreenResponse(
            DriverId: driverId,
            DriverName: driverName,
            DriverCode: driverCode,
            ProfileImageUrl: driver?.ProfileImageStorageKey,
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
    }
}
