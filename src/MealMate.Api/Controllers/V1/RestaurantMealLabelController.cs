using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.MealLabels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize(Policy = "RestaurantStaffPolicy")]
[Route("api/v1/restaurant/meal-labels")]
[Route("api/restaurant/meal-labels")]
[Route("api/v1/restaurants/meal-labels")]
public sealed class RestaurantMealLabelController : ApiControllerBase
{
    private readonly IMealLabelService _labelService;
    private readonly ICurrentUser _currentUser;

    public RestaurantMealLabelController(
        IMealLabelService labelService,
        ICurrentUser currentUser)
    {
        _labelService = labelService;
        _currentUser = currentUser;
    }

    [HttpGet("templates")]
    public async Task<IActionResult> GetAllowedTemplates(CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
        {
            return Forbid();
        }

        var templates = await _labelService.GetAllowedTemplatesForRestaurantAsync(_currentUser.RestaurantId.Value, ct);
        return Ok(templates);
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
        {
            return Forbid();
        }

        var settings = await _labelService.GetRestaurantSettingsAsync(_currentUser.RestaurantId.Value, ct);
        return Ok(settings);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SelectTemplate(
        [FromBody] RestaurantSelectMealLabelTemplateRequest request,
        CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
        {
            return Forbid();
        }

        return HandleResult(await _labelService.RestaurantSelectTemplateAsync(
            _currentUser.RestaurantId.Value,
            request,
            ct));
    }

    [HttpGet("orders/{orderId:guid}")]
    public async Task<IActionResult> GetOrderMealLabels(Guid orderId, CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
        {
            return Forbid();
        }

        return HandleResult(await _labelService.GetOrderMealLabelsAsync(
            orderId,
            _currentUser.RestaurantId.Value,
            ct));
    }

    [HttpGet("preview")]
    public async Task<IActionResult> GetPreview(
        [FromQuery] Guid? templateId = null,
        [FromQuery] Guid? mealId = null,
        CancellationToken ct = default)
    {
        if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
        {
            return Forbid();
        }

        return HandleResult(await _labelService.GetPreviewMealLabelAsync(
            templateId,
            _currentUser.RestaurantId.Value,
            mealId,
            ct));
    }

    [HttpPost("orders/{orderId:guid}/print-log")]
    public async Task<IActionResult> LogPrint(
        Guid orderId,
        [FromBody] LogMealLabelPrintRequest request,
        [FromQuery] Guid? mealId = null,
        CancellationToken ct = default)
    {
        if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
        {
            return Forbid();
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userId = _currentUser.UserId ?? Guid.Empty;

        return HandleResult(await _labelService.LogPrintAsync(
            _currentUser.RestaurantId.Value,
            orderId,
            mealId,
            request.TemplateId,
            request.TemplateVersion,
            userId,
            clientIp,
            request.PrintStatus,
            request.Notes,
            ct));
    }
}
