using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
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

        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue || _currentUser.UserType is not (UserType.DeliveryManager or UserType.Admin))
            return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", _messages.GetMessage("dispatcher_support.forbidden", "Dispatcher access is required."));
        if (_currentUser.UserType == UserType.DeliveryManager && !_currentUser.RestaurantId.HasValue)
            return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", _messages.GetMessage("dispatcher_support.restaurant_scope_required", "A restaurant scope is required."));
        if (_currentUser.UserType == UserType.DeliveryManager && request.RestaurantId.HasValue && request.RestaurantId != _currentUser.RestaurantId)
            return Error.Forbidden("DISPATCHER_SCOPE_FORBIDDEN", _messages.GetMessage("dispatcher_support.forbidden", "You cannot access this restaurant."));
        var restaurantId = _currentUser.UserType == UserType.Admin ? request.RestaurantId : _currentUser.RestaurantId;

        // 1. Fetch complaints
        var complaints = await _db.Complaints
            .AsNoTracking()
            .Include(c => c.EvidencePhotos)
            .Include(c => c.Classifications)
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

        var stopIds = allStops.Select(s => s.Id).ToList();
        var resolvedStopIds = stopIds.Count == 0 ? new HashSet<Guid>() : await _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityId.HasValue && stopIds.Contains(a.EntityId.Value) && a.Action == "SupportIssueResolved")
            .Select(a => a.EntityId!.Value).Distinct().ToHashSetAsync(cancellationToken);

        var stopsWithFailures = allStops
            .Where(s => (!string.IsNullOrEmpty(s.FailureReason) || resolvedStopIds.Contains(s.Id))
                && !complaints.Any(c => c.DriverProfileId.HasValue && c.OrderId == (s.DailyOrderId ?? s.Id)
                    && c.SubscriptionDayId == s.SubscriptionDayId
                    && c.Category is "AddressIssue" or "SevereDelay" or "RoadIssue" or "VehicleBreakdown" or "TrafficAccident" or "SevereWeather" or "DeliveryIssue" or "DriverReassignmentRequested"))
            .ToList();

        var stopById = allStops.ToDictionary(s => s.Id);
        var stopBySubDayId = allStops.Where(s => s.SubscriptionDayId.HasValue)
            .GroupBy(s => s.SubscriptionDayId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var stopByDailyOrderId = allStops.Where(s => s.DailyOrderId.HasValue)
            .GroupBy(s => s.DailyOrderId!.Value)
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
        var areaInfoByIssueId = new Dictionary<Guid, (string Key, string Name)>();

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
            else if (c.OrderId.HasValue && stopByDailyOrderId.TryGetValue(c.OrderId.Value, out var s3))
            {
                stop = s3;
            }
            if (stop is not null)
                areaInfoByIssueId[c.Id] = (StableAreaKey(stop.ZoneNameEn, stop.ZoneNameAr), NonBlank(isAr ? stop.ZoneNameAr : stop.ZoneNameEn) ?? NonBlank(stop.ZoneNameEn) ?? NonBlank(stop.ZoneNameAr) ?? string.Empty);

            var driver = c.DriverProfileId.HasValue && driverById.TryGetValue(c.DriverProfileId.Value, out var d)
                ? d
                : (stop?.Trip?.DriverAssignments?.OrderByDescending(da => da.AssignedAtUtc).FirstOrDefault() is { } da && driverById.TryGetValue(da.DriverProfileId, out var td) ? td : null);

            var reg = driver != null && regByDriverId.TryGetValue(driver.Id, out var r) ? r : null;

            var vehicleText = BuildVehicleInfo(
                driver?.VehicleModel ?? reg?.VehicleModel ?? driver?.VehicleType ?? reg?.VehicleType,
                driver?.VehicleColor ?? reg?.VehicleColor);

            var boxCode = FormatOrderCode(stop?.OrderCode);

            var driverCode = NonBlank(driver?.LicenseNumber) ?? NonBlank(reg?.LicenseNumber);
            var driverName = driver == null ? null : NonBlank(isAr ? driver.FullNameAr : driver.FullNameEn);

            string? areaName;
            if (stop != null && (!string.IsNullOrWhiteSpace(stop.ZoneNameAr) || !string.IsNullOrWhiteSpace(stop.ZoneNameEn)))
            {
                areaName = isAr ? stop.ZoneNameAr : (stop.ZoneNameEn ?? stop.ZoneNameAr);
            }
            else
            {
                areaName = null;
            }

            var timeDiffMinutes = (int)Math.Max(1, (nowUtc - createdAtUtc).TotalMinutes);
            var timeText = isAr ? $"منذ {timeDiffMinutes} دقيقة" : $"{timeDiffMinutes} mins ago";
            var title = !string.IsNullOrWhiteSpace(c.Title) ? c.Title : (!string.IsNullOrWhiteSpace(c.Description) ? c.Description : (isAr ? "شكوى عميل" : "Customer Complaint"));
            var category = c.Category ?? "DeliveryIssue";
            var categoryColor = badgeColor == "Red" ? "#EF4444" : "#F59E0B";
            var priority = GetComplaintSeverity(c);

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
                DriverId: driver?.Id,
                DriverCode: driverCode,
                DriverName: driverName,
                DriverAvatarUrl: CatalogImageStorage.ToContentUrl(driver?.ProfileImageStorageKey),
                Area: areaName,
                VehicleText: vehicleText,
                VehicleInfo: vehicleText,
                Priority: priority,
                PriorityLabel: GetPriorityLabel(priority, isAr),
                PriorityColor: GetPriorityColor(priority),
                Status: statusStr,
                StatusLabel: statusLabel));
        }

        // Add stops with failures not in complaints
        var existingBoxCodes = allIssueCards.Select(i => i.BoxCode).ToHashSet();

        foreach (var stop in stopsWithFailures)
        {
            var createdAtUtc = stop.DeliveredAtUtc ?? stop.ModifiedAtUtc ?? stop.CreatedAtUtc;
            var boxCode = FormatOrderCode(stop.OrderCode);
            if (existingBoxCodes.Contains(boxCode))
            {
                continue;
            }

            var assignment = stop.Trip?.DriverAssignments?.OrderByDescending(da => da.AssignedAtUtc).FirstOrDefault();
            var driver = assignment != null && driverById.TryGetValue(assignment.DriverProfileId, out var d) ? d : null;
            var reg = driver != null && regByDriverId.TryGetValue(driver.Id, out var r) ? r : null;

            var vehicleText = BuildVehicleInfo(
                driver?.VehicleModel ?? reg?.VehicleModel ?? driver?.VehicleType ?? reg?.VehicleType,
                driver?.VehicleColor ?? reg?.VehicleColor);

            var driverCode = NonBlank(driver?.LicenseNumber) ?? NonBlank(reg?.LicenseNumber);
            var driverName = driver == null ? null : NonBlank(isAr ? driver.FullNameAr : driver.FullNameEn);
            var areaName = NonBlank(isAr ? stop.ZoneNameAr : stop.ZoneNameEn)
                ?? NonBlank(isAr ? stop.ZoneNameEn : stop.ZoneNameAr);
            if (string.IsNullOrWhiteSpace(areaName)) areaName = null;
            if (areaName is not null) areaInfoByIssueId[stop.Id] = (StableAreaKey(stop.ZoneNameEn, stop.ZoneNameAr), areaName);

            var (badgeText, badgeColor) = MapCategoryToBadge(stop.FailureReason ?? "", stop.FailureReason ?? "", isAr);
            var timeDiffMinutes = (int)Math.Max(1, (nowUtc - createdAtUtc).TotalMinutes);
            var timeText = isAr ? $"منذ {timeDiffMinutes} دقيقة" : $"{timeDiffMinutes} mins ago";
            var title = stop.FailureReason ?? (isAr ? "مشكلة في تسليم الطلب" : "Delivery stop issue");
            var statusStr = resolvedStopIds.Contains(stop.Id) ? "Resolved"
                : string.IsNullOrWhiteSpace(stop.FailureReason) ? "Resolved" : (stop.Trip?.Status == TripStatus.InProgress ? "InProgress" : "Open");
            var statusLabel = MapStatusLabel(statusStr, isAr);
            var categoryColor = badgeColor == "Red" ? "#EF4444" : "#F59E0B";
            var priority = (string?)null;

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
                DriverId: driver?.Id,
                DriverCode: driverCode,
                DriverName: driverName,
                DriverAvatarUrl: CatalogImageStorage.ToContentUrl(driver?.ProfileImageStorageKey),
                Area: areaName,
                VehicleText: vehicleText,
                VehicleInfo: vehicleText,
                Priority: priority,
                PriorityLabel: GetPriorityLabel(priority, isAr),
                PriorityColor: GetPriorityColor(priority),
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
            new("all", isAr ? "كل المناطق" : "All Areas", dateFilteredList.Count,
                string.IsNullOrWhiteSpace(request.Area) || request.Area.Equals("all", StringComparison.OrdinalIgnoreCase) || request.Area.Equals("كل المناطق", StringComparison.OrdinalIgnoreCase),
                isAr ? "كل المناطق" : "All Areas", dateFilteredList.Count)
        };
        areaChips.AddRange(dateFilteredList.Where(i => areaInfoByIssueId.ContainsKey(i.Id))
            .GroupBy(i => areaInfoByIssueId[i.Id].Key, StringComparer.OrdinalIgnoreCase).Select(group =>
            {
                var name = areaInfoByIssueId[group.First().Id].Name;
                return new SupportAreaChipDto(AreaKey: group.Key, Name: name, Count: group.Count(),
                    IsSelected: string.Equals(request.Area, group.Key, StringComparison.OrdinalIgnoreCase),
                    AreaName: name, IssueCount: group.Count());
            }));
        // 9. Filter Issues (Rule: issues affected by status, area, search, date)
        var filteredIssues = dateFilteredList.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.Status) && !request.Status.Equals("All", StringComparison.OrdinalIgnoreCase) && !request.Status.Equals("الكل", StringComparison.OrdinalIgnoreCase))
        {
            filteredIssues = filteredIssues.Where(i => StatusMatches(i.Status, request.Status));
        }

        if (!string.IsNullOrWhiteSpace(request.Area) && !request.Area.Equals("all", StringComparison.OrdinalIgnoreCase) && !request.Area.Equals("كل المناطق", StringComparison.OrdinalIgnoreCase))
        {
            filteredIssues = filteredIssues.Where(i => areaInfoByIssueId.TryGetValue(i.Id, out var area)
                && (string.Equals(area.Key, request.Area, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(area.Name, request.Area, StringComparison.OrdinalIgnoreCase)));
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

    private static string? NonBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? FormatOrderCode(string? orderCode)
    {
        var value = NonBlank(orderCode);
        if (value is null || value.StartsWith("#", StringComparison.Ordinal))
        {
            return value;
        }

        return $"#{value}";
    }

    private static string? BuildVehicleInfo(string? identity, string? color)
    {
        var parts = new[] { NonBlank(identity), NonBlank(color) }
            .Where(part => part is not null);
        var value = string.Join(" • ", parts);
        return value.Length == 0 ? null : value;
    }

    private static string? GetComplaintSeverity(Complaint complaint)
        => complaint.Classifications
            .OrderByDescending(classification => classification.ClassifiedAtUtc)
            .Select(classification => NonBlank(classification.Severity))
            .FirstOrDefault(severity => severity is not null);

    private string GetPriorityLabel(string? severity, bool isAr)
    {
        if (severity is null)
        {
            return _messages.GetMessage("dispatcher_support.data_unavailable", isAr ? "غير متوفر" : "Unavailable");
        }

        return severity.ToLowerInvariant() switch
        {
            "urgent" => isAr ? "عاجلة" : "Urgent",
            "high" => isAr ? "عالية" : "High",
            "medium" => isAr ? "متوسطة" : "Medium",
            "low" => isAr ? "منخفضة" : "Low",
            _ => severity
        };
    }

    private static string? GetPriorityColor(string? severity)
        => severity?.ToLowerInvariant() switch
        {
            "urgent" or "high" => "#EF4444",
            "medium" => "#F59E0B",
            "low" => "#6B7280",
            _ => null
        };

    private static string StableAreaKey(string? englishName, string? arabicName)
    {
        var source = NonBlank(englishName) ?? NonBlank(arabicName) ?? "";
        return string.Join('-', source.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static (string text, string color) MapCategoryToBadge(string category, string title, bool isAr)
    {
        return category switch
        {
            "AddressIssue" => (isAr ? "مشكلة في العنوان" : "Address issue", "Red"),
            "SevereDelay" => (isAr ? "تأخير شديد" : "Severe delay", "Red"),
            "TrafficAccident" => (isAr ? "حادث مروري" : "Traffic accident", "Red"),
            "RoadIssue" => (isAr ? "مشكلة في الطريق" : "Road issue", "Red"),
            "VehicleBreakdown" => (isAr ? "تعطل المركبة" : "Vehicle breakdown", "Red"),
            "SevereWeather" => (isAr ? "سوء الأحوال الجوية" : "Severe weather", "Orange"),
            "CustomerUnavailable" or "CustomerRefusal" => (isAr ? "العميل غير متاح" : "Customer unavailable", "Orange"),
            "DamagedBox" => (isAr ? "بوكس تالف" : "Damaged box", "Orange"),
            _ => (isAr ? "مشكلة توصيل" : "Delivery issue", "Orange")
        };
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
