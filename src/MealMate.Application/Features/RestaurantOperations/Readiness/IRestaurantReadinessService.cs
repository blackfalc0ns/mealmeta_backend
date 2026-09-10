using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.RestaurantOperations.Readiness;

public interface IRestaurantReadinessService
{
    Task<Result<RestaurantEligibilityDto>> GetMyReadinessAsync(
        Guid userId,
        DateOnly? from,
        int days = 7,
        Guid? programId = null,
        Guid? bundleId = null,
        RestaurantTier? tier = null,
        Guid? regionId = null,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<RestaurantEligibilityDto>>> ListAdminReadinessAsync(
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        Guid regionId,
        DateOnly? from,
        int days = 7,
        CancellationToken ct = default);

    Task<Result<RestaurantEligibilityDto?>> GetAdminRestaurantReadinessAsync(
        Guid restaurantId,
        Guid programId,
        Guid bundleId,
        RestaurantTier tier,
        Guid regionId,
        DateOnly? from,
        int days = 7,
        CancellationToken ct = default);

    Task<Result<MealSnapshotBackfillResultDto>> BackfillSnapshotsAsync(CancellationToken ct = default);
}
