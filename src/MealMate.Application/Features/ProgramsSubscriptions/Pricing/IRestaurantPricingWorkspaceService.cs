using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.ProgramsSubscriptions.Pricing;

public interface IRestaurantPricingWorkspaceService
{
    Task<Result<IReadOnlyList<RestaurantPricingWorkspaceRowDto>>> GetWorkspaceAsync(Guid restaurantId, CancellationToken ct = default);
    Task<Result<RestaurantBundlePricingDto>> SavePricingAsync(Guid restaurantId, SaveMyRestaurantPricingRequest body, CancellationToken ct = default);
    Task<Result<IReadOnlyList<RestaurantPricingWorkspaceRowDto>>> GetAvailableCatalogAsync(Guid restaurantId, CancellationToken ct = default);
    Task<Result> AddOfferAsync(Guid restaurantId, AddRestaurantPricingOfferRequest body, CancellationToken ct = default);
}
