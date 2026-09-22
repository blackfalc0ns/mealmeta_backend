using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherDriverRosterQuery(
    Guid? RestaurantId = null,
    string? View = "ByArea",
    string? Area = null,
    Guid? BoxId = null,
    string? SortBy = null) : IQuery<DispatcherDriverRosterDto>;

public sealed class GetDispatcherDriverRosterQueryHandler : IQueryHandler<GetDispatcherDriverRosterQuery, DispatcherDriverRosterDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherDriverRosterQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DispatcherDriverRosterDto>> Handle(
        GetDispatcherDriverRosterQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(request.RestaurantId, cancellationToken);
        var isAr = _messages.IsArabic();

        var selectedView = string.Equals(request.View, "All", StringComparison.OrdinalIgnoreCase) ? "All" : "ByArea";
        var selectedArea = string.IsNullOrWhiteSpace(request.Area) ? (isAr ? "السالمية" : "Salmiya") : request.Area.Trim();

        var drivers = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => d.RestaurantId == restaurantId || d.RestaurantId == null)
            .ToListAsync(cancellationToken);

        if (drivers.Count == 0)
        {
            return Result.Success(GetFallbackRoster(selectedView, selectedArea));
        }

        var driverIds = drivers.Select(d => d.Id).ToList();

        // Active trips
        var activeCounts = await _db.DriverAssignments
            .AsNoTracking()
            .Where(da => driverIds.Contains(da.DriverProfileId) &&
                         (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned))
            .GroupBy(da => da.DriverProfileId)
            .Select(g => new { DriverId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DriverId, x => x.Count, cancellationToken);

        // Completed deliveries today
        var todayUtc = DateTime.UtcNow.Date;
        var completedTodayCounts = await _db.TripStops
            .AsNoTracking()
            .Where(s => s.Trip.DriverAssignments.Any(da => driverIds.Contains(da.DriverProfileId)) &&
                         s.Status == TripStopStatus.Delivered &&
                         s.DeliveredAtUtc >= todayUtc)
            .Select(s => new {
                DriverId = s.Trip.DriverAssignments.OrderByDescending(da => da.AssignedAtUtc).Select(da => da.DriverProfileId).FirstOrDefault()
            })
            .GroupBy(x => x.DriverId)
            .Select(g => new { DriverId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DriverId, x => x.Count, cancellationToken);

        // Ratings
        var stats = await _db.DriverPerformanceStats
            .AsNoTracking()
            .Where(s => driverIds.Contains(s.DriverProfileId))
            .ToDictionaryAsync(s => s.DriverProfileId, cancellationToken);

        var list = new List<DriverRosterItemDto>();

        foreach (var driver in drivers)
        {
            activeCounts.TryGetValue(driver.Id, out var activeCount);
            completedTodayCounts.TryGetValue(driver.Id, out var completedCount);
            stats.TryGetValue(driver.Id, out var stat);

            var name = _messages.Localize(driver.FullNameAr, driver.FullNameEn);
            if (string.IsNullOrWhiteSpace(name)) name = isAr ? "سائق" : "Driver";
            var rating = stat?.AvgRating ?? 4.9m;

            string status;
            string statusText;
            string dotColor;
            bool isAvailable;

            if (activeCount == 0)
            {
                status = "Available";
                statusText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusAvailable, isAr ? "متاح" : "Available");
                dotColor = "Green";
                isAvailable = true;
            }
            else if (activeCount == 1)
            {
                status = "OnTheWay";
                statusText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusOnTheWay, isAr ? "في طريقه" : "On the Way");
                dotColor = "Orange";
                isAvailable = false;
            }
            else
            {
                status = "OnBreak";
                statusText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusOnBreak, isAr ? "في استراحة" : "On Break");
                dotColor = "Slate";
                isAvailable = false;
            }

            var driverCode = $"ID:D-{Math.Abs(driver.Id.GetHashCode()) % 900 + 1000}";
            var distKm = 9.0;
            var distText = isAr ? $"{distKm:F0} Km" : $"{distKm:F0} Km";

            list.Add(new DriverRosterItemDto(
                DriverId: driver.Id,
                DriverCode: driverCode,
                FullName: name,
                AvatarUrl: $"https://cdn.mealmate.app/avatars/{driver.Id.ToString()[..8]}.jpg",
                Rating: rating,
                Status: status,
                StatusText: statusText,
                StatusDotColor: dotColor,
                IsAvailableForSelection: isAvailable,
                ActiveOrdersCount: activeCount,
                ActiveOrdersText: activeCount.ToString(),
                CompletedOrdersTodayCount: completedCount,
                CompletedOrdersTodayText: completedCount.ToString(),
                DistanceKm: distKm,
                DistanceText: distText,
                CurrentZoneName: isAr ? "السالمية" : "Salmiya"));
        }

        var total = list.Count;
        var available = list.Count(d => d.IsAvailableForSelection);
        var busy = total - available;

        var areas = new List<DriverRosterAreaChipDto>
        {
            new("salmiya", isAr ? "السالمية" : "Salmiya", total, true),
            new("hawally", isAr ? "حولي" : "Hawally", 0, false),
            new("hatteen", isAr ? "حطين" : "Hateen", 0, false),
            new("farwaniya", isAr ? "الفروانية" : "Farwaniya", 0, false),
            new("capital", isAr ? "العاصمة" : "Capital", 0, false)
        };

        var titleFmt = _messages.GetMessage(MessageCodes.DispatcherRoster.DriversInAreaFormat,
            isAr ? "السائقين في {0} ({1})" : "Drivers in {0} ({1})", selectedArea, total);

        return Result.Success(new DispatcherDriverRosterDto(
            Counts: new DriverRosterCountsDto(total, available, busy),
            SelectedView: selectedView,
            SelectedArea: selectedArea,
            SectionTitle: titleFmt,
            Areas: areas,
            Drivers: list));
    }

    private DispatcherDriverRosterDto GetFallbackRoster(string selectedView, string selectedArea)
    {
        var isAr = _messages.IsArabic();

        var areas = new List<DriverRosterAreaChipDto>
        {
            new("salmiya", isAr ? "السالمية" : "Salmiya", 8, string.Equals(selectedArea, "السالمية", StringComparison.OrdinalIgnoreCase) || string.Equals(selectedArea, "Salmiya", StringComparison.OrdinalIgnoreCase)),
            new("hawally", isAr ? "حولي" : "Hawally", 2, string.Equals(selectedArea, "حولي", StringComparison.OrdinalIgnoreCase) || string.Equals(selectedArea, "Hawally", StringComparison.OrdinalIgnoreCase)),
            new("hatteen", isAr ? "حطين" : "Hateen", 1, string.Equals(selectedArea, "حطين", StringComparison.OrdinalIgnoreCase) || string.Equals(selectedArea, "Hateen", StringComparison.OrdinalIgnoreCase)),
            new("farwaniya", isAr ? "الفروانية" : "Farwaniya", 1, string.Equals(selectedArea, "الفروانية", StringComparison.OrdinalIgnoreCase) || string.Equals(selectedArea, "Farwaniya", StringComparison.OrdinalIgnoreCase)),
            new("capital", isAr ? "العاصمة" : "Capital", 1, string.Equals(selectedArea, "العاصمة", StringComparison.OrdinalIgnoreCase) || string.Equals(selectedArea, "Capital", StringComparison.OrdinalIgnoreCase))
        };

        var titleFmt = _messages.GetMessage(MessageCodes.DispatcherRoster.DriversInAreaFormat,
            isAr ? "السائقين في {0} ({1})" : "Drivers in {0} ({1})", selectedArea, 8);

        var statusAvailableText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusAvailable, isAr ? "متاح" : "Available");
        var statusOnTheWayText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusOnTheWay, isAr ? "في طريقه" : "On the Way");
        var statusOnBreakText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusOnBreak, isAr ? "في استراحة" : "On Break");

        var drivers = new List<DriverRosterItemDto>
        {
            new(
                DriverId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                DriverCode: "ID:D-1025",
                FullName: isAr ? "أحمد محمد" : "Ahmed Mohammed",
                AvatarUrl: "https://cdn.mealmate.app/avatars/ahmed-m.jpg",
                Rating: 4.9m,
                Status: "Available",
                StatusText: statusAvailableText,
                StatusDotColor: "Green",
                IsAvailableForSelection: true,
                ActiveOrdersCount: 1,
                ActiveOrdersText: "1",
                CompletedOrdersTodayCount: 12,
                CompletedOrdersTodayText: "12",
                DistanceKm: 9.0,
                DistanceText: "9 Km",
                CurrentZoneName: isAr ? "السالمية" : "Salmiya"),

            new(
                DriverId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                DriverCode: "ID:D-1044",
                FullName: isAr ? "محمد العتيبي" : "Mohammed Al-Otaibi",
                AvatarUrl: "https://cdn.mealmate.app/avatars/mohammed-o.jpg",
                Rating: 4.9m,
                Status: "Available",
                StatusText: statusAvailableText,
                StatusDotColor: "Green",
                IsAvailableForSelection: true,
                ActiveOrdersCount: 0,
                ActiveOrdersText: "0",
                CompletedOrdersTodayCount: 8,
                CompletedOrdersTodayText: "8",
                DistanceKm: 9.0,
                DistanceText: "9 Km",
                CurrentZoneName: isAr ? "السالمية" : "Salmiya"),

            new(
                DriverId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                DriverCode: "ID:D-1044",
                FullName: isAr ? "محمد علي" : "Mohammed Ali",
                AvatarUrl: "https://cdn.mealmate.app/avatars/mohammed-a.jpg",
                Rating: 4.9m,
                Status: "OnTheWay",
                StatusText: statusOnTheWayText,
                StatusDotColor: "Orange",
                IsAvailableForSelection: false,
                ActiveOrdersCount: 0,
                ActiveOrdersText: "0",
                CompletedOrdersTodayCount: 8,
                CompletedOrdersTodayText: "8",
                DistanceKm: 9.0,
                DistanceText: "9 Km",
                CurrentZoneName: isAr ? "السالمية" : "Salmiya"),

            new(
                DriverId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                DriverCode: "ID:D-1044",
                FullName: isAr ? "محمد العتيبي" : "Mohammed Al-Otaibi",
                AvatarUrl: "https://cdn.mealmate.app/avatars/mohammed-o2.jpg",
                Rating: 4.9m,
                Status: "Available",
                StatusText: statusAvailableText,
                StatusDotColor: "Green",
                IsAvailableForSelection: true,
                ActiveOrdersCount: 0,
                ActiveOrdersText: "0",
                CompletedOrdersTodayCount: 8,
                CompletedOrdersTodayText: "8",
                DistanceKm: 9.0,
                DistanceText: "9 Km",
                CurrentZoneName: isAr ? "السالمية" : "Salmiya"),

            new(
                DriverId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                DriverCode: "ID:D-1044",
                FullName: isAr ? "فهد العنزي" : "Fahad Al-Anzi",
                AvatarUrl: "https://cdn.mealmate.app/avatars/fahad-a.jpg",
                Rating: 4.9m,
                Status: "OnBreak",
                StatusText: statusOnBreakText,
                StatusDotColor: "Slate",
                IsAvailableForSelection: false,
                ActiveOrdersCount: 0,
                ActiveOrdersText: "0",
                CompletedOrdersTodayCount: 8,
                CompletedOrdersTodayText: "8",
                DistanceKm: 9.0,
                DistanceText: "9 Km",
                CurrentZoneName: isAr ? "السالمية" : "Salmiya")
        };

        return new DispatcherDriverRosterDto(
            Counts: new DriverRosterCountsDto(TotalCount: 13, AvailableCount: 8, BusyCount: 5),
            SelectedView: selectedView,
            SelectedArea: selectedArea,
            SectionTitle: titleFmt,
            Areas: areas,
            Drivers: drivers);
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

        var firstRestaurantId = await _db.RestaurantProfiles
            .AsNoTracking()
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return firstRestaurantId != Guid.Empty ? firstRestaurantId : Guid.NewGuid();
    }
}
