using MealMate.Domain.Common;
using MealMate.Domain.Foundation;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.ProgramsSubscriptions;

/// <summary>
/// Versioned subscription pricing parameters (Rmax/Rmin curve, rounding).
/// Optional link to Foundation <see cref="RuleVersion"/> for approval workflow.
/// </summary>
public sealed class PricingRuleVersion : AuditableEntity
{
    public Guid? RuleVersionId { get; set; }

    public RuleVersion? RuleVersion { get; set; }

    public Percentage RMax { get; set; } = default!;

    public Percentage RMin { get; set; } = default!;

    public int DShort { get; set; }

    public int DLong { get; set; }

    public decimal Gamma { get; set; }

    public decimal RoundingStepKwd { get; set; }

    public bool IsActive { get; set; } = true;
}
