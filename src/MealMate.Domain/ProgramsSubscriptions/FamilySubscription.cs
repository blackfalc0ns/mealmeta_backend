using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class FamilySubscription : AuditableEntity
{
    public Guid PrimarySubscriptionId { get; set; }

    public Subscription PrimarySubscription { get; set; } = default!;

    public Guid MemberCustomerUserId { get; set; }

    public string? DisplayName { get; set; }

    public bool IsActive { get; set; } = true;
}
