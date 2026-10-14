using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class FamilyMemberAccess : AuditableEntity
{
    public Guid FamilyMemberId { get; set; }
    public FamilyMember FamilyMember { get; set; } = default!;
    public bool HasSeparateAccount { get; set; }
    public bool RequiresPasswordSetup { get; set; }
    public Guid? LinkedCustomerUserId { get; set; }
    public string? InviteTokenHash { get; set; }
    public DateTime? InviteExpiresAtUtc { get; set; }
    public bool IsPaused { get; set; }
    public DateTime? PausedAtUtc { get; set; }
    public DateTime? RemovedAtUtc { get; set; }
}
