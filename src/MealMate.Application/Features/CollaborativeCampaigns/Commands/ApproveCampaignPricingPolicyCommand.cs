using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.CollaborativeCampaigns;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CollaborativeCampaigns.Commands;

/// <summary>
/// Creates a new campaign pricing policy version and records the explicit admin approval in one action.
/// There is no implicit or default policy: checkout refuses to price while no approved version exists.
/// </summary>
public sealed record ApproveCampaignPricingPolicyCommand(
    string Model,
    string CurrencyCode,
    string RoundingRule,
    string ContributionRulesJson,
    string StackingRulesJson,
    string RefundRulesJson) : ICommand<CampaignPricingPolicyDto>;

public sealed record CampaignPricingPolicyDto(
    Guid Id,
    int Version,
    string Model,
    string CurrencyCode,
    string RoundingRule,
    string ContributionRulesJson,
    string StackingRulesJson,
    string RefundRulesJson,
    Guid? ApprovedByUserId,
    DateTime? ApprovedAtUtc);

public sealed class ApproveCampaignPricingPolicyCommandHandler
    : ICommandHandler<ApproveCampaignPricingPolicyCommand, CampaignPricingPolicyDto>
{
    private readonly IAppDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ApproveCampaignPricingPolicyCommandHandler(
        IAppDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CampaignPricingPolicyDto>> Handle(
        ApproveCampaignPricingPolicyCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Error.Unauthorized("Auth.Unauthorized", "User is not authenticated.");
        }

        var nextVersion = await _context.CampaignPricingPolicies
            .AnyAsync(cancellationToken)
            ? await _context.CampaignPricingPolicies.MaxAsync(p => p.Version, cancellationToken) + 1
            : 1;

        var createResult = CampaignPricingPolicy.Create(
            nextVersion,
            request.Model,
            request.CurrencyCode,
            request.RoundingRule,
            request.ContributionRulesJson,
            request.StackingRulesJson,
            request.RefundRulesJson);

        if (createResult.IsFailure)
        {
            return Error.Validation("Campaign.PricingPolicyInvalid", createResult.Error ?? "Invalid pricing policy.");
        }

        var policy = createResult.Value;
        var approveResult = policy.Approve(_currentUser.UserId.Value, _dateTimeProvider.UtcNow);
        if (approveResult.IsFailure)
        {
            return Error.Validation("Campaign.PricingPolicyInvalid", approveResult.Error ?? "Policy could not be approved.");
        }

        policy.CreatedAtUtc = _dateTimeProvider.UtcNow;
        policy.CreatedByUserId = _currentUser.UserId;

        _context.CampaignPricingPolicies.Add(policy);
        await _context.SaveChangesAsync(cancellationToken);

        return new CampaignPricingPolicyDto(
            policy.Id,
            policy.Version,
            policy.Model,
            policy.CurrencyCode,
            policy.RoundingRule,
            policy.ContributionRulesJson,
            policy.StackingRulesJson,
            policy.RefundRulesJson,
            policy.ApprovedByUserId,
            policy.ApprovedAtUtc);
    }
}
