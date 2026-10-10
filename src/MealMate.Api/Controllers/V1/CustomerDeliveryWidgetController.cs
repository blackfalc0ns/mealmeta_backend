using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DriverDelivery.CustomerTracking;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// Standalone, read-only customer delivery widget: all boxes visible to the customer for
/// one business date, with the shared delivery status used by the calendar. Filters are
/// optional; an omitted serviceDate means today in the business timezone.
/// </summary>
[ApiController]
[Route("api/v1/customer")]
[Authorize]
public sealed class CustomerDeliveryWidgetController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;

    public CustomerDeliveryWidgetController(ICurrentUser currentUser) => _currentUser = currentUser;

    /// <summary>
    /// Returns the delivery widget cards for the requested business date (default: today).
    /// Read-only: never prepares, prints, picks up, delivers or repairs anything.
    /// </summary>
    [HttpGet("delivery-widget")]
    [ProducesResponseType(typeof(CustomerDeliveryWidgetResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeliveryWidget(
        [FromQuery] DateOnly? serviceDate,
        [FromQuery] Guid? subscriptionId,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserType != UserType.Customer || !_currentUser.UserId.HasValue)
        {
            return Forbid();
        }

        return HandleResult(await Sender.Send(
            new GetCustomerDeliveryWidgetQuery(serviceDate, subscriptionId), cancellationToken));
    }
}
