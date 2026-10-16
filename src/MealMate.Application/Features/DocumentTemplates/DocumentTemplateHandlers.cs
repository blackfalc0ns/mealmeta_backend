using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DocumentTemplates;

internal static class DocumentTemplateMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static DocumentTemplateDto ToDto(DocumentTemplate entity)
    {
        var elements = ParseElements(entity.LayoutJson);
        return new DocumentTemplateDto(
            entity.Id,
            entity.Kind,
            entity.Status,
            entity.NameAr,
            entity.NameEn,
            entity.DescriptionAr,
            entity.DescriptionEn,
            entity.Language,
            entity.PageWidthPx,
            entity.PageHeightPx,
            entity.MarginPx,
            entity.PageSizeLabel,
            entity.BackgroundColor,
            entity.AccentColor,
            entity.ShowAppLogo,
            entity.ShowRestaurantLogo,
            entity.Version,
            entity.PublishedAtUtc,
            entity.CreatedAtUtc,
            entity.ModifiedAtUtc ?? entity.CreatedAtUtc,
            entity.ModifiedByUserId?.ToString() ?? string.Empty,
            entity.LayoutJson,
            elements);
    }

    public static IReadOnlyList<TemplateElementDto> ParseElements(string layoutJson)
    {
        if (string.IsNullOrWhiteSpace(layoutJson))
            return Array.Empty<TemplateElementDto>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<TemplateElementDto>>(layoutJson, JsonOptions);
            return parsed is null ? Array.Empty<TemplateElementDto>() : parsed;
        }
        catch (JsonException)
        {
            return Array.Empty<TemplateElementDto>();
        }
    }

    public static string NormalizeLayoutJson(string layoutJson) =>
        string.IsNullOrWhiteSpace(layoutJson) ? "[]" : layoutJson.Trim();
}

internal static class DocumentTemplateSeed
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string MinimalLayout(string appLogoId, string restaurantLogoId) =>
        JsonSerializer.Serialize(new object[]
        {
            new
            {
                id = appLogoId,
                kind = "logo",
                source = "static",
                labelAr = "شعار التطبيق",
                labelEn = "App logo",
                textAr = "mealmate_logo",
                textEn = "mealmate_logo",
                x = 24,
                y = 24,
                width = 80,
                height = 48,
                visible = true,
                locked = false,
                zIndex = 1,
                style = new
                {
                    fontSize = 12,
                    fontWeight = 600,
                    color = "#0f172a",
                    backgroundColor = "#ecfdf5",
                    borderColor = "#e2e8f0",
                    radius = 8,
                    padding = 8,
                    align = "center",
                },
            },
            new
            {
                id = restaurantLogoId,
                kind = "logo",
                source = "restaurant",
                labelAr = "شعار المطعم",
                labelEn = "Restaurant logo",
                textAr = "restaurant_logo",
                textEn = "restaurant_logo",
                x = 110,
                y = 24,
                width = 80,
                height = 48,
                visible = true,
                locked = false,
                zIndex = 2,
                style = new
                {
                    fontSize = 12,
                    fontWeight = 600,
                    color = "#0f172a",
                    backgroundColor = "#ecfdf5",
                    borderColor = "#e2e8f0",
                    radius = 8,
                    padding = 8,
                    align = "center",
                },
            },
        }, JsonOptions);

    public static DocumentTemplate CreateDefault(DocumentTemplateKind kind, DateTime createdAtUtc)
    {
        var (nameAr, nameEn, width, height, pageSize) = kind switch
        {
            DocumentTemplateKind.Invoice => ("فاتورة افتراضية", "Default invoice", 794, 1123, "A4"),
            DocumentTemplateKind.BoxLabel => ("ملصق بوكس افتراضي", "Default box label", 420, 300, "100 x 72 mm"),
            DocumentTemplateKind.MealLabel => ("ملصق وجبة افتراضي", "Default meal label", 300, 520, "meal label"),
            _ => ("قالب افتراضي", "Default template", 420, 300, "default"),
        };

        return new DocumentTemplate
        {
            Kind = kind,
            NameAr = nameAr,
            NameEn = nameEn,
            DescriptionAr = "قالب افتراضي تم إنشاؤه تلقائياً",
            DescriptionEn = "Auto-generated default template",
            Language = "both",
            Status = DocumentTemplateStatus.Draft,
            PageWidthPx = width,
            PageHeightPx = height,
            MarginPx = 24,
            PageSizeLabel = pageSize,
            BackgroundColor = "#FFFFFF",
            AccentColor = "#0B6E4F",
            ShowAppLogo = true,
            ShowRestaurantLogo = true,
            LayoutJson = MinimalLayout("mealmate_logo", "restaurant_logo"),
            Version = 1,
            CreatedAtUtc = createdAtUtc,
        };
    }
}

