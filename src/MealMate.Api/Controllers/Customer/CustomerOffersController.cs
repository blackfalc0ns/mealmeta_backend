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

    /// <summary>
    /// Offers hub for the mobile client: the same visible offers as GET available
    /// plus the authoritative server clock. The response shape is additive — the
    /// legacy /available contract stays untouched.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("hub")]
    public async Task<IActionResult> GetOffersHub(
        [FromQuery] decimal cartAmount = 0m,
        [FromQuery] int durationDays = 0,
        [FromQuery] string? deliveryGovernorate = null,
        [FromQuery] Guid? programId = null,
        [FromQuery] Guid? bundleId = null,
        [FromQuery] Guid? restaurantId = null,
        CancellationToken cancellationToken = default)
    {
        var customerId = ResolveCustomerId();

        var result = await Sender.Send(new GetCustomerOffersHubQuery(
            CustomerId: customerId,
            CartAmount: cartAmount,
            DurationDays: durationDays,
            DeliveryGovernorate: deliveryGovernorate,
            ProgramId: programId,
            BundleId: bundleId,
            RestaurantId: restaurantId
        ), cancellationToken);
        return HandleResult(result);
    }

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
    [HttpGet("{offerId:guid}/details")]
    public async Task<IActionResult> GetOfferDetails(
        Guid offerId,
        [FromQuery] decimal cartAmount = 0m,
        [FromQuery] int durationDays = 0,
        [FromQuery] string? deliveryGovernorate = null,
        [FromQuery] Guid? programId = null,
        [FromQuery] Guid? bundleId = null,
        [FromQuery] Guid? restaurantId = null,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender.Send(new GetCustomerOfferDetailsQuery(
            offerId, ResolveCustomerId(), cartAmount, durationDays, deliveryGovernorate,
            programId, bundleId, restaurantId), cancellationToken));

    [Authorize]
    [HttpGet("{offerId:guid}/purchase-options")]
    public async Task<IActionResult> GetOfferPurchaseOptions(
        Guid offerId,
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] bool includeFriday = false,
        CancellationToken cancellationToken = default)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        var result = await Sender.Send(new GetCustomerOfferPurchaseOptionsQuery(
            offerId, CurrentUser.UserId.Value, startDate, includeFriday), cancellationToken);
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
    [HttpPost("{offerId:guid}/save")]
    public async Task<IActionResult> SaveOffer(Guid offerId, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        var result = await Sender.Send(new SaveOfferCommand(offerId), cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpDelete("{offerId:guid}/save")]
    public async Task<IActionResult> UnsaveOffer(Guid offerId, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        var result = await Sender.Send(new UnsaveOfferCommand(offerId), cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpPost("{offerId:guid}/calendar")]
    public async Task<IActionResult> AddOfferToCalendar(
        Guid offerId, [FromBody] AddOfferToCalendarRequest request, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        var result = await Sender.Send(new AddOfferToCalendarCommand(offerId, request.ScheduledDate), cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpDelete("{offerId:guid}/calendar/{date}")]
    public async Task<IActionResult> RemoveOfferFromCalendar(Guid offerId, DateOnly date, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        var result = await Sender.Send(new RemoveOfferFromCalendarCommand(offerId, date), cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpGet("calendar")]
    public async Task<IActionResult> GetOfferCalendar(CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        var result = await Sender.Send(new ListOfferCalendarQuery(), cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpGet("saved")]
    public async Task<IActionResult> GetSavedOffers(CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        var result = await Sender.Send(new GetSavedOffersQuery(), cancellationToken);
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
    [HttpGet("{offerId:guid}/eligible-restaurants")]
    public async Task<IActionResult> GetEligibleRestaurants(Guid offerId, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserType != UserType.Customer || !CurrentUser.UserId.HasValue)
        {
            return Forbid();
        }

        var result = await Sender.Send(new GetOfferEligibleRestaurantsQuery(offerId), cancellationToken);
        return HandleResult(result);
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new GetCustomerOfferDetailsQuery(id, ResolveCustomerId()), cancellationToken);
        return result.IsFailure
            ? HandleResult(result)
            : Ok(result.Value!.Offer);
    }

    [AllowAnonymous]
    [HttpPost("validate-discount")]
    public async Task<IActionResult> ValidateDiscount(
        [FromBody] ValidateDiscountRequest request,
        CancellationToken cancellationToken = default)
    {
        var customerId = ResolveCustomerId(request.CustomerUserId);
        var result = await Sender.Send(new MealMate.Application.Features.ProgramsSubscriptions.ValidateDiscountCodeQuery(
            request.Code,
            request.SubscriptionId,
            request.TotalAmountKwd,
            customerId != Guid.Empty ? customerId : null,
            request.ProgramId,
            request.BundleId,
            request.SubscriptionType), cancellationToken);
        return HandleResult(result);
    }
}

public sealed record ValidateDiscountRequest(
    string Code,
    Guid? SubscriptionId = null,
    decimal TotalAmountKwd = 0m,
    Guid? CustomerUserId = null,
    Guid? ProgramId = null,
    Guid? BundleId = null,
    string? SubscriptionType = null);

