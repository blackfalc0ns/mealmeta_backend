using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.DocumentTemplates;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/admin/operations/document-templates")]
public sealed class AdminDocumentTemplatesController : ApiControllerBase
{
    [HttpGet]
    [HasPermission(PermissionCodes.DocumentsView)]
    public async Task<IActionResult> List(
        [FromQuery] DocumentTemplateKind? kind,
        [FromQuery] DocumentTemplateStatus? status,
        CancellationToken ct)
        => HandleResult(await Sender.Send(new GetDocumentTemplatesQuery(kind, status), ct));

    [HttpPost]
    [HasPermission(PermissionCodes.SettingsManage)]
    public async Task<IActionResult> Create([FromBody] CreateDocumentTemplateRequest body, CancellationToken ct)
    {
        var result = await Sender.Send(new CreateDocumentTemplateCommand(
            body.Kind,
            body.NameAr,
            body.NameEn,
            body.DescriptionAr,
            body.DescriptionEn,
            body.Language,
            body.PageWidthPx,
            body.PageHeightPx,
            body.MarginPx,
            body.PageSizeLabel,
            body.BackgroundColor,
            body.AccentColor,
            body.ShowAppLogo,
            body.ShowRestaurantLogo,
            body.LayoutJson), ct);

        return HandleCreated(result, $"{Request.Path}/{result.Value?.Id}");
    }

    [HttpGet("{id:guid}")]
    [HasPermission(PermissionCodes.DocumentsView)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => HandleResult(await Sender.Send(new GetDocumentTemplateQuery(id), ct));

    [HttpPut("{id:guid}")]
    [HasPermission(PermissionCodes.SettingsManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDocumentTemplateRequest body, CancellationToken ct)
        => HandleResult(await Sender.Send(new UpdateDocumentTemplateCommand(
            id,
            body.NameAr,
            body.NameEn,
            body.DescriptionAr,
            body.DescriptionEn,
            body.Language,
            body.PageWidthPx,
            body.PageHeightPx,
            body.MarginPx,
            body.PageSizeLabel,
            body.BackgroundColor,
            body.AccentColor,
            body.ShowAppLogo,
            body.ShowRestaurantLogo,
            body.LayoutJson), ct));

    [HttpPost("{id:guid}/publish")]
    [HasPermission(PermissionCodes.SettingsManage)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
        => HandleResult(await Sender.Send(new PublishDocumentTemplateCommand(id), ct));

    [HttpPost("{id:guid}/archive")]
    [HasPermission(PermissionCodes.SettingsManage)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
        => HandleResult(await Sender.Send(new ArchiveDocumentTemplateCommand(id), ct));

    [HttpPost("seed-defaults")]
    [HasPermission(PermissionCodes.SettingsManage)]
    public async Task<IActionResult> SeedDefaults(CancellationToken ct)
        => HandleResult(await Sender.Send(new SeedDocumentTemplatesCommand(), ct));
}

public sealed record CreateDocumentTemplateRequest(
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
    string LayoutJson);

public sealed record UpdateDocumentTemplateRequest(
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
    string LayoutJson);
