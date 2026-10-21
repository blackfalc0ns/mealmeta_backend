using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions.Pricing;

public enum PricingQuoteRoute : byte
{
    Individual = 1,
    Family = 2,
    Campaign = 3,
}

public enum PricingQuoteStatus : byte
{
    Quoted = 1,
    Accepted = 2,
    Consumed = 3,
    Expired = 4,
    Invalidated = 5,
}

/// <summary>Server-created, immutable quote common to every checkout route.</summary>
public sealed class PricingQuote : BaseEntity
{
    public Guid CustomerId { get; set; }
    public PricingQuoteRoute Route { get; set; }
    public Guid ProgramId { get; set; }
    public Guid BundleId { get; set; }
    public RestaurantTier Tier { get; set; }
    public int DurationDays { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateOnly[] ServiceDates { get; set; } = [];
    public Guid? OfferId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? FamilyGroupId { get; set; }
    public string SelectionSnapshotJson { get; set; } = default!;
    public string SelectionHash { get; set; } = default!;
    public Guid RuleVersionId { get; set; }
    public string RuleVersionContentHash { get; set; } = default!;
    public Guid? ClassificationRunId { get; set; }
    public string SourceVersionsHash { get; set; } = default!;
    public string SourceVersionsJson { get; set; } = "[]";
    public decimal ReferencePrice26Kwd { get; set; }
    public decimal CohortMeanReferencePrice26Kwd { get; set; }
    public decimal ContractedSupplyPrice26Kwd { get; set; }
    public decimal CustomerListPriceKwd { get; set; }
    public string SelectedDiscountSource { get; set; } = "None";
    public decimal SelectedDiscountKwd { get; set; }
    public decimal CustomerNetPriceKwd { get; set; }
    public decimal ExternalCaptureAmountKwd { get; set; }
    public decimal ExpectedGatewayCostKwd { get; set; }
    public string CurrencyCode { get; set; } = "KWD";
    public string FinancialSnapshotJson { get; set; } = default!;
    public string FinancialSnapshotHash { get; set; } = default!;
    public PricingQuoteStatus Status { get; set; } = PricingQuoteStatus.Quoted;
    public ProfitabilityRiskState RiskState { get; set; } = ProfitabilityRiskState.NotEvaluated;
    public string[] RiskBlockerCodes { get; set; } = [];
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public int RowVersion { get; set; }
}
