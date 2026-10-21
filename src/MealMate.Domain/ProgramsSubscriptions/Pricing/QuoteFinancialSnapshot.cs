namespace MealMate.Domain.ProgramsSubscriptions.Pricing;

/// <summary>Financial inputs and results pinned to a customer-accepted quote.</summary>
public sealed record QuoteFinancialSnapshot(
    decimal ReferencePrice26Kwd,
    decimal ContractedSupplyPrice26Kwd,
    decimal CustomerListPriceKwd,
    string SelectedDiscountSource,
    decimal SelectedDiscountKwd,
    decimal CustomerNetPriceKwd,
    decimal ExternalCaptureAmountKwd,
    decimal ExpectedGatewayCostKwd,
    decimal EffectiveRestaurantCostKwd,
    decimal? AppCommissionKwd,
    decimal? InfluencerCostKwd,
    decimal? IncrementalServiceCostKwd,
    decimal? EstimatedContributionKwd,
    string CurrencyCode,
    Guid? RestaurantBundlePricingId,
    Guid? ClassificationRunId,
    Guid RuleVersionId,
    string RuleVersionContentHash,
    string SourceVersionsHash,
    DateTime CapturedAtUtc,
    string SnapshotHash);
