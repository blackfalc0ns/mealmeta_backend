using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.Catalog;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

/// <summary>
/// Shared presentation for the customer delivery widget and the per-day details page.
/// Reads one already-authorized set of subscription days and returns the same item
/// contract for both callers; it never authorizes and never writes. Driver, vehicle,
/// location and rating data are batched (no per-card queries), and ratings use the
/// same aggregation policy as the driver profile: rounded average, null when none.
/// </summary>
public interface ICustomerDeliveryPresentationBuilder
{
    Task<IReadOnlyList<CustomerDeliveryWidgetItemDto>> BuildAsync(
        IReadOnlyCollection<Guid> authorizedDayIds,
        CancellationToken cancellationToken);
}

public sealed class CustomerDeliveryPresentationBuilder : ICustomerDeliveryPresentationBuilder
{
    private readonly IAppDbContext _db;
    private readonly ICustomerDeliveryReadService _readService;
    private readonly ICustomerDeliveryLocationBuilder _locationBuilder;
    private readonly ILocalizedMessageProvider _messages;

    public CustomerDeliveryPresentationBuilder(
        IAppDbContext db,
        ICustomerDeliveryReadService readService,
        ICustomerDeliveryLocationBuilder locationBuilder,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _readService = readService;
        _locationBuilder = locationBuilder;
        _messages = messages;
    }

