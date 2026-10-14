using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

/// <summary>
/// Approved financial policy governing how campaign prices are derived.
/// One row per version; approved versions are immutable business records.
/// Every field is an explicit business decision — this entity carries no defaults.
/// </summary>
public sealed class CampaignPricingPolicy : AuditableEntity
{
    public const string ModelReferenceCustomerDiscount = "ReferenceCustomerDiscount";
    public const string ModelOriginalCampaignTier = "OriginalCampaignTier";

    public const string RoundingAwayFromZero = "AwayFromZero";
    public const string RoundingToEven = "ToEven";

    public int Version { get; set; }

    /// <summary>Which pricing model the approved numbers describe. No implicit default.</summary>
    public string Model { get; set; } = default!;

    public string CurrencyCode { get; set; } = default!;

    public string RoundingRule { get; set; } = default!;

    /// <summary>Who funds the customer discount (shares, caps, floors) — JSON document.</summary>
    public string ContributionRulesJson { get; set; } = default!;

    /// <summary>Whether coupons/referrals may stack on campaign prices, and under which caps — JSON document.</summary>
    public string StackingRulesJson { get; set; } = default!;

    /// <summary>How partial-fulfilment refunds are computed — JSON document.</summary>
    public string RefundRulesJson { get; set; } = default!;

    public Guid? ApprovedByUserId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public bool IsApproved => ApprovedByUserId.HasValue && ApprovedAtUtc.HasValue;

    public static Result<CampaignPricingPolicy> Create(
        int version,
        string model,
        string currencyCode,
        string roundingRule,
        string contributionRulesJson,
        string stackingRulesJson,
        string refundRulesJson)
    {
        if (version <= 0)
        {
            return Result.Failure<CampaignPricingPolicy>("Policy version must be a positive integer.");
        }

        if (model != ModelReferenceCustomerDiscount && model != ModelOriginalCampaignTier)
        {
            return Result.Failure<CampaignPricingPolicy>($"Unknown pricing model '{model}'.");
        }

        if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
        {
            return Result.Failure<CampaignPricingPolicy>("Policy currency code must be a 3-letter ISO code.");
        }

        if (roundingRule != RoundingAwayFromZero && roundingRule != RoundingToEven)
        {
            return Result.Failure<CampaignPricingPolicy>($"Unknown rounding rule '{roundingRule}'.");
        }

        foreach (var (value, name) in new[]
                 {
                     (contributionRulesJson, nameof(contributionRulesJson)),
                     (stackingRulesJson, nameof(stackingRulesJson)),
                     (refundRulesJson, nameof(refundRulesJson))
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Result.Failure<CampaignPricingPolicy>($"{name} is required; financial rules are never implicit.");
            }
        }

        return Result.Success(new CampaignPricingPolicy
        {
            Version = version,
            Model = model,
            CurrencyCode = currencyCode.Trim().ToUpperInvariant(),
            RoundingRule = roundingRule,
            ContributionRulesJson = contributionRulesJson,
            StackingRulesJson = stackingRulesJson,
            RefundRulesJson = refundRulesJson
        });
    }

    /// <summary>Records the explicit approver; a policy without approval must never price a checkout.</summary>
    public Result Approve(Guid approverUserId, DateTime approvedAtUtc)
    {
        if (IsApproved)
        {
            return Result.Failure("Policy version has already been approved.");
        }

        ApprovedByUserId = approverUserId;
        ApprovedAtUtc = approvedAtUtc;
        return Result.Success();
    }
}
