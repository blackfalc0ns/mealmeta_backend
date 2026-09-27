using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherDriverDetailsQuery(Guid DriverId) : IQuery<DispatcherDriverDetailsResponseDto>;

public sealed class GetDispatcherDriverDetailsQueryHandler : IQueryHandler<GetDispatcherDriverDetailsQuery, DispatcherDriverDetailsResponseDto>
{
    private static readonly TimeZoneInfo KuwaitTimeZone = ResolveKuwaitTimeZone();

    private readonly IAppDbContext _db;
    private readonly ICurrentUser? _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    [ActivatorUtilitiesConstructor]
    public GetDispatcherDriverDetailsQueryHandler(
        IAppDbContext db,
        ICurrentUser? currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public GetDispatcherDriverDetailsQueryHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages)
        : this(db, null, messages)
    {
    }

    public async Task<Result<DispatcherDriverDetailsResponseDto>> Handle(
        GetDispatcherDriverDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var driver = await _db.DriverProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.DriverId, cancellationToken);

        if (driver is null)
        {
            return Error.NotFound(
                MessageCodes.Dispatcher.DriverNotFound,
                _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "Driver profile was not found."));
        }

        // Restaurant tenant isolation: cross-tenant driver queries return 404
        if (_currentUser != null && _currentUser.IsAuthenticated && _currentUser.RestaurantId.HasValue && _currentUser.RestaurantId.Value != Guid.Empty)
        {
            if (driver.RestaurantId != _currentUser.RestaurantId.Value)
            {
                return Error.NotFound(
                    MessageCodes.Dispatcher.DriverNotFound,
                    _messages.GetMessage(MessageCodes.Dispatcher.DriverNotFound, "Driver profile was not found."));
            }
        }

        var nowUtc = DateTime.UtcNow;
        var kuwaitNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, KuwaitTimeZone);
        var kuwaitToday = DateOnly.FromDateTime(kuwaitNow);
        var kuwaitStartOfDayUtc = TimeZoneInfo.ConvertTimeToUtc(kuwaitToday.ToDateTime(TimeOnly.MinValue), KuwaitTimeZone);
        var kuwaitEndOfDayUtc = TimeZoneInfo.ConvertTimeToUtc(kuwaitToday.ToDateTime(TimeOnly.MaxValue), KuwaitTimeZone);

        // 1. Phone number from Driver Registration (zero mock fallback)
        var phone = await _db.DriverRegistrations
            .AsNoTracking()
            .Where(r => r.ResultingDriverProfileId == driver.Id || (r.SubmittedByUserId.HasValue && r.SubmittedByUserId.Value == driver.UserId))
            .Select(r => r.Phone)
            .FirstOrDefaultAsync(cancellationToken);

        // 2. Active deliveries count (Pending or PickedUp on InProgress or Assigned trips)
        var activeDeliveries = await _db.TripStops
            .AsNoTracking()
            .Where(s => s.Trip.DriverAssignments.Any(da => da.DriverProfileId == driver.Id &&
                                                           (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned)) &&
                        (s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Pending))
            .CountAsync(cancellationToken);

        // 3. Completed deliveries today (Kuwait day boundary in UTC)
        var completedDeliveries = await _db.TripStops
            .AsNoTracking()
            .Where(s => s.Trip.DriverAssignments.Any(da => da.DriverProfileId == driver.Id) &&
                        s.Status == TripStopStatus.Delivered &&
                        s.DeliveredAtUtc >= kuwaitStartOfDayUtc &&
                        s.DeliveredAtUtc <= kuwaitEndOfDayUtc)
            .CountAsync(cancellationToken);

        // 4. Failed deliveries today
        var failedDeliveries = await _db.TripStops
            .AsNoTracking()
            .Where(s => s.Trip.DriverAssignments.Any(da => da.DriverProfileId == driver.Id) &&
                        s.Status == TripStopStatus.Failed &&
                        ((s.ModifiedAtUtc >= kuwaitStartOfDayUtc && s.ModifiedAtUtc <= kuwaitEndOfDayUtc) ||
                         (s.CreatedAtUtc >= kuwaitStartOfDayUtc && s.CreatedAtUtc <= kuwaitEndOfDayUtc)))
            .CountAsync(cancellationToken);

        // 5. Tracking points for today & distance calculation (zero mock multiplier)
        var trackingPoints = await _db.DeliveryTrackingPoints
            .AsNoTracking()
            .Where(p => p.DriverProfileId == driver.Id &&
                        p.RecordedAtUtc >= kuwaitStartOfDayUtc &&
                        p.RecordedAtUtc <= kuwaitEndOfDayUtc)
            .OrderBy(p => p.RecordedAtUtc)
            .ToListAsync(cancellationToken);

        var latestPoint = trackingPoints.LastOrDefault();
        if (latestPoint is null)
        {
            latestPoint = await _db.DeliveryTrackingPoints
                .AsNoTracking()
                .Where(p => p.DriverProfileId == driver.Id)
                .OrderByDescending(p => p.RecordedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }

        double totalDistanceKm = 0.0;
        if (trackingPoints.Count > 1)
        {
            for (var i = 1; i < trackingPoints.Count; i++)
            {
                var prev = trackingPoints[i - 1];
                var curr = trackingPoints[i];
                totalDistanceKm += KuwaitGeoBounds.HaversineKm(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
            }
        }
        var distanceKm = Math.Round(totalDistanceKm, 1);

        // 6. Operational status
        string operationalStatus;
        if (activeDeliveries > 0)
        {
            operationalStatus = "InDelivery";
        }
        else if (driver.IsAvailable)
        {
            operationalStatus = "Available";
        }
        else
        {
            operationalStatus = "Unavailable";
        }

        // 7. Performance metrics (real database sources only)
        var totalDelivered = await _db.TripStops
            .AsNoTracking()
            .Where(s => s.Trip.DriverAssignments.Any(da => da.DriverProfileId == driver.Id) &&
                        s.Status == TripStopStatus.Delivered)
            .CountAsync(cancellationToken);

        var scores = await _db.DriverRatings
            .AsNoTracking()
            .Where(r => r.DriverProfileId == driver.Id)
            .Select(r => (decimal)r.Score)
            .ToListAsync(cancellationToken);

        decimal? averageRating = scores.Count > 0 ? Math.Round(scores.Average(), 1) : null;
        var ratingsCount = scores.Count;

        var stat = await _db.DriverPerformanceStats
            .AsNoTracking()
            .Where(s => s.DriverProfileId == driver.Id)
            .OrderByDescending(s => s.PeriodEnd)
            .FirstOrDefaultAsync(cancellationToken);

        double? onTimeRatePercent = stat?.OnTimeRate != null ? (double?)stat.OnTimeRate.AsPercent : null;

        var violationsCount = await _db.Complaints
            .AsNoTracking()
            .Where(c => c.DriverProfileId == driver.Id && c.Status != ComplaintStatus.Rejected)
            .CountAsync(cancellationToken);

        // 8. Vehicle & Documents
        var vehicle = new DriverVehicleDetailsDto(
            Type: driver.VehicleType,
            Model: driver.VehicleModel,
            Year: driver.VehicleYear,
            Color: driver.VehicleColor,
            PlateNumber: driver.VehiclePlate);

        var currentLocation = new DriverLocationDto(
            Latitude: latestPoint?.Latitude,
            Longitude: latestPoint?.Longitude,
            RecordedAtUtc: latestPoint?.RecordedAtUtc,
            Heading: null,
            SpeedKmh: null);

        var verificationStatusStr = driver.VerificationStatus.ToString();
        var documents = new List<DriverDocumentDto>
        {
            new DriverDocumentDto(
                Type: "DriverLicense",
                Status: EvaluateDocumentStatus(driver.LicenseExpiryUtc, nowUtc),
                VerificationStatus: verificationStatusStr,
                ExpiresAtUtc: driver.LicenseExpiryUtc),
            new DriverDocumentDto(
                Type: "VehicleRegistration",
                Status: EvaluateDocumentStatus(driver.VehicleRegistrationExpiryUtc, nowUtc),
                VerificationStatus: verificationStatusStr,
                ExpiresAtUtc: driver.VehicleRegistrationExpiryUtc)
        };

        var name = _messages.Localize(driver.FullNameAr, driver.FullNameEn);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = !string.IsNullOrWhiteSpace(driver.FullNameEn)
                ? driver.FullNameEn
                : (!string.IsNullOrWhiteSpace(driver.FullNameAr) ? driver.FullNameAr : "Driver");
        }

        var driverCode = !string.IsNullOrWhiteSpace(driver.LicenseNumber)
            ? driver.LicenseNumber
            : null;

        var header = new DriverDetailsHeaderDto(
            DriverId: driver.Id,
            DriverCode: driverCode,
            FullName: name,
            PhoneNumber: phone,
            AvatarStorageKey: driver.ProfileImageStorageKey,
            IsAvailable: driver.IsAvailable,
            OperationalStatus: operationalStatus,
            LastLocationAtUtc: latestPoint?.RecordedAtUtc);

        var todayStats = new DriverTodayStatsDto(
            CompletedDeliveries: completedDeliveries,
            ActiveDeliveries: activeDeliveries,
            FailedDeliveries: failedDeliveries,
            DistanceKm: distanceKm);

        var performance = new DriverPerformanceDto(
            TotalDelivered: totalDelivered,
            AverageRating: averageRating,
            RatingsCount: ratingsCount,
            OnTimeRatePercent: onTimeRatePercent,
            ViolationsCount: violationsCount);

        return Result.Success(new DispatcherDriverDetailsResponseDto(
            Driver: header,
            Today: todayStats,
            Vehicle: vehicle,
            CurrentLocation: currentLocation,
            Performance: performance,
            Documents: documents));
    }

    private static string EvaluateDocumentStatus(DateTime? expiryUtc, DateTime nowUtc)
    {
        if (!expiryUtc.HasValue) return "Missing";
        if (expiryUtc.Value < nowUtc) return "Expired";
        if (expiryUtc.Value <= nowUtc.AddDays(30)) return "ExpiringSoon";
        return "Valid";
    }

    private static TimeZoneInfo ResolveKuwaitTimeZone()
    {
        foreach (var id in new[] { "Asia/Kuwait", "Arab Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Asia/Kuwait", TimeSpan.FromHours(3), "Kuwait Time", "Kuwait Time");
    }
}
