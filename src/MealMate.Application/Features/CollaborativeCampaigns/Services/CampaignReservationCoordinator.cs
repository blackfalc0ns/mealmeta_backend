using MealMate.Application.Common.Abstractions;

namespace MealMate.Application.Features.CollaborativeCampaigns.Services;

public sealed record CapacityLockTarget(
    Guid CampaignId,
    Guid BundleId,
    DateOnly ServiceDate,
    Guid RestaurantId);

public sealed record ReservationRequest(
    Guid CampaignId,
    Guid BundleId,
    Guid CustomerId,
    SubscriberCapState CapState,
    IReadOnlyList<DateOnly> RequestedServiceDates,
    IReadOnlyList<RestaurantDailyAvailability> AvailableCapacities,
    TimeSpan ReservationTtl);

public sealed record CampaignReservationResult(
    bool IsSuccess,
    Guid? ReservationId,
    DateTime? ExpiresAtUtc,
    SubscriberCapState? UpdatedCapState,
    IReadOnlyList<CampaignReservationAllocationItem> Allocations,
    IReadOnlyList<CapacityLockTarget> DeterministicLockTargets,
    string? FailureReason = null)
{
    public static CampaignReservationResult Success(
        Guid reservationId,
        DateTime expiresAtUtc,
        SubscriberCapState updatedCapState,
        IReadOnlyList<CampaignReservationAllocationItem> allocations,
        IReadOnlyList<CapacityLockTarget> deterministicLockTargets) =>
        new(true, reservationId, expiresAtUtc, updatedCapState, allocations, deterministicLockTargets);

    public static CampaignReservationResult Failure(string reason) =>
        new(false, null, null, null, Array.Empty<CampaignReservationAllocationItem>(), Array.Empty<CapacityLockTarget>(), reason);
}

public interface ICampaignReservationCoordinator
{
    CampaignReservationResult CoordinateReservation(ReservationRequest request);
}

public sealed class CampaignReservationCoordinator : ICampaignReservationCoordinator
{
    private readonly ISubscriberCapManager _capManager;
    private readonly ICampaignFulfillmentAllocator _allocator;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CampaignReservationCoordinator(
        ISubscriberCapManager capManager,
        ICampaignFulfillmentAllocator allocator,
        IDateTimeProvider dateTimeProvider)
    {
        _capManager = capManager;
        _allocator = allocator;
        _dateTimeProvider = dateTimeProvider;
    }

    public CampaignReservationResult CoordinateReservation(ReservationRequest request)
    {
        // 1. Check Subscriber Cap
        var capResult = _capManager.TryReserve(request.CapState, slotsToReserve: 1);
        if (!capResult.IsSuccess)
        {
            return CampaignReservationResult.Failure(capResult.FailureReason ?? "Subscriber cap reached.");
        }

        // 2. Allocate Daily Fulfillment Capacity with Restaurant Diversification
        var allocationResult = _allocator.Allocate(request.RequestedServiceDates, request.AvailableCapacities);
        if (!allocationResult.IsSuccess)
        {
            return CampaignReservationResult.Failure(allocationResult.FailureReason ?? "Failed to allocate fulfillment capacity.");
        }

        // 3. Build Deterministic Sorted Lock Targets for Deadlock Prevention
        var lockTargets = allocationResult.Allocations
            .Select(a => new CapacityLockTarget(
                request.CampaignId,
                request.BundleId,
                a.ServiceDate,
                a.RestaurantId))
            .OrderBy(t => t.ServiceDate)
            .ThenBy(t => t.RestaurantId)
            .ToList();

        var reservationId = Guid.NewGuid();
        var expiresAtUtc = _dateTimeProvider.UtcNow.Add(request.ReservationTtl);

        return CampaignReservationResult.Success(
            reservationId,
            expiresAtUtc,
            capResult.NewState,
            allocationResult.Allocations,
            lockTargets);
    }
}
