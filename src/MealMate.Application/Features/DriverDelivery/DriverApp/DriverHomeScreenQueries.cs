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

/// <summary>
/// Resolves the authenticated user's driver profile id. Used by DriverHub to bind
/// SignalR connections to driver groups from the JWT identity only.
/// </summary>
public sealed record GetDriverIdentityForCurrentUserQuery(Guid? DriverId = null) : IQuery<DriverIdentityForCurrentUserResponse>;

public sealed record DriverIdentityForCurrentUserResponse(Guid DriverProfileId, Guid UserId);

public sealed class DriverHomeScreenQueryHandlers
    : IQueryHandler<GetDriverHomeScreenQuery, DriverHomeScreenResponse>,
      IQueryHandler<GetDriverShiftSummaryQuery, DriverShiftSummaryResponse>,
      IQueryHandler<GetDriverIdentityForCurrentUserQuery, DriverIdentityForCurrentUserResponse>
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

    public async Task<Result<DriverIdentityForCurrentUserResponse>> Handle(
        GetDriverIdentityForCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        var driver = await ResolveDriverAsync(request.DriverId, cancellationToken);
        if (driver is null)
        {
            return Error.NotFound(MessageCodes.Common.NotFound, "لم يتم العثور على حساب السائق.");
        }

        return Result.Success(new DriverIdentityForCurrentUserResponse(driver.Id, driver.UserId));
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

        // GET home is a pure read: it never opens a session, never flips
        // availability, and never repairs legacy data as a side effect.
        var response = await DriverHomeScreenBuilder.BuildAsync(_db, _messages, driver, cancellationToken);
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
