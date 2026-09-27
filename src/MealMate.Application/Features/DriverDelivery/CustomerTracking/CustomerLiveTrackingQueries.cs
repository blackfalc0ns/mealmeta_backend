using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

public sealed record GetCustomerLiveOrderTrackingQuery(
    Guid OrderId) : IQuery<CustomerOrderTrackingResponseDto>;

public sealed record CustomerOrderTrackingResponseDto(
    Guid OrderId,
    string OrderCode,
    string Status,
    string StatusText,
    string StatusColor,
    CustomerTrackingDriverDto? Driver,
    CustomerTrackingLocationDto Location,
    CustomerTrackingAddressDto Address,
    CustomerTrackingTimelineDto Timeline);

public sealed record CustomerTrackingDriverDto(
    string DriverName,
    string MaskedPhone,
    string? AvatarUrl,
    string VehicleType,
    string PlateNumber,
    double Rating);

public sealed record CustomerTrackingLocationDto(
    double? DriverLatitude,
    double? DriverLongitude,
    int EstimatedArrivalMinutes,
    int DistanceMeters,
    bool IsLive);

public sealed record CustomerTrackingAddressDto(
    string ZoneName,
    string AddressShort,
    double? DestinationLatitude,
    double? DestinationLongitude);

public sealed record CustomerTrackingTimelineDto(
    DateTime? PickedUpAtUtc,
    DateTime? ArrivedAtCustomerAtUtc,
    DateTime? DeliveredAtUtc,
    string? ProofPhotoStorageKey);

