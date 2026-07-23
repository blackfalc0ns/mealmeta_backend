using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class ReferralCode : AuditableEntity
{
    public string Code { get; set; } = default!;

    public Guid OwnerUserId { get; set; }

    public Percentage? RewardPercent { get; set; }

    public int? MaxUses { get; set; }

    public int UsedCount { get; set; }

    public bool IsActive { get; set; } = true;
}
