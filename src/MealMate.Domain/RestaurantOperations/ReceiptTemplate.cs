using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class ReceiptTemplate : AuditableEntity
{
    public string Key { get; set; } = default!; // "classic-minimal" | "organic-elegant" | "premium-dark"

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsPublished { get; set; } = true;

    public bool IsRestaurantVisible { get; set; } = true;

    public bool IsGlobalDefault { get; set; }

    public int Version { get; set; } = 1;

    public string ConfigurationJson { get; set; } = "{}";

    public DateTime? PublishedAtUtc { get; set; }
}
