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
    private readonly IUserAccountService? _users;

    public DriverShiftCommandHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IUserAccountService? users = null)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _users = users;
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

        var eligibility = await DriverOperationalEligibility.CheckEligibilityAsync(
            _db, _users, driver, now, isAr, cancellationToken: cancellationToken);
        if (eligibility.IsFailure)
        {
            return eligibility.Error;
        }

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

        // Build the current screen state through the shared home builder so the
        // response cannot diverge from GET home.
        var response = await DriverHomeScreenBuilder.BuildAsync(_db, _messages, driver, cancellationToken);
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
}