    public async Task<IReadOnlyList<CustomerDeliveryWidgetItemDto>> BuildAsync(
        IReadOnlyCollection<Guid> authorizedDayIds,
        CancellationToken cancellationToken)
    {
        if (authorizedDayIds.Count == 0)
        {
            return [];
        }

        var projections = await _readService.GetForDaysAsync(authorizedDayIds, cancellationToken);
        if (projections.Count == 0)
        {
            return [];
        }

        var stopIds = projections.Values
            .Where(p => p.Attempt is not null)
            .Select(p => p.Attempt!.TripStopId)
            .Distinct()
            .ToList();
        var stopDetails = await _db.TripStops.AsNoTracking()
            .Where(s => stopIds.Contains(s.Id))
            .Select(s => new
            {
                s.Id,
                s.OrderCode,
                s.FullAddress,
                s.ZoneNameAr,
                s.ZoneNameEn,
                s.Latitude,
                s.Longitude
            })
            .ToListAsync(cancellationToken);
        var stopById = stopDetails.ToDictionary(s => s.Id);

        var driverIds = projections.Values
            .Where(p => p.DriverProfileId.HasValue)
            .Select(p => p.DriverProfileId!.Value)
            .Distinct()
            .ToList();
        var driversById = await LoadDriversAsync(driverIds, cancellationToken);
        var ratingsByDriver = await LoadRatingsAsync(driverIds, cancellationToken);

        var isAr = _messages.IsArabic();

        var items = new List<CustomerDeliveryWidgetItemDto>(projections.Count);
        foreach (var projection in projections.Values
                     .OrderBy(p => StatusOrder(p.Snapshot))
                     .ThenBy(p => p.SubscriptionDayId))
        {
            var snapshot = projection.Snapshot;
            var attempt = projection.Attempt;
            stopById.TryGetValue(attempt?.TripStopId ?? Guid.Empty, out var stop);

            // Ambiguous current attempts expose no driver or route data at all; terminal
            // boxes keep the driver identity (who delivered) but never a live route.
            var showDriverAndVehicle = projection.DriverProfileId.HasValue
                && snapshot.DeliveryStatus != CustomerDeliveryStatusCodes.TrackingUnavailable
                && (snapshot.IsTrackable || snapshot.IsTerminal);

            CustomerDeliveryLocationDto? location = null;
            int? etaMinutes = null;
            string? etaSource = null;
            if (showDriverAndVehicle && !snapshot.IsTerminal && attempt is not null && stop is not null)
            {
                var locationResult = await _locationBuilder.BuildAsync(new CustomerDeliveryLocationRequest(
                    projection.DriverProfileId!.Value,
                    attempt.TripId,
                    attempt.TripStopId,
                    IsArrived: attempt.ArrivedAtUtc.HasValue && attempt.Status == TripStopStatus.PickedUp,
                    IsTerminal: snapshot.IsTerminal,
                    projection.HasEarlierUndeliveredStops,
                    stop.Latitude,
                    stop.Longitude), cancellationToken);

                if (locationResult.Latitude.HasValue && locationResult.Longitude.HasValue)
                {
                    location = new CustomerDeliveryLocationDto(
                        locationResult.Latitude.Value,
                        locationResult.Longitude.Value,
                        locationResult.CapturedAtUtc!.Value,
                        locationResult.ReceivedAtUtc,
                        locationResult.IsLive);
                }

                etaMinutes = locationResult.EtaMinutes;
                etaSource = locationResult.EtaSource;
            }

            driversById.TryGetValue(projection.DriverProfileId ?? Guid.Empty, out var driver);
            double? rating = projection.DriverProfileId.HasValue
                && ratingsByDriver.TryGetValue(projection.DriverProfileId.Value, out var resolvedRating)
                    ? resolvedRating
                    : null;

            items.Add(new CustomerDeliveryWidgetItemDto(
                SubscriptionId: projection.SubscriptionId,
                SubscriptionDayId: projection.SubscriptionDayId,
                OrderId: projection.DailyOrderId,
                TripStopId: attempt?.TripStopId,
                OrderCode: projection.OrderCode,
                DeliveryStatus: snapshot.DeliveryStatus,
                DeliveryStatusTextAr: snapshot.StatusTextAr,
                DeliveryStatusTextEn: snapshot.StatusTextEn,
                DeliveryStatusText: isAr ? snapshot.StatusTextAr : snapshot.StatusTextEn,
                IsTerminal: snapshot.IsTerminal,
                IsTrackable: snapshot.IsTrackable,
                UnavailableReasonCode: snapshot.UnavailableReasonCode,
                ReadyAtUtc: snapshot.ReadyAtUtc,
                PickedUpAtUtc: snapshot.PickedUpAtUtc,
                ArrivedAtUtc: snapshot.ArrivedAtUtc,
                DeliveredAtUtc: snapshot.DeliveredAtUtc,
                Driver: showDriverAndVehicle ? driver?.ToDto(isAr, rating) : null,
                Vehicle: showDriverAndVehicle ? driver?.ToVehicleDto(isAr) : null,
                Location: location,
                EtaMinutes: etaMinutes,
                EtaSource: etaSource,
                DestinationAddress: stop is null || snapshot.DeliveryStatus == CustomerDeliveryStatusCodes.TrackingUnavailable
                    ? null
                    : new CustomerDeliveryDestinationDto(
                    stop.FullAddress,
                    (double?)stop.Latitude,
                    (double?)stop.Longitude),
                DetailsUrl: attempt is null
                    ? null
                    : $"/api/v1/customer/orders/{attempt.TripStopId}/live-tracking")
            {
                // Additive day-level link: available even before any trip stop exists.
                DeliveryDetailsUrl = $"/api/v1/customer/subscription-days/{projection.SubscriptionDayId}/delivery-details"
            });
        }

        return items;
    }

    /// <summary>Deterministic card order: active boxes first, then states needing
    /// attention, then completed; ties break by SubscriptionDayId so refreshing one
    /// item never reshuffles the list.</summary>
    private static int StatusOrder(CustomerDeliveryStatusSnapshot snapshot)
        => snapshot.DeliveryStatus switch
        {
            CustomerDeliveryStatusCodes.Delivered => 2,
            CustomerDeliveryStatusCodes.Failed
                or CustomerDeliveryStatusCodes.ReturnedToKitchen
                or CustomerDeliveryStatusCodes.TrackingUnavailable
                or CustomerDeliveryStatusCodes.Frozen
                or CustomerDeliveryStatusCodes.Cancelled => 1,
            _ => 0,
        };