public sealed class GetDocumentTemplatesQueryHandler : IQueryHandler<GetDocumentTemplatesQuery, IReadOnlyList<DocumentTemplateDto>>
{
    private readonly IAppDbContext _db;

    private readonly IDateTimeProvider _clock;

    public GetDocumentTemplatesQueryHandler(IAppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<DocumentTemplateDto>>> Handle(GetDocumentTemplatesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.DocumentTemplates.AsNoTracking().AsQueryable();
        if (request.Kind.HasValue)
            query = query.Where(x => x.Kind == request.Kind.Value);
        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);

        var items = await query
            .OrderBy(x => x.Kind)
            .ThenByDescending(x => x.Status == DocumentTemplateStatus.Published)
            .ThenByDescending(x => x.ModifiedAtUtc ?? x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (items.Count == 0 && !request.Kind.HasValue && !request.Status.HasValue)
        {
            var seedHandler = new SeedDocumentTemplatesCommandHandler(_db, _clock);
            await seedHandler.Handle(new SeedDocumentTemplatesCommand(), cancellationToken);

            items = await _db.DocumentTemplates.AsNoTracking()
                .OrderBy(x => x.Kind)
                .ThenByDescending(x => x.Status == DocumentTemplateStatus.Published)
                .ThenByDescending(x => x.ModifiedAtUtc ?? x.CreatedAtUtc)
                .ToListAsync(cancellationToken);
        }

        IReadOnlyList<DocumentTemplateDto> mapped = items.Select(DocumentTemplateMapper.ToDto).ToList();
        return Result<IReadOnlyList<DocumentTemplateDto>>.Success(mapped);
    }
}

public sealed class GetDocumentTemplateQueryHandler : IQueryHandler<GetDocumentTemplateQuery, DocumentTemplateDto>
{
    private readonly IAppDbContext _db;

    public GetDocumentTemplateQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<DocumentTemplateDto>> Handle(GetDocumentTemplateQuery request, CancellationToken cancellationToken)
    {
        var entity = await _db.DocumentTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (entity is null)
            return Error.NotFound("document_template.not_found", "Document template was not found.");

        return Result<DocumentTemplateDto>.Success(DocumentTemplateMapper.ToDto(entity));
    }
}

public sealed class CreateDocumentTemplateCommandHandler : ICommandHandler<CreateDocumentTemplateCommand, DocumentTemplateDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public CreateDocumentTemplateCommandHandler(IAppDbContext db, IDateTimeProvider clock)
        => (_db, _clock) = (db, clock);

    public async Task<Result<DocumentTemplateDto>> Handle(CreateDocumentTemplateCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
            return Error.Validation("document_template.names_required", "Arabic and English names are required.");

        var entity = new DocumentTemplate
        {
            Kind = request.Kind,
            NameAr = request.NameAr.Trim(),
            NameEn = request.NameEn.Trim(),
            DescriptionAr = request.DescriptionAr?.Trim(),
            DescriptionEn = request.DescriptionEn?.Trim(),
            Language = string.IsNullOrWhiteSpace(request.Language) ? "both" : request.Language.Trim(),
            Status = DocumentTemplateStatus.Draft,
            PageWidthPx = request.PageWidthPx,
            PageHeightPx = request.PageHeightPx,
            MarginPx = request.MarginPx,
            PageSizeLabel = request.PageSizeLabel.Trim(),
            BackgroundColor = request.BackgroundColor.Trim(),
            AccentColor = request.AccentColor.Trim(),
            ShowAppLogo = request.ShowAppLogo,
            ShowRestaurantLogo = request.ShowRestaurantLogo,
            LayoutJson = DocumentTemplateMapper.NormalizeLayoutJson(request.LayoutJson),
            Version = 1,
            CreatedAtUtc = _clock.UtcNow,
        };

        _db.DocumentTemplates.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return Result<DocumentTemplateDto>.Success(DocumentTemplateMapper.ToDto(entity));
    }
}

