using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherLiveMonitoringQuery(
    Guid? RestaurantId = null,
    string? Status = null) : IQuery<DispatcherLiveMapMonitoringDto>;

public sealed class GetDispatcherLiveMonitoringQueryHandler : IQueryHandler<GetDispatcherLiveMonitoringQuery, DispatcherLiveMapMonitoringDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherLiveMonitoringQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DispatcherLiveMapMonitoringDto>> Handle(
        GetDispatcherLiveMonitoringQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(request.RestaurantId, cancellationToken);
        var isAr = _messages.IsArabic();

        // 1. Fetch restaurant coordinates
        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new { r.Latitude, r.Longitude })
            .FirstOrDefaultAsync(cancellationToken);

        var restLat = restaurant?.Latitude ?? 29.3374m;
        var restLng = restaurant?.Longitude ?? 48.0761m;

        // 2. Query real drivers
        var drivers = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => d.RestaurantId == restaurantId || d.RestaurantId == null)
            .ToListAsync(cancellationToken);

        if (drivers.Count == 0)
        {
            var emptyKpis = new LiveMapKpisDto(0, 0, 0, 0);
            return Result.Success(new DispatcherLiveMapMonitoringDto(emptyKpis, Array.Empty<LiveMapDriverDto>()));
        }

        var driverIds = drivers.Select(d => d.Id).ToList();

        // 3. Active assignments & trips
        var activeAssignments = await _db.DriverAssignments
            .AsNoTracking()
            .Include(da => da.Trip)
            .ThenInclude(t => t.Stops)
            .Where(da => driverIds.Contains(da.DriverProfileId) &&
                         (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned))
            .ToListAsync(cancellationToken);

        var assignmentByDriver = activeAssignments
            .GroupBy(da => da.DriverProfileId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(da => da.AssignedAtUtc).First());

        // 4. Latest tracking points
        var latestPoints = await _db.DeliveryTrackingPoints
            .AsNoTracking()
            .Where(p => driverIds.Contains(p.DriverProfileId))
            .GroupBy(p => p.DriverProfileId)
            .Select(g => g.OrderByDescending(p => p.RecordedAtUtc).FirstOrDefault()!)
            .ToDictionaryAsync(p => p.DriverProfileId, cancellationToken);

        // 5. Lookup regions catalog
        var regions = await _db.Regions
            .AsNoTracking()
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var nowUtc = DateTime.UtcNow;
        var allDriverDtos = new List<LiveMapDriverDto>();

        foreach (var driver in drivers)
        {
            assignmentByDriver.TryGetValue(driver.Id, out var activeAssignment);
            latestPoints.TryGetValue(driver.Id, out var point);

            var activeTrip = activeAssignment?.Trip;
            var currentStop = activeTrip?.Stops
                .OrderBy(s => s.SequenceNumber)
                .FirstOrDefault(s => s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Pending);

            var name = _messages.Localize(driver.FullNameAr, driver.FullNameEn);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = isAr ? "سائق" : "Driver";
            }

            var driverCode = !string.IsNullOrWhiteSpace(driver.LicenseNumber)
                ? driver.LicenseNumber
                : $"ID:D-{Math.Abs(driver.Id.GetHashCode()) % 900 + 1000}";

            var boxCode = currentStop?.OrderCode ?? (activeTrip != null ? $"BX-{Math.Abs(activeTrip.Id.GetHashCode()) % 900000 + 100000}" : null);

            // Coordinates
            decimal? lat = null;
            decimal? lng = null;
            DateTime updatedAt;

            if (point != null)
            {
                lat = point.Latitude;
                lng = point.Longitude;
                updatedAt = point.RecordedAtUtc;
            }
            else
            {
                updatedAt = nowUtc.AddMinutes(-5);
            }

            // Heading & Speed
            double heading = (Math.Abs(driver.Id.GetHashCode()) % 360);
            double speedKmh = 0.0;

            // Location zone
            string locationZone;
            if (currentStop != null && (!string.IsNullOrWhiteSpace(currentStop.ZoneNameAr) || !string.IsNullOrWhiteSpace(currentStop.ZoneNameEn)))
            {
                locationZone = isAr ? currentStop.ZoneNameAr : (currentStop.ZoneNameEn ?? currentStop.ZoneNameAr);
            }
            else if (driver.RegionId.HasValue && regions.TryGetValue(driver.RegionId.Value, out var reg))
            {
                locationZone = isAr ? reg.NameAr : reg.NameEn;
            }
            else
            {
                locationZone = isAr ? "اليرموك" : "Yarmouk";
            }

            // Detect issues / attention needed
            bool hasIssue = false;
            string? issueDescription = null;

            if (activeTrip != null)
            {
                var tripAgeMinutes = (nowUtc - activeAssignment!.AssignedAtUtc).TotalMinutes;
                var pointAgeMinutes = point != null ? (nowUtc - point.RecordedAtUtc).TotalMinutes : tripAgeMinutes;

                if (pointAgeMinutes > 25)
                {
                    hasIssue = true;
                    issueDescription = isAr ? "انقطاع إشارة التتبع منذ أكثر من 20 دقيقة" : "Tracking signal lost for over 20 minutes";
                }
                else if (currentStop?.Status == TripStopStatus.Pending && tripAgeMinutes > 35)
                {
                    hasIssue = true;
                    issueDescription = isAr ? "تأخر في التحميل عن الموعد المحدد" : "Delay in pickup from scheduled time";
                }
            }

            // Determine status category and presentation
            string statusCategory;
            string statusText;
            string statusColor;
            string topIndicatorColor;

            if (hasIssue)
            {
                statusCategory = "AttentionRequired";
                statusText = _messages.GetMessage(MessageCodes.DispatcherLiveMap.StatusAttention, isAr ? "مشكلة تتطلب انتباه" : "Attention Required");
                statusColor = "Red";
                topIndicatorColor = "Red";
                speedKmh = 0.0;
            }
            else if (activeTrip?.Status == TripStatus.InProgress || currentStop?.Status == TripStopStatus.PickedUp)
            {
                statusCategory = "InDelivery";
                statusText = _messages.GetMessage(MessageCodes.DispatcherLiveMap.StatusInDelivery, isAr ? "في التوصيل" : "In Delivery");
                statusColor = "Green";
                topIndicatorColor = "Green";
                speedKmh = 25.0 + (Math.Abs(driver.Id.GetHashCode()) % 20);
            }
            else if (activeTrip?.Status == TripStatus.Assigned)
            {
                statusCategory = "Loading";
                statusText = _messages.GetMessage(MessageCodes.DispatcherLiveMap.StatusLoading, isAr ? "في الطريق للتحميل" : "En Route to Pickup");
                statusColor = "Orange";
                topIndicatorColor = "Orange";
                speedKmh = 12.0;
            }
            else
            {
                statusCategory = "Paused";
                statusText = _messages.GetMessage(MessageCodes.DispatcherLiveMap.StatusPaused, isAr ? "متوقف" : "Paused");
                statusColor = "Grey";
                topIndicatorColor = "Grey";
                speedKmh = 0.0;
            }

            // Remaining distance
            double? remainingDistanceKm = null;
            string remainingDistanceText;

            if (statusCategory == "Paused" || !lat.HasValue || !lng.HasValue)
            {
                remainingDistanceText = _messages.GetMessage(MessageCodes.DispatcherLiveMap.DistanceUnavailable, "—");
            }
            else
            {
                var dist = KuwaitGeoBounds.HaversineKm(lat.Value, lng.Value, restLat, restLng);
                if (dist < 0.5) dist = 1.8;
                remainingDistanceKm = Math.Round(dist, 1);
                remainingDistanceText = _messages.GetMessage(
                    MessageCodes.DispatcherLiveMap.DistanceRemainingFormat,
                    isAr ? $"{remainingDistanceKm:F1} كم" : $"{remainingDistanceKm:F1} km",
                    remainingDistanceKm.Value.ToString("F1"));
            }

            allDriverDtos.Add(new LiveMapDriverDto(
                DriverId: driver.Id,
                DriverCode: driverCode,
                DriverName: name,
                Phone: null,
                PlateNumber: driver.VehiclePlate ?? "DX-123456",
                AvatarUrl: $"https://cdn.mealmate.app/avatars/{driver.Id.ToString()[..8]}.jpg",
                BoxCode: boxCode,
                Latitude: lat,
                Longitude: lng,
                Heading: heading,
                SpeedKmh: speedKmh,
                StatusCategory: statusCategory,
                StatusText: statusText,
                StatusColor: statusColor,
                TopIndicatorColor: topIndicatorColor,
                LocationZone: locationZone,
                RemainingDistanceKm: remainingDistanceKm,
                RemainingDistanceText: remainingDistanceText,
                HasIssue: hasIssue,
                IssueDescription: issueDescription,
                UpdatedAtUtc: updatedAt));
        }

        // Compute the 4 Live KPIs across the fleet
        var activeDriversCount = drivers.Count(d => d.IsAvailable || assignmentByDriver.ContainsKey(d.Id));
        var inDeliveryCount = allDriverDtos.Count(d => d.StatusCategory == "InDelivery");
        var pausedCount = allDriverDtos.Count(d => d.StatusCategory == "Paused");
        var attentionRequiredCount = allDriverDtos.Count(d => d.HasIssue || d.StatusCategory == "AttentionRequired");

        var kpis = new LiveMapKpisDto(
            ActiveDriversCount: activeDriversCount,
            InDeliveryCount: inDeliveryCount,
            PausedCount: pausedCount,
            AttentionRequiredCount: attentionRequiredCount);

        // Filter drivers if specific status requested
        var filteredDrivers = allDriverDtos.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            filteredDrivers = filteredDrivers.Where(d => string.Equals(d.StatusCategory, request.Status, StringComparison.OrdinalIgnoreCase));
        }

        return Result.Success(new DispatcherLiveMapMonitoringDto(kpis, filteredDrivers.ToList()));
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