    /// <summary>Batched real rating aggregation for every driver on the page — one
    /// query, no per-card reads; policy matches the driver profile (rounded average
    /// of real scores; drivers without ratings get no entry, never a fabricated value).</summary>
    private async Task<Dictionary<Guid, double>> LoadRatingsAsync(
        IReadOnlyCollection<Guid> driverIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, double>();
        if (driverIds.Count == 0)
        {
            return result;
        }

        var scores = await _db.DriverRatings.AsNoTracking()
            .Where(r => driverIds.Contains(r.DriverProfileId))
            .Select(r => new { r.DriverProfileId, r.Score })
            .ToListAsync(cancellationToken);

        foreach (var group in scores.GroupBy(r => r.DriverProfileId))
        {
            result[group.Key] = (double)Math.Round(group.Select(r => (decimal)r.Score).Average(), 2);
        }

        return result;
    }

    private async Task<Dictionary<Guid, DriverPresentation>> LoadDriversAsync(
        IReadOnlyCollection<Guid> driverIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, DriverPresentation>(driverIds.Count);
        if (driverIds.Count == 0)
        {
            return result;
        }

        var profiles = await _db.DriverProfiles.AsNoTracking()
            .Where(d => driverIds.Contains(d.Id))
            .ToListAsync(cancellationToken);

        var userIds = profiles.Select(p => p.UserId).Distinct().ToList();
        var phones = await _db.DriverRegistrations.AsNoTracking()
            .Where(r => r.ResultingDriverProfileId != null && driverIds.Contains(r.ResultingDriverProfileId.Value)
                        || r.SubmittedByUserId != null && userIds.Contains(r.SubmittedByUserId.Value))
            .Select(r => new { r.ResultingDriverProfileId, r.SubmittedByUserId, r.Phone })
            .ToListAsync(cancellationToken);

        foreach (var profile in profiles)
        {
            var phone = phones
                .Where(p => p.ResultingDriverProfileId == profile.Id
                            || (p.SubmittedByUserId.HasValue && p.SubmittedByUserId.Value == profile.UserId))
                .Select(p => p.Phone)
                .FirstOrDefault();

            result[profile.Id] = new DriverPresentation(profile, phone, AvatarUrlOf(profile));
        }

        return result;
    }

    /// <summary>Approved profile images only, exposed through the authorized content
    /// route — never a raw private storage key.</summary>
    private static string? AvatarUrlOf(DriverProfile profile)
        => profile.ProfileImageStatus == VerificationStatus.Approved
               && !string.IsNullOrWhiteSpace(profile.ProfileImageStorageKey)
            ? $"/api/v1/uploads/content?key={Uri.EscapeDataString(profile.ProfileImageStorageKey)}"
            : null;

    private sealed record DriverPresentation(
        DriverProfile Profile,
        string? Phone,
        string? AvatarUrl)
    {
        public CustomerDeliveryDriverDto ToDto(bool isAr, double? rating)
        {
            var nameAr = string.IsNullOrWhiteSpace(Profile.FullNameAr) ? Profile.FullNameEn : Profile.FullNameAr;
            var nameEn = string.IsNullOrWhiteSpace(Profile.FullNameEn) ? Profile.FullNameAr : Profile.FullNameEn;
            return new CustomerDeliveryDriverDto(
                Profile.Id,
                nameAr ?? string.Empty,
                nameEn ?? string.Empty,
                isAr ? nameAr ?? string.Empty : nameEn ?? string.Empty,
                MaskPhone(Phone),
                AvatarUrl,
                Rating: rating);
        }

        public CustomerDeliveryVehicleDto ToVehicleDto(bool isAr)
        {
            var typeCode = Profile.VehicleType ?? string.Empty;
            var catalogType = VehicleCatalogData.Types
                .FirstOrDefault(t => string.Equals(t.Code, typeCode, StringComparison.OrdinalIgnoreCase));
            var labelAr = catalogType?.NameAr ?? typeCode;
            var labelEn = catalogType?.NameEn ?? typeCode;
            return new CustomerDeliveryVehicleDto(
                typeCode,
                labelAr,
                labelEn,
                isAr ? labelAr : labelEn,
                Profile.VehiclePlate ?? string.Empty);
        }
    }

    private static string? MaskPhone(string? phone)
        => string.IsNullOrWhiteSpace(phone) || phone.Length <= 4
            ? phone
            : $"{phone[..3]}***{phone[^2..]}";
}
