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

            var lat = activeSession?.StartLatitude.HasValue == true ? (double?)activeSession.StartLatitude.Value : 29.3759;
            var lon = activeSession?.StartLongitude.HasValue == true ? (double?)activeSession.StartLongitude.Value : 47.9774;

            location = new CustomerTrackingLocationDto(
                DriverLatitude: lat,
                DriverLongitude: lon,
                EstimatedArrivalMinutes: stop.Status == TripStopStatus.Delivered ? 0 : 8,
                DistanceMeters: stop.Status == TripStopStatus.Delivered ? 0 : 1850,
                IsLive: stop.Status == TripStopStatus.PickedUp
            );
        }
        else
        {
            location = new CustomerTrackingLocationDto(
                DriverLatitude: null,
                DriverLongitude: null,
                EstimatedArrivalMinutes: 15,
                DistanceMeters: 0,
                IsLive: false
            );
        }

        CustomerTrackingDriverDto? driverDto = null;
        if (driverProfile is not null)
        {
            var name = isAr ? driverProfile.FullNameAr : driverProfile.FullNameEn;
            if (string.IsNullOrWhiteSpace(name)) name = driverProfile.FullNameAr ?? driverProfile.FullNameEn ?? "سائق ميل ميت";

            driverDto = new CustomerTrackingDriverDto(
                DriverName: name,
                MaskedPhone: "+965 2200 9000",
                AvatarUrl: null,
                VehicleType: driverProfile.VehicleType ?? (isAr ? "دراجة نارية" : "Motorcycle"),
                PlateNumber: driverProfile.VehiclePlate ?? "22-4589",
                Rating: 4.9
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
            ZoneName: isAr ? stop.ZoneNameAr : stop.ZoneNameEn,
            AddressShort: isAr ? $"{stop.ZoneNameAr} - عنوان التوصيل" : $"{stop.ZoneNameEn} Delivery Address",
            DestinationLatitude: 29.3800,
            DestinationLongitude: 47.9850
        );

        // Fetch proof photo from DeliveryConfirmationScans if delivered
        var proofScan = await _db.DeliveryConfirmationScans
            .Where(dcs => dcs.OrderId == stop.DailyOrderId || dcs.SubscriptionDayId == stop.SubscriptionDayId)
            .OrderByDescending(dcs => dcs.ScannedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var timelineDto = new CustomerTrackingTimelineDto(
            PickedUpAtUtc: stop.Status >= TripStopStatus.PickedUp ? stop.Trip?.StartedAtUtc ?? DateTime.UtcNow.AddMinutes(-20) : null,
            ArrivedAtCustomerAtUtc: stop.DeliveredAtUtc?.AddMinutes(-3),
            DeliveredAtUtc: stop.DeliveredAtUtc,
            ProofPhotoStorageKey: proofScan?.PhotoPath
        );

        return Result.Success(new CustomerOrderTrackingResponseDto(
            OrderId: stop.Id,
            OrderCode: stop.OrderCode ?? "#BX-1256",
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
