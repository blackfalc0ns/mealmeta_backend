using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
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
    string? DetailsUrl)
{
    /// <summary>
    /// Additive day-level details link keyed by SubscriptionDayId: available even
    /// before any trip stop exists and after delivery. <see cref="DetailsUrl"/> keeps
    /// its legacy trip-stop-based meaning and stays null without an attempt.
    /// </summary>
    public string? DeliveryDetailsUrl { get; init; }
}

public sealed record CustomerDeliveryDriverDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string Name,
    string? MaskedPhone,
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
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFamilyCalendarAccessGate _accessGate;
    private readonly ICustomerDeliveryPresentationBuilder _presentationBuilder;
    private readonly IDateTimeProvider _clock;

    public GetCustomerDeliveryWidgetQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IFamilyCalendarAccessGate accessGate,
        ICustomerDeliveryPresentationBuilder presentationBuilder,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _accessGate = accessGate;
        _presentationBuilder = presentationBuilder;
        _clock = clock;
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

        var items = await _presentationBuilder.BuildAsync(
            days.Select(d => d.Id).ToList(), cancellationToken);

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
}
