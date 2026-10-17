using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class MealBoxLabelTemplate : AuditableEntity
{
    /// <summary>
    /// Unique template identifier: "classic-clean" | "organic-nature" | "luxury-dark" | "fresh-modern" | "eco-kraft"
    /// </summary>
    public string Key { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    public int DefaultWidthMm { get; set; } = 70;

    public int DefaultHeightMm { get; set; } = 100;

    public string DefaultOrientation { get; set; } = "portrait"; // "portrait" | "landscape" | "square"

    public bool IsEnabled { get; set; } = true;

    public bool IsPublished { get; set; } = true;

    public bool IsRestaurantVisible { get; set; } = true;

    public bool IsGlobalDefault { get; set; }

    public int Version { get; set; } = 1;

    public string ConfigurationJson { get; set; } = "{}";

    public DateTime? PublishedAtUtc { get; set; }
}
