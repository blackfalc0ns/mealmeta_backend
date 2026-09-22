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
    string? Search = null,
    string? DatePreset = null,
    DateTime? FromDateUtc = null,
    DateTime? ToDateUtc = null,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<DispatcherSupportIssuesResponseDto>;

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
        var nowUtc = DateTime.UtcNow;

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
            .Include(c => c.EvidencePhotos)
            .Where(c => !restaurantId.HasValue || c.RestaurantId == restaurantId.Value)
            .OrderByDescending(c => c.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

        // 2. Fetch all trip stops for this restaurant
        var allStops = await _db.TripStops
            .AsNoTracking()
            .Include(s => s.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .Where(s => !restaurantId.HasValue || s.Trip.RestaurantId == restaurantId.Value)
            .ToListAsync(cancellationToken);

        var stopsWithFailures = allStops
            .Where(s => !string.IsNullOrEmpty(s.FailureReason))
            .ToList();

        var stopById = allStops.ToDictionary(s => s.Id);
        var stopBySubDayId = allStops.Where(s => s.SubscriptionDayId.HasValue)
            .GroupBy(s => s.SubscriptionDayId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        // 3. Fetch drivers & registrations for vehicle details
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

        // 4. Combine into standard issues list
        var allIssueCards = new List<SupportIssueCardDto>();

        foreach (var c in complaints)
        {
            var createdAtUtc = c.SubmittedAtUtc != default ? c.SubmittedAtUtc : c.CreatedAtUtc;
            var (badgeText, badgeColor) = MapCategoryToBadge(c.Category ?? string.Empty, c.Title, isAr);
            var statusStr = MapComplaintStatus(c.Status);
            var statusLabel = MapStatusLabel(statusStr, isAr);

            TripStop? stop = null;
            if (c.OrderId.HasValue && stopById.TryGetValue(c.OrderId.Value, out var s1))
            {
                stop = s1;
            }
            else if (c.SubscriptionDayId.HasValue && stopBySubDayId.TryGetValue(c.SubscriptionDayId.Value, out var s2))
            {
                stop = s2;
            }

            var driver = c.DriverProfileId.HasValue && driverById.TryGetValue(c.DriverProfileId.Value, out var d)
                ? d
                : (stop?.Trip?.DriverAssignments?.OrderByDescending(da => da.AssignedAtUtc).FirstOrDefault() is { } da && driverById.TryGetValue(da.DriverProfileId, out var td) ? td : null);

            var reg = driver != null && regByDriverId.TryGetValue(driver.Id, out var r) ? r : null;

            var vehicleText = reg != null && !string.IsNullOrWhiteSpace(reg.VehicleType)
                ? $"{reg.VehicleType} • {reg.VehicleColor ?? (isAr ? "أبيض" : "White")}"
                : (isAr ? "تويوتا كامري • أبيض" : "Toyota Camry • White");

            var boxCode = stop != null
                ? (stop.OrderCode.StartsWith("#") ? stop.OrderCode : $"#{stop.OrderCode}")
                : (c.OrderId.HasValue
                    ? $"#BX-{Math.Abs(c.OrderId.Value.GetHashCode()) % 9000 + 1000}"
                    : $"#BX-{Math.Abs(c.Id.GetHashCode()) % 9000 + 1000}");

            var driverCode = driver != null ? $"DR-{Math.Abs(driver.Id.GetHashCode()) % 900 + 1000}" : "DR-1025";
            var driverName = driver != null ? (isAr ? driver.FullNameAr : driver.FullNameEn) : (isAr ? "أحمد السعيد" : "Ahmed Al-Saeed");

            string areaName;
            if (stop != null && (!string.IsNullOrWhiteSpace(stop.ZoneNameAr) || !string.IsNullOrWhiteSpace(stop.ZoneNameEn)))
            {
                areaName = isAr ? stop.ZoneNameAr : (stop.ZoneNameEn ?? stop.ZoneNameAr);
            }
            else
            {
                areaName = ResolveAreaFromText($"{c.Title} {c.Description}", isAr);
            }

            var timeDiffMinutes = (int)Math.Max(1, (nowUtc - createdAtUtc).TotalMinutes);
            var timeText = isAr ? $"منذ {timeDiffMinutes} دقيقة" : $"{timeDiffMinutes} mins ago";
            var title = !string.IsNullOrWhiteSpace(c.Title) ? c.Title : (!string.IsNullOrWhiteSpace(c.Description) ? c.Description : (isAr ? "شكوى عميل" : "Customer Complaint"));
            var category = c.Category ?? "DeliveryIssue";
            var categoryColor = badgeColor == "Red" ? "#EF4444" : "#F59E0B";

            allIssueCards.Add(new SupportIssueCardDto(
                Id: c.Id,
                IssueId: c.Id,
                BoxCode: boxCode,
                IssueCode: boxCode,
                Title: title,
                Category: category,
                IssueCategory: badgeText,
                BadgeText: badgeText,
                BadgeColor: badgeColor,
                IssueCategoryColor: categoryColor,
                ReportedTimeText: timeText,
                TimeAgo: timeText,
                CreatedAtUtc: createdAtUtc,
                DriverId: driver?.Id ?? Guid.Empty,
                DriverCode: driverCode,
                DriverName: driverName,
                DriverAvatarUrl: driver != null ? $"https://cdn.mealmate.app/avatars/{driver.Id.ToString()[..8]}.jpg" : null,
                Area: areaName,
                VehicleText: vehicleText,
                VehicleInfo: vehicleText,
                Priority: "High",
                PriorityLabel: isAr ? "أولوية عالية" : "High Priority",
                PriorityColor: "#EF4444",
                Status: statusStr,
                StatusLabel: statusLabel));
        }

        // Add stops with failures not in complaints
        var existingBoxCodes = allIssueCards.Select(i => i.BoxCode).ToHashSet();

        foreach (var stop in stopsWithFailures)
        {
            var createdAtUtc = stop.DeliveredAtUtc ?? stop.ModifiedAtUtc ?? stop.CreatedAtUtc;
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

            var driverCode = driver != null ? $"DR-{Math.Abs(driver.Id.GetHashCode()) % 900 + 1000}" : "DR-1025";
            var driverName = driver != null ? (isAr ? driver.FullNameAr : driver.FullNameEn) : (isAr ? "محمد العنزي" : "Mohammed Al-Anzi");
            var areaName = isAr ? stop.ZoneNameAr : (stop.ZoneNameEn ?? stop.ZoneNameAr);
            if (string.IsNullOrWhiteSpace(areaName)) areaName = ResolveAreaFromText(stop.FailureReason ?? "", isAr);

            var (badgeText, badgeColor) = MapCategoryToBadge(stop.FailureReason ?? "", stop.FailureReason ?? "", isAr);
            var timeDiffMinutes = (int)Math.Max(1, (nowUtc - createdAtUtc).TotalMinutes);
            var timeText = isAr ? $"منذ {timeDiffMinutes} دقيقة" : $"{timeDiffMinutes} mins ago";
            var title = stop.FailureReason ?? (isAr ? "مشكلة في تسليم الطلب" : "Delivery stop issue");
            var statusStr = string.IsNullOrWhiteSpace(stop.FailureReason) ? "Resolved" : (stop.Trip?.Status == TripStatus.InProgress ? "InProgress" : "Open");
            var statusLabel = MapStatusLabel(statusStr, isAr);
            var categoryColor = badgeColor == "Red" ? "#EF4444" : "#F59E0B";

            allIssueCards.Add(new SupportIssueCardDto(
                Id: stop.Id,
                IssueId: stop.Id,
                BoxCode: boxCode,
                IssueCode: boxCode,
                Title: title,
                Category: "DeliveryIssue",
                IssueCategory: badgeText,
                BadgeText: badgeText,
                BadgeColor: badgeColor,
                IssueCategoryColor: categoryColor,
                ReportedTimeText: timeText,
                TimeAgo: timeText,
                CreatedAtUtc: createdAtUtc,
                DriverId: driver?.Id ?? Guid.Empty,
                DriverCode: driverCode,
                DriverName: driverName,
                DriverAvatarUrl: driver != null ? $"https://cdn.mealmate.app/avatars/{driver.Id.ToString()[..8]}.jpg" : null,
                Area: areaName,
                VehicleText: vehicleText,
                VehicleInfo: vehicleText,
                Priority: "High",
                PriorityLabel: isAr ? "أولوية عالية" : "High Priority",
                PriorityColor: "#EF4444",
                Status: statusStr,
                StatusLabel: statusLabel));
        }

        // 5. Order all issues by CreatedAtUtc DESC
        allIssueCards = allIssueCards.OrderByDescending(i => i.CreatedAtUtc).ToList();

        // 6. Apply Date filter
        DateTime? fromDate = null;
        DateTime? toDate = null;

        if (!string.IsNullOrWhiteSpace(request.DatePreset))
        {
            var preset = request.DatePreset.Trim();
            if (preset.Equals("Today", StringComparison.OrdinalIgnoreCase))
            {
                fromDate = nowUtc.Date;
                toDate = nowUtc.Date.AddDays(1);
            }
            else if (preset.Equals("Last7Days", StringComparison.OrdinalIgnoreCase))
            {
                fromDate = nowUtc.Date.AddDays(-7);
                toDate = nowUtc.Date.AddDays(1);
            }
            else if (preset.Equals("Last30Days", StringComparison.OrdinalIgnoreCase))
            {
                fromDate = nowUtc.Date.AddDays(-30);
                toDate = nowUtc.Date.AddDays(1);
            }
            else if (preset.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            {
                fromDate = request.FromDateUtc;
                if (request.ToDateUtc.HasValue)
                {
                    toDate = request.ToDateUtc.Value.TimeOfDay == TimeSpan.Zero
                        ? request.ToDateUtc.Value.Date.AddDays(1).AddTicks(-1)
                        : request.ToDateUtc.Value;
                }
            }
        }
        else if (request.FromDateUtc.HasValue || request.ToDateUtc.HasValue)
        {
            fromDate = request.FromDateUtc;
            if (request.ToDateUtc.HasValue)
            {
                toDate = request.ToDateUtc.Value.TimeOfDay == TimeSpan.Zero
                    ? request.ToDateUtc.Value.Date.AddDays(1).AddTicks(-1)
                    : request.ToDateUtc.Value;
            }
        }

        var dateFiltered = allIssueCards.AsEnumerable();
        if (fromDate.HasValue)
        {
            dateFiltered = dateFiltered.Where(i => i.CreatedAtUtc >= fromDate.Value);
        }
        if (toDate.HasValue)
        {
            dateFiltered = dateFiltered.Where(i => i.CreatedAtUtc <= toDate.Value);
        }
        var dateFilteredList = dateFiltered.ToList();

        // 7. Compute Counters (Rule: counters NOT affected by status filter)
        var openCount = dateFilteredList.Count(i => i.Status.Equals("Open", StringComparison.OrdinalIgnoreCase));
        var inProgressCount = dateFilteredList.Count(i => i.Status.Equals("InProgress", StringComparison.OrdinalIgnoreCase) || i.Status.Equals("InResolution", StringComparison.OrdinalIgnoreCase));
        var resolvedCount = dateFilteredList.Count(i => i.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase));

        var counters = new SupportCountersDto(
            OpenCount: openCount,
            InProgressCount: inProgressCount,
            ResolvedCount: resolvedCount,
            InResolutionCount: inProgressCount);

        // 8. Area Chips (Rule: areaChips NOT affected by selected area)
        var areaChips = new List<SupportAreaChipDto>
        {
            new(
                AreaKey: "all",
                Name: isAr ? "كل المناطق" : "All Areas",
                Count: dateFilteredList.Count,
                IsSelected: string.IsNullOrWhiteSpace(request.Area) || request.Area.Equals("all", StringComparison.OrdinalIgnoreCase) || request.Area.Equals("كل المناطق", StringComparison.OrdinalIgnoreCase),
                AreaName: isAr ? "كل المناطق" : "All Areas",
                IssueCount: dateFilteredList.Count),

            new(
                AreaKey: "salmiya",
                Name: isAr ? "السالمية" : "Salmiya",
                Count: dateFilteredList.Count(i => AreaMatches(i.Area, "salmiya")),
                IsSelected: AreaMatches(request.Area ?? "", "salmiya") && !string.IsNullOrWhiteSpace(request.Area) && !request.Area.Equals("all", StringComparison.OrdinalIgnoreCase),
                AreaName: isAr ? "السالمية" : "Salmiya",
                IssueCount: dateFilteredList.Count(i => AreaMatches(i.Area, "salmiya"))),

            new(
                AreaKey: "hawally",
                Name: isAr ? "حولي" : "Hawally",
                Count: dateFilteredList.Count(i => AreaMatches(i.Area, "hawally")),
                IsSelected: AreaMatches(request.Area ?? "", "hawally") && !string.IsNullOrWhiteSpace(request.Area) && !request.Area.Equals("all", StringComparison.OrdinalIgnoreCase),
                AreaName: isAr ? "حولي" : "Hawally",
                IssueCount: dateFilteredList.Count(i => AreaMatches(i.Area, "hawally"))),

            new(
                AreaKey: "farwaniya",
                Name: isAr ? "الفروانية" : "Farwaniya",
                Count: dateFilteredList.Count(i => AreaMatches(i.Area, "farwaniya")),
                IsSelected: AreaMatches(request.Area ?? "", "farwaniya") && !string.IsNullOrWhiteSpace(request.Area) && !request.Area.Equals("all", StringComparison.OrdinalIgnoreCase),
                AreaName: isAr ? "الفروانية" : "Farwaniya",
                IssueCount: dateFilteredList.Count(i => AreaMatches(i.Area, "farwaniya"))),

            new(
                AreaKey: "capital",
                Name: isAr ? "العاصمة" : "Capital",
                Count: dateFilteredList.Count(i => AreaMatches(i.Area, "capital")),
                IsSelected: AreaMatches(request.Area ?? "", "capital") && !string.IsNullOrWhiteSpace(request.Area) && !request.Area.Equals("all", StringComparison.OrdinalIgnoreCase),
                AreaName: isAr ? "العاصمة" : "Capital",
                IssueCount: dateFilteredList.Count(i => AreaMatches(i.Area, "capital")))
        };

        // 9. Filter Issues (Rule: issues affected by status, area, search, date)
        var filteredIssues = dateFilteredList.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.Status) && !request.Status.Equals("All", StringComparison.OrdinalIgnoreCase) && !request.Status.Equals("الكل", StringComparison.OrdinalIgnoreCase))
        {
            filteredIssues = filteredIssues.Where(i => StatusMatches(i.Status, request.Status));
        }

        if (!string.IsNullOrWhiteSpace(request.Area) && !request.Area.Equals("all", StringComparison.OrdinalIgnoreCase) && !request.Area.Equals("كل المناطق", StringComparison.OrdinalIgnoreCase))
        {
            filteredIssues = filteredIssues.Where(i => AreaMatches(i.Area, request.Area));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            filteredIssues = filteredIssues.Where(i =>
                (i.BoxCode != null && i.BoxCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (i.DriverName != null && i.DriverName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (i.DriverCode != null && i.DriverCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (i.Category != null && i.Category.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (i.IssueCategory != null && i.IssueCategory.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (i.BadgeText != null && i.BadgeText.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (i.Title != null && i.Title.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        var issueList = filteredIssues.ToList();

        // 10. Pagination (PageNumber starts from 1, default pageSize 20, max 50)
        var totalItems = issueList.Count;
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize > 0 ? request.PageSize : 20, 1, 50);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var hasPreviousPage = pageNumber > 1 && totalPages > 0;
        var hasNextPage = pageNumber < totalPages;

        var pagedIssues = issueList
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var pagination = new SupportPaginationDto(
            PageNumber: pageNumber,
            PageSize: pageSize,
            TotalItems: totalItems,
            TotalPages: totalPages,
            HasPreviousPage: hasPreviousPage,
            HasNextPage: hasNextPage);

        return Result.Success(new DispatcherSupportIssuesResponseDto(
            Counters: counters,
            AreaChips: areaChips,
            Issues: pagedIssues,
            Pagination: pagination,
            SelectedArea: request.Area ?? (isAr ? "كل المناطق" : "All Areas"),
            Areas: areaChips,
            SelectedStatus: request.Status ?? "Open"));
    }

    private static bool StatusMatches(string issueStatus, string filterStatus)
    {
        if (filterStatus.Equals("InProgress", StringComparison.OrdinalIgnoreCase) || filterStatus.Equals("InResolution", StringComparison.OrdinalIgnoreCase))
        {
            return issueStatus.Equals("InProgress", StringComparison.OrdinalIgnoreCase) || issueStatus.Equals("InResolution", StringComparison.OrdinalIgnoreCase);
        }

        return issueStatus.Equals(filterStatus, StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreaMatches(string issueArea, string filterArea)
    {
        if (string.IsNullOrWhiteSpace(filterArea) || filterArea.Equals("all", StringComparison.OrdinalIgnoreCase) || filterArea.Equals("كل المناطق", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(issueArea))
        {
            return false;
        }

        if (issueArea.Contains(filterArea, StringComparison.OrdinalIgnoreCase) || filterArea.Contains(issueArea, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var key = NormalizeAreaKey(filterArea);
        var issueKey = NormalizeAreaKey(issueArea);

        return key == issueKey;
    }

    private static string NormalizeAreaKey(string area)
    {
        var a = area.ToLowerInvariant();
        if (a.Contains("سالمية") || a.Contains("salmiya") || a.Contains("البدع") || a.Contains("البلاجات")) return "salmiya";
        if (a.Contains("حولي") || a.Contains("hawally") || a.Contains("الجابرية") || a.Contains("تونس") || a.Contains("بيان") || a.Contains("مشرف")) return "hawally";
        if (a.Contains("فروانية") || a.Contains("farwaniya") || a.Contains("خيطان") || a.Contains("العارضية") || a.Contains("الرحاب")) return "farwaniya";
        if (a.Contains("عاصمة") || a.Contains("capital") || a.Contains("شرق") || a.Contains("دسمان") || a.Contains("المرقاب") || a.Contains("القبلة") || a.Contains("مدينة الكويت") || a.Contains("الحمراء") || a.Contains("الراية")) return "capital";
        return a;
    }

    private static string ResolveAreaFromText(string text, bool isAr)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return isAr ? "العاصمة" : "Capital";
        }

        var key = NormalizeAreaKey(text);
        return key switch
        {
            "salmiya" => isAr ? "السالمية" : "Salmiya",
            "hawally" => isAr ? "حولي" : "Hawally",
            "farwaniya" => isAr ? "الفروانية" : "Farwaniya",
            _ => isAr ? "العاصمة" : "Capital"
        };
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
            ComplaintStatus.UnderReview or ComplaintStatus.AwaitingRestaurant or ComplaintStatus.DecisionPending => "InProgress",
            _ => "Open"
        };
    }

    private static string MapStatusLabel(string status, bool isAr)
    {
        return status switch
        {
            "Resolved" => isAr ? "تم الحل" : "Resolved",
            "InProgress" or "InResolution" => isAr ? "جاري الحل" : "In Progress",
            _ => isAr ? "مفتوحة" : "Open"
        };
    }
}
