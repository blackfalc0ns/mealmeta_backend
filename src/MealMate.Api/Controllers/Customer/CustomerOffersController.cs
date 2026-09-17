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

    [ActivatorUtilitiesConstructor]
    public CustomerOffersController(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }

    public CustomerOffersController() { }

    private ICurrentUser CurrentUser => _currentUser ??= HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    [AllowAnonymous]
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailableOffers(
        [FromQuery] decimal cartAmount = 0m,
        [FromQuery] int durationDays = 0,
        [FromQuery] string? deliveryGovernorate = null,
        [FromQuery] Guid? programId = null,
        [FromQuery] Guid? bundleId = null,
        [FromQuery] Guid? restaurantId = null,
        CancellationToken cancellationToken = default)
    {
        var customerId = ResolveCustomerId();

        var query = new GetCustomerAvailableOffersQuery(
            CustomerId: customerId,
            CartAmount: cartAmount,
            DurationDays: durationDays,
            DeliveryGovernorate: deliveryGovernorate,
            ProgramId: programId,
            BundleId: bundleId,
            RestaurantId: restaurantId
        );

        var result = await Sender.Send(query, cancellationToken);
        return HandleResult(result);
    }

    [AllowAnonymous]
    [HttpPost("available")]
    public async Task<IActionResult> GetAvailableOffersPost(
        [FromBody] GetCustomerAvailableOffersQuery? bodyQuery,
        CancellationToken cancellationToken = default)
    {
        var customerId = ResolveCustomerId(bodyQuery?.CustomerId);

        var query = (bodyQuery ?? new GetCustomerAvailableOffersQuery()) with { CustomerId = customerId };

        var result = await Sender.Send(query, cancellationToken);
        return HandleResult(result);
    }

    private Guid ResolveCustomerId(Guid? fallbackId = null)
    {
        try
        {
            if (CurrentUser.IsAuthenticated && CurrentUser.UserType == UserType.Customer && CurrentUser.UserId.HasValue)
            {
                return CurrentUser.UserId.Value;
            }
        }
        catch
        {
            // Safe fallback for unauthenticated calls
        }

        return (fallbackId.HasValue && fallbackId.Value != Guid.Empty) ? fallbackId.Value : Guid.Empty;
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
