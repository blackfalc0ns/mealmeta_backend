using MealMate.Api.Authorization;
using MealMate.Api.Common;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.MealLabels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[HasPermission(PermissionCodes.SettingsManage)]
[Route("api/v1/admin/meal-label-templates")]
[Route("api/admin/meal-label-templates")]
public sealed class AdminMealLabelTemplatesController : ApiControllerBase
{
    private readonly IMealLabelService _labelService;

    public AdminMealLabelTemplatesController(IMealLabelService labelService)
    {
        _labelService = labelService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var templates = await _labelService.GetAllTemplatesForAdminAsync(ct);
        return Ok(templates);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var template = await _labelService.GetTemplateByIdAsync(id, ct);
        if (template is null)
        {
            return NotFound(new { code = "meal_label.template.not_found", message = "Meal box label template not found." });
        }
        return Ok(template);
    }

    [HttpPut("{id:guid}/configuration")]
    public async Task<IActionResult> UpdateConfiguration(
        Guid id,
        [FromBody] UpdateMealLabelTemplateConfigRequest request,
        CancellationToken ct)
    {
        return HandleResult(await _labelService.UpdateConfigurationAsync(id, request.ConfigurationJson, ct));
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        return HandleResult(await _labelService.PublishTemplateAsync(id, ct));
    }

    [HttpPut("{id:guid}/visibility")]
    public async Task<IActionResult> UpdateVisibility(
        Guid id,
        [FromBody] UpdateMealLabelTemplateVisibilityRequest request,
        CancellationToken ct)
    {
        return HandleResult(await _labelService.UpdateVisibilityAsync(id, request.IsEnabled, request.IsRestaurantVisible, ct));
    }

    [HttpPut("default")]
    [HttpPut("{id:guid}/default")]
    public async Task<IActionResult> SetGlobalDefault(
        Guid? id,
        [FromBody] SetGlobalDefaultMealLabelTemplateRequest? request,
        CancellationToken ct)
    {
        var targetId = id ?? request?.TemplateId ?? Guid.Empty;
        return HandleResult(await _labelService.SetGlobalDefaultTemplateAsync(targetId, ct));
    }

    [HttpGet("preview")]
    public async Task<IActionResult> GetPreview(
        [FromQuery] Guid? templateId = null,
        [FromQuery] Guid? restaurantId = null,
        [FromQuery] Guid? mealId = null,
        CancellationToken ct = default)
    {
        return HandleResult(await _labelService.GetPreviewMealLabelAsync(templateId, restaurantId, mealId, ct));
    }

    [HttpGet("restaurants/{restaurantId:guid}/settings")]
    public async Task<IActionResult> GetRestaurantSettings(Guid restaurantId, CancellationToken ct)
    {
        var settings = await _labelService.GetRestaurantSettingsAsync(restaurantId, ct);
        return Ok(settings);
    }

    [HttpPut("restaurants/{restaurantId:guid}/settings")]
    public async Task<IActionResult> UpdateRestaurantSettings(
        Guid restaurantId,
        [FromBody] AdminUpdateRestaurantMealLabelSettingsRequest request,
        CancellationToken ct)
    {
        return HandleResult(await _labelService.AdminUpdateRestaurantSettingsAsync(restaurantId, request, ct));
    }
}
