using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public interface IDispatchRecommendationEngine
{
    Task<DriverSuggestionDto?> RecommendDriverForBoxAsync(
        Guid restaurantId,
        string zoneName,
        decimal destinationLatitude,
        decimal destinationLongitude,
        string priority,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CandidateDriverDto>> GetCandidateDriversForBoxAsync(
        Guid restaurantId,
        string zoneName,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken = default);
}

public sealed class DispatchRecommendationEngine : IDispatchRecommendationEngine
{
    private readonly IAppDbContext _db;
    private readonly IGoogleRoutingService _routingService;
    private readonly ILocalizedMessageProvider _messages;

    public DispatchRecommendationEngine(
        IAppDbContext db,
        IGoogleRoutingService routingService,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _routingService = routingService;
        _messages = messages;
    }

    public async Task<DriverSuggestionDto?> RecommendDriverForBoxAsync(
        Guid restaurantId,
        string zoneName,
        decimal destinationLatitude,
        decimal destinationLongitude,
        string priority,
        CancellationToken cancellationToken = default)
    {
        var candidates = await GetCandidateDriversForBoxAsync(
            restaurantId, zoneName, destinationLatitude, destinationLongitude, cancellationToken);

        if (candidates.Count == 0)
        {
            return null;
        }

        // Strategy:
        // If Urgent or HighPriority -> Pick the Nearest driver to minimize delivery time.
        // Otherwise -> Pick the Least Loaded driver to balance shift workload.
        CandidateDriverDto chosen;
        string suggestionType;
        string label;

        if (string.Equals(priority, "Urgent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(priority, "HighPriority", StringComparison.OrdinalIgnoreCase))
        {
            chosen = candidates.OrderBy(c => c.DistanceKm).First();
            suggestionType = "Nearest";
            var fmt = _messages.GetMessage(MessageCodes.DispatcherOrders.SuggestionNearest, "اقتراح: {0} الأقرب", chosen.FullName);
            label = fmt;
        }
        else
        {
            chosen = candidates.OrderBy(c => c.ActiveOrdersCount).ThenBy(c => c.DistanceKm).First();
            suggestionType = "LeastLoaded";
            var fmt = _messages.GetMessage(MessageCodes.DispatcherOrders.SuggestionLeastLoaded, "الأقل ضغطاً: {0}", chosen.FullName);
            label = fmt;
        }

        return new DriverSuggestionDto(
            DriverId: chosen.DriverId,
            DriverName: chosen.FullName,
            AvatarUrl: chosen.AvatarUrl,
            SuggestionType: suggestionType,
            Label: label);
    }

    public async Task<IReadOnlyList<CandidateDriverDto>> GetCandidateDriversForBoxAsync(
        Guid restaurantId,
        string zoneName,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken = default)
    {
        var drivers = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => (d.RestaurantId == restaurantId || d.RestaurantId == null)
                        && !d.IsDeleted
                        && d.VerificationStatus != VerificationStatus.Rejected)
            .ToListAsync(cancellationToken);

        if (drivers.Count == 0)
        {
            return Array.Empty<CandidateDriverDto>();
        }

        var driverIds = drivers.Select(d => d.Id).ToList();

        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new { r.Latitude, r.Longitude })
            .FirstOrDefaultAsync(cancellationToken);

        var restLat = restaurant?.Latitude ?? 29.3375m;
        var restLng = restaurant?.Longitude ?? 48.0280m;

        // Latest tracking point per driver
        var latestPoints = await _db.DeliveryTrackingPoints
            .AsNoTracking()
            .Where(p => driverIds.Contains(p.DriverProfileId))
            .GroupBy(p => p.DriverProfileId)
            .Select(g => g.OrderByDescending(p => p.RecordedAtUtc).FirstOrDefault()!)
            .ToDictionaryAsync(p => p.DriverProfileId, cancellationToken);

        // Active assignments count per driver
        var activeCounts = await _db.DriverAssignments
            .AsNoTracking()
            .Where(da => driverIds.Contains(da.DriverProfileId) &&
                         (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned))
            .GroupBy(da => da.DriverProfileId)
            .Select(g => new { DriverId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DriverId, x => x.Count, cancellationToken);

        // Real active box counts per driver
        var activeBoxesMap = await _db.TripStops
            .AsNoTracking()
            .Where(s => s.Trip.DriverAssignments.Any(da => driverIds.Contains(da.DriverProfileId) && da.Status == "Active") &&
                        (s.Status == TripStopStatus.Pending || s.Status == TripStopStatus.PickedUp))
            .Select(s => new {
                DriverId = s.Trip.DriverAssignments.Where(da => da.Status == "Active").Select(da => da.DriverProfileId).FirstOrDefault(),
                s.BoxCount
            })
            .GroupBy(x => x.DriverId)
            .Select(g => new { DriverId = g.Key, TotalBoxes = g.Sum(x => x.BoxCount) })
            .ToDictionaryAsync(x => x.DriverId, x => x.TotalBoxes, cancellationToken);

        // Ratings
        var stats = await _db.DriverPerformanceStats
            .AsNoTracking()
            .Where(s => driverIds.Contains(s.DriverProfileId))
            .ToDictionaryAsync(s => s.DriverProfileId, cancellationToken);

        var list = new List<CandidateDriverDto>();
        var now = DateTime.UtcNow;
        var isAr = _messages.IsArabic();

        foreach (var driver in drivers)
        {
            latestPoints.TryGetValue(driver.Id, out var point);
            activeCounts.TryGetValue(driver.Id, out var activeCount);
            activeBoxesMap.TryGetValue(driver.Id, out var loadBoxes);
            stats.TryGetValue(driver.Id, out var stat);

            var driverLat = point?.Latitude ?? restLat;
            var driverLng = point?.Longitude ?? restLng;

            var distKm = Math.Round(KuwaitGeoBounds.HaversineKm(driverLat, driverLng, destinationLatitude, destinationLongitude), 1);

            var name = _messages.Localize(driver.FullNameAr, driver.FullNameEn);
            if (string.IsNullOrWhiteSpace(name)) name = isAr ? "سائق" : "Driver";
            var rating = stat?.AvgRating ?? 0.0m;

            var loadLabel = _messages.GetMessage(MessageCodes.DispatcherOrders.BoxesCountFormat, isAr ? $"{loadBoxes} بوكسات" : $"{loadBoxes} boxes", loadBoxes);

            string statusKey;
            string statusDefaultAr;
            string statusDefaultEn;
            string statusCode;

            if (activeCount == 0)
            {
                statusCode = "Available";
                statusKey = MessageCodes.DispatcherOrders.DriverStatusAvailable;
                statusDefaultAr = "متاح";
                statusDefaultEn = "Available";
            }
            else if (activeCount >= 3)
            {
                statusCode = "Busy";
                statusKey = MessageCodes.DispatcherOrders.DriverStatusBusy;
                statusDefaultAr = "مشغول";
                statusDefaultEn = "Busy";
            }
            else
            {
                statusCode = "InDelivery";
                statusKey = MessageCodes.DispatcherOrders.DriverStatusInDelivery;
                statusDefaultAr = "في التوصيل";
                statusDefaultEn = "In Delivery";
            }

            var statusText = _messages.GetMessage(statusKey, isAr ? statusDefaultAr : statusDefaultEn);

            // Estimated Finish Time based on actual speed and pending orders
            var finishMinutes = Math.Max(10, (int)(distKm * 2.5) + (activeCount * 12));
            var etaUtc = now.AddMinutes(finishMinutes);
            var etaText = FormatKuwaitTime(etaUtc);

            list.Add(new CandidateDriverDto(
                DriverId: driver.Id,
                FullName: name,
                AvatarUrl: driver.ProfileImageStorageKey,
                PlateNumber: driver.VehiclePlate ?? string.Empty,
                Phone: null,
                DistanceKm: distKm,
                DistanceText: isAr ? $"{distKm:F1} كم" : $"{distKm:F1} km",
                ActiveOrdersCount: activeCount,
                CurrentLoadBoxes: loadBoxes,
                CurrentLoadLabel: loadLabel,
                Rating: rating,
                DriverStatus: statusCode,
                DriverStatusText: statusText,
                StatusTag: null,
                EstimatedFinishTimeText: etaText,
                Rank: 0,
                IsRecommended: false,
                RecommendationReason: null));
        }

        var ordered = list.OrderBy(c => c.DistanceKm).ThenBy(c => c.ActiveOrdersCount).ToList();

        if (ordered.Count > 0)
        {
            // First is best recommendation
            var tagNearest = _messages.GetMessage(MessageCodes.DispatcherOrders.TagNearest, isAr ? "الأقرب" : "Nearest");
            var tagLeastLoaded = _messages.GetMessage(MessageCodes.DispatcherOrders.TagLeastLoaded, isAr ? "الأقل ضغطاً" : "Least Loaded");
            var tagBusyDelivering = _messages.GetMessage(MessageCodes.DispatcherOrders.TagBusyDelivering, isAr ? "منشغل بتسليم" : "Busy Delivering");
            var tagNearCustomer = _messages.GetMessage(MessageCodes.DispatcherOrders.TagNearCustomer, isAr ? "قريب من العميل" : "Near Customer");

            for (var i = 0; i < ordered.Count; i++)
            {
                var candidate = ordered[i];
                var isBest = i == 0;
                string? tag;

                if (isBest)
                {
                    tag = tagNearest;
                }
                else if (candidate.ActiveOrdersCount == 0)
                {
                    tag = tagLeastLoaded;
                }
                else if (candidate.DriverStatus == "Busy")
                {
                    tag = tagBusyDelivering;
                }
                else
                {
                    tag = tagNearCustomer;
                }

                ordered[i] = candidate with
                {
                    Rank = isBest ? 0 : i,
                    StatusTag = tag,
                    IsRecommended = isBest,
                    RecommendationReason = isBest ? (isAr ? "الأقرب جغرافياً لمنطقة التسليم" : "Nearest geographically to delivery zone") : null
                };
            }
        }

        return ordered;
    }

    private string FormatKuwaitTime(DateTime utcTime)
    {
        var local = utcTime.AddHours(3);
        var isAr = _messages.IsArabic();
        var period = local.Hour < 12 ? (isAr ? "ص" : "AM") : (isAr ? "م" : "PM");
        var hour12 = local.Hour % 12;
        if (hour12 == 0) hour12 = 12;
        return $"{hour12}:{local.Minute:D2} {period}";
    }
}
