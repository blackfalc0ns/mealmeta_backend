using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Catalog;
using MealMate.Application.Features.FamilyManager;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.CustomerTracking;

public sealed record GetCustomerDeliveryWidgetQuery(
    DateOnly? ServiceDate,
    Guid? SubscriptionId) : IQuery<CustomerDeliveryWidgetResponseDto>;

public sealed record CustomerDeliveryWidgetResponseDto(
    DateOnly ServiceDate,
    DateTime ServerTimeUtc,
    int RefreshAfterSeconds,
    IReadOnlyList<CustomerDeliveryWidgetItemDto> Items);

public sealed record CustomerDeliveryWidgetItemDto(
    Guid SubscriptionId,
    Guid SubscriptionDayId,
    Guid? OrderId,
    Guid? TripStopId,
    string? OrderCode,
    string DeliveryStatus,
    string DeliveryStatusTextAr,
    string DeliveryStatusTextEn,
    string DeliveryStatusText,
    bool IsTerminal,
    bool IsTrackable,
    string? UnavailableReasonCode,
    DateTime? ReadyAtUtc,
    DateTime? PickedUpAtUtc,
    DateTime? ArrivedAtUtc,
    DateTime? DeliveredAtUtc,
    CustomerDeliveryDriverDto? Driver,
    CustomerDeliveryVehicleDto? Vehicle,
    CustomerDeliveryLocationDto? Location,
    int? EtaMinutes,
    string? EtaSource,
    CustomerDeliveryDestinationDto? DestinationAddress,
    string? DetailsUrl);

public sealed record CustomerDeliveryDriverDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string Name,
    string MaskedPhone,
    string? AvatarUrl,
    double? Rating);

public sealed record CustomerDeliveryVehicleDto(
    string TypeCode,
    string TypeLabelAr,
    string TypeLabelEn,
    string TypeLabel,
    string PlateNumber);

public sealed record CustomerDeliveryLocationDto(
    double Latitude,
    double Longitude,
    DateTime CapturedAtUtc,
    DateTime? ReceivedAtUtc,
    bool IsLive);

public sealed record CustomerDeliveryDestinationDto(
    string? AddressLine,
    double? Latitude,
    double? Longitude);

public static class CustomerDeliveryWidgetDefaults
{
    /// <summary>Polling cadence for the mobile widget while the screen is visible.</summary>
    public const int RefreshSeconds = 30;
}

/// <summary>
/// Business-date helper for the customer delivery widget: "today" is the Kuwait
/// business calendar date computed from the server clock, never the device timezone
/// and never raw UTC.
/// </summary>
public static class CustomerBusinessDates
{
    private static readonly TimeZoneInfo BusinessTimeZone = ResolveBusinessTimeZone();

    public static DateOnly TodayInBusinessTimezone(DateTime utcNow)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, BusinessTimeZone));

    private static TimeZoneInfo ResolveBusinessTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuwait");
        }
        catch (TimeZoneNotFoundException)
        {
            // Kuwait has no DST and is fixed at UTC+3; the custom zone keeps the
            // business date identical on hosts without the IANA database.
            return TimeZoneInfo.CreateCustomTimeZone("Asia/Kuwait", TimeSpan.FromHours(3), "Kuwait", "Kuwait");
        }
    }
}

