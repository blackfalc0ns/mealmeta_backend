using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherDriversQuery(
    Guid? RestaurantId = null,
    string? Search = null,
    string? Status = "All",
    string? SortBy = "Name",
    int PageNumber = 1,
    int PageSize = 20) : IQuery<DispatcherDriversResponseDto>;

public sealed class GetDispatcherDriversQueryHandler : IQueryHandler<GetDispatcherDriversQuery, DispatcherDriversResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherDriversQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<DispatcherDriversResponseDto>> Handle(
        GetDispatcherDriversQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Error.Unauthorized(
                MessageCodes.Auth.InvalidCredentials,
                _messages.GetMessage(MessageCodes.Auth.InvalidCredentials, "Authentication is required."));
        }

        var isDeliveryManager = _currentUser.IsInRole(AppRoles.DeliveryManager) ||
                                _currentUser.IsInRole(AppRoles.RestaurantOwner) ||
                                _currentUser.IsInRole(AppRoles.SuperAdmin) ||
                                _currentUser.IsInRole(AppRoles.OperationsAdmin);

        if (!isDeliveryManager && !_currentUser.HasPermission(PermissionCodes.DeliveryDispatchManage))
        {
            return Error.Forbidden(
                "DRIVER_ACCESS_DENIED",
                _messages.GetMessage("DRIVER_ACCESS_DENIED", "You do not have permission to view drivers."));
        }

        var restaurantId = _currentUser.RestaurantId;
        if (request.RestaurantId.HasValue && request.RestaurantId.Value != Guid.Empty)
        {
            if (restaurantId.HasValue && restaurantId.Value != Guid.Empty &&
                restaurantId.Value != request.RestaurantId.Value &&
                !_currentUser.IsInRole(AppRoles.SuperAdmin) && !_currentUser.IsInRole(AppRoles.OperationsAdmin))
            {
                return Error.Forbidden("DRIVER_ACCESS_DENIED", "You cannot access another restaurant's drivers.");
            }
            restaurantId = request.RestaurantId.Value;
        }

        if (!restaurantId.HasValue || restaurantId.Value == Guid.Empty)
        {
            var firstRest = await _db.RestaurantProfiles.Select(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            restaurantId = firstRest;
        }

        var drivers = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => d.RestaurantId == restaurantId)
            .ToListAsync(cancellationToken);

        if (drivers.Count == 0)
        {
            return Result.Success(new DispatcherDriversResponseDto(
                Counts: new DriverCountsDto(0, 0, 0, 0),
                Items: Array.Empty<DriverListItemDto>(),
                Pagination: new DriverPaginationDto(request.PageNumber, request.PageSize, 0, 0, false, false)));
        }

        var driverIds = drivers.Select(d => d.Id).ToList();
        var driverUserIds = drivers.Select(d => d.UserId).Distinct().ToList();

        var sessions = await _db.DriverWorkSessions.AsNoTracking()
            .Where(s => driverIds.Contains(s.DriverProfileId) && s.Status == "Active")
            .ToListAsync(cancellationToken);
        var sessionByDriver = sessions.GroupBy(s => s.DriverProfileId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.StartedAtUtc).First());

        // 1. Active assignments & trips with pending/picked-up stops
        var inDeliveryDriverIds = await _db.DriverAssignments
            .AsNoTracking()
            .Where(da => driverIds.Contains(da.DriverProfileId) && da.Status == "Active" &&
                         (da.Trip.Status == TripStatus.Assigned || da.Trip.Status == TripStatus.InProgress) &&
                         da.Trip.Stops.Any(s => s.Status == TripStopStatus.Pending || s.Status == TripStopStatus.PickedUp))
            .Select(da => da.DriverProfileId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var inDeliverySet = inDeliveryDriverIds.ToHashSet();

        // 2. Fetch real phone numbers from DriverRegistrations
        var driverPhones = await _db.DriverRegistrations
            .AsNoTracking()
            .Where(r => (r.ResultingDriverProfileId.HasValue && driverIds.Contains(r.ResultingDriverProfileId.Value)) ||
                        (r.SubmittedByUserId.HasValue && driverUserIds.Contains(r.SubmittedByUserId.Value)))
            .Select(r => new { r.ResultingDriverProfileId, r.SubmittedByUserId, r.Phone })
            .ToListAsync(cancellationToken);

        var phoneByDriverId = new Dictionary<Guid, string>();
        foreach (var d in drivers)
        {
            var match = driverPhones.FirstOrDefault(p =>
                (p.ResultingDriverProfileId.HasValue && p.ResultingDriverProfileId.Value == d.Id) ||
                (p.SubmittedByUserId.HasValue && p.SubmittedByUserId.Value == d.UserId));
            if (match != null && !string.IsNullOrWhiteSpace(match.Phone))
            {
                phoneByDriverId[d.Id] = match.Phone;
            }
        }

        // 3. Fetch real ratings from DriverRatings
        var ratingAggregates = await _db.DriverRatings
            .AsNoTracking()
            .Where(r => driverIds.Contains(r.DriverProfileId))
            .GroupBy(r => r.DriverProfileId)
            .Select(g => new
            {
                DriverProfileId = g.Key,
                Count = g.Count(),
                Average = Math.Round((decimal)g.Average(r => r.Score), 2)
            })
            .ToDictionaryAsync(x => x.DriverProfileId, cancellationToken);

        var isAr = _messages.IsArabic();

        // 4. Map drivers with operational priority
        var allMapped = drivers.Select(d =>
        {
            sessionByDriver.TryGetValue(d.Id, out var session);
            var state = DriverOperationalStateResolver.Resolve(d, session, _messages.IsArabic());
            string operationalStatus;
            if (state.ShiftStatus == DriverOperationalStateResolver.StatusInactive)
            {
                operationalStatus = "Unavailable";
            }
            else if (inDeliverySet.Contains(d.Id))
            {
                operationalStatus = "InDelivery";
            }
            else if (state.IsAvailable)
            {
                operationalStatus = "Available";
            }
            else
            {
                operationalStatus = "Unavailable";
            }

            phoneByDriverId.TryGetValue(d.Id, out var phone);
            decimal? avgRating = null;
            int ratingsCount = 0;
            if (ratingAggregates.TryGetValue(d.Id, out var rAgg))
            {
                avgRating = rAgg.Average;
                ratingsCount = rAgg.Count;
            }

            var name = _messages.Localize(d.FullNameAr, d.FullNameEn);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = isAr ? d.FullNameAr : d.FullNameEn;
            }

            return new
            {
                Driver = d,
                Item = new DriverListItemDto(
                    DriverId: d.Id,
                    DriverCode: d.LicenseNumber,
                    FullName: name,
                    PhoneNumber: phone,
                    AvatarStorageKey: d.ProfileImageStorageKey,
                    IsAvailable: state.IsAvailable,
                    OperationalStatus: operationalStatus,
                    Rating: avgRating,
                    RatingsCount: ratingsCount,
                    VehicleType: d.VehicleType,
                    VehicleModel: d.VehicleModel,
                    VehiclePlate: d.VehiclePlate,
                    ShiftStatus: state.ShiftStatus,
                    CurrentStatusText: state.CurrentStatusText)
            };
        }).ToList();

        // Pre-filter counts on the complete authorized restaurant set
        var totalCount = allMapped.Count;
        var availableCount = allMapped.Count(x => x.Item.OperationalStatus == "Available");
        var inDeliveryCount = allMapped.Count(x => x.Item.OperationalStatus == "InDelivery");
        var unavailableCount = allMapped.Count(x => x.Item.OperationalStatus == "Unavailable");

        var counts = new DriverCountsDto(
            Total: totalCount,
            Available: availableCount,
            InDelivery: inDeliveryCount,
            Unavailable: unavailableCount);

        // 5. Apply Status filter
        var filtered = allMapped.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.Status) && !string.Equals(request.Status, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(x => string.Equals(x.Item.OperationalStatus, request.Status, StringComparison.OrdinalIgnoreCase));
        }

        // 6. Apply Search filter
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLowerInvariant();
            filtered = filtered.Where(x =>
                (!string.IsNullOrEmpty(x.Driver.FullNameAr) && x.Driver.FullNameAr.ToLowerInvariant().Contains(s)) ||
                (!string.IsNullOrEmpty(x.Driver.FullNameEn) && x.Driver.FullNameEn.ToLowerInvariant().Contains(s)) ||
                (!string.IsNullOrEmpty(x.Item.PhoneNumber) && x.Item.PhoneNumber.ToLowerInvariant().Contains(s)) ||
                (!string.IsNullOrEmpty(x.Item.DriverCode) && x.Item.DriverCode.ToLowerInvariant().Contains(s)) ||
                (!string.IsNullOrEmpty(x.Item.VehiclePlate) && x.Item.VehiclePlate.ToLowerInvariant().Contains(s)));
        }

        // 7. Apply Sorting
        filtered = (request.SortBy?.ToLowerInvariant()) switch
        {
            "ratingdesc" or "rating" => filtered.OrderByDescending(x => x.Item.Rating ?? 0m),
            "newest" => filtered.OrderByDescending(x => x.Driver.CreatedAtUtc),
            "status" => filtered.OrderBy(x => x.Item.OperationalStatus),
            _ => filtered.OrderBy(x => x.Item.FullName)
        };

        var filteredList = filtered.Select(x => x.Item).ToList();

        // 8. Pagination
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var totalItems = filteredList.Count;
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        var pagedItems = filteredList.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

        var pagination = new DriverPaginationDto(
            PageNumber: pageNumber,
            PageSize: pageSize,
            TotalItems: totalItems,
            TotalPages: totalPages,
            HasPreviousPage: pageNumber > 1,
            HasNextPage: pageNumber < totalPages);

        return Result.Success(new DispatcherDriversResponseDto(counts, pagedItems, pagination));
    }
}


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

        // 1. Resolve dynamic areas strictly from customer order delivery addresses
        var areaDescriptors = await ResolveCustomerOrderAreasAsync(restaurantId, cancellationToken);

        // 2. Fetch Restaurant location to compute accurate distances
        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new { r.Latitude, r.Longitude })
            .FirstOrDefaultAsync(cancellationToken);

        var restLat = restaurant?.Latitude ?? 29.3374m;
        var restLng = restaurant?.Longitude ?? 48.0761m;

        // 3. Fetch real drivers
        var drivers = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => d.RestaurantId == restaurantId || d.RestaurantId == null)
            .ToListAsync(cancellationToken);

        if (drivers.Count == 0)
        {
            var emptyAreas = areaDescriptors.Select((a, idx) => new DriverRosterAreaChipDto(
                AreaKey: a.Key,
                Name: isAr ? a.NameAr : a.NameEn,
                DriverCount: 0,
                IsSelected: idx == 0)).ToList();

            var emptySelectedArea = emptyAreas.FirstOrDefault()?.Name ?? (isAr ? "السالمية" : "Salmiya");
            var emptyTitle = selectedView == "All"
                ? $"{_messages.GetMessage(MessageCodes.DispatcherRoster.TabAllDrivers, isAr ? "كل السائقين" : "All Drivers")} (0)"
                : _messages.GetMessage(MessageCodes.DispatcherRoster.DriversInAreaFormat, isAr ? "السائقين في {0} ({1})" : "Drivers in {0} ({1})", emptySelectedArea, 0);

            return Result.Success(new DispatcherDriverRosterDto(
                BoxId: request.BoxId,
                Counts: new DriverRosterCountsDto(0, 0, 0),
                SelectedView: selectedView,
                SelectedArea: emptySelectedArea,
                SectionTitle: emptyTitle,
                Areas: emptyAreas,
                Drivers: new List<DriverRosterItemDto>()));
        }

        var driverIds = drivers.Select(d => d.Id).ToList();

        // 4. Driver active assignments & trips
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

        var activeCounts = activeAssignments
            .GroupBy(da => da.DriverProfileId)
            .ToDictionary(g => g.Key, g => g.Count());

        // 5. Completed deliveries today
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

        // 6. Ratings
        var stats = await _db.DriverPerformanceStats
            .AsNoTracking()
            .Where(s => driverIds.Contains(s.DriverProfileId))
            .ToDictionaryAsync(s => s.DriverProfileId, cancellationToken);

        // 7. Latest tracking points
        var latestPoints = await _db.DeliveryTrackingPoints
            .AsNoTracking()
            .Where(p => driverIds.Contains(p.DriverProfileId))
            .GroupBy(p => p.DriverProfileId)
            .Select(g => g.OrderByDescending(p => p.RecordedAtUtc).FirstOrDefault()!)
            .ToDictionaryAsync(p => p.DriverProfileId, cancellationToken);

        // 8. Driver regions lookup
        var driverRegionIds = drivers.Where(d => d.RegionId.HasValue).Select(d => d.RegionId!.Value).Distinct().ToList();
        var driverRegions = driverRegionIds.Count > 0
            ? await _db.Regions
                .AsNoTracking()
                .Where(r => driverRegionIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, cancellationToken)
            : new();

        var buttonSelect = _messages.GetMessage(MessageCodes.DispatcherRoster.ButtonSelect, isAr ? "+ اختيار" : "+ Select");
        var buttonUnavailable = _messages.GetMessage(MessageCodes.DispatcherRoster.ButtonUnavailable, isAr ? "غير متاح" : "Unavailable");

        var list = new List<DriverRosterItemDto>();

        foreach (var driver in drivers)
        {
            activeCounts.TryGetValue(driver.Id, out var activeCount);
            completedTodayCounts.TryGetValue(driver.Id, out var completedCount);
            stats.TryGetValue(driver.Id, out var stat);
            latestPoints.TryGetValue(driver.Id, out var point);

            var name = _messages.Localize(driver.FullNameAr, driver.FullNameEn);
            if (string.IsNullOrWhiteSpace(name)) name = isAr ? "سائق" : "Driver";
            var rating = stat?.AvgRating ?? 0.0m;

            // Determine status
            string status;
            string statusText;
            string dotColor;
            bool isAvailable;

            if (!driver.IsAvailable)
            {
                status = "OnBreak";
                statusText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusOnBreak, isAr ? "في استراحة" : "On Break");
                dotColor = "Slate";
                isAvailable = false;
            }
            else if (assignmentByDriver.TryGetValue(driver.Id, out var assign) && string.Equals(assign.Status, "OnTheWay", StringComparison.OrdinalIgnoreCase))
            {
                status = "OnTheWay";
                statusText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusOnTheWay, isAr ? "في طريقه" : "On the Way");
                dotColor = "Orange";
                isAvailable = false;
            }
            else if (activeCount >= 3)
            {
                status = "OnTheWay";
                statusText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusOnTheWay, isAr ? "في طريقه" : "On the Way");
                dotColor = "Orange";
                isAvailable = false;
            }
            else
            {
                status = "Available";
                statusText = _messages.GetMessage(MessageCodes.DispatcherRoster.StatusAvailable, isAr ? "متاح" : "Available");
                dotColor = "Green";
                isAvailable = true;
            }

            // Determine driver zone
            string? driverZoneAr = null;
            string? driverZoneEn = null;

            if (assignmentByDriver.TryGetValue(driver.Id, out var activeAssignment))
            {
                var currentStop = activeAssignment.Trip.Stops
                    .OrderBy(s => s.SequenceNumber)
                    .FirstOrDefault(s => s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Pending);
                if (currentStop != null && (!string.IsNullOrWhiteSpace(currentStop.ZoneNameAr) || !string.IsNullOrWhiteSpace(currentStop.ZoneNameEn)))
                {
                    driverZoneAr = currentStop.ZoneNameAr;
                    driverZoneEn = currentStop.ZoneNameEn;
                }
            }

            if (string.IsNullOrWhiteSpace(driverZoneAr) && driver.RegionId.HasValue && driverRegions.TryGetValue(driver.RegionId.Value, out var reg))
            {
                driverZoneAr = reg.NameAr;
                driverZoneEn = reg.NameEn;
            }

            if (string.IsNullOrWhiteSpace(driverZoneAr) && areaDescriptors.Count > 0)
            {
                driverZoneAr = areaDescriptors[0].NameAr;
                driverZoneEn = areaDescriptors[0].NameEn;
            }

            var currentZoneDisplay = !string.IsNullOrWhiteSpace(driverZoneAr)
                ? (isAr ? driverZoneAr : (driverZoneEn ?? driverZoneAr))
                : string.Empty;

            // Compute realistic distance based on coordinates
            double distKm = 0.0;
            if (point != null)
            {
                var d = KuwaitGeoBounds.HaversineKm(point.Latitude, point.Longitude, restLat, restLng);
                distKm = Math.Round(d, 1);
            }

            var distText = $"{distKm:F0} Km";
            var driverCode = !string.IsNullOrWhiteSpace(driver.LicenseNumber)
                ? driver.LicenseNumber
                : string.Empty;

            list.Add(new DriverRosterItemDto(
                DriverId: driver.Id,
                DriverCode: driverCode,
                FullName: name,
                AvatarUrl: driver.ProfileImageStorageKey,
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
                CurrentZoneName: currentZoneDisplay));
        }

        var total = list.Count;
        var available = list.Count(d => d.IsAvailableForSelection);
        var busy = total - available;

        // Build dynamic Area Chips from customer order delivery addresses
        var requestedArea = request.Area?.Trim();
        var areaChips = new List<DriverRosterAreaChipDto>();
        bool anySelected = false;

        foreach (var desc in areaDescriptors)
        {
            var countInArea = list.Count(d => AreaMatches(d.CurrentZoneName, desc.NameAr, desc.NameEn, desc.Key));
            var isSelected = !string.IsNullOrWhiteSpace(requestedArea) &&
                             AreaMatches(requestedArea, desc.NameAr, desc.NameEn, desc.Key);

            if (isSelected) anySelected = true;

            areaChips.Add(new DriverRosterAreaChipDto(
                AreaKey: desc.Key,
                Name: isAr ? desc.NameAr : desc.NameEn,
                DriverCount: countInArea,
                IsSelected: isSelected));
        }

        // If none selected, default to first area chip
        if (!anySelected && areaChips.Count > 0)
        {
            var first = areaChips[0];
            areaChips[0] = first with { IsSelected = true };
        }

        var activeAreaChip = areaChips.FirstOrDefault(a => a.IsSelected) ?? areaChips.FirstOrDefault();
        var selectedArea = activeAreaChip?.Name ?? (isAr ? "السالمية" : "Salmiya");

        // Filter drivers
        var filteredDrivers = list.AsEnumerable();

        if (selectedView == "ByArea" && activeAreaChip != null)
        {
            var matchedDesc = areaDescriptors.FirstOrDefault(ad => ad.Key == activeAreaChip.AreaKey);
            var nameAr = matchedDesc?.NameAr ?? activeAreaChip.Name;
            var nameEn = matchedDesc?.NameEn ?? activeAreaChip.Name;
            var key = activeAreaChip.AreaKey;

            filteredDrivers = filteredDrivers.Where(d => AreaMatches(d.CurrentZoneName, nameAr, nameEn, key));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            filteredDrivers = filteredDrivers.Where(d => string.Equals(d.Status, request.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLowerInvariant();
            filteredDrivers = filteredDrivers.Where(d => d.FullName.ToLowerInvariant().Contains(s) || d.DriverCode.ToLowerInvariant().Contains(s));
        }

        filteredDrivers = ApplySorting(filteredDrivers, request.SortBy);

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
            Areas: areaChips,
            Drivers: filteredDrivers.ToList()));
    }

    private async Task<List<AreaDescriptor>> ResolveCustomerOrderAreasAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var areas = new Dictionary<string, AreaDescriptor>(StringComparer.OrdinalIgnoreCase);

        // 1. Fetch SubscriptionDays for this restaurant having delivery addresses, lines, or regions
        var subDays = await _db.SubscriptionDays
            .AsNoTracking()
            .Where(sd => sd.RestaurantId == restaurantId &&
                         (sd.DeliveryAddressId != null || sd.DeliveryRegionId != null || !string.IsNullOrWhiteSpace(sd.DeliveryAddressLine)))
            .Select(sd => new {
                sd.DeliveryAddressId,
                sd.DeliveryRegionId,
                sd.DeliveryAddressLine
            })
            .ToListAsync(cancellationToken);

        var addressIds = subDays
            .Where(s => s.DeliveryAddressId.HasValue)
            .Select(s => s.DeliveryAddressId!.Value)
            .Distinct()
            .ToList();

        // 2. Fetch CustomerAddresses
        var customerAddresses = addressIds.Count > 0
            ? await _db.CustomerAddresses
                .AsNoTracking()
                .Where(ca => addressIds.Contains(ca.Id))
                .Select(ca => new { ca.Id, ca.Area, ca.Governorate, ca.AddressLine, ca.RegionId })
                .ToListAsync(cancellationToken)
            : new();

        // 3. Fetch TripStops for this restaurant (customer order delivery stops)
        var tripStopZones = await _db.TripStops
            .AsNoTracking()
            .Where(s => s.Trip.RestaurantId == restaurantId && !s.Trip.IsDeleted &&
                         (!string.IsNullOrWhiteSpace(s.ZoneNameAr) || !string.IsNullOrWhiteSpace(s.ZoneNameEn)))
            .Select(s => new { s.ZoneNameAr, s.ZoneNameEn })
            .ToListAsync(cancellationToken);

        // 4. Fallback: Check if restaurant customers have addresses
        if (customerAddresses.Count == 0 && tripStopZones.Count == 0)
        {
            var customerUserIds = await _db.SubscriptionDays
                .AsNoTracking()
                .Where(sd => sd.RestaurantId == restaurantId)
                .Join(_db.Subscriptions, sd => sd.SubscriptionId, s => s.Id, (sd, s) => s.CustomerUserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (customerUserIds.Count > 0)
            {
                var extra = await _db.CustomerAddresses
                    .AsNoTracking()
                    .Where(ca => customerUserIds.Contains(ca.UserId))
                    .Select(ca => new { ca.Id, ca.Area, ca.Governorate, ca.AddressLine, ca.RegionId })
                    .ToListAsync(cancellationToken);
                customerAddresses.AddRange(extra);
            }
        }

        // 5. Fallback: Query any existing customer delivery addresses in DB
        if (customerAddresses.Count == 0 && tripStopZones.Count == 0)
        {
            var allAddresses = await _db.CustomerAddresses
                .AsNoTracking()
                .Where(ca => !string.IsNullOrWhiteSpace(ca.Area) || !string.IsNullOrWhiteSpace(ca.Governorate) || !string.IsNullOrWhiteSpace(ca.AddressLine))
                .Select(ca => new { ca.Id, ca.Area, ca.Governorate, ca.AddressLine, ca.RegionId })
                .Take(20)
                .ToListAsync(cancellationToken);
            customerAddresses.AddRange(allAddresses);
        }

        // 6. Look up Regions
        var regionIds = subDays.Where(s => s.DeliveryRegionId.HasValue).Select(s => s.DeliveryRegionId!.Value)
            .Union(customerAddresses.Where(ca => ca.RegionId.HasValue).Select(ca => ca.RegionId!.Value))
            .Distinct()
            .ToList();

        var regions = regionIds.Count > 0
            ? await _db.Regions
                .AsNoTracking()
                .Where(r => regionIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, cancellationToken)
            : new();

        // Populate areas from customer addresses
        foreach (var ca in customerAddresses)
        {
            Region? reg = null;
            if (ca.RegionId.HasValue && regions.TryGetValue(ca.RegionId.Value, out var r)) reg = r;

            var areaCandidate = !string.IsNullOrWhiteSpace(ca.Area) ? ca.Area :
                                reg != null ? reg.NameAr :
                                !string.IsNullOrWhiteSpace(ca.Governorate) ? ca.Governorate :
                                ExtractAreaFromAddressLine(ca.AddressLine);

            if (!string.IsNullOrWhiteSpace(areaCandidate))
            {
                var normalized = NormalizeArea(areaCandidate, reg);
                if (!string.IsNullOrWhiteSpace(normalized.Key) && !areas.ContainsKey(normalized.Key))
                {
                    areas[normalized.Key] = normalized;
                }
            }
        }

        // Populate areas from subscription days
        foreach (var sd in subDays)
        {
            Region? reg = null;
            if (sd.DeliveryRegionId.HasValue && regions.TryGetValue(sd.DeliveryRegionId.Value, out var r)) reg = r;

            var areaCandidate = reg != null ? reg.NameAr : ExtractAreaFromAddressLine(sd.DeliveryAddressLine);
            if (!string.IsNullOrWhiteSpace(areaCandidate))
            {
                var normalized = NormalizeArea(areaCandidate, reg);
                if (!string.IsNullOrWhiteSpace(normalized.Key) && !areas.ContainsKey(normalized.Key))
                {
                    areas[normalized.Key] = normalized;
                }
            }
        }

        // Populate areas from trip stops
        foreach (var stop in tripStopZones)
        {
            var areaCandidate = !string.IsNullOrWhiteSpace(stop.ZoneNameAr) ? stop.ZoneNameAr : stop.ZoneNameEn;
            if (!string.IsNullOrWhiteSpace(areaCandidate))
            {
                var normalized = NormalizeArea(areaCandidate);
                if (!string.IsNullOrWhiteSpace(normalized.Key) && !areas.ContainsKey(normalized.Key))
                {
                    areas[normalized.Key] = normalized;
                }
            }
        }

        // Fallback: If still empty, load Kuwait service regions or active catalog regions from DB
        if (areas.Count == 0)
        {
            var serviceRegionIds = await _db.RestaurantServiceRegions
                .AsNoTracking()
                .Where(rsr => rsr.RestaurantId == restaurantId)
                .Select(rsr => rsr.RegionId)
                .ToListAsync(cancellationToken);

            var catalogRegions = serviceRegionIds.Count > 0
                ? await _db.Regions
                    .AsNoTracking()
                    .Where(r => serviceRegionIds.Contains(r.Id))
                    .ToListAsync(cancellationToken)
                : await _db.Regions
                    .AsNoTracking()
                    .Where(r => r.IsActive)
                    .Take(5)
                    .ToListAsync(cancellationToken);

            foreach (var reg in catalogRegions)
            {
                var normalized = NormalizeArea(reg.NameAr, reg);
                if (!areas.ContainsKey(normalized.Key))
                {
                    areas[normalized.Key] = normalized;
                }
            }
        }

        return areas.Values.ToList();
    }

    private sealed record AreaDescriptor(string Key, string NameAr, string NameEn);

    private static AreaDescriptor NormalizeArea(string raw, Region? region = null)
    {
        if (region != null)
        {
            var slug = !string.IsNullOrWhiteSpace(region.Code) ? region.Code.ToLowerInvariant() : GenerateSlug(region.NameEn);
            return new AreaDescriptor(slug, region.NameAr, region.NameEn);
        }

        var clean = CleanAreaName(raw);
        var lower = clean.ToLowerInvariant();

        if (clean.Contains("سالمية") || lower.Contains("salmiya"))
            return new AreaDescriptor("salmiya", "السالمية", "Salmiya");
        if (clean.Contains("حولي") || lower.Contains("hawally") || lower.Contains("hawalli"))
            return new AreaDescriptor("hawally", "حولي", "Hawally");
        if (clean.Contains("حطين") || lower.Contains("hateen") || lower.Contains("hatteen"))
            return new AreaDescriptor("hatteen", "حطين", "Hateen");
        if (clean.Contains("فروانية") || lower.Contains("farwaniya") || lower.Contains("farwaniyah"))
            return new AreaDescriptor("farwaniya", "الفروانية", "Farwaniya");
        if (clean.Contains("عاصمة") || lower.Contains("capital") || clean.Contains("شرق") || clean.Contains("مدينة الكويت") || lower.Contains("kuwait city"))
            return new AreaDescriptor("capital", "العاصمة", "Capital");
        if (clean.Contains("جابرية") || lower.Contains("jabriya"))
            return new AreaDescriptor("jabriya", "الجابرية", "Jabriya");
        if (clean.Contains("بيان") || lower.Contains("bayan"))
            return new AreaDescriptor("bayan", "بيان", "Bayan");
        if (clean.Contains("مشرف") || lower.Contains("mishref"))
            return new AreaDescriptor("mishref", "مشرف", "Mishref");
        if (clean.Contains("روضة") || lower.Contains("rawda"))
            return new AreaDescriptor("rawda", "الروضة", "Rawda");
        if (clean.Contains("أحمدي") || lower.Contains("ahmadi"))
            return new AreaDescriptor("ahmadi", "الأحمدي", "Ahmadi");
        if (clean.Contains("جهراء") || lower.Contains("jahra"))
            return new AreaDescriptor("jahra", "الجهراء", "Jahra");
        if (clean.Contains("مبارك") || lower.Contains("mubarak"))
            return new AreaDescriptor("mubarak", "مبارك الكبير", "Mubarak Al-Kabeer");

        var slugKey = GenerateSlug(clean);
        return new AreaDescriptor(slugKey, clean, clean);
    }

    private static string CleanAreaName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        var parts = trimmed.Split(new[] { '،', ',', '-', '–' }, StringSplitOptions.RemoveEmptyEntries);
        var candidate = parts.Length > 0 ? parts[0].Trim() : trimmed;
        if (candidate.StartsWith("منطقة ")) candidate = candidate.Substring("منطقة ".Length).Trim();
        return candidate;
    }

    private static string ExtractAreaFromAddressLine(string? addressLine)
    {
        if (string.IsNullOrWhiteSpace(addressLine)) return string.Empty;
        var trimmed = addressLine.Trim();
        var parts = trimmed.Split(new[] { '،', ',', '-', '–' }, StringSplitOptions.RemoveEmptyEntries);
        var candidate = parts.Length > 0 ? parts[0].Trim() : trimmed;
        if (candidate.StartsWith("منطقة ")) candidate = candidate.Substring("منطقة ".Length).Trim();
        return candidate;
    }

    private static string GenerateSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "area";
        var chars = text.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray();
        return chars.Length > 0 ? new string(chars).ToLowerInvariant() : "area";
    }

    private static bool AreaMatches(string? text, string nameAr, string nameEn, string key)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();

        return trimmed.Contains(nameAr, StringComparison.OrdinalIgnoreCase) ||
               trimmed.Contains(nameEn, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmed, key, StringComparison.OrdinalIgnoreCase) ||
               (key == "salmiya" && trimmed.Contains("سالمية", StringComparison.OrdinalIgnoreCase)) ||
               (key == "hawally" && trimmed.Contains("حولي", StringComparison.OrdinalIgnoreCase)) ||
               (key == "hatteen" && trimmed.Contains("حطين", StringComparison.OrdinalIgnoreCase)) ||
               (key == "farwaniya" && trimmed.Contains("فروانية", StringComparison.OrdinalIgnoreCase)) ||
               (key == "capital" && (trimmed.Contains("عاصمة", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("شرق", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("مدينة الكويت", StringComparison.OrdinalIgnoreCase)));
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
