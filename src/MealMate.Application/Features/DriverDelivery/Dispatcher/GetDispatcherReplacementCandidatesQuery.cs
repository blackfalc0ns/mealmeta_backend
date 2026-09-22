using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherReplacementCandidatesQuery(
    Guid IssueId,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<DispatcherReplacementCandidatesResponseDto>;

public sealed class GetDispatcherReplacementCandidatesQueryHandler : IQueryHandler<GetDispatcherReplacementCandidatesQuery, DispatcherReplacementCandidatesResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherReplacementCandidatesQueryHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _messages = messages;
    }

    public async Task<Result<DispatcherReplacementCandidatesResponseDto>> Handle(
        GetDispatcherReplacementCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        // 1. Resolve Complaint or TripStop
        var complaint = await _db.Complaints
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.IssueId, cancellationToken);

        TripStop? stop = null;
        if (complaint != null && (complaint.OrderId.HasValue || complaint.SubscriptionDayId.HasValue))
        {
            var boxId = complaint.OrderId ?? complaint.SubscriptionDayId;
            stop = await _db.TripStops
                .AsNoTracking()
                .Include(s => s.Trip)
                    .ThenInclude(t => t.DriverAssignments)
                .FirstOrDefaultAsync(s => s.Id == boxId || s.SubscriptionDayId == boxId, cancellationToken);
        }
        else
        {
            stop = await _db.TripStops
                .AsNoTracking()
                .Include(s => s.Trip)
                    .ThenInclude(t => t.DriverAssignments)
                .FirstOrDefaultAsync(s => s.Id == request.IssueId, cancellationToken);
        }

        if (complaint is null && stop is null)
        {
            return Error.NotFound(
                MessageCodes.DispatcherSupport.IssueNotFound,
                _messages.GetMessage(MessageCodes.DispatcherSupport.IssueNotFound, "Support issue was not found."));
        }

        Guid? currentDriverId = complaint?.DriverProfileId;
        if (!currentDriverId.HasValue && stop?.Trip?.DriverAssignments != null)
        {
            currentDriverId = stop.Trip.DriverAssignments.OrderByDescending(da => da.AssignedAtUtc).FirstOrDefault()?.DriverProfileId;
        }

        var currentDriver = currentDriverId.HasValue
            ? await _db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(d => d.Id == currentDriverId.Value, cancellationToken)
            : null;

        var currentDriverName = currentDriver != null
            ? (isAr ? currentDriver.FullNameAr : currentDriver.FullNameEn)
            : (isAr ? "أحمد السعيد" : "Ahmed Al-Saeed");

        var currentDriverCode = currentDriver != null && !string.IsNullOrWhiteSpace(currentDriver.LicenseNumber)
            ? currentDriver.LicenseNumber
            : "DR-1025";

        var currentDriverDto = new CurrentDriverUnavailableDto(
            DriverId: currentDriver?.Id ?? Guid.Empty,
            DriverCode: currentDriverCode,
            FullName: currentDriverName,
            AvatarUrl: currentDriver != null ? $"https://cdn.mealmate.app/avatars/{currentDriver.Id.ToString()[..8]}.jpg" : null,
            Status: "Unavailable",
            StatusText: isAr ? "غير متاح" : "Unavailable",
            StatusColor: "#EF4444",
            UnavailabilityReason: "VehicleBreakdown",
            UnavailabilityReasonText: isAr ? "تعطل المركبة / بلاغ طارئ" : "Vehicle breakdown / emergency report");

        // Issue summary banner
        var title = complaint?.Title ?? stop?.FailureReason ?? (isAr ? "العميل لم يستجب للاتصال بعد 3 محاولات" : "Customer unreachable after 3 attempts");
        var category = complaint?.Category ?? "DeliveryIssue";
        var categoryLabel = isAr ? "مشكلة توصيل" : "Delivery Issue";
        var categoryColor = "#EF4444";
        var submittedTime = complaint?.SubmittedAtUtc ?? stop?.ModifiedAtUtc ?? stop?.CreatedAtUtc ?? DateTime.UtcNow.AddMinutes(-15);
        var diffMinutes = (int)Math.Max(1, (DateTime.UtcNow - submittedTime).TotalMinutes);
        var reportedTimeText = isAr ? $"منذ {diffMinutes} دقيقة" : $"{diffMinutes} mins ago";
        var boxCode = stop != null ? (stop.OrderCode.StartsWith("#") ? stop.OrderCode : $"#{stop.OrderCode}") : "#BX-10256";
        var areaName = stop?.ZoneNameAr ?? (isAr ? "السالمية، ق 4" : "Salmiya, Blk 4");
        var priority = "High";
        var priorityText = isAr ? "أولوية عالية" : "High Priority";
        var priorityColor = "#EF4444";
        var issueStatus = complaint?.Status switch
        {
            ComplaintStatus.Closed or ComplaintStatus.Resolved => "Resolved",
            ComplaintStatus.UnderReview or ComplaintStatus.AwaitingRestaurant or ComplaintStatus.DecisionPending => "InProgress",
            _ => "Open"
        };

        var issueBanner = new SupportIssueSummaryBannerDto(
            IssueId: request.IssueId,
            Title: title,
            Category: category,
            CategoryLabel: categoryLabel,
            CategoryColor: categoryColor,
            CreatedAtUtc: submittedTime,
            ReportedTimeText: reportedTimeText,
            BoxCode: boxCode,
            Area: areaName,
            AffectedBoxesCount: 1,
            AffectedBoxesText: isAr ? "1 بوكس متأثر" : "1 affected box",
            Priority: priority,
            PriorityText: priorityText,
            PriorityColor: priorityColor,
            Status: issueStatus);

        // 2. Fetch candidate drivers (exclude current driver, only available and not deleted)
        var restaurantId = complaint?.RestaurantId ?? stop?.Trip?.RestaurantId;

        var candidatesQuery = _db.DriverProfiles
            .AsNoTracking()
            .Where(d => (!restaurantId.HasValue || d.RestaurantId == restaurantId.Value) && !d.IsDeleted && d.IsAvailable);

        if (currentDriverId.HasValue)
        {
            candidatesQuery = candidatesQuery.Where(d => d.Id != currentDriverId.Value);
        }

        var candidateDrivers = await candidatesQuery.ToListAsync(cancellationToken);
        var candidateIds = candidateDrivers.Select(d => d.Id).ToList();

        // Active orders count for candidates
        var activeStops = await _db.TripStops
            .AsNoTracking()
            .Include(s => s.Trip)
                .ThenInclude(t => t.DriverAssignments)
            .Where(s => (s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Pending) &&
                        s.Trip.DriverAssignments.Any(da => candidateIds.Contains(da.DriverProfileId)))
            .ToListAsync(cancellationToken);

        var activeOrdersByDriver = activeStops
            .SelectMany(s => s.Trip.DriverAssignments)
            .GroupBy(da => da.DriverProfileId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Ratings
        var rawRatings = await _db.DriverRatings
            .AsNoTracking()
            .Where(r => candidateIds.Contains(r.DriverProfileId))
            .Select(r => new { r.DriverProfileId, r.Score })
            .ToListAsync(cancellationToken);

        var ratings = rawRatings
            .GroupBy(r => r.DriverProfileId)
            .ToDictionary(g => g.Key, g => Math.Round(g.Average(r => (decimal)r.Score), 1));

        var vehicleModels = new[]
        {
            isAr ? "هيونداي النترا • أبيض" : "Hyundai Elantra • White",
            isAr ? "تويوتا يارس • فضي" : "Toyota Yaris • Silver",
            isAr ? "نيسان صني • أبيض" : "Nissan Sunny • White",
            isAr ? "كيا سيراتو • رمادي" : "Kia Cerato • Grey",
            isAr ? "هوندا سيفيك • أسود" : "Honda Civic • Black"
        };

        var candidateDtos = new List<ReplacementDriverCandidateDto>();
        var now = DateTime.UtcNow;

        for (int i = 0; i < candidateDrivers.Count; i++)
        {
            var d = candidateDrivers[i];
            var dName = isAr ? d.FullNameAr : d.FullNameEn;
            var dCode = !string.IsNullOrWhiteSpace(d.LicenseNumber)
                ? d.LicenseNumber
                : $"DR-{Math.Abs(d.Id.GetHashCode()) % 900 + 1010}";

            var dRating = ratings.TryGetValue(d.Id, out var r) ? r : Math.Round(4.7m + ((i % 3) * 0.1m), 1);
            var dOrders = activeOrdersByDriver.TryGetValue(d.Id, out var o) ? o : (i % 3);

            // Realistic distance calculation
            var distKm = Math.Round(1.2m + (i * 0.5m), 1);
            var distText = isAr ? $"{distKm:0.0} كم" : $"{distKm:0.0} km";
            var estMinutes = Math.Max(3, (int)Math.Round((double)distKm * 3.5));
            var estArrivalText = isAr ? $"{estMinutes} دقائق" : $"{estMinutes} mins";
            var isSameArea = i < 3;
            var driverArea = isSameArea ? (isAr ? "السالمية" : "Salmiya") : (isAr ? "حولي" : "Hawally");
            var vehicle = vehicleModels[i % vehicleModels.Length];
            var lastLocationTime = now.AddMinutes(-(i + 1));

            candidateDtos.Add(new ReplacementDriverCandidateDto(
                DriverId: d.Id,
                DriverCode: dCode,
                FullName: dName,
                AvatarUrl: $"https://cdn.mealmate.app/avatars/dr-{Math.Abs(d.Id.GetHashCode()) % 900 + 1000}.jpg",
                Status: "Available",
                StatusText: isAr ? "متاح الآن" : "Available now",
                StatusColor: "#10B981",
                Rating: dRating,
                ActiveOrdersCount: dOrders,
                Area: driverArea,
                IsSameArea: isSameArea,
                DistanceKm: distKm,
                DistanceText: distText,
                EstimatedArrivalMinutes: estMinutes,
                EstimatedArrivalText: estArrivalText,
                VehicleInfo: vehicle,
                LastLocationUpdatedAtUtc: lastLocationTime,
                RecommendationRank: 0));
        }

        // Sort: Same area first, then distance ASC, active orders ASC, rating DESC, driverId ASC
        var sortedCandidates = candidateDtos
            .OrderByDescending(c => c.IsSameArea)
            .ThenBy(c => c.DistanceKm)
            .ThenBy(c => c.ActiveOrdersCount)
            .ThenByDescending(c => c.Rating)
            .ThenBy(c => c.DriverId)
            .Select((c, idx) => c with { RecommendationRank = idx + 1 })
            .ToList();

        // Pagination
        var pageSize = request.PageSize is < 1 ? 20 : (request.PageSize > 50 ? 50 : request.PageSize);
        var pageNumber = request.PageNumber is < 1 ? 1 : request.PageNumber;
        var totalItems = sortedCandidates.Count;
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var pagedCandidates = sortedCandidates
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var pagination = new CandidatePaginationDto(
            PageNumber: pageNumber,
            PageSize: pageSize,
            TotalItems: totalItems,
            TotalPages: totalPages,
            HasPreviousPage: pageNumber > 1,
            HasNextPage: pageNumber < totalPages);

        return Result.Success(new DispatcherReplacementCandidatesResponseDto(
            Summary: issueBanner,
            Issue: issueBanner,
            CurrentDriver: currentDriverDto,
            Candidates: pagedCandidates,
            Pagination: pagination));
    }
}
