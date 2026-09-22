using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.Dispatcher;

public sealed record GetDispatcherDriverLocationsQuery(Guid? RestaurantId = null) : IQuery<IReadOnlyList<LiveDriverLocationDto>>;

public sealed class GetDispatcherDriverLocationsQueryHandler : IQueryHandler<GetDispatcherDriverLocationsQuery, IReadOnlyList<LiveDriverLocationDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILocalizedMessageProvider _messages;

    public GetDispatcherDriverLocationsQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _messages = messages;
    }

    public async Task<Result<IReadOnlyList<LiveDriverLocationDto>>> Handle(
        GetDispatcherDriverLocationsQuery request,
        CancellationToken cancellationToken)
    {
        var restaurantId = await ResolveRestaurantIdAsync(request.RestaurantId, cancellationToken);

        var drivers = await _db.DriverProfiles
            .AsNoTracking()
            .Where(d => d.RestaurantId == restaurantId || d.RestaurantId == null)
            .ToListAsync(cancellationToken);

        var labelEnRouteAr = _messages.GetMessage(MessageCodes.Dispatcher.StatusEnRouteToCustomer, "في الطريق للعميل");
        var labelInDeliveryAr = _messages.GetMessage(MessageCodes.Dispatcher.StatusInDelivery, "في التوصيل");
        var labelOnBreakAr = _messages.GetMessage(MessageCodes.Dispatcher.StatusOnBreak, "في استراحة");
        var labelAvailableAr = _messages.GetMessage(MessageCodes.Dispatcher.StatusAvailable, "متاح");

        if (drivers.Count == 0)
        {
            return Result.Success<IReadOnlyList<LiveDriverLocationDto>>(Array.Empty<LiveDriverLocationDto>());
        }

        var driverIds = drivers.Select(d => d.Id).ToList();

        // Get latest tracking points for each driver
        var latestPoints = await _db.DeliveryTrackingPoints
            .AsNoTracking()
            .Where(p => driverIds.Contains(p.DriverProfileId))
            .GroupBy(p => p.DriverProfileId)
            .Select(g => g.OrderByDescending(p => p.RecordedAtUtc).FirstOrDefault()!)
            .ToDictionaryAsync(p => p.DriverProfileId, cancellationToken);

        // Get active trips and assignments
        var activeAssignments = await _db.DriverAssignments
            .AsNoTracking()
            .Include(da => da.Trip)
            .ThenInclude(t => t.Stops)
            .Where(da => driverIds.Contains(da.DriverProfileId)
                         && (da.Trip.Status == TripStatus.InProgress || da.Trip.Status == TripStatus.Assigned))
            .ToListAsync(cancellationToken);

        var assignmentByDriver = activeAssignments
            .GroupBy(da => da.DriverProfileId)
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<LiveDriverLocationDto>();

        foreach (var driver in drivers)
        {
            latestPoints.TryGetValue(driver.Id, out var point);
            assignmentByDriver.TryGetValue(driver.Id, out var assignment);

            var activeTrip = assignment?.Trip;
            var currentStop = activeTrip?.Stops
                .OrderBy(s => s.SequenceNumber)
                .FirstOrDefault(s => s.Status == TripStopStatus.PickedUp || s.Status == TripStopStatus.Pending);

            string status;
            string statusAr;
            string statusEn;
            string? activeOrderId = currentStop?.OrderCode;
            string? customerAddress = currentStop != null
                ? $"{currentStop.ZoneNameAr} - {currentStop.CustomerName ?? currentStop.CustomerMaskedId}"
                : null;

            if (currentStop?.Status == TripStopStatus.PickedUp)
            {
                status = "EnRouteToCustomer";
                statusAr = labelEnRouteAr;
                statusEn = "En Route to Customer";
            }
            else if (activeTrip?.Status == TripStatus.InProgress)
            {
                status = "InDelivery";
                statusAr = labelInDeliveryAr;
                statusEn = "In Delivery";
            }
            else if (!driver.IsAvailable)
            {
                status = "OnBreak";
                statusAr = labelOnBreakAr;
                statusEn = "On Break";
            }
            else
            {
                status = "Available";
                statusAr = labelAvailableAr;
                statusEn = "Available";
            }

            var lat = point?.Latitude ?? 29.3759m;
            var lng = point?.Longitude ?? 47.9774m;
            var updatedAt = point?.RecordedAtUtc ?? DateTime.UtcNow;
            var name = _messages.Localize(driver.FullNameAr, driver.FullNameEn);
            if (string.IsNullOrWhiteSpace(name)) name = _messages.IsArabic() ? "سائق" : "Driver";
            var statusLabel = _messages.Localize(statusAr, statusEn);

            result.Add(new LiveDriverLocationDto(
                DriverId: driver.Id,
                FullName: name,
                Phone: null,
                PlateNumber: driver.VehiclePlate ?? "DX-123456",
                AvatarUrl: $"https://cdn.mealmate.app/avatars/{driver.Id.ToString()[..8]}.jpg",
                Status: status,
                StatusLabel: statusLabel,
                StatusLabelAr: statusAr,
                StatusLabelEn: statusEn,
                Latitude: lat,
                Longitude: lng,
                Heading: 120.0,
                SpeedKmh: status == "EnRouteToCustomer" ? 38.0 : status == "InDelivery" ? 15.0 : 0.0,
                ActiveOrderId: activeOrderId,
                CustomerAddress: customerAddress,
                UpdatedAtUtc: updatedAt));
        }

        return Result.Success<IReadOnlyList<LiveDriverLocationDto>>(result);
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
