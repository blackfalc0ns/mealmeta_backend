using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ReceiptPrinting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

[Authorize(Policy = "RestaurantStaffPolicy")]
[Route("api/v1/restaurant/receipt")]
[Route("api/restaurant/receipt")]
[Route("api/v1/restaurants/receipt")]
public sealed class RestaurantReceiptController : ApiControllerBase
{
    private readonly IReceiptPrintingService _receiptService;
    private readonly ICurrentUser _currentUser;

    public RestaurantReceiptController(
        IReceiptPrintingService receiptService,
        ICurrentUser currentUser)
    {
        _receiptService = receiptService;
        _currentUser = currentUser;
    }

    [HttpGet("templates")]
    public async Task<IActionResult> GetAllowedTemplates(CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
        {
            return Forbid();
        }

        var templates = await _receiptService.GetAllowedTemplatesForRestaurantAsync(_currentUser.RestaurantId.Value, ct);
        return Ok(templates);
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
        {
            return Forbid();
        }

        var settings = await _receiptService.GetRestaurantSettingsAsync(_currentUser.RestaurantId.Value, ct);
        return Ok(settings);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SelectTemplate(
        [FromBody] SelectRestaurantTemplateRequest request,
        CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
        {
            return Forbid();
        }

        return HandleResult(await _receiptService.RestaurantSelectTemplateAsync(
            _currentUser.RestaurantId.Value,
            request.SelectedTemplateId,
            ct));
    }

    [HttpGet("orders/{orderId:guid}")]
    public async Task<IActionResult> GetOrderReceipt(Guid orderId, CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
        {
            return Forbid();
        }

        return HandleResult(await _receiptService.GetOperationalReceiptAsync(
            orderId,
            _currentUser.RestaurantId.Value,
            ct));
    }

    [HttpPost("orders/{orderId:guid}/print-log")]
    public async Task<IActionResult> LogPrint(
        Guid orderId,
        [FromBody] LogReceiptPrintRequest request,
        CancellationToken ct)
    {
        if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
        {
            return Forbid();
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userId = _currentUser.UserId ?? Guid.Empty;

        return HandleResult(await _receiptService.LogPrintAsync(
            orderId,
            _currentUser.RestaurantId.Value,
            request.TemplateId,
            request.TemplateVersion,
            userId,
            clientIp,
            request.PrintStatus,
            request.Notes,
            ct));
    }
}
