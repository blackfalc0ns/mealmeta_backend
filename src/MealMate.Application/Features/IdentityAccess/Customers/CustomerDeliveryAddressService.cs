using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Customers;

public sealed record CustomerActiveDeliveryAddress(
    Guid Id,
    Guid RegionId,
    string Label,
    string AddressLine,
    string? Governorate,
    string? Area,
    string? Block,
    Guid? BlockRegionId = null);

public interface ICustomerDeliveryAddressService
{
    Task<CustomerProfile> GetOrCreateProfileAsync(Guid userId, CancellationToken ct);
    Task<CustomerActiveDeliveryAddress?> ResolveActiveDeliveryAddressAsync(Guid userId, CancellationToken ct);
    Task TryApplyPendingAsync(CustomerProfile profile, CancellationToken ct);
    Task InitializeActiveDeliveryAsync(CustomerProfile profile, CustomerAddress address, CancellationToken ct);
    Task ScheduleDeliveryChangeAsync(CustomerProfile profile, Guid addressId, CancellationToken ct);
    Task SnapshotDayDeliveryAsync(SubscriptionDay day, Guid customerUserId, DateTime evaluationUtc, CancellationToken ct);
}

public sealed class CustomerDeliveryAddressService : ICustomerDeliveryAddressService
{
    public static readonly TimeSpan ChangeDelay = TimeSpan.FromHours(72);

    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public CustomerDeliveryAddressService(IAppDbContext db, IDateTimeProvider clock)
        => (_db, _clock) = (db, clock);

    public async Task<CustomerProfile> GetOrCreateProfileAsync(Guid userId, CancellationToken ct)
    {
        var profile = await _db.CustomerProfiles.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (profile is not null)
        {
            await EnsureInitializedAsync(profile, userId, ct);
            return profile;
        }

        profile = new CustomerProfile { UserId = userId };
        _db.CustomerProfiles.Add(profile);
        await EnsureInitializedAsync(profile, userId, ct);
        return profile;
    }

