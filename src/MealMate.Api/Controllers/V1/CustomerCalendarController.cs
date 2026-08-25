using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MealMate.Api.Controllers.V1;

/// <summary>Mobile individual-subscription calendar: add box, pick restaurant, fill slots, complete, auto-select.</summary>
[Authorize]
[Route("api/v1/customer/subscriptions/{subscriptionId:guid}")]
public sealed class CustomerCalendarController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;

    public CustomerCalendarController(ICurrentUser currentUser) => _currentUser = currentUser;

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;

    [HttpGet("calendar")]
    [ProducesResponseType(typeof(CustomerCalendarScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCalendar(Guid subscriptionId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetCustomerCalendarQuery(subscriptionId), cancellationToken));
    }

    [HttpGet("days/{date}/restaurants")]
    [ProducesResponseType(typeof(CustomerDayRestaurantsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDayRestaurants(Guid subscriptionId, DateOnly date, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetCustomerDayRestaurantsQuery(subscriptionId, date), cancellationToken));
    }

    [HttpGet("days/{date}/restaurants/{restaurantId:guid}/meals")]
    [ProducesResponseType(typeof(CustomerDayRestaurantMealsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRestaurantMeals(
        Guid subscriptionId, DateOnly date, Guid restaurantId, [FromQuery] string? slot, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetCustomerDayRestaurantMealsQuery(subscriptionId, date, restaurantId, slot), cancellationToken));
    }

    [HttpPost("days/{date}/restaurant")]
    [ProducesResponseType(typeof(CustomerCalendarDayDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SelectRestaurant(
        Guid subscriptionId, DateOnly date, [FromBody] SelectDayRestaurantRequest request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new SelectCustomerDayRestaurantCommand(subscriptionId, date, request.RestaurantId), cancellationToken));
    }

    [HttpPost("days/{date}/meals/slot")]
    [ProducesResponseType(typeof(CustomerCalendarDayDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SelectSlotMeal(
        Guid subscriptionId, DateOnly date, [FromBody] SelectDaySlotMealRequest request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(
            new SelectCustomerDaySlotMealCommand(subscriptionId, date, request.MealId, request.Slot), cancellationToken));
    }

    [HttpPost("days/{date}/complete")]
    [ProducesResponseType(typeof(CustomerCalendarDayDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CompleteBox(
        Guid subscriptionId, DateOnly date, [FromBody] CompleteDayBoxRequest? request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        var idempotency = request?.IdempotencyKey ?? Request.Headers["Idempotency-Key"].FirstOrDefault();
        return HandleResult(await Sender.Send(new CompleteCustomerDayBoxCommand(subscriptionId, date, idempotency), cancellationToken));
    }

    [HttpPost("days/{date}/auto-select")]
    [ProducesResponseType(typeof(CustomerCalendarDayDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AutoSelect(Guid subscriptionId, DateOnly date, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new AutoSelectCustomerDayBoxCommand(subscriptionId, date), cancellationToken));
    }
}
