using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.ProgramsSubscriptions.Freeze;

public interface ISubscriptionFreezeService
{
    Task<Result<CustomerSubscriptionFreezePolicyDto>> GetPolicyAsync(CancellationToken ct = default);
    Task<Result<CustomerSubscriptionFreezeDetailsDto>> GetCurrentFreezeDetailsAsync(Guid customerUserId, CancellationToken ct = default);
    Task<Result<CustomerSubscriptionFreezeDetailsDto>> GetFreezeDetailsAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default);
    Task<Result<SubscriptionFreezeRequestDto>> RequestFreezeAsync(Guid subscriptionId, Guid customerUserId, CustomerFreezeRequest request, CancellationToken ct = default);
}
