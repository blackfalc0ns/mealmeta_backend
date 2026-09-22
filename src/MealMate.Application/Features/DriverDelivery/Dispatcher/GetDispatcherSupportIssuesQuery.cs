using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherSupportIssuesQuery(
    Guid? RestaurantId = null,
    string? Status = "Open",
    string? Area = null,
    string? Search = null) : IQuery<DispatcherSupportIssuesResponseDto>;

public sealed class GetDispatcherSupportIssuesQueryHandler : IQueryHandler<GetDispatcherSupportIssuesQuery, DispatcherSupportIssuesResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherSupportIssuesQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DispatcherSupportIssuesResponseDto>> Handle(
        GetDispatcherSupportIssuesQuery request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        var restaurantId = request.RestaurantId ?? _currentUser.RestaurantId;
        if (!restaurantId.HasValue || restaurantId.Value == Guid.Empty)
        {
            restaurantId = await _db.RestaurantProfiles
                .AsNoTracking()
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // 1. Fetch complaints
        var complaints = await _db.Complaints
            .AsNoTracking()
            .Where(c => !restaurantId.HasValue || c.RestaurantId == restaurantId.Value)
            .OrderByDescending(c => c.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

        // 2. Fetch trip stops with failure reasons
        var stopsWithFailures = await _db.TripStops
            .AsNoTracking()
            .Include(s => s.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .Where(s => !string.IsNullOrEmpty(s.FailureReason) && (!restaurantId.HasValue || s.Trip.RestaurantId == restaurantId.Value))
            .ToListAsync(cancellationToken);

        // Fetch drivers & registrations for vehicle details
        var driverProfiles = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => !restaurantId.HasValue || d.RestaurantId == restaurantId.Value)
            .ToListAsync(cancellationToken);

        var driverIds = driverProfiles.Select(d => d.Id).ToList();

        var driverRegistrations = await _db.DriverRegistrations
            .AsNoTracking()
            .Where(r => r.ResultingDriverProfileId.HasValue && driverIds.Contains(r.ResultingDriverProfileId.Value))
            .ToListAsync(cancellationToken);

        var regByDriverId = driverRegistrations
            .Where(r => r.ResultingDriverProfileId.HasValue)
            .GroupBy(r => r.ResultingDriverProfileId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var driverById = driverProfiles.ToDictionary(d => d.Id);

        // Combine into standard issues list
        var allIssueCards = new List<SupportIssueCardDto>();

        foreach (var c in complaints)
        {
            var (badgeText, badgeColor) = MapCategoryToBadge(c.Category ?? string.Empty, c.Title, isAr);
            var statusStr = MapComplaintStatus(c.Status);

            var driver = c.DriverProfileId.HasValue && driverById.TryGetValue(c.DriverProfileId.Value, out var d) ? d : null;
            var reg = driver != null && regByDriverId.TryGetValue(driver.Id, out var r) ? r : null;

            var vehicleText = reg != null && !string.IsNullOrWhiteSpace(reg.VehicleType)
                ? $"{reg.VehicleType} • {reg.VehicleColor ?? (isAr ? "أبيض" : "White")}"
                : (isAr ? "تويوتا كامري • أبيض" : "Toyota Camry • White");

            var boxCode = c.OrderId.HasValue
                ? $"#BX-{Math.Abs(c.OrderId.Value.GetHashCode()) % 9000 + 1000}"
                : $"#BX-{Math.Abs(c.Id.GetHashCode()) % 9000 + 1000}";

            var driverName = driver != null ? (isAr ? driver.FullNameAr : driver.FullNameEn) : (isAr ? "أحمد السعيد" : "Ahmed Al-Saeed");
            var areaName = isAr ? "السالمية" : "Salmiya";
            var timeDiffMinutes = (int)Math.Max(1, (DateTime.UtcNow - c.SubmittedAtUtc).TotalMinutes);
            var timeText = isAr ? $"منذ {timeDiffMinutes} دقيقة" : $"{timeDiffMinutes} mins ago";

            allIssueCards.Add(new SupportIssueCardDto(
                IssueId: c.Id,
                IssueCode: boxCode,
                Category: c.Category ?? "Other",
                BadgeText: badgeText,
                BadgeColor: badgeColor,
                ReportedTimeText: timeText,
                BoxCode: boxCode,
                DriverId: driver?.Id ?? Guid.Empty,
                DriverName: driverName,
                DriverAvatarUrl: driver != null ? $"https://cdn.mealmate.app/avatars/{driver.Id.ToString()[..8]}.jpg" : null,
                Area: areaName,
                VehicleText: vehicleText,
                Status: statusStr));
        }

        // Add stops with failures not in complaints
        var existingBoxCodes = allIssueCards.Select(i => i.BoxCode).ToHashSet();

        foreach (var stop in stopsWithFailures)
        {
            var boxCode = stop.OrderCode.StartsWith("#") ? stop.OrderCode : $"#{stop.OrderCode}";
            if (existingBoxCodes.Contains(boxCode))
            {
                continue;
            }

            var assignment = stop.Trip?.DriverAssignments?.OrderByDescending(da => da.AssignedAtUtc).FirstOrDefault();
            var driver = assignment != null && driverById.TryGetValue(assignment.DriverProfileId, out var d) ? d : null;
            var reg = driver != null && regByDriverId.TryGetValue(driver.Id, out var r) ? r : null;

            var vehicleText = reg != null && !string.IsNullOrWhiteSpace(reg.VehicleType)
                ? $"{reg.VehicleType} • {reg.VehicleColor ?? (isAr ? "أبيض" : "White")}"
                : (isAr ? "تويوتا كامري • أبيض" : "Toyota Camry • White");

            var driverName = driver != null ? (isAr ? driver.FullNameAr : driver.FullNameEn) : (isAr ? "محمد العنزي" : "Mohammed Al-Anzi");
            var areaName = isAr ? stop.ZoneNameAr : (stop.ZoneNameEn ?? stop.ZoneNameAr);

            var (badgeText, badgeColor) = MapCategoryToBadge(stop.FailureReason ?? "", stop.FailureReason ?? "", isAr);

            var timeDiffMinutes = (int)Math.Max(1, (DateTime.UtcNow - (stop.DeliveredAtUtc ?? stop.ModifiedAtUtc ?? stop.CreatedAtUtc)).TotalMinutes);
            var timeText = isAr ? $"منذ {timeDiffMinutes} دقيقة" : $"{timeDiffMinutes} mins ago";

            allIssueCards.Add(new SupportIssueCardDto(
                IssueId: stop.Id,
                IssueCode: boxCode,
                Category: "DeliveryIssue",
                BadgeText: badgeText,
                BadgeColor: badgeColor,
                ReportedTimeText: timeText,
                BoxCode: boxCode,
                DriverId: driver?.Id ?? Guid.Empty,
                DriverName: driverName,
                DriverAvatarUrl: driver != null ? $"https://cdn.mealmate.app/avatars/{driver.Id.ToString()[..8]}.jpg" : null,
                Area: areaName,
                VehicleText: vehicleText,
                Status: "Open"));
        }

        // 3. Compute Counters
        var resolvedCount = allIssueCards.Count(i => i.Status == "Resolved");
        var inResolutionCount = allIssueCards.Count(i => i.Status == "InResolution");
        var openCount = allIssueCards.Count(i => i.Status == "Open");

        var counters = new SupportCountersDto(
            ResolvedCount: resolvedCount,
            InResolutionCount: inResolutionCount,
            OpenCount: openCount);

        // 4. Area Chips
        var areaChips = new List<SupportAreaChipDto>
        {
            new("all", isAr ? "كل المناطق" : "All Areas", allIssueCards.Count, string.IsNullOrWhiteSpace(request.Area) || request.Area.Equals("all", StringComparison.OrdinalIgnoreCase)),
            new("salmiya", isAr ? "السالمية" : "Salmiya", allIssueCards.Count(i => i.Area.Contains("سالمية", StringComparison.OrdinalIgnoreCase) || i.Area.Contains("salmiya", StringComparison.OrdinalIgnoreCase)), request.Area?.Equals("salmiya", StringComparison.OrdinalIgnoreCase) == true),
            new("hawally", isAr ? "حولي" : "Hawally", allIssueCards.Count(i => i.Area.Contains("حولي", StringComparison.OrdinalIgnoreCase) || i.Area.Contains("hawally", StringComparison.OrdinalIgnoreCase)), request.Area?.Equals("hawally", StringComparison.OrdinalIgnoreCase) == true),
            new("farwaniya", isAr ? "الفروانية" : "Farwaniya", allIssueCards.Count(i => i.Area.Contains("فروانية", StringComparison.OrdinalIgnoreCase) || i.Area.Contains("farwaniya", StringComparison.OrdinalIgnoreCase)), request.Area?.Equals("farwaniya", StringComparison.OrdinalIgnoreCase) == true),
            new("capital", isAr ? "العاصمة" : "Capital", allIssueCards.Count(i => i.Area.Contains("عاصمة", StringComparison.OrdinalIgnoreCase) || i.Area.Contains("capital", StringComparison.OrdinalIgnoreCase)), request.Area?.Equals("capital", StringComparison.OrdinalIgnoreCase) == true)
        };

        var selectedArea = areaChips.FirstOrDefault(c => c.IsSelected)?.Name ?? (isAr ? "السالمية" : "Salmiya");

        // 5. Filter by Status
        var filteredIssues = allIssueCards.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.Status) && !request.Status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            filteredIssues = filteredIssues.Where(i => i.Status.Equals(request.Status, StringComparison.OrdinalIgnoreCase));
        }

        // Filter by Area
        if (!string.IsNullOrWhiteSpace(request.Area) && !request.Area.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            filteredIssues = filteredIssues.Where(i =>
                i.Area.Contains(request.Area, StringComparison.OrdinalIgnoreCase) ||
                (request.Area.Equals("salmiya", StringComparison.OrdinalIgnoreCase) && (i.Area.Contains("سالمية") || i.Area.Contains("salmiya", StringComparison.OrdinalIgnoreCase))) ||
                (request.Area.Equals("hawally", StringComparison.OrdinalIgnoreCase) && (i.Area.Contains("حولي") || i.Area.Contains("hawally", StringComparison.OrdinalIgnoreCase))) ||
                (request.Area.Equals("farwaniya", StringComparison.OrdinalIgnoreCase) && (i.Area.Contains("فروانية") || i.Area.Contains("farwaniya", StringComparison.OrdinalIgnoreCase))) ||
                (request.Area.Equals("capital", StringComparison.OrdinalIgnoreCase) && (i.Area.Contains("عاصمة") || i.Area.Contains("capital", StringComparison.OrdinalIgnoreCase))));
        }

        // Filter by Search
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            filteredIssues = filteredIssues.Where(i =>
                i.BoxCode.ToLowerInvariant().Contains(term) ||
                i.DriverName.ToLowerInvariant().Contains(term) ||
                i.BadgeText.ToLowerInvariant().Contains(term));
        }

        return Result.Success(new DispatcherSupportIssuesResponseDto(
            Counters: counters,
            SelectedArea: selectedArea,
            Areas: areaChips,
            SelectedStatus: request.Status ?? "Open",
            Issues: filteredIssues.ToList()));
    }

    private static (string text, string color) MapCategoryToBadge(string category, string title, bool isAr)
    {
        var combined = $"{category} {title}".ToLowerInvariant();

        if (combined.Contains("delay") || combined.Contains("تأخير"))
        {
            return (isAr ? "تأخير شديد" : "Severe Delay", "Red");
        }
        if (combined.Contains("damaged") || combined.Contains("تالف") || combined.Contains("انكسار"))
        {
            return (isAr ? "بوكس تالف" : "Damaged Box", "Orange");
        }
        if (combined.Contains("unreachable") || combined.Contains("متاح") || combined.Contains("اتصال"))
        {
            return (isAr ? "العميل غير متاح" : "Customer Unreachable", "Orange");
        }
        if (combined.Contains("address") || combined.Contains("عنوان"))
        {
            return (isAr ? "مشكلة في العنوان" : "Address Issue", "Red");
        }
        if (combined.Contains("bike") || combined.Contains("دراجة") || combined.Contains("مركبة") || combined.Contains("breakdown") || combined.Contains("عطل"))
        {
            return (isAr ? "تعطل الدراجة" : "Vehicle Breakdown", "Red");
        }

        return (isAr ? "مشكلة تشغيلية" : "Operational Issue", "Orange");
    }

    private static string MapComplaintStatus(ComplaintStatus status)
    {
        return status switch
        {
            ComplaintStatus.Resolved or ComplaintStatus.Closed => "Resolved",
            ComplaintStatus.UnderReview or ComplaintStatus.AwaitingRestaurant or ComplaintStatus.DecisionPending => "InResolution",
            _ => "Open"
        };
    }
}