public sealed class CustomerLiveTrackingQueryHandler
    : IQueryHandler<GetCustomerLiveOrderTrackingQuery, CustomerOrderTrackingResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;

    public CustomerLiveTrackingQueryHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _messages = messages;
    }

    public async Task<Result<CustomerOrderTrackingResponseDto>> Handle(
        GetCustomerLiveOrderTrackingQuery request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        // Find stop by Id, DailyOrderId, or SubscriptionDayId
        var stop = await _db.TripStops
            .Include(s => s.Trip)
            .FirstOrDefaultAsync(s =>
                s.Id == request.OrderId ||
                s.DailyOrderId == request.OrderId ||
                s.SubscriptionDayId == request.OrderId,
                cancellationToken);

        if (stop is null)
        {
            return Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على بيانات تتبع هذا الطلب.");
        }

        // Find active driver assigned to this trip
        var assignment = await _db.DriverAssignments
            .Where(da => da.TripId == stop.TripId && da.Status == "Active")
            .FirstOrDefaultAsync(cancellationToken);

        MealMate.Domain.IdentityAccess.DriverProfile? driverProfile = null;
        if (assignment is not null)
        {
            driverProfile = await _db.DriverProfiles
                .FirstOrDefaultAsync(dp => dp.Id == assignment.DriverProfileId, cancellationToken);
        }

        // Resolve driver active session coordinates
        CustomerTrackingLocationDto location;
        if (driverProfile is not null)
        {
            var activeSession = await _db.DriverWorkSessions
                .FirstOrDefaultAsync(s => s.DriverProfileId == driverProfile.Id && s.Status == "Active", cancellationToken);

            var lat = activeSession?.StartLatitude.HasValue == true ? (double?)activeSession.StartLatitude.Value : null;
            var lon = activeSession?.StartLongitude.HasValue == true ? (double?)activeSession.StartLongitude.Value : null;

            int distanceMeters = 0;
            int estimatedArrivalMinutes = 0;
            if (lat.HasValue && lon.HasValue && stop.Latitude.HasValue && stop.Longitude.HasValue)
            {
                var dLat = ((double)stop.Latitude.Value - lat.Value) * Math.PI / 180.0;
                var dLon = ((double)stop.Longitude.Value - lon.Value) * Math.PI / 180.0;
                var lat1 = lat.Value * Math.PI / 180.0;
                var lat2 = (double)stop.Latitude.Value * Math.PI / 180.0;

                var a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                        Math.Cos(lat1) * Math.Cos(lat2) *
                        Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
                var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
                var distKm = 6371.0 * c;
                distanceMeters = (int)Math.Round(distKm * 1000.0);
                estimatedArrivalMinutes = Math.Max(1, (int)Math.Ceiling(distKm * 2.0 + 3.0));
            }

            location = new CustomerTrackingLocationDto(
                DriverLatitude: lat,
                DriverLongitude: lon,
                EstimatedArrivalMinutes: estimatedArrivalMinutes,
                DistanceMeters: distanceMeters,
                IsLive: stop.Status == TripStopStatus.PickedUp && lat.HasValue
            );
        }
        else
        {
            location = new CustomerTrackingLocationDto(
                DriverLatitude: null,
                DriverLongitude: null,
                EstimatedArrivalMinutes: 0,
                DistanceMeters: 0,
                IsLive: false
            );
        }

        CustomerTrackingDriverDto? driverDto = null;
        if (driverProfile is not null)
        {
            var name = isAr ? driverProfile.FullNameAr : driverProfile.FullNameEn;
            if (string.IsNullOrWhiteSpace(name)) name = driverProfile.FullNameAr ?? driverProfile.FullNameEn ?? string.Empty;

            var driverPhone = await _db.DriverRegistrations
                .AsNoTracking()
                .Where(r => r.ResultingDriverProfileId == driverProfile.Id || (r.SubmittedByUserId.HasValue && r.SubmittedByUserId.Value == driverProfile.UserId))
                .Select(r => r.Phone)
                .FirstOrDefaultAsync(cancellationToken)
                ?? string.Empty;

            var maskedPhone = !string.IsNullOrWhiteSpace(driverPhone) && driverPhone.Length > 4
                ? $"{driverPhone[..3]}***{driverPhone[^2..]}"
                : driverPhone;

            driverDto = new CustomerTrackingDriverDto(
                DriverName: name,
                MaskedPhone: maskedPhone,
                AvatarUrl: driverProfile.ProfileImageStorageKey,
                VehicleType: driverProfile.VehicleType ?? string.Empty,
                PlateNumber: driverProfile.VehiclePlate ?? string.Empty,
                Rating: 0.0
            );
        }

        var statusStr = stop.Status switch
        {
            TripStopStatus.Pending => "Scheduled",
            TripStopStatus.PickedUp => "OutForDelivery",
            TripStopStatus.Delivered => "Delivered",
            TripStopStatus.Failed => "Failed",
            TripStopStatus.ReturnedToKitchen => "ReturnedToKitchen",
            _ => "Pending"
        };

        var statusText = stop.Status switch
        {
            TripStopStatus.Pending => isAr ? "قيد التجهيز بالمطبخ" : "Preparing at Kitchen",
            TripStopStatus.PickedUp => isAr ? "في الطريق إليك 🚗" : "Out for Delivery",
            TripStopStatus.Delivered => isAr ? "تم التسليم بنجاح ✅" : "Delivered",
            TripStopStatus.Failed => isAr ? "تعذر التسليم" : "Delivery Failed",
            TripStopStatus.ReturnedToKitchen => isAr ? "أعيد للمطعم" : "Returned to Restaurant",
            _ => isAr ? "قيد المعالجة" : "Processing"
        };

        var statusColor = stop.Status switch
        {
            TripStopStatus.Delivered => "#10B981",
            TripStopStatus.PickedUp => "#7C3AED",
            TripStopStatus.Failed => "#EF4444",
            _ => "#F59E0B"
        };

        var addressDto = new CustomerTrackingAddressDto(
            ZoneName: isAr ? (stop.ZoneNameAr ?? string.Empty) : (stop.ZoneNameEn ?? string.Empty),
            AddressShort: isAr ? (stop.ZoneNameAr ?? string.Empty) : (stop.ZoneNameEn ?? string.Empty),
            DestinationLatitude: (double?)stop.Latitude,
            DestinationLongitude: (double?)stop.Longitude
        );

        // Fetch proof photo from DeliveryConfirmationScans if delivered
        var proofScan = await _db.DeliveryConfirmationScans
            .Where(dcs => dcs.OrderId == stop.DailyOrderId || dcs.SubscriptionDayId == stop.SubscriptionDayId)
            .OrderByDescending(dcs => dcs.ScannedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var timelineDto = new CustomerTrackingTimelineDto(
            PickedUpAtUtc: stop.Status >= TripStopStatus.PickedUp ? stop.Trip?.StartedAtUtc : null,
            ArrivedAtCustomerAtUtc: stop.DeliveredAtUtc,
            DeliveredAtUtc: stop.DeliveredAtUtc,
            ProofPhotoStorageKey: proofScan?.PhotoPath
        );

        return Result.Success(new CustomerOrderTrackingResponseDto(
            OrderId: stop.Id,
            OrderCode: stop.OrderCode ?? string.Empty,
            Status: statusStr,
            StatusText: statusText,
            StatusColor: statusColor,
            Driver: driverDto,
            Location: location,
            Address: addressDto,
            Timeline: timelineDto
        ));
    }
}
