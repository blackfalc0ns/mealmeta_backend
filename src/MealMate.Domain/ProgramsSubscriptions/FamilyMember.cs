using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class FamilyMember : AuditableEntity
{
    public Guid FamilyGroupId { get; set; }
    public FamilyGroup FamilyGroup { get; set; } = default!;
    public FamilyMemberRole Role { get; set; }
    public string DisplayName { get; set; } = default!;
    public string RelationshipCode { get; set; } = default!;
    public FamilyMemberGender Gender { get; set; }
    public int AgeYears { get; set; }
    public string NutritionalGoalCode { get; set; } = default!;
    public FamilyActivityLevel ActivityLevel { get; set; }
    public Guid? DeliveryAddressId { get; set; }
    public FamilyMemberSetupStatus SetupStatus { get; set; } = FamilyMemberSetupStatus.NotStarted;
    public Guid? ProgramId { get; set; }
    public Guid? BundleId { get; set; }
    public RestaurantTier? SelectedTier { get; set; }
    public int? DurationDays { get; set; }
    public bool IncludeFriday { get; set; }
    public Guid? MemberSubscriptionId { get; set; }
    public Subscription? MemberSubscription { get; set; }
}
