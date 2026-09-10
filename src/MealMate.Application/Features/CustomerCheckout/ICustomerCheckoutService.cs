using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.CustomerCheckout;

public interface ICustomerCheckoutService
{
    Task<Result<IReadOnlyList<CheckoutCatalogItem>>> GetCatalogAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<EligibleRestaurantDto>>> GetRestaurantsAsync(CheckoutAvailabilityRequest request, Guid? customerUserId, CancellationToken ct = default);
    Task<Result<CheckoutStartedDto>> StartCheckoutAsync(StartCheckoutRequest request, Guid customerUserId, CancellationToken ct = default);
    Task<Result<SubscriptionPaymentStatusDto>> GetPaymentStatusAsync(Guid id, Guid customerUserId, CancellationToken ct = default);
    Task<Result<CustomerSubscriptionResumePreviewDto>> GetResumePreviewAsync(Guid id, Guid customerUserId, CancellationToken ct = default);
    Task<Result<CheckoutStartedDto>> ResumeCheckoutAsync(Guid id, ResumeCheckoutRequest? request, Guid customerUserId, CancellationToken ct = default);
    Task<Result<SubscriptionRestaurantPoolDto>> GetSubscriptionRestaurantsAsync(Guid id, Guid customerUserId, string? query, int page, int pageSize, string? acceptLanguage = null, CancellationToken ct = default);
}
