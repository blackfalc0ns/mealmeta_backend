using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCompensations;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// Customer-facing compensation workflow for operational service failures (unfulfilled boxes, kitchen overload, timeouts).
/// Supports:
/// 1. 48-hour customer choice window between CashRefund and AlternativeDay.
/// 2. Detailed refund breakdown (Card vs. Wallet vs. Apology Credit).
/// 3. Retrieval of eligible alternative dates honoring the 72h operational lead time and restaurant capacity.
/// 4. Rescheduling independent delivery commitments without consuming subscription days.
/// </summary>
[Authorize]
[Route("api/v1/customer/compensations")]
public sealed class CustomerCompensationsController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerCompensationService _compensationService;

    public CustomerCompensationsController(
        ICurrentUser currentUser,
        ICustomerCompensationService compensationService)
    {
        _currentUser = currentUser;
        _compensationService = compensationService;
    }

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerCompensationSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCompensations(CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();

        var result = await _compensationService.GetCompensationsAsync(_currentUser.UserId!.Value, ct);
        return ToHttpResult(result);
    }

    [HttpGet("{compensationId:guid}/alternative-dates")]
    [ProducesResponseType(typeof(IReadOnlyList<EligibleAlternativeDateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAlternativeDates(
        Guid compensationId,
        [FromQuery] int count = 14,
        CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();

        var result = await _compensationService.GetAlternativeDatesAsync(
            compensationId,
            _currentUser.UserId!.Value,
            count,
            ct);

        return ToHttpResult(result);
    }

    [HttpPost("{compensationId:guid}/choose")]
    [ProducesResponseType(typeof(ChooseCompensationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Choose(
        Guid compensationId,
        [FromBody] ChooseCompensationRequest request,
        CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();

        var result = await _compensationService.ChooseCompensationAsync(
            compensationId,
            _currentUser.UserId!.Value,
            request,
            ct);

        return ToHttpResult(result);
    }

    private IActionResult ToHttpResult<T>(Result<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);

        var error = result.Error;

        if (error.Code == "FORBIDDEN")
            return Forbid();

        if (error.Code == "COMPENSATION_NOT_FOUND")
            return NotFound(new { error = error.Code, message = error.Description });

        if (error.Code is "COMPENSATION_CHOICE_WINDOW_EXPIRED" or "COMPENSATION_ALREADY_DECIDED" or "CAPACITY_UNAVAILABLE")
            return Conflict(new { error = error.Code, message = error.Description });

        return BadRequest(new { error = error.Code, message = error.Description });
    }
}
