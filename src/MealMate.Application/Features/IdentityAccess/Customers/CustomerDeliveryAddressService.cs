using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Domain.CalendarMeals;using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Customers;

public interface ICustomerDeliveryAddressService
{
    Task<CustomerProfile> GetOrCreateProfileAsync(Guid userId, CancellationToken ct);
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

    public Task ScheduleDeliveryChangeAsync(CustomerProfile profile, Guid addressId, CancellationToken ct)
    {
        profile.PendingDeliveryAddressId = addressId;
        profile.PendingDeliveryAddressEffectiveFromUtc = _clock.UtcNow.Add(ChangeDelay);
        return Task.CompletedTask;
    }

    public static LocalizedTextDto BuildDeliveryChangeNotice(DateTime effectiveFromUtc)
        => new(
            Ar: "تم حفظ العنوان الجديد. لن يُفعَّل ولن تُسلَّم عليه الطلبات إلا بعد 72 ساعة. أي بوكسات خلال هذه المدة ستُسلَّم على عنوان التوصيل الحالي.",
            En: "Your new address is saved. It will not become active and will not receive deliveries until after 72 hours. Any boxes due before then will still go to your current delivery address.");

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
           || address.Latitude != request.Latitude
           || address.Longitude != request.Longitude
           || !string.Equals(TrimOrNull(address.Governorate), TrimOrNull(request.Governorate), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Area), TrimOrNull(request.Area), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Block), TrimOrNull(request.Block), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Street), TrimOrNull(request.Street), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Building), TrimOrNull(request.Building), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Unit), TrimOrNull(request.Unit), StringComparison.Ordinal)
           || !string.Equals(TrimOrNull(address.Floor), TrimOrNull(request.Floor), StringComparison.Ordinal);

    private async Task EnsureInitializedAsync(CustomerProfile profile, Guid userId, CancellationToken ct)
    {
        if (profile.ActiveDeliveryAddressId.HasValue)
            return;

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

        var subscriptions = await _db.Subscriptions
            .Where(x => x.CustomerUserId == userId
                && (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.Paid))
            .ToListAsync(ct);
        foreach (var subscription in subscriptions)
        {
            subscription.DeliveryAddress = profile.ActiveDeliveryAddressLine;
            subscription.RegionId = profile.ActiveDeliveryRegionId;
        }
    }

    private static void CopyActiveSnapshot(CustomerProfile profile, CustomerAddress address)
    {
        profile.ActiveDeliveryAddressId = address.Id;
        profile.ActiveDeliveryAddressLine = address.AddressLine;
        profile.ActiveDeliveryRegionId = address.RegionId;
        profile.ActiveDeliveryBlockRegionId = address.BlockRegionId;
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
