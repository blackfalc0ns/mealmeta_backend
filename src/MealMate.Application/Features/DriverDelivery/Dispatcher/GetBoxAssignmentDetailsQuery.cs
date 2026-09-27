using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetBoxAssignmentDetailsQuery(
    Guid BoxId,
    Guid? RestaurantId = null) : IQuery<BoxAssignmentDetailsDto>;

public sealed class GetBoxAssignmentDetailsQueryHandler : IQueryHandler<GetBoxAssignmentDetailsQuery, BoxAssignmentDetailsDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IDispatchRecommendationEngine _recommendationEngine;

    public GetBoxAssignmentDetailsQueryHandler(
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

    public async Task<Result<BoxAssignmentDetailsDto>> Handle(
        GetBoxAssignmentDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(request.RestaurantId, cancellationToken);
        var isAr = _messages.IsArabic();

        // 1. Fetch TripStop or SubscriptionDay
        var stop = await _db.TripStops
            .AsNoTracking()
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s => s.Id == request.BoxId, cancellationToken);

        BoxDetailsSummaryDto boxDto;
        decimal destLat;
        decimal destLng;
        string zone;

        var restaurant = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(r => r.Id == restaurantId)
            .Select(r => new { r.Latitude, r.Longitude })
            .FirstOrDefaultAsync(cancellationToken);

        var restLat = restaurant?.Latitude ?? 29.3375m;
        var restLng = restaurant?.Longitude ?? 48.0280m;

        if (stop is not null)
        {
            var priority = DeterminePriority(stop);
            var priorityText = priority switch
            {
                "Urgent" => _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeUrgent, isAr ? "عاجل" : "Urgent"),
                "HighPriority" => _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeHighPriority, isAr ? "عالية" : "High"),
                "New" => _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNew, isAr ? "جديد" : "New"),
                _ => _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNormal, isAr ? "عادي" : "Normal")
            };

            var statusBadge = stop.Status switch
            {
                TripStopStatus.Pending => _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNew, isAr ? "جديد" : "New"),
                TripStopStatus.PickedUp => isAr ? "قيد التوصيل" : "In Delivery",
                TripStopStatus.Failed => isAr ? "مشاكل" : "Issues",
                _ => isAr ? "تم الإسناد" : "Assigned"
            };

            var mealCount = stop.BoxCount * 2;
            var mealsLabel = _messages.GetMessage(MessageCodes.DispatcherOrders.MealsCountFormat, isAr ? $"{mealCount} وجبات" : $"{mealCount} meals", mealCount);

            destLat = stop.Latitude ?? 29.3375m;
            destLng = stop.Longitude ?? 48.0280m;
            zone = stop.ZoneNameAr ?? "السالمية";

            var distanceKm = Math.Round(MealMate.Application.Common.Geography.KuwaitGeoBounds.HaversineKm(restLat, restLng, destLat, destLng), 1);
            var distanceText = isAr ? $"{distanceKm:F1} كم" : $"{distanceKm:F1} km";

            boxDto = new BoxDetailsSummaryDto(
                BoxId: stop.Id,
                BoxCode: stop.OrderCode ?? $"#BX-{Math.Abs(stop.Id.GetHashCode()) % 9000 + 1000}",
                ZoneName: isAr ? $"منطقة {stop.ZoneNameAr}" : $"{stop.ZoneNameEn} Zone",
                DeliveryTimeWindow: string.IsNullOrWhiteSpace(stop.SlotLabel) ? (isAr ? "09:30-10:30 ص" : "09:30-10:30 AM") : stop.SlotLabel,
                MealCount: mealCount,
                MealCountLabel: mealsLabel,
                DistanceKm: distanceKm,
                DistanceText: distanceText,
                Priority: priority,
                PriorityBadgeText: priorityText,
                Status: stop.Status.ToString(),
                StatusBadgeText: statusBadge);
        }
        else
        {
            var day = await _db.SubscriptionDays
                .AsNoTracking()
                .Include(d => d.MealSelections)
                .FirstOrDefaultAsync(d => d.Id == request.BoxId, cancellationToken);

            if (day is null)
            {
                return Error.NotFound(MessageCodes.Common.NotFound, isAr ? "لم يتم العثور على البوكس المطلوب." : "Requested box not found.");
            }

            var dailyOrder = await _db.RestaurantDailyOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.SubscriptionDayId == day.Id, cancellationToken);

            MealMate.Domain.IdentityAccess.CustomerAddress? customerAddr = null;
            if (day.DeliveryAddressId.HasValue)
            {
                customerAddr = await _db.CustomerAddresses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == day.DeliveryAddressId.Value, cancellationToken);
            }

            destLat = customerAddr?.Latitude ?? 29.3375m;
            destLng = customerAddr?.Longitude ?? 48.0280m;
            zone = !string.IsNullOrWhiteSpace(day.DeliveryAddressLine) ? day.DeliveryAddressLine : "السالمية";

            var mealCount = day.MealSelections.Count > 0
                ? day.MealSelections.Count
                : (dailyOrder != null && dailyOrder.BoxCount > 0 ? dailyOrder.BoxCount * 2 : 2);

            var mealsLabel = _messages.GetMessage(MessageCodes.DispatcherOrders.MealsCountFormat, isAr ? $"{mealCount} وجبات" : $"{mealCount} meals", mealCount);
            var distanceKm = Math.Round(MealMate.Application.Common.Geography.KuwaitGeoBounds.HaversineKm(restLat, restLng, destLat, destLng), 1);
            var distanceText = isAr ? $"{distanceKm:F1} كم" : $"{distanceKm:F1} km";

            var isReady = dailyOrder?.Status == OrderStatus.ReadyForPickup;
            var priority = (isReady || day.Status == SubscriptionDayStatus.Preparing)
                ? "Urgent"
                : (day.CreatedAtUtc >= DateTime.UtcNow.AddMinutes(-30) ? "New" : "Normal");

            var priorityText = priority switch
            {
                "Urgent" => _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeUrgent, isAr ? "عاجل" : "Urgent"),
                "HighPriority" => _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeHighPriority, isAr ? "عالية" : "High"),
                "New" => _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNew, isAr ? "جديد" : "New"),
                _ => _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNormal, isAr ? "عادي" : "Normal")
            };

            var statusBadge = _messages.GetMessage(MessageCodes.DispatcherOrders.BadgeNew, isAr ? "جديد" : "New");

            boxDto = new BoxDetailsSummaryDto(
                BoxId: day.Id,
                BoxCode: $"#BX-{Math.Abs(day.Id.GetHashCode()) % 9000 + 1000}",
                ZoneName: isAr ? (zone.StartsWith("منطقة") ? zone : $"منطقة {zone}") : $"{zone} Zone",
                DeliveryTimeWindow: isAr ? "09:30-10:30 ص" : "09:30-10:30 AM",
                MealCount: mealCount,
                MealCountLabel: mealsLabel,
                DistanceKm: distanceKm,
                DistanceText: distanceText,
                Priority: priority,
                PriorityBadgeText: priorityText,
                Status: "Pending",
                StatusBadgeText: statusBadge);
        }

        // 2. Fetch candidate drivers
        var allCandidates = await _recommendationEngine.GetCandidateDriversForBoxAsync(
            restaurantId, zone, destLat, destLng, cancellationToken);

        // 3. Isolate best suggestion vs list
        var bestSuggestion = allCandidates.FirstOrDefault(c => c.IsRecommended) ?? allCandidates.FirstOrDefault();

        var candidateList = allCandidates
            .Where(c => bestSuggestion == null || c.DriverId != bestSuggestion.DriverId)
            .Select((c, index) => c with { Rank = index + 1 })
            .ToList();

        return Result.Success(new BoxAssignmentDetailsDto(
            Box: boxDto,
            BestSuggestion: bestSuggestion,
            Candidates: candidateList));
    }

    private static string DeterminePriority(TripStop stop)
    {
        if (stop.SequenceNumber == 1) return "Urgent";
        if (stop.BoxCount >= 3) return "HighPriority";
        if (stop.CreatedAtUtc >= DateTime.UtcNow.AddMinutes(-30)) return "New";
        return "Normal";
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
