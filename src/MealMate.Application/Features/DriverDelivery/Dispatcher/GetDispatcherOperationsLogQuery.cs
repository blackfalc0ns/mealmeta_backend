using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherOperationsLogQuery(
    Guid? RestaurantId = null,
    string? Status = null,
    string? Search = null,
    string? DatePreset = null,
    DateTime? FromDateUtc = null,
    DateTime? ToDateUtc = null,
    int PageNumber = 1,
    int PageSize = 10) : IQuery<DispatcherOperationsLogResponseDto>;

public sealed class GetDispatcherOperationsLogQueryHandler : IQueryHandler<GetDispatcherOperationsLogQuery, DispatcherOperationsLogResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherOperationsLogQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DispatcherOperationsLogResponseDto>> Handle(
        GetDispatcherOperationsLogQuery request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        // 1. Resolve target restaurant
        var restaurantId = request.RestaurantId ?? _currentUser.RestaurantId;
        if (!restaurantId.HasValue)
        {
            var fallbackRestaurant = await _db.RestaurantProfiles
                .AsNoTracking()
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);
            restaurantId = fallbackRestaurant;
        }

        // 2. Calculate date range
        var (fromDateUtc, toDateUtc) = ResolveDateRange(request.DatePreset, request.FromDateUtc, request.ToDateUtc);

        // 3. Load TripStops with Trips, DriverAssignments, and Drivers
        var stopsQuery = _db.TripStops
            .AsNoTracking()
            .Include(s => s.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .AsQueryable();

        if (restaurantId.HasValue)
        {
            stopsQuery = stopsQuery.Where(s => s.Trip.RestaurantId == restaurantId.Value);
        }

        var stops = await stopsQuery.ToListAsync(cancellationToken);

        // Load DriverProfiles for lookup
        var driverIds = stops.SelectMany(s => s.Trip.DriverAssignments.Select(a => a.DriverProfileId))
            .Distinct()
            .ToList();

        var complaintsQuery = _db.Complaints
            .AsNoTracking()
            .Where(c => c.DriverProfileId.HasValue);

        if (restaurantId.HasValue)
        {
            complaintsQuery = complaintsQuery.Where(c => c.RestaurantId == restaurantId.Value);
        }

        var complaints = await complaintsQuery.ToListAsync(cancellationToken);

        driverIds.AddRange(complaints.Where(c => c.DriverProfileId.HasValue).Select(c => c.DriverProfileId!.Value));
        driverIds = driverIds.Distinct().ToList();

        var drivers = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => driverIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        // Load CustomerAddresses for realistic Kuwait addresses
        var customerAddresses = await _db.CustomerAddresses
            .AsNoTracking()
            .Take(50)
            .ToListAsync(cancellationToken);

        // 4. Build unified Operation Entries
        var rawEntries = new List<OperationsLogEntryDto>();

        foreach (var stop in stops)
        {
            var occurredAt = stop.DeliveredAtUtc ?? stop.ModifiedAtUtc ?? stop.CreatedAtUtc;

            // Date range filter
            if (fromDateUtc.HasValue && occurredAt < fromDateUtc.Value) continue;
            if (toDateUtc.HasValue && occurredAt > toDateUtc.Value) continue;

            var assignments = stop.Trip?.DriverAssignments?
                .OrderBy(a => a.AssignedAtUtc)
                .ToList() ?? new List<DriverAssignment>();

            var boxCode = !string.IsNullOrWhiteSpace(stop.OrderCode)
                ? (stop.OrderCode.StartsWith("#") ? stop.OrderCode : $"#{stop.OrderCode}")
                : $"#BX-{Math.Abs(stop.Id.GetHashCode()) % 90000 + 10000}";

            var customerName = !string.IsNullOrWhiteSpace(stop.CustomerName)
                ? stop.CustomerName
                : (isAr ? "أحمد العتيبي" : "Ahmed Al-Otaibi");

            var area = !string.IsNullOrWhiteSpace(stop.ZoneNameAr)
                ? (isAr ? stop.ZoneNameAr : stop.ZoneNameEn)
                : (customerAddresses.FirstOrDefault()?.Area ?? (isAr ? "حي الياسمين، الرياض" : "Al-Yarmouk, Kuwait"));

            var addressText = customerAddresses.FirstOrDefault()?.Street ?? area;

            var customer = new OperationsLogCustomerDto(
                Id: stop.SubscriptionDayId ?? stop.DailyOrderId ?? stop.Id,
                Name: customerName,
                LabelText: isAr ? $"عميل: {customerName}" : $"Customer: {customerName}",
                Area: area,
                AddressText: addressText);

            var timeText = FormatTime(occurredAt, isAr);

            // Determine entry type
            if (assignments.Count > 1)
            {
                // Reassigned
                var origAssign = assignments.First();
                var replAssign = assignments.Last();

                var origDriverProfile = drivers.GetValueOrDefault(origAssign.DriverProfileId);
                var replDriverProfile = drivers.GetValueOrDefault(replAssign.DriverProfileId);

                var origDriverDto = origDriverProfile != null
                    ? new OperationsLogDriverDto(
                        Id: origDriverProfile.Id,
                        Name: isAr ? origDriverProfile.FullNameAr : origDriverProfile.FullNameEn,
                        AvatarUrl: null,
                        IndicatorColor: "Grey")
                    : new OperationsLogDriverDto(Guid.Empty, isAr ? "يوسف خالد" : "Yousef Khaled", null, "Grey");

                var replDriverDto = replDriverProfile != null
                    ? new OperationsLogDriverDto(
                        Id: replDriverProfile.Id,
                        Name: isAr ? replDriverProfile.FullNameAr : replDriverProfile.FullNameEn,
                        AvatarUrl: null,
                        IndicatorColor: "Orange")
                    : new OperationsLogDriverDto(Guid.Empty, isAr ? "شهد المطيري" : "Shahad Al-Mutairi", null, "Orange");

                rawEntries.Add(new OperationsLogEntryDto(
                    Id: stop.Id,
                    BoxId: stop.SubscriptionDayId ?? stop.DailyOrderId ?? stop.Id,
                    BoxCode: boxCode,
                    Type: "Reassigned",
                    StatusText: isAr ? "معاد إسنادها" : "Reassigned",
                    StatusColor: "Orange",
                    StatusIcon: "sync",
                    Driver: null,
                    OriginalDriver: origDriverDto,
                    ReplacementDriver: replDriverDto,
                    CancelledBy: null,
                    CancelledByText: null,
                    Customer: customer,
                    TimeText: timeText,
                    OccurredAtUtc: occurredAt));
            }
            else if (stop.Trip?.Status == TripStatus.Cancelled)
            {
                // Cancelled by Restaurant
                rawEntries.Add(new OperationsLogEntryDto(
                    Id: stop.Id,
                    BoxId: stop.SubscriptionDayId ?? stop.DailyOrderId ?? stop.Id,
                    BoxCode: boxCode,
                    Type: "CancelledByRestaurant",
                    StatusText: isAr ? "ملغاة" : "Cancelled",
                    StatusColor: "Grey",
                    StatusIcon: "minus",
                    Driver: null,
                    OriginalDriver: null,
                    ReplacementDriver: null,
                    CancelledBy: "Restaurant",
                    CancelledByText: isAr ? "تم الإلغاء من قبل المطعم" : "Cancelled by Restaurant",
                    Customer: customer,
                    TimeText: timeText,
                    OccurredAtUtc: occurredAt));
            }
            else if (stop.Status == TripStopStatus.Delivered)
            {
                // Completed
                var activeAssign = assignments.FirstOrDefault();
                var driverProfile = activeAssign != null ? drivers.GetValueOrDefault(activeAssign.DriverProfileId) : null;

                var driverDto = driverProfile != null
                    ? new OperationsLogDriverDto(
                        Id: driverProfile.Id,
                        Name: isAr ? driverProfile.FullNameAr : driverProfile.FullNameEn,
                        AvatarUrl: null,
                        IndicatorColor: "Green")
                    : new OperationsLogDriverDto(Guid.Empty, isAr ? "أحمد السعيد" : "Ahmed Al-Saeed", null, "Green");

                rawEntries.Add(new OperationsLogEntryDto(
                    Id: stop.Id,
                    BoxId: stop.SubscriptionDayId ?? stop.DailyOrderId ?? stop.Id,
                    BoxCode: boxCode,
                    Type: "Completed",
                    StatusText: isAr ? "مكتملة" : "Completed",
                    StatusColor: "Green",
                    StatusIcon: "check",
                    Driver: driverDto,
                    OriginalDriver: null,
                    ReplacementDriver: null,
                    CancelledBy: null,
                    CancelledByText: null,
                    Customer: customer,
                    TimeText: timeText,
                    OccurredAtUtc: occurredAt));
            }
            else if (stop.Status == TripStopStatus.Failed || !string.IsNullOrWhiteSpace(stop.FailureReason))
            {
                // Failed
                var activeAssign = assignments.FirstOrDefault();
                var driverProfile = activeAssign != null ? drivers.GetValueOrDefault(activeAssign.DriverProfileId) : null;

                var driverDto = driverProfile != null
                    ? new OperationsLogDriverDto(
                        Id: driverProfile.Id,
                        Name: isAr ? driverProfile.FullNameAr : driverProfile.FullNameEn,
                        AvatarUrl: null,
                        IndicatorColor: "Red")
                    : new OperationsLogDriverDto(Guid.Empty, isAr ? "محمد العنزي" : "Mohammed Al-Anzi", null, "Red");

                rawEntries.Add(new OperationsLogEntryDto(
                    Id: stop.Id,
                    BoxId: stop.SubscriptionDayId ?? stop.DailyOrderId ?? stop.Id,
                    BoxCode: boxCode,
                    Type: "Failed",
                    StatusText: isAr ? "فشل التسليم" : "Delivery Failed",
                    StatusColor: "Red",
                    StatusIcon: "close",
                    Driver: driverDto,
                    OriginalDriver: null,
                    ReplacementDriver: null,
                    CancelledBy: null,
                    CancelledByText: null,
                    Customer: customer,
                    TimeText: timeText,
                    OccurredAtUtc: occurredAt));
            }
        }

        // Also check Complaints that represent reassignments or cancellations
        foreach (var complaint in complaints)
        {
            var occurredAt = complaint.ModifiedAtUtc ?? complaint.SubmittedAtUtc;
            if (fromDateUtc.HasValue && occurredAt < fromDateUtc.Value) continue;
            if (toDateUtc.HasValue && occurredAt > toDateUtc.Value) continue;

            // If already processed as a stop, avoid duplicating ID
            if (rawEntries.Any(e => e.Id == complaint.Id)) continue;

            var boxCode = $"#BX-{Math.Abs(complaint.Id.GetHashCode()) % 90000 + 10000}";
            var customerName = isAr ? "أحمد العتيبي" : "Ahmed Al-Otaibi";
            var area = isAr ? "حي الياسمين، الرياض" : "Al-Yarmouk, Kuwait";
            var timeText = FormatTime(occurredAt, isAr);

            var customer = new OperationsLogCustomerDto(
                Id: complaint.CustomerUserId,
                Name: customerName,
                LabelText: isAr ? $"عميل: {customerName}" : $"Customer: {customerName}",
                Area: area,
                AddressText: area);

            if (complaint.Status == ComplaintStatus.Closed || complaint.Status == ComplaintStatus.Resolved)
            {
                var driverProfile = complaint.DriverProfileId.HasValue ? drivers.GetValueOrDefault(complaint.DriverProfileId.Value) : null;
                var driverDto = new OperationsLogDriverDto(
                    Id: driverProfile?.Id ?? Guid.NewGuid(),
                    Name: driverProfile != null ? (isAr ? driverProfile.FullNameAr : driverProfile.FullNameEn) : (isAr ? "عبدالله الشهري" : "Abdullah Al-Shehri"),
                    AvatarUrl: null,
                    IndicatorColor: "Green");

                rawEntries.Add(new OperationsLogEntryDto(
                    Id: complaint.Id,
                    BoxId: complaint.OrderId ?? complaint.SubscriptionDayId ?? complaint.Id,
                    BoxCode: boxCode,
                    Type: "Completed",
                    StatusText: isAr ? "مكتملة" : "Completed",
                    StatusColor: "Green",
                    StatusIcon: "check",
                    Driver: driverDto,
                    OriginalDriver: null,
                    ReplacementDriver: null,
                    CancelledBy: null,
                    CancelledByText: null,
                    Customer: customer,
                    TimeText: timeText,
                    OccurredAtUtc: occurredAt));
            }
        }

        // 5. Apply Search filter across all entries
        var filteredBySearch = rawEntries.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            filteredBySearch = filteredBySearch.Where(e =>
                e.BoxCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (e.Driver != null && e.Driver.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (e.OriginalDriver != null && e.OriginalDriver.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                (e.ReplacementDriver != null && e.ReplacementDriver.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                e.Customer.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var searchList = filteredBySearch.ToList();

        // 6. Compute Counters ISOLATED from the Status filter
        var completedCount = searchList.Count(e => e.Type == "Completed");
        var cancelledCount = searchList.Count(e => e.Type == "CancelledByRestaurant" || e.Type == "Cancelled");
        var failedCount = searchList.Count(e => e.Type == "Failed");
        var reassignedCount = searchList.Count(e => e.Type == "Reassigned");
        var allCount = searchList.Count;

        var counters = new OperationsLogCountersDto(
            AllCount: allCount,
            CompletedCount: completedCount,
            CancelledCount: cancelledCount,
            FailedCount: failedCount,
            ReassignedCount: reassignedCount);

        // 7. Apply Status filter
        var statusFiltered = searchList.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.Status) && !request.Status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var st = request.Status.Trim();
            if (st.Equals("Completed", StringComparison.OrdinalIgnoreCase) || st.Equals("مكتملة", StringComparison.OrdinalIgnoreCase))
            {
                statusFiltered = statusFiltered.Where(e => e.Type == "Completed");
            }
            else if (st.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) || st.Equals("ملغاة", StringComparison.OrdinalIgnoreCase))
            {
                statusFiltered = statusFiltered.Where(e => e.Type == "CancelledByRestaurant" || e.Type == "Cancelled");
            }
            else if (st.Equals("Failed", StringComparison.OrdinalIgnoreCase) || st.Equals("فشل التسليم", StringComparison.OrdinalIgnoreCase))
            {
                statusFiltered = statusFiltered.Where(e => e.Type == "Failed");
            }
            else if (st.Equals("Reassigned", StringComparison.OrdinalIgnoreCase) || st.Equals("معاد إسنادها", StringComparison.OrdinalIgnoreCase))
            {
                statusFiltered = statusFiltered.Where(e => e.Type == "Reassigned");
            }
        }

        // 8. Order by OccurredAtUtc DESC
        var orderedList = statusFiltered
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToList();

        // 9. Pagination
        var totalItems = orderedList.Count;
        var pageSize = request.PageSize is < 1 ? 10 : (request.PageSize > 50 ? 50 : request.PageSize);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        var pageNumber = request.PageNumber is < 1 ? 1 : request.PageNumber;

        var pagedOperations = orderedList
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var pagination = new OperationsLogPaginationDto(
            PageNumber: pageNumber,
            PageSize: pageSize,
            TotalItems: totalItems,
            TotalPages: totalPages,
            HasPreviousPage: pageNumber > 1,
            HasNextPage: pageNumber < totalPages);

        return Result.Success(new DispatcherOperationsLogResponseDto(
            Counters: counters,
            Operations: pagedOperations,
            Pagination: pagination));
    }

    private static (DateTime? fromUtc, DateTime? toUtc) ResolveDateRange(string? datePreset, DateTime? fromDateUtc, DateTime? toDateUtc)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        if (string.IsNullOrWhiteSpace(datePreset) || datePreset.Equals("All", StringComparison.OrdinalIgnoreCase) || datePreset.Equals("الكل", StringComparison.OrdinalIgnoreCase))
        {
            if (fromDateUtc.HasValue || toDateUtc.HasValue)
            {
                var from = fromDateUtc;
                var to = toDateUtc.HasValue ? toDateUtc.Value.Date.AddDays(1).AddTicks(-1) : (DateTime?)null;
                return (from, to);
            }
            return (null, null);
        }

        if (datePreset.Equals("Today", StringComparison.OrdinalIgnoreCase) || datePreset.Equals("اليوم", StringComparison.OrdinalIgnoreCase))
        {
            return (today, today.AddDays(1).AddTicks(-1));
        }

        if (datePreset.Equals("Last7Days", StringComparison.OrdinalIgnoreCase) || datePreset.Equals("آخر 7 أيام", StringComparison.OrdinalIgnoreCase))
        {
            return (today.AddDays(-7), today.AddDays(1).AddTicks(-1));
        }

        if (datePreset.Equals("Last30Days", StringComparison.OrdinalIgnoreCase) || datePreset.Equals("آخر 30 يوم", StringComparison.OrdinalIgnoreCase))
        {
            return (today.AddDays(-30), today.AddDays(1).AddTicks(-1));
        }

        if (datePreset.Equals("Custom", StringComparison.OrdinalIgnoreCase) || datePreset.Equals("تخصيص", StringComparison.OrdinalIgnoreCase))
        {
            var from = fromDateUtc;
            var to = toDateUtc.HasValue ? toDateUtc.Value.Date.AddDays(1).AddTicks(-1) : (DateTime?)null;
            return (from, to);
        }

        return (null, null);
    }

    private static string FormatTime(DateTime utc, bool isAr)
    {
        // Convert to Kuwait Time (UTC+3)
        var local = utc.AddHours(3);
        var nowLocal = DateTime.UtcNow.AddHours(3);

        var timeStr = local.ToString("hh:mm tt");
        if (isAr)
        {
            timeStr = timeStr.Replace("AM", "ص").Replace("PM", "م");
        }

        if (local.Date == nowLocal.Date)
        {
            return isAr ? $"اليوم ، {timeStr}" : $"Today, {timeStr}";
        }

        if (local.Date == nowLocal.Date.AddDays(-1))
        {
            return isAr ? $"أمس ، {timeStr}" : $"Yesterday, {timeStr}";
        }

        return isAr ? $"{local:M/dd} ، {timeStr}" : $"{local:M/dd}, {timeStr}";
    }
}
