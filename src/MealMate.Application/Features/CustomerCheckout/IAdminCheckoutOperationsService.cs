using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.CustomerCheckout;

public interface IAdminCheckoutOperationsService
{
    Task<Result<IReadOnlyList<CapacityHoldItemDto>>> GetCapacityHoldsAsync(CapacityHoldStatus? status, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SnapshotMigrationIssueDto>>> GetSnapshotMigrationIssuesAsync(bool unresolvedOnly = true, CancellationToken ct = default);
    Task<Result<ExpireHoldsOutcome>> ExpireHoldsAsync(CancellationToken ct = default);
    Task<Result<RetryFulfillmentOutcome>> RetryFulfillmentAsync(Guid subscriptionId, CancellationToken ct = default);
    Task<Result<ReassignmentOutcome>> ReassignRestaurantAsync(Guid subscriptionId, DateOnly date, ReassignRestaurantRequest request, Guid currentUserId, UserType currentUserType, CancellationToken ct = default);
    Task<Result<IReadOnlyList<FrozenRestaurantOption>>> GetFrozenRestaurantOptionsAsync(Guid subscriptionId, DateOnly date, CancellationToken ct = default);
}
