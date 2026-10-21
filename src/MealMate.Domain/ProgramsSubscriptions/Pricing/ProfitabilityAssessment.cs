namespace MealMate.Domain.ProgramsSubscriptions.Pricing;

public enum ProfitabilityRiskState : byte
{
    NotEvaluated = 0,
    Eligible = 1,
    NeedsReview = 2,
    ApprovedException = 3,
    Blocked = 4,
}

/// <summary>Decision evidence, separate from the quoted customer amount.</summary>
public sealed record ProfitabilityAssessment(
    ProfitabilityRiskState State,
    decimal CustomerNetKwd,
    decimal EffectiveRestaurantCostKwd,
    decimal ExpectedGatewayCostKwd,
    decimal? EligibleInfluencerCostKwd,
    decimal? IncrementalServiceCostKwd,
    decimal? NetAdjustmentsKwd,
    decimal? ContributionKwd,
    decimal? ContributionRate,
    decimal? MinimumContributionKwd,
    decimal? RequiredFundingDeficitKwd,
    IReadOnlyList<string> BlockerCodes);
