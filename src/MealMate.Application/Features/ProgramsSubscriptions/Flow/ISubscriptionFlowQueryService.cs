using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Flow;

public interface ISubscriptionFlowQueryService
{
    Task<Result<IReadOnlyList<SubscriptionTypeOptionDto>>> GetSubscriptionTypesAsync(Guid customerUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerProgramCardDto>>> GetProgramsAsync(Guid customerUserId, CancellationToken ct = default);
    /// <summary>Paged programs contract. Enabled when the client sends page and/or pageSize.</summary>
    Task<Result<PagedResult<CustomerProgramCardDto>>> GetProgramsPagedAsync(Guid customerUserId, PageRequest page, CancellationToken ct = default);
    Task<Result<CustomerProgramBundlesDto>> GetProgramBundlesAsync(Guid programId, Guid customerUserId, CancellationToken ct = default);
    /// <summary>Legacy bundles contract with additive pagination metadata when requested.</summary>
    Task<Result<CustomerProgramBundlesDto>> GetProgramBundlesPagedAsync(Guid programId, Guid customerUserId, PageRequest page, CancellationToken ct = default);
    Task<Result<CustomerSubscriptionPrerequisitesDto>> GetPrerequisitesAsync(Guid customerUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerDurationOptionDto>>> GetDurationsAsync(Guid programId, Guid? bundleId, Guid customerUserId, bool includeFriday, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerTierOptionDto>>> GetTiersAsync(Guid programId, Guid? bundleId, Guid customerUserId, int? durationDays, bool includeFriday, Guid? familyMemberId = null, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerRestaurantBrowseCardDto>>> GetTierRestaurantsAsync(Guid programId, Guid? bundleId, RestaurantTier tier, Guid customerUserId, Guid[]? restaurantIds = null, Guid? familyMemberId = null, CancellationToken ct = default);
    /// <summary>Paged tier restaurants contract. Enabled when the client sends page and/or pageSize.</summary>
    Task<Result<PagedResult<CustomerRestaurantBrowseCardDto>>> GetTierRestaurantsPagedAsync(Guid programId, Guid? bundleId, RestaurantTier tier, Guid customerUserId, Guid[]? restaurantIds, Guid? familyMemberId, PageRequest page, CancellationToken ct = default);
    Task<Result<CustomerRestaurantBrowseCardDto>> GetRestaurantBrowseAsync(Guid programId, Guid bundleId, Guid restaurantId, Guid customerUserId, CancellationToken ct = default);
    Task<Result<CustomerRestaurantBrowseDetailsDto>> GetRestaurantBrowseDetailsAsync(Guid programId, Guid bundleId, Guid restaurantId, Guid customerUserId, CancellationToken ct = default);
    Task<Result<CustomerRestaurantBrowseMealDetailsDto>> GetMealBrowseDetailsAsync(Guid programId, Guid bundleId, Guid restaurantId, Guid mealId, Guid customerUserId, CancellationToken ct = default);
    Task<Result<CustomerRestaurantBrowseMealDetailsDto>> GetMealBrowseDetailsAsync(Guid bundleId, Guid mealId, Guid customerUserId, CancellationToken ct = default);
    Task<Result<SubscriptionConfigurationDto>> GetConfigurationAsync(Guid programId, Guid bundleId, Guid customerUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerRestaurantCardDto>>> GetRestaurantsAsync(SubscriptionFlowContext request, Guid customerUserId, CancellationToken ct = default);
    Task<Result<CustomerRestaurantDetailsDto>> GetRestaurantDetailsAsync(Guid restaurantId, SubscriptionFlowContext request, Guid customerUserId, CancellationToken ct = default);
    Task<Result<CustomerMealDetailsDto>> GetMealDetailsAsync(Guid restaurantId, Guid mealId, SubscriptionFlowContext request, Guid customerUserId, CancellationToken ct = default);
    Task<Result<SubscriptionCheckoutReadinessDto>> GetReadinessAsync(SubscriptionSummaryRequest request, Guid customerUserId, CancellationToken ct = default);
    Task<Result<SubscriptionOrderSummaryDto>> GetOrderSummaryAsync(SubscriptionSummaryRequest request, Guid customerUserId, CancellationToken ct = default);
    /// <summary>Contextual availability verdict plus eligible browse cards for the same selections the summary reviews.</summary>
    Task<Result<CustomerRestaurantAvailabilityResponseDto>> GetRestaurantAvailabilityAsync(SubscriptionSummaryRequest request, Guid customerUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerPaymentMethodDto>>> GetPaymentMethodsAsync(decimal amount, CancellationToken ct = default, bool allowSandboxCompletion = true);
}