public sealed class UpdateDocumentTemplateCommandHandler : ICommandHandler<UpdateDocumentTemplateCommand, DocumentTemplateDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public UpdateDocumentTemplateCommandHandler(IAppDbContext db, IDateTimeProvider clock)
        => (_db, _clock) = (db, clock);

    public async Task<Result<DocumentTemplateDto>> Handle(UpdateDocumentTemplateCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.DocumentTemplates.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (entity is null)
            return Error.NotFound("document_template.not_found", "Document template was not found.");

        if (string.IsNullOrWhiteSpace(request.NameAr) || string.IsNullOrWhiteSpace(request.NameEn))
            return Error.Validation("document_template.names_required", "Arabic and English names are required.");

        entity.NameAr = request.NameAr.Trim();
        entity.NameEn = request.NameEn.Trim();
        entity.DescriptionAr = request.DescriptionAr?.Trim();
        entity.DescriptionEn = request.DescriptionEn?.Trim();
        entity.Language = string.IsNullOrWhiteSpace(request.Language) ? entity.Language : request.Language.Trim();
        entity.PageWidthPx = request.PageWidthPx;
        entity.PageHeightPx = request.PageHeightPx;
        entity.MarginPx = request.MarginPx;
        entity.PageSizeLabel = request.PageSizeLabel.Trim();
        entity.BackgroundColor = request.BackgroundColor.Trim();
        entity.AccentColor = request.AccentColor.Trim();
        entity.ShowAppLogo = request.ShowAppLogo;
        entity.ShowRestaurantLogo = request.ShowRestaurantLogo;
        entity.LayoutJson = DocumentTemplateMapper.NormalizeLayoutJson(request.LayoutJson);
        entity.ModifiedAtUtc = _clock.UtcNow;
        if (entity.Status == DocumentTemplateStatus.Published)
            entity.Version += 1;

        await _db.SaveChangesAsync(cancellationToken);

        return Result<DocumentTemplateDto>.Success(DocumentTemplateMapper.ToDto(entity));
    }
}

public sealed class PublishDocumentTemplateCommandHandler : ICommandHandler<PublishDocumentTemplateCommand, DocumentTemplateDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public PublishDocumentTemplateCommandHandler(IAppDbContext db, IDateTimeProvider clock)
        => (_db, _clock) = (db, clock);

    public async Task<Result<DocumentTemplateDto>> Handle(PublishDocumentTemplateCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.DocumentTemplates.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (entity is null)
            return Error.NotFound("document_template.not_found", "Document template was not found.");

        var publishedPeers = await _db.DocumentTemplates
            .Where(x => x.Kind == entity.Kind && x.Status == DocumentTemplateStatus.Published && x.Id != entity.Id)
            .ToListAsync(cancellationToken);

        foreach (var peer in publishedPeers)
        {
            peer.Status = DocumentTemplateStatus.Archived;
            peer.ModifiedAtUtc = _clock.UtcNow;
        }

        entity.Status = DocumentTemplateStatus.Published;
        entity.PublishedAtUtc = _clock.UtcNow;
        entity.Version += 1;
        entity.ModifiedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Result<DocumentTemplateDto>.Success(DocumentTemplateMapper.ToDto(entity));
    }
}

