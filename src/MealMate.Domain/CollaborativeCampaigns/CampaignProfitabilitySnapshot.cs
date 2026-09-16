using MealMate.Domain.Common;

namespace MealMate.Domain.CollaborativeCampaigns;

public sealed record DurationMarginDetail(
    int DurationDays,
    decimal CustomerPrice,
    decimal ExpectedPayout,
    decimal ExpectedNetProfit,
    decimal ExpectedMarginPct,
    decimal WorstCaseMarginPct);

public sealed class CampaignProfitabilitySnapshot : AuditableEntity
{
    public Guid CampaignId { get; set; }

    public Campaign Campaign { get; set; } = default!;

    public int ProfitabilityVersion { get; set; } = 1;

    public decimal MinimumExpectedMarginPct { get; set; } = 0.10m;

    public decimal OverallExpectedMarginPct { get; set; }

    public decimal OverallWorstCaseMarginPct { get; set; }

    public bool IsViable { get; set; }

    public bool RequiresOverride { get; set; }

    public string? OverrideReason { get; set; }

    public Guid? OverrideApprovedByUserId { get; set; }

    public DateTime? OverrideApprovedAtUtc { get; set; }

    public string DetailsJson { get; set; } = default!;

    public DateTime EvaluatedAtUtc { get; set; } = DateTime.UtcNow;
}
