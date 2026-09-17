using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Offers.Commands;
using MealMate.Application.Features.Offers.Queries;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.Customer;

[Route("api/v1/customer/offers")]
public sealed class CustomerOffersController : ApiControllerBase
{
    private ICurrentUser? _currentUser;

    public CustomerOffersController() { }

    public CustomerOffersController(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    private ICurrentUser CurrentUser => _currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    [AllowAnonymous]
    [HttpGet("available")]
    [HttpPost("available")]
    public async Task<IActionResult> GetAvailableOffers(
        [FromBody] GetCustomerAvailableOffersQuery? bodyQuery,
        [FromQuery] decimal? cartAmount,
        [FromQuery] int? durationDays,
        [FromQuery] string? deliveryGovernorate,
        [FromQuery] Guid? programId,
        [FromQuery] Guid? bundleId,
        [FromQuery] Guid? restaurantId,
        CancellationToken cancellationToken)
    {
        var customerId = (CurrentUser.IsAuthenticated && CurrentUser.UserType == UserType.Customer && CurrentUser.UserId.HasValue)
            ? CurrentUser.UserId.Value
            : (bodyQuery?.CustomerId != null && bodyQuery.CustomerId != Guid.Empty ? bodyQuery.CustomerId : Guid.Empty);

        var query = new GetCustomerAvailableOffersQuery(
            CustomerId: customerId,
            CartAmount: bodyQuery?.CartAmount ?? cartAmount ?? 0m,
            DurationDays: bodyQuery?.DurationDays ?? durationDays ?? 0,
            DeliveryGovernorate: bodyQuery?.DeliveryGovernorate ?? deliveryGovernorate,
            ProgramId: bodyQuery?.ProgramId ?? programId,
            BundleId: bodyQuery?.BundleId ?? bundleId,
            RestaurantId: bodyQuery?.RestaurantId ?? restaurantId
        );

        var result = await Sender.Send(query, cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpPost("redeem")]
    public async Task<IActionResult> Redeem([FromBody] RedeemOfferCommand command, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        if (command.CustomerId != CurrentUser.UserId.Value)
        {
            command = command with { CustomerId = CurrentUser.UserId.Value };
        }

        var result = await Sender.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate([FromBody] EvaluateOfferDiscountQuery query, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        if (query.CustomerId != CurrentUser.UserId.Value)
        {
            query = query with { CustomerId = CurrentUser.UserId.Value };
        }

        var result = await Sender.Send(query, cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetOfferByIdQuery(id), cancellationToken);
        return HandleResult(result);
    }
}
