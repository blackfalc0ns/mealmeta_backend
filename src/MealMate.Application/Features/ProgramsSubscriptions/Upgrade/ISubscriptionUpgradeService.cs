using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.ProgramsSubscriptions.Upgrade;

public interface ISubscriptionUpgradeService
{
    Task<Result<CustomerUpgradeOptionsDto>> GetUpgradeOptionsAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default);
    Task<Result<CustomerUpgradeSummaryDto>> GetUpgradeSummaryAsync(Guid subscriptionId, Guid customerUserId, RestaurantTier targetTier, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerPaymentMethodDto>>> GetUpgradePaymentMethodsAsync(Guid subscriptionId, Guid customerUserId, decimal? amount, CancellationToken ct = default);
    Task<Result<CustomerUpgradeStartedDto>> RequestUpgradeAsync(Guid subscriptionId, Guid customerUserId, CustomerUpgradeRequest request, CancellationToken ct = default);
    Task<Result<CustomerUpgradeStatusDto>> GetUpgradeStatusAsync(Guid subscriptionId, Guid upgradeId, Guid customerUserId, string? paymentId = null, CancellationToken ct = default);
}
