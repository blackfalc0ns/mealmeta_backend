using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.ProgramsSubscriptions.Renewal;

public interface ISubscriptionRenewalService
{
    Task<Result<CustomerRenewalRequestDto>> RequestRenewalAsync(Guid subscriptionId, Guid customerUserId, CancellationToken ct = default);
}
