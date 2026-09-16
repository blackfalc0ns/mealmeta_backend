using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public interface IInfluencerAnalyticsService
{
    Task<Result<InfluencerHomeDashboardDto>> GetHomeDashboardAsync(Guid userId, CancellationToken ct = default);
    Task<Result<InfluencerAnalyticsDto>> GetAnalyticsAsync(Guid userId, string period, CancellationToken ct = default);
    Task<Result<PagedResultDto<InfluencerRecentActivityDto>>> GetActivitiesAsync(Guid userId, int page = 1, int pageSize = 20, CancellationToken ct = default);
}
