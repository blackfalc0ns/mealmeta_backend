using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Referrals;

public sealed record GetReferralSummaryQuery : IQuery<ReferralSummaryDto>;

/// <summary>
/// The caller's own referral data only. No invited-customer names or phones are exposed.
/// Reward amounts are reported only when an explicit reward policy exists on the code.
/// </summary>
public sealed record ReferralSummaryDto(
    string? Code,
    string? ShareUrl,
    bool HasRewardPolicy,
    decimal? RewardPercent,
    int AttributedCount,
    int RewardedCount,
    string? EligibilityNote);

public sealed class GetReferralSummaryQueryHandler : IQueryHandler<GetReferralSummaryQuery, ReferralSummaryDto>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    public GetReferralSummaryQueryHandler(
        IAppDbContext context,
        ICurrentUser currentUser,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _context = context;
        _currentUser = currentUser;
        _configuration = configuration;
    }

    public async Task<Result<ReferralSummaryDto>> Handle(GetReferralSummaryQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        var userId = _currentUser.UserId.Value;

        var code = await _context.ReferralCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OwnerUserId == userId, cancellationToken);

        var attributedCount = await _context.ReferralAttributions
            .AsNoTracking()
            .CountAsync(a => a.ReferrerUserId == userId, cancellationToken);

        var baseUrl = _configuration["PublicBaseUrl"];
        string? shareUrl = code is not null && !string.IsNullOrWhiteSpace(baseUrl)
            ? $"{baseUrl.TrimEnd('/')}/r/{code.Code}"
            : null;

        return Result.Success(new ReferralSummaryDto(
            Code: code?.Code,
            ShareUrl: shareUrl,
            HasRewardPolicy: code?.RewardPercent is not null,
            RewardPercent: code?.RewardPercent?.Value,
            AttributedCount: attributedCount,
            RewardedCount: 0,
            EligibilityCode()));
    }

    private static string? EligibilityCode() => "reward_pending_first_paid_subscription";
}
