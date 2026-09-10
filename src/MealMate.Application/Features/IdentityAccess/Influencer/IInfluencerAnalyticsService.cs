using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public interface IInfluencerAnalyticsService
{
    Task<Result<InfluencerHomeDashboardDto>> GetHomeDashboardAsync(Guid userId, CancellationToken ct = default);
    Task<Result<InfluencerAnalyticsDto>> GetAnalyticsAsync(Guid userId, string period, CancellationToken ct = default);
}
