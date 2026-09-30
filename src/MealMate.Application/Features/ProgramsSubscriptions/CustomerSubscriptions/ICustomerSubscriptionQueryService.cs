using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.ProgramsSubscriptions.CustomerSubscriptions;

public interface ICustomerSubscriptionQueryService
{
    Task<Result<CustomerSubscriptionManagementScreenDto>> GetManagementScreenAsync(Guid customerUserId, bool includeHistory, CancellationToken ct = default);
    Task<Result<CustomerCurrentSubscriptionScreenDto>> GetCurrentSubscriptionAsync(Guid customerUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerSubscriptionSlimDto>>> ListSubscriptionsAsync(Guid customerUserId, bool includeHistory, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerUncompletedSubscriptionDto>>> GetUncompletedSubscriptionsAsync(Guid customerUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerSubscriptionSlimDto>>> GetArchivedSubscriptionsAsync(Guid customerUserId, CancellationToken ct = default);
    Task<Result<CustomerManagedSubscriptionDto>> GetManagementDetailsAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerSubscriptionInvoiceListDto>>> GetInvoicesAsync(Guid customerUserId, CancellationToken ct = default);
    Task<Result<CustomerSubscriptionInvoiceDto>> GetInvoiceDetailAsync(Guid receiptId, Guid customerUserId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerSubscriptionInvoiceDto>>> GetSubscriptionInvoicesAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default);
    Task<Result<CustomerDefaultPaymentMethodDto>> GetDefaultPaymentMethodAsync(Guid customerUserId, CancellationToken ct = default);
}
