using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.ProgramsSubscriptions;

public interface ISubscriptionPricingSettingsService
{
    Task<Result<SubscriptionPricingSettingsDocument>> GetSettingsAsync(CancellationToken ct = default);
    Task<Result<TierLabelsSettings>> GetTierLabelsAsync(CancellationToken ct = default);
    Task<Result<SubscriptionPricingSettingsDocument>> UpdateSettingsAsync(SubscriptionPricingSettingsDocument document, CancellationToken ct = default);
}
