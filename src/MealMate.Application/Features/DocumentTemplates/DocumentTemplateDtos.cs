using MealMate.Application.Common.Messaging;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.DocumentTemplates;

public sealed record TemplateElementStyleDto(
    int FontSize,
    int FontWeight,
    string Color,
    string BackgroundColor,
    string BorderColor,
    int Radius,
    int Padding,
    string Align);

public sealed record TemplateElementDto(
    string Id,
    string Kind,
    string Source,
    string LabelAr,
    string LabelEn,
    string TextAr,
    string TextEn,
    int X,
    int Y,
    int Width,
    int Height,
    bool Visible,
    bool Locked,
    int ZIndex,
    TemplateElementStyleDto Style);

public sealed record DocumentTemplateDto(
    Guid Id,
    DocumentTemplateKind Kind,
    DocumentTemplateStatus Status,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string Language,
    int WidthPx,
    int HeightPx,
    int MarginPx,
    string PageSizeLabel,
    string BackgroundColor,
    string AccentColor,
    bool ShowAppLogo,
    bool ShowRestaurantLogo,
    int Version,
    DateTime? PublishedAtUtc,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAt,
    string UpdatedBy,
    string LayoutJson,
    IReadOnlyList<TemplateElementDto> Elements);

public sealed record GetDocumentTemplatesQuery(
    DocumentTemplateKind? Kind,
    DocumentTemplateStatus? Status) : IQuery<IReadOnlyList<DocumentTemplateDto>>;

public sealed record GetDocumentTemplateQuery(Guid Id) : IQuery<DocumentTemplateDto>;

public sealed record CreateDocumentTemplateCommand(
    DocumentTemplateKind Kind,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string Language,
    int PageWidthPx,
    int PageHeightPx,
    int MarginPx,
    string PageSizeLabel,
    string BackgroundColor,
    string AccentColor,
    bool ShowAppLogo,
    bool ShowRestaurantLogo,
    string LayoutJson) : ICommand<DocumentTemplateDto>;

public sealed record UpdateDocumentTemplateCommand(
    Guid Id,
    string NameAr,
    string NameEn,
    string? DescriptionAr,
    string? DescriptionEn,
    string Language,
    int PageWidthPx,
    int PageHeightPx,
    int MarginPx,
    string PageSizeLabel,
    string BackgroundColor,
    string AccentColor,
    bool ShowAppLogo,
    bool ShowRestaurantLogo,
    string LayoutJson) : ICommand<DocumentTemplateDto>;

public sealed record PublishDocumentTemplateCommand(Guid Id) : ICommand<DocumentTemplateDto>;

public sealed record ArchiveDocumentTemplateCommand(Guid Id) : ICommand<DocumentTemplateDto>;

public sealed record SeedDocumentTemplatesCommand() : ICommand<IReadOnlyList<DocumentTemplateDto>>;
