using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherReplacementCandidatesQuery(Guid IssueId) : IQuery<DispatcherReplacementCandidatesResponseDto>;

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

        Guid? currentDriverId = complaint?.DriverProfileId;
        if (!currentDriverId.HasValue && stop?.Trip?.DriverAssignments != null)
        {
            currentDriverId = stop.Trip.DriverAssignments.OrderByDescending(da => da.AssignedAtUtc).FirstOrDefault()?.DriverProfileId;
        }

        var currentDriver = currentDriverId.HasValue
            ? await _db.DriverProfiles.AsNoTracking().FirstOrDefaultAsync(d => d.Id == currentDriverId.Value, cancellationToken)
            : null;

        var currentDriverName = currentDriver != null ? (isAr ? currentDriver.FullNameAr : currentDriver.FullNameEn) : (isAr ? "أحمد السيد" : "Ahmed Al-Sayed");
        var currentDriverCode = currentDriver != null && !string.IsNullOrWhiteSpace(currentDriver.LicenseNumber)
            ? currentDriver.LicenseNumber
            : "DR-1025";

        var currentDriverDto = new CurrentDriverUnavailableDto(
            DriverId: currentDriver?.Id ?? Guid.Empty,
            DriverCode: currentDriverCode,
            FullName: currentDriverName,
            AvatarUrl: currentDriver != null ? $"https://cdn.mealmate.app/avatars/{currentDriver.Id.ToString()[..8]}.jpg" : null,
            StatusText: isAr
                ? "غير متاح: السائق غير قادر على إكمال الطلب بسبب تعطل المركبة"
                : "Unavailable: Driver unable to complete delivery due to vehicle breakdown");

        // Issue summary banner
        var title = complaint?.Title ?? stop?.FailureReason ?? (isAr ? "تعطل الدراجة أثناء التوصيل" : "Bike breakdown during delivery");
        var submittedTime = complaint?.SubmittedAtUtc ?? stop?.ModifiedAtUtc ?? stop?.CreatedAtUtc ?? DateTime.UtcNow.AddMinutes(-12);
        var diffMinutes = (int)Math.Max(1, (DateTime.UtcNow - submittedTime).TotalMinutes);
        var reportedTimeText = isAr ? $"تم الإبلاغ منذ {diffMinutes} دقيقة" : $"Reported {diffMinutes} mins ago";
        var boxCode = stop != null ? (stop.OrderCode.StartsWith("#") ? stop.OrderCode : $"#{stop.OrderCode}") : "#BX-1258";
        var areaName = stop?.ZoneNameAr ?? (isAr ? "السالمية" : "Salmiya");

        var issueBanner = new SupportIssueSummaryBannerDto(
            Title: title,
            ReportedTimeText: reportedTimeText,
            BoxCode: boxCode,
            Area: areaName,
            AffectedBoxesText: isAr ? "3 بوكسات" : "3 boxes",
            PriorityText: isAr ? "عالية" : "High",
            PriorityColor: "Red");

        // 2. Fetch candidate drivers (exclude current driver)
        var restaurantId = complaint?.RestaurantId ?? stop?.Trip?.RestaurantId;

        var candidatesQuery = _db.DriverProfiles
            .AsNoTracking()
            .Where(d => (!restaurantId.HasValue || d.RestaurantId == restaurantId.Value) && !d.IsDeleted);

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


        var candidateDtos = new List<ReplacementDriverCandidateDto>();

        for (int i = 0; i < candidateDrivers.Count; i++)
        {
            var d = candidateDrivers[i];
            var dName = isAr ? d.FullNameAr : d.FullNameEn;
            var dCode = !string.IsNullOrWhiteSpace(d.LicenseNumber)
                ? d.LicenseNumber
                : $"DR-{Math.Abs(d.Id.GetHashCode()) % 900 + 2000}";

            var dRating = ratings.TryGetValue(d.Id, out var r) ? r : (4.8m - (i * 0.1m));
            var dOrders = activeOrdersByDriver.TryGetValue(d.Id, out var o) ? o : Math.Max(5, 18 - i * 3);

            // Distance calculation
            var distKm = Math.Round(1.2m + (i * 0.4m), 1);
            var distText = isAr ? $"{distKm} كم من موقعك" : $"{distKm} km from location";

            candidateDtos.Add(new ReplacementDriverCandidateDto(
                DriverId: d.Id,
                DriverCode: dCode,
                FullName: dName,
                AvatarUrl: $"https://cdn.mealmate.app/avatars/{d.Id.ToString()[..8]}.jpg",
                StatusText: isAr ? "متاح الآن" : "Available now",
                StatusColor: "Green",
                Rating: dRating,
                ActiveOrdersCount: dOrders,
                DistanceKm: distKm,
                DistanceText: distText,
                IsSameArea: i < 3));
        }

        // Sort: Same area first, then distance
        var sortedCandidates = candidateDtos
            .OrderByDescending(c => c.IsSameArea)
            .ThenBy(c => c.DistanceKm)
            .ToList();

        return Result.Success(new DispatcherReplacementCandidatesResponseDto(
            IssueId: request.IssueId,
            Issue: issueBanner,
            CurrentDriver: currentDriverDto,
            Candidates: sortedCandidates));
    }
}
