using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed class GetInfluencerHomeDashboardQueryHandler : IQueryHandler<GetInfluencerHomeDashboardQuery, InfluencerHomeDashboardDto>
{
    private readonly IInfluencerAnalyticsService _analyticsService;
    private readonly ICurrentUser _currentUser;

    public GetInfluencerHomeDashboardQueryHandler(IInfluencerAnalyticsService analyticsService, ICurrentUser currentUser)
    {
        _analyticsService = analyticsService;
        _currentUser = currentUser;
    }

    public async Task<Result<InfluencerHomeDashboardDto>> Handle(GetInfluencerHomeDashboardQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _analyticsService.GetHomeDashboardAsync(userId, cancellationToken);
    }
}

public sealed class GetInfluencerAnalyticsQueryHandler : IQueryHandler<GetInfluencerAnalyticsQuery, InfluencerAnalyticsDto>
{
    private readonly IInfluencerAnalyticsService _analyticsService;
    private readonly ICurrentUser _currentUser;

    public GetInfluencerAnalyticsQueryHandler(IInfluencerAnalyticsService analyticsService, ICurrentUser currentUser)
    {
        _analyticsService = analyticsService;
        _currentUser = currentUser;
    }

    public async Task<Result<InfluencerAnalyticsDto>> Handle(GetInfluencerAnalyticsQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _analyticsService.GetAnalyticsAsync(userId, request.Period, cancellationToken);
    }
}

public sealed class GetInfluencerActivitiesQueryHandler : IQueryHandler<GetInfluencerActivitiesQuery, PagedResultDto<InfluencerRecentActivityDto>>
{
    private readonly IInfluencerAnalyticsService _analyticsService;
    private readonly ICurrentUser _currentUser;

    public GetInfluencerActivitiesQueryHandler(IInfluencerAnalyticsService analyticsService, ICurrentUser currentUser)
    {
        _analyticsService = analyticsService;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResultDto<InfluencerRecentActivityDto>>> Handle(GetInfluencerActivitiesQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials);
        }

        return await _analyticsService.GetActivitiesAsync(userId, request.Page, request.PageSize, cancellationToken);
    }
}
