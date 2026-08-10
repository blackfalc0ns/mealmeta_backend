using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class RestaurantBranch : AuditableEntity
{
    public Guid RestaurantRegistrationId { get; set; }

    public string BranchCode { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public string AddressAr { get; set; } = default!;

    public string AddressEn { get; set; } = default!;

    public string GovernorateAr { get; set; } = default!;

    public string GovernorateEn { get; set; } = default!;

    public string ContactPhone { get; set; } = default!;

    public bool IsActive { get; set; } = true;

    public bool IsPrimary { get; set; }

    public Guid? ManagerStaffId { get; set; }

    public string? PhotoStorageKey { get; set; }
}