public sealed class GetCustomerDeliveryWidgetQueryHandler
    : IQueryHandler<GetCustomerDeliveryWidgetQuery, CustomerDeliveryWidgetResponseDto>
{
    private static readonly string[] VehicleTypeCodes =
        VehicleCatalogData.Types.Select(t => t.Code).ToArray();

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFamilyCalendarAccessGate _accessGate;
    private readonly ICustomerDeliveryReadService _readService;
    private readonly ICustomerDeliveryLocationBuilder _locationBuilder;
    private readonly IDateTimeProvider _clock;
    private readonly ILocalizedMessageProvider _messages;

    public GetCustomerDeliveryWidgetQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IFamilyCalendarAccessGate accessGate,
        ICustomerDeliveryReadService readService,
        ICustomerDeliveryLocationBuilder locationBuilder,
        IDateTimeProvider clock,
        ILocalizedMessageProvider messages)
    {
        _db = db;
        _currentUser = currentUser;
        _accessGate = accessGate;
        _readService = readService;
        _locationBuilder = locationBuilder;
        _clock = clock;
        _messages = messages;
    }

    public async Task<Result<CustomerDeliveryWidgetResponseDto>> Handle(
        GetCustomerDeliveryWidgetQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserType != UserType.Customer || _currentUser.UserId is null)
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden);
        }

        var serviceDate = request.ServiceDate ?? CustomerBusinessDates.TodayInBusinessTimezone(_clock.UtcNow);
        var isAr = _messages.IsArabic();

        var authorization = await ResolveAuthorizedSubscriptionIdsAsync(request.SubscriptionId, cancellationToken);
        if (authorization.Error is not null)
        {
            // The gate's own error convention applies to explicit requests; no driver
            // or address details are leaked either way.
            return Result.Failure<CustomerDeliveryWidgetResponseDto>(authorization.Error);
        }

        var authorizedSubscriptionIds = authorization.Ids;

        if (authorizedSubscriptionIds.Count == 0)
        {
            return Result.Success(new CustomerDeliveryWidgetResponseDto(
                serviceDate, _clock.UtcNow, CustomerDeliveryWidgetDefaults.RefreshSeconds, []));
        }

        var days = await _db.SubscriptionDays.AsNoTracking()
            .Where(d => authorizedSubscriptionIds.Contains(d.SubscriptionId) && d.ServiceDate == serviceDate)
            .Select(d => new { d.Id, d.SubscriptionId })
            .ToListAsync(cancellationToken);

        if (days.Count == 0)
        {
            return Result.Success(new CustomerDeliveryWidgetResponseDto(
                serviceDate, _clock.UtcNow, CustomerDeliveryWidgetDefaults.RefreshSeconds, []));
        }

        var projections = await _readService.GetForDaysAsync(
            days.Select(d => d.Id).ToList(), cancellationToken);

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

        var items = new List<CustomerDeliveryWidgetItemDto>(days.Count);
        foreach (var day in days)
        {
            if (!projections.TryGetValue(day.Id, out var projection))
            {
                continue;
            }

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
                    IsArrived: attempt.ArrivedAtUtc.HasValue && attempt.Status == Domain.Enums.TripStopStatus.PickedUp,
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
                Driver: showDriverAndVehicle ? driver?.ToDto(isAr) : null,
                Vehicle: showDriverAndVehicle ? driver?.ToVehicleDto(isAr) : null,
                Location: location,
                EtaMinutes: etaMinutes,
                EtaSource: etaSource,
                DestinationAddress: stop is null ? null : new CustomerDeliveryDestinationDto(
                    stop.FullAddress,
                    (double?)stop.Latitude,
                    (double?)stop.Longitude),
                DetailsUrl: attempt is null
                    ? null
                    : $"/api/v1/customer/orders/{attempt.TripStopId}/live-tracking"));
        }

        return Result.Success(new CustomerDeliveryWidgetResponseDto(
            serviceDate, _clock.UtcNow, CustomerDeliveryWidgetDefaults.RefreshSeconds, items));
    }

    private sealed record SubscriptionAuthorization(IReadOnlyCollection<Guid> Ids, Error? Error);

    /// <summary>
    /// Explicit subscription requests enforce the existing family read gate and return its
    /// error on failure. Unfiltered requests enumerate only subscriptions the same gate
    /// authorizes: owned subscriptions plus family member subscriptions linked to this
    /// customer — never a broadened family or manager access.
    /// </summary>
    private async Task<SubscriptionAuthorization> ResolveAuthorizedSubscriptionIdsAsync(
        Guid? requestedSubscriptionId,
        CancellationToken cancellationToken)
    {
        if (requestedSubscriptionId.HasValue)
        {
            var requested = await _db.Subscriptions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == requestedSubscriptionId.Value, cancellationToken);
            if (requested is null)
            {
                return new SubscriptionAuthorization([], Error.NotFound(MessageCodes.Common.NotFound));
            }

            var requestedAuth = await _accessGate.AuthorizeAsync(requested, requireWrite: false, cancellationToken);
            if (requestedAuth.IsFailure)
            {
                return new SubscriptionAuthorization([], requestedAuth.Error);
            }

            return new SubscriptionAuthorization([requested.Id], null);
        }

        var userId = _currentUser.UserId!.Value;

        // Owned subscriptions plus family member subscriptions linked to this user,
        // gathered in two batched reads (no navigations, no correlated subqueries).
        var ownedIds = await _db.Subscriptions.AsNoTracking()
            .Where(s => s.CustomerUserId == userId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var linkedIds = await _db.FamilyMemberAccesses.AsNoTracking()
            .Where(a => a.LinkedCustomerUserId == userId && a.RemovedAtUtc == null)
            .Join(_db.FamilyMembers.AsNoTracking(),
                a => a.FamilyMemberId,
                m => m.Id,
                (a, m) => m.MemberSubscriptionId)
            .ToListAsync(cancellationToken);

        var candidateIds = ownedIds
            .Concat(linkedIds.Where(id => id.HasValue).Select(id => id!.Value))
            .Distinct()
            .ToList();

        var candidates = candidateIds.Count == 0
            ? new List<Subscription>()
            : await _db.Subscriptions.AsNoTracking()
                .Where(s => candidateIds.Contains(s.Id))
                .ToListAsync(cancellationToken);

        var authorized = new List<Guid>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var auth = await _accessGate.AuthorizeAsync(candidate, requireWrite: false, cancellationToken);
            if (auth.IsSuccess)
            {
                authorized.Add(candidate.Id);
            }
        }

        return new SubscriptionAuthorization(authorized, null);
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
        public CustomerDeliveryDriverDto ToDto(bool isAr)
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
                Rating: null); // no verified driver-rating source exists; null instead of a fabricated value
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
