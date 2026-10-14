using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

/// <summary>
/// Records that a new customer arrived through a referral code.
/// Attribution is immutable and unique per referred customer; rewards are granted
/// separately by the approved financial policy — never by mere registration.
/// </summary>
public sealed class ReferralAttribution : AuditableEntity
{
    public string Code { get; set; } = default!;

    public Guid ReferrerUserId { get; set; }

    public Guid ReferredCustomerUserId { get; set; }

    public DateTime AttributedAtUtc { get; set; }

    /// <summary>ReferralCode whose policy version applied at attribution time, when known.</summary>
    public Guid? ReferralCodeId { get; set; }
}