    public async Task<CustomerActiveDeliveryAddress?> ResolveActiveDeliveryAddressAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty)
            return null;

        var profile = await _db.CustomerProfiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.ActiveDeliveryAddressId, x.ActiveDeliveryRegionId })
            .FirstOrDefaultAsync(ct);

        if (profile?.ActiveDeliveryAddressId is Guid activeAddressId)
        {
            var active = await LoadActiveDeliveryAddressAsync(activeAddressId, userId, ct);
            if (active is not null)
                return active;
        }

        if (profile?.ActiveDeliveryRegionId is Guid activeRegionId && activeRegionId != Guid.Empty)
        {
            var byRegion = await _db.CustomerAddresses.AsNoTracking()
                .Where(x => x.UserId == userId && x.RegionId == activeRegionId)
                .OrderByDescending(x => x.IsDefault)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);
            if (byRegion is not null)
                return MapActiveDeliveryAddress(byRegion);
        }

        var fallback = await _db.CustomerAddresses.AsNoTracking()
            .Where(x => x.UserId == userId && x.RegionId != null && x.RegionId != Guid.Empty)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        return fallback is null ? null : MapActiveDeliveryAddress(fallback);
    }

    public async Task TryApplyPendingAsync(CustomerProfile profile, CancellationToken ct)
    {
        if (profile.PendingDeliveryAddressId is not { } pendingId
            || profile.PendingDeliveryAddressEffectiveFromUtc is not { } effectiveFrom
            || _clock.UtcNow < effectiveFrom)
            return;

        var address = await _db.CustomerAddresses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == pendingId && x.UserId == profile.UserId, ct);
        if (address is null)
        {
            profile.PendingDeliveryAddressId = null;
            profile.PendingDeliveryAddressEffectiveFromUtc = null;
            return;
        }

        CopyActiveSnapshot(profile, address);
        profile.PendingDeliveryAddressId = null;
        profile.PendingDeliveryAddressEffectiveFromUtc = null;
        await SyncActiveSubscriptionsAsync(profile.UserId, profile, ct);
    }

    public async Task InitializeActiveDeliveryAsync(CustomerProfile profile, CustomerAddress address, CancellationToken ct)
    {
        CopyActiveSnapshot(profile, address);
        profile.PendingDeliveryAddressId = null;
        profile.PendingDeliveryAddressEffectiveFromUtc = null;
        await SyncActiveSubscriptionsAsync(profile.UserId, profile, ct);
    }

    public async Task ScheduleDeliveryChangeAsync(CustomerProfile profile, Guid addressId, CancellationToken ct)
    {
        var address = await _db.CustomerAddresses.FirstOrDefaultAsync(x => x.Id == addressId && x.UserId == profile.UserId, ct);
        if (address is not null)
        {
            await InitializeActiveDeliveryAsync(profile, address, ct);
        }
    }

    public static LocalizedTextDto BuildDeliveryChangeNotice(DateTime effectiveFromUtc)
        => new(
            Ar: "تم حفظ العنوان الجديد وتفعيله كعنوان توصيل.",
            En: "Your new address is saved and active for deliveries.");

    public static LocalizedTextDto BuildRestaurantCoverageNotice()
        => new(
            Ar: "تنبيه: عند اعتماد هذا العنوان للتوصيل، قد تتغير قائمة المطاعم المتاحة لاشتراكك؛ ستظهر مطاعم تغطي منطقتك الجديدة وتختفي مطاعم لا تدعم التوصيل إليها.",
            En: "Notice: When this address is used for delivery, available restaurants may change; restaurants serving your new area will appear, and restaurants not covering it will no longer be available.");

    public async Task SnapshotDayDeliveryAsync(
        SubscriptionDay day,
        Guid customerUserId,
        DateTime evaluationUtc,
        CancellationToken ct)
    {
        var profile = await GetOrCreateProfileAsync(customerUserId, ct);
        if (profile.PendingDeliveryAddressId is not null
            && profile.PendingDeliveryAddressEffectiveFromUtc is not null
            && evaluationUtc >= profile.PendingDeliveryAddressEffectiveFromUtc)
            await TryApplyPendingAsync(profile, ct);

        day.DeliveryAddressId = profile.ActiveDeliveryAddressId;
        day.DeliveryAddressLine = profile.ActiveDeliveryAddressLine;
        day.DeliveryRegionId = profile.ActiveDeliveryRegionId;
        day.DeliveryBlockRegionId = profile.ActiveDeliveryBlockRegionId;
    }

    public static bool HasDeliveryContentChanged(CustomerAddress address, UpdateCustomerAddressCommand request)
        => !string.Equals(address.AddressLine.Trim(), request.AddressLine.Trim(), StringComparison.Ordinal)
           || address.RegionId != request.RegionId
           || address.BlockRegionId != request.BlockRegionId
           || (request.Latitude.HasValue && address.Latitude != request.Latitude)
           || (request.Longitude.HasValue && address.Longitude != request.Longitude)
           || !string.Equals(TrimOrNull(address.Governorate), TrimOrNull(request.Governorate), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Area), TrimOrNull(request.Area), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Block), TrimOrNull(request.Block), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Street), TrimOrNull(request.Street), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Avenue), TrimOrNull(request.Avenue), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Building), TrimOrNull(request.Building), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Unit), TrimOrNull(request.Unit), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Floor), TrimOrNull(request.Floor), StringComparison.Ordinal);

    private async Task EnsureInitializedAsync(CustomerProfile profile, Guid userId, CancellationToken ct)
    {
        if (profile.ActiveDeliveryAddressId is { } activeId)
        {
            var activeStillExists = await _db.CustomerAddresses.AsNoTracking()
                .AnyAsync(x => x.Id == activeId && x.UserId == userId, ct);
            if (activeStillExists)
                return;
        }

        var defaultAddress = await _db.CustomerAddresses.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (defaultAddress is null)
            return;

        CopyActiveSnapshot(profile, defaultAddress);
    }

    private async Task SyncActiveSubscriptionsAsync(Guid userId, CustomerProfile profile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.ActiveDeliveryAddressLine))
            return;

        var activeStatuses = new[]
        {
            SubscriptionStatus.Active,
            SubscriptionStatus.Paid,
            SubscriptionStatus.Scheduled,
            SubscriptionStatus.Frozen,
            SubscriptionStatus.CancellationPending,
            SubscriptionStatus.Upgraded
        };

        var subscriptions = await _db.Subscriptions
            .Where(x => x.CustomerUserId == userId && activeStatuses.Contains(x.Status))
            .ToListAsync(ct);
        foreach (var subscription in subscriptions)
        {
            subscription.DeliveryAddress = profile.ActiveDeliveryAddressLine;
            subscription.RegionId = profile.ActiveDeliveryRegionId;
        }

        var subscriptionIds = subscriptions.Select(x => x.Id).ToList();
        if (subscriptionIds.Count > 0)
        {
            var today = DateOnly.FromDateTime(_clock.UtcNow);
            var upcomingDays = await _db.SubscriptionDays
                .Where(d => subscriptionIds.Contains(d.SubscriptionId)
                    && d.ServiceDate >= today
                    && d.Status != SubscriptionDayStatus.Delivered
                    && d.Status != SubscriptionDayStatus.Cancelled)
                .ToListAsync(ct);

            var dayIds = upcomingDays.Select(d => d.Id).ToList();
            var printedDayIds = dayIds.Count == 0
                ? new HashSet<Guid>()
                : (await _db.MealInvoices.AsNoTracking()
                    .Where(i => i.SubscriptionDayId != null && dayIds.Contains(i.SubscriptionDayId.Value))
                    .Select(i => i.SubscriptionDayId!.Value)
                    .ToListAsync(ct)).ToHashSet();

            foreach (var day in upcomingDays)
            {
                if (printedDayIds.Contains(day.Id))
                    continue;

                day.DeliveryAddressId = profile.ActiveDeliveryAddressId;
                day.DeliveryAddressLine = profile.ActiveDeliveryAddressLine;
                day.DeliveryRegionId = profile.ActiveDeliveryRegionId;
                day.DeliveryBlockRegionId = profile.ActiveDeliveryBlockRegionId;
            }
        }
    }

    private static void CopyActiveSnapshot(CustomerProfile profile, CustomerAddress address)
    {
        profile.ActiveDeliveryAddressId = address.Id;
        profile.ActiveDeliveryAddressLine = address.AddressLine;
        profile.ActiveDeliveryRegionId = address.RegionId;
        profile.ActiveDeliveryBlockRegionId = address.BlockRegionId;
    }

    private async Task<CustomerActiveDeliveryAddress?> LoadActiveDeliveryAddressAsync(
        Guid addressId, Guid userId, CancellationToken ct)
    {
        var address = await _db.CustomerAddresses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == addressId && x.UserId == userId, ct);
        if (address is null || address.RegionId is null || address.RegionId == Guid.Empty)
            return null;
        return MapActiveDeliveryAddress(address);
    }

    private static CustomerActiveDeliveryAddress MapActiveDeliveryAddress(CustomerAddress address)
        => new(
            address.Id,
            address.RegionId!.Value,
            address.Label,
            address.AddressLine,
            address.Governorate,
            address.Area,
            address.Block,
            address.BlockRegionId);

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
