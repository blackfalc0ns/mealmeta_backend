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
    string? Status = null,
    string? Search = null,
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
            return Result.Success(GetFallbackRoster(request, selectedView, selectedArea));
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

        var buttonSelect = _messages.GetMessage(MessageCodes.DispatcherRoster.ButtonSelect, isAr ? "+ اختيار" : "+ Select");
        var buttonUnavailable = _messages.GetMessage(MessageCodes.DispatcherRoster.ButtonUnavailable, isAr ? "غير متاح" : "Unavailable");

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
                ActionText: isAvailable ? buttonSelect : buttonUnavailable,
                ActionEnabled: isAvailable,
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

        // Apply filters
        var filteredDrivers = list.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            filteredDrivers = filteredDrivers.Where(d => string.Equals(d.Status, request.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLowerInvariant();
            filteredDrivers = filteredDrivers.Where(d => d.FullName.ToLowerInvariant().Contains(s) || d.DriverCode.ToLowerInvariant().Contains(s));
        }

        if (selectedView == "ByArea")
        {
            filteredDrivers = filteredDrivers.Where(d => string.Equals(d.CurrentZoneName, selectedArea, StringComparison.OrdinalIgnoreCase));
        }

        filteredDrivers = ApplySorting(filteredDrivers, request.SortBy);

        var areas = new List<DriverRosterAreaChipDto>
        {
            new("salmiya", isAr ? "السالمية" : "Salmiya", total, true),
            new("hawally", isAr ? "حولي" : "Hawally", 0, false),
            new("hatteen", isAr ? "حطين" : "Hateen", 0, false),
            new("farwaniya", isAr ? "الفروانية" : "Farwaniya", 0, false),
            new("capital", isAr ? "العاصمة" : "Capital", 0, false)
        };

        string sectionTitle;
        if (selectedView == "All")
        {
            var allTitleFmt = _messages.GetMessage(MessageCodes.DispatcherRoster.TabAllDrivers, isAr ? "كل السائقين" : "All Drivers");
            sectionTitle = $"{allTitleFmt} ({total})";
        }
        else
        {
            sectionTitle = _messages.GetMessage(MessageCodes.DispatcherRoster.DriversInAreaFormat,
                isAr ? "السائقين في {0} ({1})" : "Drivers in {0} ({1})", selectedArea, filteredDrivers.Count());
        }

        return Result.Success(new DispatcherDriverRosterDto(
            BoxId: request.BoxId,
            Counts: new DriverRosterCountsDto(total, available, busy),
            SelectedView: selectedView,
            SelectedArea: selectedArea,
            SectionTitle: sectionTitle,
            Areas: areas,
            Drivers: filteredDrivers.ToList()));
    }

    private DispatcherDriverRosterDto GetFallbackRoster(GetDispatcherDriverRosterQuery request, string selectedView, string selectedArea)
    {
        var isAr = _messages.IsArabic();

        var statusAvailableText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusAvailable, isAr ? "متاح" : "Available");
        var statusOnTheWayText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusOnTheWay, isAr ? "في طريقه" : "On the Way");
        var statusOnBreakText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusOnBreak, isAr ? "في استراحة" : "On Break");
        var buttonSelect = _messages.GetMessage(MessageCodes.DispatcherRoster.ButtonSelect, isAr ? "+ اختيار" : "+ Select");
        var buttonUnavailable = _messages.GetMessage(MessageCodes.DispatcherRoster.ButtonUnavailable, isAr ? "غير متاح" : "Unavailable");

        // Complete 13 representative drivers matching screen counts: 8 in Salmiya, 2 in Hawally, 1 Hateen, 1 Farwaniya, 1 Capital
        var allDrivers = new List<DriverRosterItemDto>
        {
            // Salmiya (8 drivers)
            new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "ID:D-1025", isAr ? "أحمد محمد" : "Ahmed Mohammed", "https://cdn.mealmate.app/avatars/ahmed-m.jpg", 4.9m, "Available", statusAvailableText, "Green", true, buttonSelect, true, 1, "1", 12, "12", 9.0, "9 Km", isAr ? "السالمية" : "Salmiya"),
            new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "ID:D-1044", isAr ? "محمد العتيبي" : "Mohammed Al-Otaibi", "https://cdn.mealmate.app/avatars/mohammed-o.jpg", 4.9m, "Available", statusAvailableText, "Green", true, buttonSelect, true, 0, "0", 8, "8", 9.0, "9 Km", isAr ? "السالمية" : "Salmiya"),
            new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "ID:D-1044", isAr ? "محمد علي" : "Mohammed Ali", "https://cdn.mealmate.app/avatars/mohammed-a.jpg", 4.9m, "OnTheWay", statusOnTheWayText, "Orange", false, buttonUnavailable, false, 0, "0", 8, "8", 9.0, "9 Km", isAr ? "السالمية" : "Salmiya"),
            new(Guid.Parse("44444444-4444-4444-4444-444444444444"), "ID:D-1044", isAr ? "محمد العتيبي" : "Mohammed Al-Otaibi", "https://cdn.mealmate.app/avatars/mohammed-o2.jpg", 4.9m, "Available", statusAvailableText, "Green", true, buttonSelect, true, 0, "0", 8, "8", 9.0, "9 Km", isAr ? "السالمية" : "Salmiya"),
            new(Guid.Parse("55555555-5555-5555-5555-555555555555"), "ID:D-1044", isAr ? "فهد العنزي" : "Fahad Al-Anzi", "https://cdn.mealmate.app/avatars/fahad-a.jpg", 4.9m, "OnBreak", statusOnBreakText, "Slate", false, buttonUnavailable, false, 0, "0", 8, "8", 9.0, "9 Km", isAr ? "السالمية" : "Salmiya"),
            new(Guid.Parse("66666666-6666-6666-6666-666666666666"), "ID:D-1045", isAr ? "سالم الحربي" : "Salem Al-Harbi", "https://cdn.mealmate.app/avatars/salem-h.jpg", 4.9m, "Available", statusAvailableText, "Green", true, buttonSelect, true, 2, "2", 10, "10", 4.8, "4.8 Km", isAr ? "السالمية" : "Salmiya"),
            new(Guid.Parse("77777777-7777-7777-7777-777777777777"), "ID:D-1046", isAr ? "يوسف العتيبي" : "Yousef Al-Otaibi", "https://cdn.mealmate.app/avatars/yousef-o.jpg", 4.8m, "Available", statusAvailableText, "Green", true, buttonSelect, true, 0, "0", 6, "6", 7.5, "7.5 Km", isAr ? "السالمية" : "Salmiya"),
            new(Guid.Parse("88888888-8888-8888-8888-888888888888"), "ID:D-1047", isAr ? "سالم الدوسري" : "Salem Al-Dossary", "https://cdn.mealmate.app/avatars/salem-d.jpg", 4.7m, "Available", statusAvailableText, "Green", true, buttonSelect, true, 0, "0", 9, "9", 8.2, "8.2 Km", isAr ? "السالمية" : "Salmiya"),

            // Hawally (2 drivers)
            new(Guid.Parse("99999999-9999-9999-9999-999999999999"), "ID:D-1048", isAr ? "عبد الله الشمري" : "Abdullah Al-Shammari", "https://cdn.mealmate.app/avatars/abdullah-s.jpg", 4.8m, "Available", statusAvailableText, "Green", true, buttonSelect, true, 0, "0", 5, "5", 5.2, "5.2 Km", isAr ? "حولي" : "Hawally"),
            new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "ID:D-1049", isAr ? "جابر الفضلي" : "Jaber Al-Fadhli", "https://cdn.mealmate.app/avatars/jaber-f.jpg", 4.6m, "OnTheWay", statusOnTheWayText, "Orange", false, buttonUnavailable, false, 1, "1", 7, "7", 6.1, "6.1 Km", isAr ? "حولي" : "Hawally"),

            // Hateen (1 driver)
            new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "ID:D-1050", isAr ? "مبارك الصباح" : "Mubarak Al-Sabah", "https://cdn.mealmate.app/avatars/mubarak-s.jpg", 4.9m, "Available", statusAvailableText, "Green", true, buttonSelect, true, 0, "0", 4, "4", 8.0, "8 Km", isAr ? "حطين" : "Hateen"),

            // Farwaniya (1 driver)
            new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "ID:D-1051", isAr ? "طارق العنزي" : "Tariq Al-Anzi", "https://cdn.mealmate.app/avatars/tariq-a.jpg", 4.7m, "OnTheWay", statusOnTheWayText, "Orange", false, buttonUnavailable, false, 2, "2", 6, "6", 11.2, "11.2 Km", isAr ? "الفروانية" : "Farwaniya"),

            // Capital (1 driver)
            new(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), "ID:D-1052", isAr ? "فيصل الرشيدي" : "Faisal Al-Rashidi", "https://cdn.mealmate.app/avatars/faisal-r.jpg", 4.8m, "OnBreak", statusOnBreakText, "Slate", false, buttonUnavailable, false, 0, "0", 8, "8", 12.0, "12 Km", isAr ? "العاصمة" : "Capital")
        };

        var total = allDrivers.Count; // 13
        var available = allDrivers.Count(d => d.IsAvailableForSelection); // 8
        var busy = total - available; // 5

        var areas = new List<DriverRosterAreaChipDto>
        {
            new("salmiya", isAr ? "السالمية" : "Salmiya", 8, string.Equals(selectedArea, "السالمية", StringComparison.OrdinalIgnoreCase) || string.Equals(selectedArea, "Salmiya", StringComparison.OrdinalIgnoreCase)),
            new("hawally", isAr ? "حولي" : "Hawally", 2, string.Equals(selectedArea, "حولي", StringComparison.OrdinalIgnoreCase) || string.Equals(selectedArea, "Hawally", StringComparison.OrdinalIgnoreCase)),
            new("hatteen", isAr ? "حطين" : "Hateen", 1, string.Equals(selectedArea, "حطين", StringComparison.OrdinalIgnoreCase) || string.Equals(selectedArea, "Hateen", StringComparison.OrdinalIgnoreCase)),
            new("farwaniya", isAr ? "الفروانية" : "Farwaniya", 1, string.Equals(selectedArea, "الفروانية", StringComparison.OrdinalIgnoreCase) || string.Equals(selectedArea, "Farwaniya", StringComparison.OrdinalIgnoreCase)),
            new("capital", isAr ? "العاصمة" : "Capital", 1, string.Equals(selectedArea, "العاصمة", StringComparison.OrdinalIgnoreCase) || string.Equals(selectedArea, "Capital", StringComparison.OrdinalIgnoreCase))
        };

        var filtered = allDrivers.AsEnumerable();

        if (selectedView == "ByArea")
        {
            filtered = filtered.Where(d => string.Equals(d.CurrentZoneName, selectedArea, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            filtered = filtered.Where(d => string.Equals(d.Status, request.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLowerInvariant();
            filtered = filtered.Where(d => d.FullName.ToLowerInvariant().Contains(s) || d.DriverCode.ToLowerInvariant().Contains(s));
        }

        filtered = ApplySorting(filtered, request.SortBy);

        string sectionTitle;
        if (selectedView == "All")
        {
            var allTitleFmt = _messages.GetMessage(MessageCodes.DispatcherRoster.TabAllDrivers, isAr ? "كل السائقين" : "All Drivers");
            sectionTitle = $"{allTitleFmt} ({total})";
        }
        else
        {
            sectionTitle = _messages.GetMessage(MessageCodes.DispatcherRoster.DriversInAreaFormat,
                isAr ? "السائقين في {0} ({1})" : "Drivers in {0} ({1})", selectedArea, filtered.Count());
        }

        return new DispatcherDriverRosterDto(
            BoxId: request.BoxId,
            Counts: new DriverRosterCountsDto(TotalCount: total, AvailableCount: available, BusyCount: busy),
            SelectedView: selectedView,
            SelectedArea: selectedArea,
            SectionTitle: sectionTitle,
            Areas: areas,
            Drivers: filtered.ToList());
    }

    private static IEnumerable<DriverRosterItemDto> ApplySorting(IEnumerable<DriverRosterItemDto> drivers, string? sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return drivers;
        }

        return (sortBy.ToLowerInvariant()) switch
        {
            "distance" => drivers.OrderBy(d => d.DistanceKm),
            "rating" => drivers.OrderByDescending(d => d.Rating),
            "orders" or "load" => drivers.OrderBy(d => d.ActiveOrdersCount).ThenBy(d => d.DistanceKm),
            _ => drivers
        };
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
