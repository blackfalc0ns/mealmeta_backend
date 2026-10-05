using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerNotifications;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// Customer in-app notification inbox (mobile bell). Listing never marks anything read;
/// reading is an explicit PUT by the owner.
/// </summary>
[Authorize]
[Route("api/v1/customers/me/notifications")]
public sealed class CustomerNotificationsController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;

    public CustomerNotificationsController(ICurrentUser currentUser) => _currentUser = currentUser;

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CustomerNotificationItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetCustomerNotificationsQuery(page, pageSize), cancellationToken));
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new MarkCustomerNotificationReadCommand(id), cancellationToken));
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new MarkAllCustomerNotificationsReadCommand(), cancellationToken));
    }
}
