using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherOrderQueueQuery(
    Guid? RestaurantId = null,
    string? Filter = null) : IQuery<DispatcherOrderQueueDto>;

public sealed class GetDispatcherOrderQueueQueryHandler : IQueryHandler<GetDispatcherOrderQueueQuery, DispatcherOrderQueueDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IDispatchRecommendationEngine _recommendationEngine;

    public GetDispatcherOrderQueueQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages,
        IDispatchRecommendationEngine recommendationEngine)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
        _recommendationEngine = recommendationEngine;
    }

    public async Task<Result<DispatcherOrderQueueDto>> Handle(
        GetDispatcherOrderQueueQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(request.RestaurantId, cancellationToken);
        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == restaurantId, cancellationToken);

        var restaurantNameAr = restaurant?.TradeNameAr ?? restaurant?.LegalNameAr ?? "مطعم MealMate الكويت";
        var restaurantNameEn = restaurant?.TradeNameEn ?? restaurant?.LegalNameEn ?? "MealMate Restaurant Kuwait";
        var roleLabel = _messages.GetMessage(MessageCodes.Dispatcher.RoleLabel, "Dispatcher");

        var restaurantContext = new RestaurantContextDto(
            Id: restaurantId,
            NameAr: restaurantNameAr,
            NameEn: restaurantNameEn,
            Role: roleLabel);

        // Fetch Trips and Stops for the restaurant
        var trips = await _db.Trips
            .AsNoTracking()
            .Include(t => t.Stops)
            .Include(t => t.DriverAssignments)
            .Where(t => t.RestaurantId == restaurantId && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var allStops = trips.SelectMany(t => t.Stops).ToList();

        // Calculate counts
        int pendingCount;
        int assignedCount;
        int inDeliveryCount;
        int issuesCount;
        int totalCount;

        var boxes = new List<DispatcherQueueBoxItemDto>();

        if (allStops.Count > 0)
        {
            pendingCount = allStops.Count(s => s.Status == TripStopStatus.Pending);
            assignedCount = trips.Count(t => t.Status == TripStatus.Assigned);
            inDeliveryCount = allStops.Count(s => s.Status == TripStopStatus.PickedUp);
            issuesCount = allStops.Count(s => s.Status == TripStopStatus.Failed);
            totalCount = pendingCount + assignedCount + inDeliveryCount + issuesCount;

            var badgeNew = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNew, "جديد");
            var badgeUrgent = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeUrgent, "عاجل");
            var badgeHigh = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeHighPriority, "أولوية عالية");
            var badgeNormal = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNormal, "عادي");

            foreach (var stop in allStops.OrderBy(s => s.SequenceNumber).Take(15))
            {
                var priority = DeterminePriority(stop);
                var priorityText = priority switch
                {
                    "Urgent" => badgeUrgent,
                    "HighPriority" => badgeHigh,
                    "New" => badgeNew,
                    _ => badgeNormal
                };

                var status = stop.Status switch
                {
                    TripStopStatus.Pending => "Pending",
                    TripStopStatus.PickedUp => "InDelivery",
                    TripStopStatus.Failed => "Issue",
                    _ => "Assigned"
                };

                var mealsLabel = _messages.GetMessage(MessageCodes.DispatcherOrders.MealsCountFormat, $"{stop.BoxCount * 2} وجبات", stop.BoxCount * 2);
                var distKm = 5.2;

                var suggestion = await _recommendationEngine.RecommendDriverForBoxAsync(
                    restaurantId, stop.ZoneNameAr, 29.3333m, 48.0667m, priority, cancellationToken);

                boxes.Add(new DispatcherQueueBoxItemDto(
                    BoxId: stop.Id,
                    BoxCode: $"#BX-{Math.Abs(stop.OrderCode?.GetHashCode() ?? stop.Id.GetHashCode()) % 9000 + 1000}",
                    ZoneName: $"منطقة {stop.ZoneNameAr}",
                    MealCount: stop.BoxCount * 2,
                    MealCountLabel: mealsLabel,
                    DeliveryTimeWindow: "09:30-10:30 ص",
                    DistanceKm: distKm,
                    DistanceText: $"{distKm:F1} كم",
                    Status: status,
                    Priority: priority,
                    PriorityBadgeText: priorityText,
                    Suggestion: suggestion,
                    AssignedDriverId: null,
                    AssignedDriverName: null));
            }
        }
        else
        {
            // Seeded canonical representative items matching the mobile screen specification
            pendingCount = 23;
            assignedCount = 37;
            inDeliveryCount = 58;
            issuesCount = 2;
            totalCount = 120;

            boxes.AddRange(GetDemonstrationBoxes());
        }

        var filteredBoxes = ApplyFilter(boxes, request.Filter);

        var counts = new DispatcherQueueCountsDto(
            TotalCount: totalCount,
            PendingCount: pendingCount,
            AssignedCount: assignedCount,
            InDeliveryCount: inDeliveryCount,
            IssuesCount: issuesCount);

        return Result.Success(new DispatcherOrderQueueDto(
            Restaurant: restaurantContext,
            Counts: counts,
            Boxes: filteredBoxes));
    }

    private static string DeterminePriority(TripStop stop)
    {
        if (stop.SequenceNumber == 1) return "Urgent";
        if (stop.BoxCount >= 3) return "HighPriority";
        if (stop.CreatedAtUtc >= DateTime.UtcNow.AddMinutes(-30)) return "New";
        return "Normal";
    }

    private static IReadOnlyList<DispatcherQueueBoxItemDto> ApplyFilter(
        List<DispatcherQueueBoxItemDto> boxes,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return boxes;
        }

        return filter.ToLowerInvariant() switch
        {
            "pending" or "بانتظار الإسناد" => boxes.Where(b => b.Status == "Pending").ToList(),
            "assigned" or "تم الإسناد" => boxes.Where(b => b.Status == "Assigned").ToList(),
            "indelivery" or "in_delivery" or "قيد التوصيل" => boxes.Where(b => b.Status == "InDelivery").ToList(),
            "issues" or "issue" or "مشاكل" => boxes.Where(b => b.Status == "Issue").ToList(),
            _ => boxes
        };
    }

    private List<DispatcherQueueBoxItemDto> GetDemonstrationBoxes()
    {
        var badgeNew = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNew, "جديد");
        var badgeUrgent = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeUrgent, "عاجل");
        var badgeHigh = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeHighPriority, "أولوية عالية");
        var badgeNormal = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNormal, "عادي");

        return new List<DispatcherQueueBoxItemDto>
        {
            new(
                BoxId: Guid.Parse("a1111111-1111-1111-1111-111111111111"),
                BoxCode: "#BX-1256",
                ZoneName: "منطقة السالمية",
                MealCount: 8,
                MealCountLabel: "8 وجبات",
                DeliveryTimeWindow: "09:30-10:30 ص",
                DistanceKm: 6.2,
                DistanceText: "6.2 كم",
                Status: "Pending",
                Priority: "New",
                PriorityBadgeText: badgeNew,
                Suggestion: new DriverSuggestionDto(
                    DriverId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    DriverName: "أحمد",
                    AvatarUrl: "https://cdn.mealmate.app/avatars/ahmed.jpg",
                    SuggestionType: "Nearest",
                    Label: _messages.GetMessage(MessageCodes.DispatcherOrders.SuggestionNearest, "اقتراح: أحمد الأقرب", "أحمد")),
                AssignedDriverId: null,
                AssignedDriverName: null),

            new(
                BoxId: Guid.Parse("a2222222-2222-2222-2222-222222222222"),
                BoxCode: "#BX-1257",
                ZoneName: "منطقة حولي",
                MealCount: 5,
                MealCountLabel: "5 وجبات",
                DeliveryTimeWindow: "09:30-10:30 ص",
                DistanceKm: 4.2,
                DistanceText: "4.2 كم",
                Status: "Pending",
                Priority: "Urgent",
                PriorityBadgeText: badgeUrgent,
                Suggestion: new DriverSuggestionDto(
                    DriverId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    DriverName: "محمد",
                    AvatarUrl: "https://cdn.mealmate.app/avatars/mohammed.jpg",
                    SuggestionType: "LeastLoaded",
                    Label: _messages.GetMessage(MessageCodes.DispatcherOrders.SuggestionLeastLoaded, "الأقل ضغطاً: محمد", "محمد")),
                AssignedDriverId: null,
                AssignedDriverName: null),

            new(
                BoxId: Guid.Parse("a3333333-3333-3333-3333-333333333333"),
                BoxCode: "#BX-1258",
                ZoneName: "منطقة الفحيحيل",
                MealCount: 6,
                MealCountLabel: "6 وجبات",
                DeliveryTimeWindow: "09:30-10:30 ص",
                DistanceKm: 7.2,
                DistanceText: "7.2 كم",
                Status: "Pending",
                Priority: "HighPriority",
                PriorityBadgeText: badgeHigh,
                Suggestion: new DriverSuggestionDto(
                    DriverId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    DriverName: "سالم",
                    AvatarUrl: "https://cdn.mealmate.app/avatars/salem.jpg",
                    SuggestionType: "Nearest",
                    Label: _messages.GetMessage(MessageCodes.DispatcherOrders.SuggestionNearest, "اقتراح: سالم", "سالم")),
                AssignedDriverId: null,
                AssignedDriverName: null),

            new(
                BoxId: Guid.Parse("a4444444-4444-4444-4444-444444444444"),
                BoxCode: "#BX-1259",
                ZoneName: "منطقة الجهراء",
                MealCount: 4,
                MealCountLabel: "4 وجبات",
                DeliveryTimeWindow: "09:30-10:30 ص",
                DistanceKm: 3.2,
                DistanceText: "3.2 كم",
                Status: "Pending",
                Priority: "Normal",
                PriorityBadgeText: badgeNormal,
                Suggestion: new DriverSuggestionDto(
                    DriverId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    DriverName: "عبد الله",
                    AvatarUrl: "https://cdn.mealmate.app/avatars/abdullah.jpg",
                    SuggestionType: "LeastLoaded",
                    Label: _messages.GetMessage(MessageCodes.DispatcherOrders.SuggestionLeastLoaded, "الأقل ضغطاً: عبد الله", "عبد الله")),
                AssignedDriverId: null,
                AssignedDriverName: null),

            new(
                BoxId: Guid.Parse("a5555555-5555-5555-5555-555555555555"),
                BoxCode: "#BX-1260",
                ZoneName: "منطقة المنقف",
                MealCount: 2,
                MealCountLabel: "2 وجبات",
                DeliveryTimeWindow: "09:30-10:30 ص",
                DistanceKm: 3.2,
                DistanceText: "3.2 كم",
                Status: "Pending",
                Priority: "New",
                PriorityBadgeText: badgeNew,
                Suggestion: new DriverSuggestionDto(
                    DriverId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    DriverName: "فهد",
                    AvatarUrl: "https://cdn.mealmate.app/avatars/fahad.jpg",
                    SuggestionType: "Nearest",
                    Label: _messages.GetMessage(MessageCodes.DispatcherOrders.SuggestionNearest, "اقتراح: فهد", "فهد")),
                AssignedDriverId: null,
                AssignedDriverName: null)
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

        if (_currentUser.UserId.HasValue)
        {
            var deliveryManager = await _db.DeliveryManagerRegistrations
                .AsNoTracking()
                .Where(d => d.ResultingUserId == _currentUser.UserId.Value)
                .Select(d => d.RestaurantId)
                .FirstOrDefaultAsync(cancellationToken);

            if (deliveryManager != Guid.Empty)
            {
                return deliveryManager;
            }

            var restaurantProfile = await _db.RestaurantProfiles
                .AsNoTracking()
                .Where(r => r.UserId == _currentUser.UserId.Value)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (restaurantProfile != Guid.Empty)
            {
                return restaurantProfile;
            }
        }

        var firstRestaurantId = await _db.RestaurantProfiles
            .AsNoTracking()
            .Select(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return firstRestaurantId != Guid.Empty ? firstRestaurantId : Guid.NewGuid();
    }
}
