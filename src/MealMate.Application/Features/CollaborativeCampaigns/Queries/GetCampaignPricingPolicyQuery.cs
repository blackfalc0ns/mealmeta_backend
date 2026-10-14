using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CollaborativeCampaigns.Commands;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Queries;

public sealed record GetCampaignPricingPolicyQuery : IQuery<CampaignPricingPolicyDto?>;

public sealed class GetCampaignPricingPolicyQueryHandler
    : IQueryHandler<GetCampaignPricingPolicyQuery, CampaignPricingPolicyDto?>
{
    private readonly IAppDbContext _context;

    public GetCampaignPricingPolicyQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<CampaignPricingPolicyDto?>> Handle(
        GetCampaignPricingPolicyQuery request,
        CancellationToken cancellationToken)
    {
        var policy = await _context.CampaignPricingPolicies
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(cancellationToken);

        if (policy == null)
        {
            // Honest empty state: no policy has been created or approved yet.
            return Result.Success<CampaignPricingPolicyDto?>(null);
        }

        return Result.Success<CampaignPricingPolicyDto?>(new CampaignPricingPolicyDto(
            policy.Id,
            policy.Version,
            policy.Model,
            policy.CurrencyCode,
            policy.RoundingRule,
            policy.ContributionRulesJson,
            policy.StackingRulesJson,
            policy.RefundRulesJson,
            policy.ApprovedByUserId,
            policy.ApprovedAtUtc));
    }
}
