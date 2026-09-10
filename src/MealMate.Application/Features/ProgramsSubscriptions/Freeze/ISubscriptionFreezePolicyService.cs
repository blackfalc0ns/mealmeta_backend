using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.ProgramsSubscriptions.Freeze;

public interface ISubscriptionFreezePolicyService
{
    Task<Result<SubscriptionFreezePolicyDocument>> GetPolicyAsync(CancellationToken ct = default);
    Task<Result<SubscriptionFreezePolicyDocument>> UpdatePolicyAsync(SubscriptionFreezePolicyDocument document, CancellationToken ct = default);
}
