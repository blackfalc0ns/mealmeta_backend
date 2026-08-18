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

    public string? DietaryPattern { get; set; }

    public string? DietaryNotes { get; set; }

    /// <summary>Set when the customer explicitly saves the allergies screen, including NONE.</summary>
    public DateTime? AllergiesCompletedAtUtc { get; set; }

    /// <summary>Set when the customer explicitly saves dietary preferences, including NONE.</summary>
    public DateTime? DietaryPreferencesCompletedAtUtc { get; set; }
}
