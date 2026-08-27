using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

public sealed class DocumentTemplate : AuditableEntity
{
    public DocumentTemplateKind Kind { get; set; }

    public string NameAr { get; set; } = "";

    public string NameEn { get; set; } = "";

    public string? DescriptionAr { get; set; }

    public string? DescriptionEn { get; set; }

    public string Language { get; set; } = "both"; // ar|en|both

    public DocumentTemplateStatus Status { get; set; } = DocumentTemplateStatus.Draft;

    public int PageWidthPx { get; set; }

    public int PageHeightPx { get; set; }

    public int MarginPx { get; set; }

    public string PageSizeLabel { get; set; } = "A5";

    public string BackgroundColor { get; set; } = "#FFFFFF";

    public string AccentColor { get; set; } = "#0B6E4F";

    public bool ShowAppLogo { get; set; } = true;

    public bool ShowRestaurantLogo { get; set; } = true;

    public string LayoutJson { get; set; } = "[]";

    public int Version { get; set; } = 1;

    public DateTime? PublishedAtUtc { get; set; }
}