public sealed class ArchiveDocumentTemplateCommandHandler : ICommandHandler<ArchiveDocumentTemplateCommand, DocumentTemplateDto>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public ArchiveDocumentTemplateCommandHandler(IAppDbContext db, IDateTimeProvider clock)
        => (_db, _clock) = (db, clock);

    public async Task<Result<DocumentTemplateDto>> Handle(ArchiveDocumentTemplateCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.DocumentTemplates.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (entity is null)
            return Error.NotFound("document_template.not_found", "Document template was not found.");

        entity.Status = DocumentTemplateStatus.Archived;
        entity.ModifiedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Result<DocumentTemplateDto>.Success(DocumentTemplateMapper.ToDto(entity));
    }
}

public sealed class SeedDocumentTemplatesCommandHandler : ICommandHandler<SeedDocumentTemplatesCommand, IReadOnlyList<DocumentTemplateDto>>
{
    private readonly IAppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SeedDocumentTemplatesCommandHandler(IAppDbContext db, IDateTimeProvider clock)
        => (_db, _clock) = (db, clock);

    public async Task<Result<IReadOnlyList<DocumentTemplateDto>>> Handle(SeedDocumentTemplatesCommand request, CancellationToken cancellationToken)
    {
        var touched = new List<DocumentTemplateDto>();
        var now = _clock.UtcNow;
        var packs = DocumentTemplateProfessionalLayouts.AllPacks();
        var keepNames = DocumentTemplateProfessionalLayouts.PackNameEns
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Wipe previous designs: archive anything outside the production pack.
        var obsolete = await _db.DocumentTemplates
            .Where(x => x.Status != DocumentTemplateStatus.Archived && !keepNames.Contains(x.NameEn))
            .ToListAsync(cancellationToken);
        foreach (var item in obsolete)
        {
            item.Status = DocumentTemplateStatus.Archived;
            item.ModifiedAtUtc = now;
        }

        if (obsolete.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        foreach (var pack in packs)
        {
            var entity = await _db.DocumentTemplates
                .FirstOrDefaultAsync(x => x.NameEn == pack.NameEn && x.Kind == pack.Kind, cancellationToken);

            if (entity is null)
            {
                entity = DocumentTemplateProfessionalLayouts.ToEntity(pack, now);
                _db.DocumentTemplates.Add(entity);
            }
            else
            {
                entity.NameAr = pack.NameAr;
                entity.DescriptionAr = pack.DescriptionAr;
                entity.DescriptionEn = pack.DescriptionEn;
                entity.PageWidthPx = pack.WidthPx;
                entity.PageHeightPx = pack.HeightPx;
                entity.MarginPx = pack.MarginPx;
                entity.PageSizeLabel = pack.PageSizeLabel;
                entity.BackgroundColor = "#FFFFFF";
                entity.AccentColor = pack.AccentColor;
                entity.ShowAppLogo = true;
                entity.ShowRestaurantLogo = true;
                entity.Language = "both";
                entity.LayoutJson = pack.LayoutJson;
                entity.ModifiedAtUtc = now;
            }

            await _db.SaveChangesAsync(cancellationToken);

            if (pack.Publish)
            {
                var peers = await _db.DocumentTemplates
                    .Where(x => x.Kind == pack.Kind && x.Status == DocumentTemplateStatus.Published && x.Id != entity.Id)
                    .ToListAsync(cancellationToken);
                foreach (var peer in peers)
                {
                    peer.Status = DocumentTemplateStatus.Archived;
                    peer.ModifiedAtUtc = now;
                }

                if (entity.Status != DocumentTemplateStatus.Published)
                {
                    entity.Status = DocumentTemplateStatus.Published;
                    entity.PublishedAtUtc = now;
                    entity.Version = Math.Max(1, entity.Version);
                }
                else
                {
                    entity.Version += 1;
                }

                entity.ModifiedAtUtc = now;
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                if (entity.Status == DocumentTemplateStatus.Published || entity.Status == DocumentTemplateStatus.Archived)
                {
                    entity.Status = DocumentTemplateStatus.Draft;
                    entity.ModifiedAtUtc = now;
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            touched.Add(DocumentTemplateMapper.ToDto(entity));
        }

        IReadOnlyList<DocumentTemplateDto> seeded = touched;
        return Result<IReadOnlyList<DocumentTemplateDto>>.Success(seeded);
    }
}
