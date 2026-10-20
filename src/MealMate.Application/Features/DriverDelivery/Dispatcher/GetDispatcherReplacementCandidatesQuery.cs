using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherReplacementCandidatesQuery(Guid IssueId, int PageNumber = 1, int PageSize = 20) : IQuery<DispatcherReplacementCandidatesResponseDto>;

public sealed class GetDispatcherReplacementCandidatesQueryHandler : IQueryHandler<GetDispatcherReplacementCandidatesQuery, DispatcherReplacementCandidatesResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;
    private readonly ICurrentUser? _user;

    public GetDispatcherReplacementCandidatesQueryHandler(IAppDbContext db, ILocalizedMessageProvider messages, ICurrentUser? user = null)
        => (_db, _messages, _user) = (db, messages, user);

    public async Task<Result<DispatcherReplacementCandidatesResponseDto>> Handle(GetDispatcherReplacementCandidatesQuery request, CancellationToken ct)
    {
        var isAr = _messages.IsArabic();
        if (_user is null || !_user.IsAuthenticated || !_user.UserId.HasValue || _user.UserType is not (UserType.DeliveryManager or UserType.Admin))
            return Error.Forbidden("DISPATCHER_SCOPE_REQUIRED", Txt("dispatcher_support.forbidden", "Dispatcher access is required.", "يلزم صلاحية مسؤول التوصيل."));
        var complaint = await _db.Complaints.AsNoTracking().Include(c => c.Classifications).FirstOrDefaultAsync(c => c.Id == request.IssueId && !c.IsDeleted, ct);
        TripStop? stop;
        if (complaint is not null && (complaint.OrderId.HasValue || complaint.SubscriptionDayId.HasValue))
            stop = await _db.TripStops.AsNoTracking().Include(s => s.Trip).ThenInclude(t => t.DriverAssignments)
                .Where(s => complaint.OrderId.HasValue && (s.Id == complaint.OrderId.Value || s.DailyOrderId == complaint.OrderId.Value)
                    || complaint.SubscriptionDayId.HasValue && s.SubscriptionDayId == complaint.SubscriptionDayId.Value)
                .Where(s => _user.UserType == UserType.Admin || s.Trip.RestaurantId == _user.RestaurantId)
                .OrderByDescending(s => s.CreatedAtUtc).FirstOrDefaultAsync(ct);
        else if (complaint is null)
            stop = await _db.TripStops.AsNoTracking().Include(s => s.Trip).ThenInclude(t => t.DriverAssignments)
                .FirstOrDefaultAsync(s => s.Id == request.IssueId && (_user.UserType == UserType.Admin || s.Trip.RestaurantId == _user.RestaurantId), ct);
        else stop = null;
        if (complaint is null && stop is null) return Error.NotFound("SUPPORT_ISSUE_NOT_FOUND", Txt("dispatcher_support.issue_not_found", "Support issue was not found.", "لم يتم العثور على البلاغ."));
        var restaurantId = complaint?.RestaurantId ?? stop?.Trip?.RestaurantId;
        if (!restaurantId.HasValue) return Error.Conflict("SUPPORT_ISSUE_RESTAURANT_UNAVAILABLE", Txt("dispatcher_support.restaurant_unavailable", "The issue has no restaurant scope.", "لا يوجد نطاق مطعم لهذا البلاغ."));
        if (_user.UserType == UserType.DeliveryManager && _user.RestaurantId != restaurantId.Value)
            return Error.Forbidden("DISPATCHER_SCOPE_FORBIDDEN", Txt("dispatcher_support.forbidden", "You cannot access this restaurant issue.", "لا تملك صلاحية الوصول إلى بلاغ هذا المطعم."));

        var assignmentDriverId = stop?.Trip?.DriverAssignments.Where(a => a.Status == "Active" || a.Status == "Assigned").OrderByDescending(a => a.AssignedAtUtc).Select(a => (Guid?)a.DriverProfileId).FirstOrDefault();
        var currentDriverId = complaint?.DriverProfileId ?? assignmentDriverId;
        var currentDriver = currentDriverId.HasValue ? await _db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(d => d.Id == currentDriverId.Value && !d.IsDeleted, ct) : null;
        (decimal Latitude, decimal Longitude)? pickupLocation = null;
        if (complaint?.Latitude is decimal complaintLatitude && complaint.Longitude is decimal complaintLongitude
            && complaintLatitude is >= -90 and <= 90 && complaintLongitude is >= -180 and <= 180)
            pickupLocation = (complaintLatitude, complaintLongitude);
        else if (currentDriverId.HasValue)
        {
            var reporterPoint = await _db.DeliveryTrackingPoints.AsNoTracking().Where(p => p.DriverProfileId == currentDriverId.Value)
                .OrderByDescending(p => p.RecordedAtUtc).Select(p => new { p.Latitude, p.Longitude }).FirstOrDefaultAsync(ct);
            if (reporterPoint is not null && reporterPoint.Latitude is >= -90 and <= 90 && reporterPoint.Longitude is >= -180 and <= 180)
                pickupLocation = (reporterPoint.Latitude, reporterPoint.Longitude);
        }
        var issueTitle = NonBlank(complaint?.Title) ?? NonBlank(stop?.FailureReason) ?? Txt("dispatcher_support.title_unavailable", "Support issue", "بلاغ دعم");
        var category = NonBlank(complaint?.Category) ?? NonBlank(stop?.FailureReason) ?? "DeliveryIssue";
        var categoryLabel = CategoryLabel(category, issueTitle, isAr);
        var submitted = complaint?.SubmittedAtUtc ?? stop?.CreatedAtUtc;
        var reported = submitted.HasValue ? RelativeTime(submitted.Value, isAr) : Txt("dispatcher_support.time_unavailable", "Time unavailable", "الوقت غير متوفر");
        var code = NonBlank(stop?.OrderCode);
        var area = NonBlank(isAr ? stop?.ZoneNameAr : stop?.ZoneNameEn);
        var severity = complaint?.Classifications.OrderByDescending(x => x.ClassifiedAtUtc).Select(x => NonBlank(x.Severity)).FirstOrDefault(x => x is not null);
        var issueStatus = complaint is null ? "Open" : complaint.Status switch
        {
            ComplaintStatus.Resolved or ComplaintStatus.Closed => "Resolved",
            ComplaintStatus.UnderReview or ComplaintStatus.AwaitingRestaurant or ComplaintStatus.DecisionPending => "InProgress",
            _ => "Open"
        };
        var titleColor = severity is "urgent" or "high" ? "#EF4444" : severity == "medium" ? "#F59E0B" : severity is null ? null : "#6B7280";
        var banner = new SupportIssueSummaryBannerDto(request.IssueId, issueTitle, category, categoryLabel, titleColor,
            submitted, reported, code is null ? null : code.StartsWith('#') ? code : $"#{code}", area,
            stop?.BoxCount, stop is null ? Txt("dispatcher_support.data_unavailable", "Unavailable", "غير متوفر") : isAr ? $"{stop.BoxCount} صندوق متأثر" : $"{stop.BoxCount} affected boxes",
            severity, severity is null ? Txt("dispatcher_support.priority_unavailable", "Unavailable", "غير متوفر") : severity,
            titleColor, issueStatus);
        CurrentDriverUnavailableDto? current = currentDriver is null ? null : new CurrentDriverUnavailableDto(currentDriver.Id,
            NonBlank(currentDriver.LicenseNumber), NonBlank(isAr ? currentDriver.FullNameAr : currentDriver.FullNameEn),
            currentDriver.ProfileImageStorageKey is null ? null : MealMate.Application.Common.Storage.CatalogImageStorage.ToContentUrl(currentDriver.ProfileImageStorageKey),
            currentDriver.IsAvailable ? "Available" : "Unavailable", currentDriver.IsAvailable ? Txt("driver.available", "Available", "متاح") : Txt("driver.unavailable", "Unavailable", "غير متاح"),
            currentDriver.IsAvailable ? "#10B981" : "#EF4444", null, null);

        var restaurantLinks = _db.RestaurantDriverLinks.AsNoTracking().Where(x => x.RestaurantId == restaurantId && x.IsActive).Select(x => x.DriverProfileId);
        var nowUtc = DateTime.UtcNow;
        var candidates = await _db.DriverProfiles.AsNoTracking().Where(d => !d.IsDeleted && d.IsAvailable && d.VerificationStatus == VerificationStatus.Approved
                && (!d.LicenseExpiryUtc.HasValue || d.LicenseExpiryUtc > nowUtc)
                && (!d.NationalIdExpiryUtc.HasValue || d.NationalIdExpiryUtc > nowUtc)
                && (!d.VehicleRegistrationExpiryUtc.HasValue || d.VehicleRegistrationExpiryUtc > nowUtc)
                && (!d.ContractExpiryUtc.HasValue || d.ContractExpiryUtc > nowUtc)
                && (d.RestaurantId == restaurantId || restaurantLinks.Contains(d.Id)) && d.Id != currentDriverId)
            .ToListAsync(ct);
        var activeUsers = await _db.GetActiveAccountUserIdsAsync(candidates.Select(x => x.UserId).ToArray(), ct);
        var candidateIds = candidates.Where(x => activeUsers.Contains(x.UserId)).Select(x => x.Id).ToArray();
        var activeAssignments = await _db.DriverAssignments.AsNoTracking().Where(x => candidateIds.Contains(x.DriverProfileId) && (x.Status == "Active" || x.Status == "Assigned"))
            .Select(x => x.DriverProfileId).Distinct().ToListAsync(ct);
        var activeHandovers = await _db.DriverSupportHandovers.AsNoTracking().Where(x => candidateIds.Contains(x.ReplacementDriverProfileId)
                && (x.Status == "Assigned" || x.Status == "Arrived" || x.Status == "Receiving"))
            .Select(x => x.ReplacementDriverProfileId).Distinct().ToListAsync(ct);
        var onlineSessions = await _db.DriverWorkSessions.AsNoTracking().Where(x => candidateIds.Contains(x.DriverProfileId) && x.Status == "Active" && x.EndedAtUtc == null)
            .Select(x => x.DriverProfileId).Distinct().ToListAsync(ct);
        var eligibleIds = candidateIds.Except(activeAssignments).Except(activeHandovers).Intersect(onlineSessions).ToHashSet();
        var ratings = await _db.DriverRatings.AsNoTracking().Where(x => eligibleIds.Contains(x.DriverProfileId)).GroupBy(x => x.DriverProfileId)
            .Select(g => new { DriverId = g.Key, Rating = (decimal?)g.Average(x => (decimal)x.Score) }).ToDictionaryAsync(x => x.DriverId, x => x.Rating, ct);
        var activeCounts = await _db.DriverAssignments.AsNoTracking().Where(x => eligibleIds.Contains(x.DriverProfileId) && (x.Status == "Active" || x.Status == "Assigned"))
            .GroupBy(x => x.DriverProfileId).Select(g => new { DriverId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.DriverId, x => x.Count, ct);
        var locationRows = await _db.DeliveryTrackingPoints.AsNoTracking().Where(p => eligibleIds.Contains(p.DriverProfileId))
            .GroupBy(p => p.DriverProfileId).Select(g => g.OrderByDescending(p => p.RecordedAtUtc).First()).ToListAsync(ct);
        var latestLocations = locationRows.ToDictionary(p => p.DriverProfileId);
        var candidateDtos = candidates.Where(x => eligibleIds.Contains(x.Id)).Select(d => new ReplacementDriverCandidateDto(
            d.Id, NonBlank(d.LicenseNumber), NonBlank(isAr ? d.FullNameAr : d.FullNameEn),
            d.ProfileImageStorageKey is null ? null : MealMate.Application.Common.Storage.CatalogImageStorage.ToContentUrl(d.ProfileImageStorageKey),
            "Available", Txt("driver.available", "Available", "متاح"), "#10B981", ratings.GetValueOrDefault(d.Id), activeCounts.GetValueOrDefault(d.Id),
            null, null,
            pickupLocation.HasValue && latestLocations.TryGetValue(d.Id, out var point) ? DistanceKm(pickupLocation.Value.Latitude, pickupLocation.Value.Longitude, point.Latitude, point.Longitude) : null,
            pickupLocation.HasValue && latestLocations.TryGetValue(d.Id, out var textPoint)
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, Txt("dispatcher_live_map.distance_remaining_format", "{0} km", "{0} كم"), DistanceKm(pickupLocation.Value.Latitude, pickupLocation.Value.Longitude, textPoint.Latitude, textPoint.Longitude)?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                : null,
            null, null, Vehicle(d), latestLocations.TryGetValue(d.Id, out var lastPoint) ? lastPoint.RecordedAtUtc : null, 0)).ToList();
        candidateDtos = candidateDtos.OrderBy(x => x.ActiveOrdersCount).ThenBy(x => x.DistanceKm).ThenByDescending(x => x.Rating).ThenBy(x => x.DriverId)
            .Select((x, i) => x with { RecommendationRank = i + 1 }).ToList();
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 50); var page = Math.Max(1, request.PageNumber);
        var total = candidateDtos.Count; var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        return Result.Success(new DispatcherReplacementCandidatesResponseDto(banner, banner, current,
            candidateDtos.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            new CandidatePaginationDto(page, pageSize, total, totalPages, page > 1 && totalPages > 0, page < totalPages)));
    }

    private string Txt(string key, string en, string ar) => _messages.GetMessage(key, _messages.IsArabic() ? ar : en);
    private static string? NonBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
    private static decimal? DistanceKm(decimal fromLatitude, decimal fromLongitude, decimal toLatitude, decimal toLongitude)
    {
        const double earthRadiusKm = 6371.0;
        var lat1 = (double)fromLatitude * Math.PI / 180.0;
        var lat2 = (double)toLatitude * Math.PI / 180.0;
        var deltaLat = ((double)toLatitude - (double)fromLatitude) * Math.PI / 180.0;
        var deltaLon = ((double)toLongitude - (double)fromLongitude) * Math.PI / 180.0;
        var a = Math.Pow(Math.Sin(deltaLat / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(deltaLon / 2), 2);
        return (decimal)(earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }
    private static string? Vehicle(MealMate.Domain.IdentityAccess.DriverProfile driver)
    {
        var info = string.Join(" • ", new[] { NonBlank(driver.VehicleModel), NonBlank(driver.VehicleType), NonBlank(driver.VehicleColor), NonBlank(driver.VehiclePlate) }.Where(x => x is not null));
        return info.Length == 0 ? null : info;
    }
    private string RelativeTime(DateTime at, bool isAr)
    {
        var elapsed = DateTime.UtcNow - at.ToUniversalTime();
        if (elapsed.TotalMinutes < 1) return Txt("dispatcher_support.time_now", "Just now", "الآن");
        if (elapsed.TotalMinutes < 60) return string.Format(isAr ? "قبل {0} دقيقة" : "{0} min ago", Math.Max(1, (int)elapsed.TotalMinutes));
        if (elapsed.TotalHours < 24) return string.Format(isAr ? "قبل {0} ساعة" : "{0} hr ago", Math.Max(1, (int)elapsed.TotalHours));
        return at.ToLocalTime().ToString(isAr ? "yyyy/MM/dd HH:mm" : "MMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    private string CategoryLabel(string category, string title, bool isAr)
        => Txt($"driver_issue.category.{category}", category switch
        {
            "AddressIssue" => "Address issue", "SevereDelay" => "Severe delay", "TrafficAccident" => "Traffic accident",
            "RoadIssue" => "Road issue", "VehicleBreakdown" => "Vehicle breakdown", "SevereWeather" => "Severe weather",
            "CustomerUnavailable" => "Customer unavailable", "DamagedBox" => "Damaged box", "CustomerRefusal" => "Customer refusal",
            _ => "Delivery issue"
        }, category switch
        {
            "AddressIssue" => "مشكلة في العنوان", "SevereDelay" => "تأخير شديد", "TrafficAccident" => "حادث مروري",
            "RoadIssue" => "مشكلة في الطريق", "VehicleBreakdown" => "تعطل المركبة", "SevereWeather" => "سوء الأحوال الجوية",
            "CustomerUnavailable" => "العميل غير متاح", "DamagedBox" => "صندوق تالف", "CustomerRefusal" => "رفض العميل الاستلام",
            _ => "مشكلة توصيل"
        });
}
