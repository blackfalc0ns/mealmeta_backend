using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

public sealed class CustomerProfile : AuditableEntity
{
    public Guid UserId { get; set; }

    public string? PreferredLanguage { get; set; }

    public Guid? DefaultRegionId { get; set; }

    public string? DeliveryAddressLine { get; set; }

    public decimal? DeliveryLatitude { get; set; }

    public decimal? DeliveryLongitude { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public bool OnboardingCompleted { get; set; }

    public string? ProfileImageStorageKey { get; set; }
}
