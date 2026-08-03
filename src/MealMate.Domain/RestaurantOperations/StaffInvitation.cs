using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class StaffInvitation : AuditableEntity
{
    public Guid RestaurantRegistrationId { get; set; }

    public string FullNameAr { get; set; } = default!;

    public string FullNameEn { get; set; } = default!;

    public string Email { get; set; } = default!;

    public string Phone { get; set; } = default!;

    public string RoleId { get; set; } = default!;

    public bool AllBranches { get; set; } = true;

    public string? BranchIdsJson { get; set; }

    public string InviteToken { get; set; } = default!;

    public string Status { get; set; } = "Invited"; // Invited, Accepted, Expired, Revoked

    public DateTime ExpiresAtUtc { get; set; }
}
