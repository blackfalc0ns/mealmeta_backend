using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ReceiptPrinting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[HasPermission(PermissionCodes.SettingsManage)]
[Route("api/v1/admin/receipt-templates")]
public sealed class AdminReceiptTemplatesController : ApiControllerBase
{
    private readonly IReceiptPrintingService _receiptService;

    public AdminReceiptTemplatesController(IReceiptPrintingService receiptService)
    {
        _receiptService = receiptService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var templates = await _receiptService.GetAllTemplatesForAdminAsync(ct);
        return Ok(templates);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var template = await _receiptService.GetTemplateByIdAsync(id, ct);
        if (template is null)
        {
            return NotFound(new { code = "receipt.template.not_found", message = "Receipt template not found." });
        }
        return Ok(template);
    }

    [HttpPut("{id:guid}/configuration")]
    public async Task<IActionResult> UpdateConfiguration(
        Guid id,
        [FromBody] UpdateReceiptTemplateConfigRequest request,
        CancellationToken ct)
    {
        return HandleResult(await _receiptService.UpdateConfigurationAsync(id, request.ConfigurationJson, ct));
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        return HandleResult(await _receiptService.PublishTemplateAsync(id, ct));
    }

    [HttpPut("{id:guid}/visibility")]
    public async Task<IActionResult> UpdateVisibility(
        Guid id,
        [FromBody] UpdateReceiptTemplateVisibilityRequest request,
        CancellationToken ct)
    {
        return HandleResult(await _receiptService.UpdateVisibilityAsync(id, request.IsEnabled, request.IsRestaurantVisible, ct));
    }

    [HttpPut("default")]
    [HttpPut("{id:guid}/default")]
    public async Task<IActionResult> SetGlobalDefault(
        Guid? id,
        [FromBody] SetGlobalDefaultTemplateRequest? request,
        CancellationToken ct)
    {
        var targetId = id ?? request?.TemplateId ?? Guid.Empty;
        return HandleResult(await _receiptService.SetGlobalDefaultTemplateAsync(targetId, ct));
    }

    [HttpGet("preview")]
    public async Task<IActionResult> GetPreview(
        [FromQuery] Guid? templateId = null,
        [FromQuery] Guid? restaurantId = null,
        CancellationToken ct = default)
    {
        return HandleResult(await _receiptService.GetPreviewOperationalReceiptAsync(templateId, restaurantId, ct));
    }

    [HttpGet("restaurants/{restaurantId:guid}/settings")]
    public async Task<IActionResult> GetRestaurantSettings(Guid restaurantId, CancellationToken ct)
    {
        var settings = await _receiptService.GetRestaurantSettingsAsync(restaurantId, ct);
        return Ok(settings);
    }

    [HttpPut("restaurants/{restaurantId:guid}/settings")]
    public async Task<IActionResult> UpdateRestaurantSettings(
        Guid restaurantId,
        [FromBody] AdminUpdateRestaurantSettingsRequest request,
        CancellationToken ct)
    {
        return HandleResult(await _receiptService.AdminUpdateRestaurantSettingsAsync(
            restaurantId,
            request.AdminAssignedTemplateId,
            request.AllowRestaurantSelection,
            ct));
    }
}
