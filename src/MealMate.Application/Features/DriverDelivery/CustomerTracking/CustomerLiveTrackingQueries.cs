using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyManager;
using MealMate.Domain.DriverDelivery;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
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
    double? Rating);

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

/// <summary>
/// Legacy live-tracking route (GET /api/v1/customer/orders/{orderId}/live-tracking).
/// The identifier is accepted as a TripStop id, DailyOrderId or SubscriptionDayId alias.
/// Authentication alone is NOT authorization: the stop is resolved to its subscription and
/// the existing family calendar read gate decides access before any driver/address/proof
/// data is read. Timestamps come from real events only (current non-revoked handover,
/// TripStop arrival/delivery), and the shared read service guarantees the same delivery
/// status the widget and calendar report.
/// </summary>
public sealed class CustomerLiveTrackingQueryHandler
    : IQueryHandler<GetCustomerLiveOrderTrackingQuery, CustomerOrderTrackingResponseDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IFamilyCalendarAccessGate _accessGate;
    private readonly ICustomerDeliveryReadService _readService;
    private readonly ICustomerDeliveryLocationBuilder? _locationBuilder;

    public CustomerLiveTrackingQueryHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages,
        IFamilyCalendarAccessGate? accessGate = null,
        ICustomerDeliveryReadService? readService = null,
        ICustomerDeliveryLocationBuilder? locationBuilder = null)
    {
        _db = db;
        _messages = messages;
        _accessGate = accessGate ?? new FamilyCalendarAccessGate(db, UnauthenticatedUser.Instance);
        _readService = readService ?? new CustomerDeliveryReadService(db);
        _locationBuilder = locationBuilder;
    }

    public async Task<Result<CustomerOrderTrackingResponseDto>> Handle(
        GetCustomerLiveOrderTrackingQuery request,
        CancellationToken cancellationToken)
    {
        var isAr = _messages.IsArabic();

        // Find candidate stops by Id, DailyOrderId, or SubscriptionDayId alias.
        // Nullable identifiers are guarded so a null column can never equal the
        // requested Guid through the null-lifted == operator.
        var candidates = await _db.TripStops.AsNoTracking()
            .Where(s => s.Id == request.OrderId
                        || (s.DailyOrderId != null && s.DailyOrderId == request.OrderId)
                        || (s.SubscriptionDayId != null && s.SubscriptionDayId == request.OrderId))
            .Select(s => new { s.Id, s.TripId, s.SubscriptionDayId, s.DailyOrderId })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return NotFoundOrder();
        }

        // Resolve ownership of every candidate day BEFORE reading sensitive data, then
        // keep only the days this caller may read through the existing family gate.
        var dayIds = candidates
            .Where(c => c.SubscriptionDayId.HasValue)
            .Select(c => c.SubscriptionDayId!.Value)
            .Distinct()
            .ToList();

        var daySubscriptions = await _db.SubscriptionDays.AsNoTracking()
            .Where(d => dayIds.Contains(d.Id))
            .Join(_db.Subscriptions.AsNoTracking(),
                d => d.SubscriptionId,
                s => s.Id,
                (d, s) => new { d.Id, Subscription = s })
            .ToListAsync(cancellationToken);

        var accessibleDayIds = new List<Guid>(dayIds.Count);
        foreach (var row in daySubscriptions)
        {
            var auth = await _accessGate.AuthorizeAsync(row.Subscription, requireWrite: false, cancellationToken);
            if (auth.IsSuccess)
            {
                accessibleDayIds.Add(row.Id);
            }
        }

        var accessibleCandidates = candidates
            .Where(c => c.SubscriptionDayId.HasValue && accessibleDayIds.Contains(c.SubscriptionDayId.Value))
            .ToList();

        if (accessibleCandidates.Count == 0)
        {
            // Same not-found as a missing order: the existence of another customer's box
            // is never disclosed, and no driver/address/proof data was read for it.
            return NotFoundOrder();
        }

        // The shared projection selects the current attempt for the identified box;
        // one identifier resolving to several accessible boxes is refused, never guessed.
        var projections = await _readService.GetForDaysAsync(accessibleDayIds, cancellationToken);

        CustomerDeliveryDayProjection? projection = null;
        foreach (var candidate in accessibleCandidates)
        {
            if (!projections.TryGetValue(candidate.SubscriptionDayId!.Value, out var candidateProjection))
            {
                continue;
            }

            var projectionStopId = candidateProjection.Attempt?.TripStopId;
            if (projectionStopId is not null && projectionStopId != candidate.Id)
            {
                // A specific stop id was asked for that is not the current attempt of its
                // day (an old failed stop after a retry): the day projection still answers.
                continue;
            }

            if (projection is not null && projection.SubscriptionDayId != candidateProjection.SubscriptionDayId)
            {
                return Error.NotFound(MessageCodes.Tracking.Unavailable, _messages.GetMessage(
                    MessageCodes.Tracking.Unavailable,
                    "تتبع هذا الطلب غير واضح؛ يطابق المعرف أكثر من صندوق."));
            }

            projection = candidateProjection;
        }

        if (projection is null || projection.Snapshot.DeliveryStatus == CustomerDeliveryStatusCodes.TrackingUnavailable)
        {
            return Error.NotFound(MessageCodes.Tracking.Unavailable, _messages.GetMessage(
                MessageCodes.Tracking.Unavailable,
                "تتبع هذا الطلب غير متاح حالياً."));
        }

        var snapshot = projection.Snapshot;
        var attempt = projection.Attempt;

        var stop = attempt is null
            ? null
            : await _db.TripStops.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == attempt.TripStopId, cancellationToken);

        // Driver identity: only the currently assigned driver through an active
        // assignment — never a revoked handover or an inactive assignment.
        DriverProfile? driverProfile = null;
        if (projection.DriverProfileId.HasValue && snapshot.IsTrackable)
        {
            driverProfile = await _db.DriverProfiles.AsNoTracking()
                .FirstOrDefaultAsync(dp => dp.Id == projection.DriverProfileId.Value, cancellationToken);
        }

        var location = await BuildLocationAsync(projection, driverProfile, stop, snapshot, cancellationToken);

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
                AvatarUrl: AvatarUrlOf(driverProfile),
                VehicleType: driverProfile.VehicleType ?? string.Empty,
                PlateNumber: driverProfile.VehiclePlate ?? string.Empty,
                Rating: null); // no verified driver-rating source exists yet
        }

        var statusStr = snapshot.DeliveryStatus switch
        {
            CustomerDeliveryStatusCodes.OutForDelivery => "OutForDelivery",
            CustomerDeliveryStatusCodes.Delivered => "Delivered",
            CustomerDeliveryStatusCodes.Failed => "Failed",
            CustomerDeliveryStatusCodes.ReturnedToKitchen => "ReturnedToKitchen",
            _ => "Scheduled"
        };

        var statusText = snapshot.DeliveryStatus switch
        {
            CustomerDeliveryStatusCodes.OutForDelivery
                or CustomerDeliveryStatusCodes.Delivered
                or CustomerDeliveryStatusCodes.Failed
                or CustomerDeliveryStatusCodes.ReturnedToKitchen
                or CustomerDeliveryStatusCodes.ReadyForPickup => isAr ? snapshot.StatusTextAr : snapshot.StatusTextEn,
            _ => isAr ? "قيد التجهيز بالمطبخ" : "Preparing at Kitchen"
        };

        var statusColor = snapshot.DeliveryStatus switch
        {
            CustomerDeliveryStatusCodes.Delivered => "#10B981",
            CustomerDeliveryStatusCodes.OutForDelivery => "#7C3AED",
            CustomerDeliveryStatusCodes.Failed or CustomerDeliveryStatusCodes.ReturnedToKitchen => "#EF4444",
            _ => "#F59E0B"
        };

        var addressDto = new CustomerTrackingAddressDto(
            ZoneName: isAr ? (stop?.ZoneNameAr ?? string.Empty) : (stop?.ZoneNameEn ?? string.Empty),
            AddressShort: stop?.FullAddress
                ?? (isAr ? (stop?.ZoneNameAr ?? string.Empty) : (stop?.ZoneNameEn ?? string.Empty)),
            DestinationLatitude: (double?)stop?.Latitude,
            DestinationLongitude: (double?)stop?.Longitude);

        // Proof photo: guarded identifiers only (no null-OR), matched to the current
        // attempt's driver, surfaced through the authorized content route — the raw
        // private storage key is never exposed.
        string? proofUrl = null;
        if (projection.DailyOrderId.HasValue || projection.SubscriptionDayId != Guid.Empty)
        {
            var proofScan = await _db.DeliveryConfirmationScans.AsNoTracking()
                .Where(dcs =>
                    (projection.DailyOrderId.HasValue && dcs.OrderId != null && dcs.OrderId == projection.DailyOrderId.Value)
                    || (dcs.SubscriptionDayId != null && dcs.SubscriptionDayId == projection.SubscriptionDayId))
                .Where(dcs => !projection.DriverProfileId.HasValue || dcs.DriverProfileId == projection.DriverProfileId.Value)
                .OrderByDescending(dcs => dcs.ScannedAtUtc)
                .Select(dcs => new { dcs.PhotoPath })
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(proofScan?.PhotoPath))
            {
                proofUrl = $"/api/v1/uploads/content?key={Uri.EscapeDataString(proofScan.PhotoPath)}";
            }
        }

        var timelineDto = new CustomerTrackingTimelineDto(
            PickedUpAtUtc: snapshot.PickedUpAtUtc,
            ArrivedAtCustomerAtUtc: snapshot.ArrivedAtUtc,
            DeliveredAtUtc: snapshot.DeliveredAtUtc,
            ProofPhotoStorageKey: proofUrl);

        return Result.Success(new CustomerOrderTrackingResponseDto(
            OrderId: stop?.Id ?? attempt?.TripStopId ?? request.OrderId,
            OrderCode: projection.OrderCode ?? string.Empty,
            Status: statusStr,
            StatusText: statusText,
            StatusColor: statusColor,
            Driver: driverDto,
            Location: location,
            Address: addressDto,
            Timeline: timelineDto
        ));
    }

    /// <summary>Real location + ETA via the shared builder when it is available: the
    /// freshest valid fix of the assigned driver for this trip, straight-line distance,
    /// and an ETA only from real routing or confirmed arrival — the shift-start session
    /// coordinate is never reported as a live location.</summary>
    private async Task<CustomerTrackingLocationDto> BuildLocationAsync(
        CustomerDeliveryDayProjection projection,
        DriverProfile? driverProfile,
        TripStop? stop,
        CustomerDeliveryStatusSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (_locationBuilder is null || driverProfile is null || stop is null || projection.Attempt is null)
        {
            return new CustomerTrackingLocationDto(null, null, 0, 0, IsLive: false);
        }

        var result = await _locationBuilder.BuildAsync(new CustomerDeliveryLocationRequest(
            driverProfile.Id,
            projection.Attempt.TripId,
            stop.Id,
            IsArrived: projection.Attempt.ArrivedAtUtc.HasValue && projection.Attempt.Status == TripStopStatus.PickedUp,
            IsTerminal: snapshot.IsTerminal,
            projection.HasEarlierUndeliveredStops,
            stop.Latitude,
            stop.Longitude), cancellationToken);

        int? distanceMeters = null;
        if (result.Latitude.HasValue && result.Longitude.HasValue && stop.Latitude.HasValue && stop.Longitude.HasValue)
        {
            distanceMeters = (int)Math.Round(MealMate.Application.Common.Geography.KuwaitGeoBounds.HaversineKm(
                (decimal)result.Latitude.Value, (decimal)result.Longitude.Value,
                stop.Latitude.Value, stop.Longitude.Value) * 1000.0);
        }

        return new CustomerTrackingLocationDto(
            DriverLatitude: result.Latitude,
            DriverLongitude: result.Longitude,
            EstimatedArrivalMinutes: result.EtaMinutes ?? 0,
            DistanceMeters: distanceMeters ?? 0,
            IsLive: result.IsLive && !snapshot.IsTerminal);
    }

    private static string? AvatarUrlOf(DriverProfile profile)
        => profile.ProfileImageStatus == VerificationStatus.Approved
               && !string.IsNullOrWhiteSpace(profile.ProfileImageStorageKey)
            ? $"/api/v1/uploads/content?key={Uri.EscapeDataString(profile.ProfileImageStorageKey)}"
            : null;

    private static Error NotFoundOrder()
        => Error.NotFound(MessageCodes.Orders.NotFound, "لم يتم العثور على بيانات تتبع هذا الطلب.");

    /// <summary>Gate fallback for callers that construct the handler without a gate:
    /// it authorizes nobody, so ownership is always fail-closed.</summary>
    private sealed class UnauthenticatedUser : ICurrentUser
    {
        public static readonly UnauthenticatedUser Instance = new();
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public UserType? UserType => null;
        public Guid? RestaurantId => null;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
        public IReadOnlyCollection<Guid> AccessScopeIds => [];
        public IReadOnlyCollection<Guid> TenantIds => [];
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
    }
}
