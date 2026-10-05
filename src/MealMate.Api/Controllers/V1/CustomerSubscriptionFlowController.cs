using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.V1;

/// <summary>Mobile-first read endpoints for every screen before subscription checkout.</summary>
[Authorize]
[Route("api/v1/customer/subscription-flow")]
public sealed class CustomerSubscriptionFlowController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly ISubscriptionFlowQueryService _queryService;

    [ActivatorUtilitiesConstructor]
    public CustomerSubscriptionFlowController(
        ICurrentUser currentUser,
        ISubscriptionFlowQueryService queryService)
    {
        _currentUser = currentUser;
        _queryService = queryService;
    }

    /// <summary>Backward-compatible constructor for existing tests and call sites.</summary>
    public CustomerSubscriptionFlowController(
        IAppDbContext db,
        ICurrentUser currentUser,
        IRestaurantEligibilityService eligibility,
        IMenuPlanResolutionService menus,
        ISubscriptionScheduleService schedule,
        IBundleSlotRequirementService slots,
        IMyFatoorahPaymentService payments,
        IAllowedSubscriptionDurationService allowedDurations,
        IRestaurantTierClassificationService tiers,
        ISubscriptionCheckoutReadinessService readiness,
        ICustomerIndividualSubscriptionGate individualGate,
        IRestaurantDiversificationCalculator diversification,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ISubscriptionFlowQueryService? queryService = null)
    {
        _currentUser = currentUser;
        _queryService = queryService ?? new SubscriptionFlowQueryService(
            db, eligibility, menus, schedule, slots, payments,
            allowedDurations, tiers, readiness, individualGate, diversification,
            new ControllerSenderAdapter(this), configuration, environment);
    }

    [HttpGet("types")]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionTypeOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Types(CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetSubscriptionTypesAsync(_currentUser.UserId ?? Guid.Empty, ct));
    }

    [HttpGet("programs")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerProgramCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Programs(CancellationToken ct = default, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _queryService.GetProgramsAsync(_currentUser.UserId ?? Guid.Empty, ct);
        if (result.IsFailure) return ToHttpResult(result);
        var items = result.Value;
        if (page is > 0 && pageSize is > 0)
        {
            items = items.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value).ToList();
        }
        return Ok(items);
    }

    [HttpGet("programs/{programId:guid}/bundles")]
    [ProducesResponseType(typeof(CustomerProgramBundlesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Bundles(Guid programId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetProgramBundlesAsync(programId, _currentUser.UserId ?? Guid.Empty, ct));
    }

    [HttpGet("prerequisites")]
    [ProducesResponseType(typeof(CustomerSubscriptionPrerequisitesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Prerequisites(CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetPrerequisitesAsync(_currentUser.UserId!.Value, ct));
    }

    [HttpGet("programs/{programId:guid}/durations")]
    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/durations")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerDurationOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Durations(Guid programId, Guid? bundleId, [FromQuery] bool includeFriday, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetDurationsAsync(programId, bundleId, _currentUser.UserId ?? Guid.Empty, includeFriday, ct));
    }

    [HttpGet("programs/{programId:guid}/tiers")]
    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/tiers")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerTierOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Tiers(Guid programId, Guid? bundleId, [FromQuery] int? durationDays,
        [FromQuery] bool includeFriday, CancellationToken ct, [FromQuery] Guid? familyMemberId = null)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetTiersAsync(programId, bundleId, _currentUser.UserId ?? Guid.Empty, durationDays, includeFriday, familyMemberId, ct));
    }

    [HttpGet("programs/{programId:guid}/tiers/{tier}/restaurants")]
    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/tiers/{tier}/restaurants")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerRestaurantBrowseCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TierRestaurants(Guid programId, Guid? bundleId, RestaurantTier tier,
        [FromQuery] Guid[]? restaurantIds, CancellationToken ct = default, [FromQuery] Guid? familyMemberId = null, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _queryService.GetTierRestaurantsAsync(programId, bundleId, tier, _currentUser.UserId ?? Guid.Empty, restaurantIds, familyMemberId, ct);
        if (result.IsFailure) return ToHttpResult(result);
        var items = result.Value;
        if (page is > 0 && pageSize is > 0)
        {
            items = items.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value).ToList();
        }
        return Ok(items);
    }

    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/restaurants/{restaurantId:guid}")]
    [ProducesResponseType(typeof(CustomerRestaurantBrowseCardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RestaurantBrowseCard(Guid programId, Guid bundleId, Guid restaurantId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetRestaurantBrowseAsync(programId, bundleId, restaurantId, _currentUser.UserId ?? Guid.Empty, ct));
    }

    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/restaurants/{restaurantId:guid}/details")]
    [ProducesResponseType(typeof(CustomerRestaurantBrowseDetailsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RestaurantBrowseDetails(Guid programId, Guid bundleId, Guid restaurantId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetRestaurantBrowseDetailsAsync(programId, bundleId, restaurantId, _currentUser.UserId ?? Guid.Empty, ct));
    }

    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/restaurants/{restaurantId:guid}/meals/{mealId:guid}")]
    [ProducesResponseType(typeof(CustomerRestaurantBrowseMealDetailsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RestaurantBrowseMealDetails(Guid programId, Guid bundleId, Guid restaurantId, Guid mealId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetMealBrowseDetailsAsync(programId, bundleId, restaurantId, mealId, _currentUser.UserId ?? Guid.Empty, ct));
    }

    /// <summary>Compact meal browse contract; program and restaurant are resolved on the server.</summary>
    [HttpGet("bundles/{bundleId:guid}/meals/{mealId:guid}")]
    [ProducesResponseType(typeof(CustomerRestaurantBrowseMealDetailsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> BundleMealBrowseDetails(Guid bundleId, Guid mealId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetMealBrowseDetailsAsync(bundleId, mealId, _currentUser.UserId ?? Guid.Empty, ct));
    }

    [HttpGet("configuration")]
    [ProducesResponseType(typeof(SubscriptionConfigurationDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Configuration([FromQuery] Guid programId, [FromQuery] Guid bundleId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetConfigurationAsync(programId, bundleId, _currentUser.UserId ?? Guid.Empty, ct));
    }

    [HttpGet("restaurants")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerRestaurantCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Restaurants([FromQuery] SubscriptionFlowContext request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetRestaurantsAsync(request, _currentUser.UserId ?? Guid.Empty, ct));
    }

    [HttpGet("restaurants/{restaurantId:guid}")]
    [ProducesResponseType(typeof(CustomerRestaurantDetailsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Restaurant(Guid restaurantId, [FromQuery] SubscriptionFlowContext request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetRestaurantDetailsAsync(restaurantId, request, _currentUser.UserId ?? Guid.Empty, ct));
    }

    [HttpGet("restaurants/{restaurantId:guid}/meals/{mealId:guid}")]
    [ProducesResponseType(typeof(CustomerMealDetailsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Meal(Guid restaurantId, Guid mealId, [FromQuery] SubscriptionFlowContext request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetMealDetailsAsync(restaurantId, mealId, request, _currentUser.UserId ?? Guid.Empty, ct));
    }

    [HttpGet("readiness")]
    [ProducesResponseType(typeof(SubscriptionCheckoutReadinessDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Readiness([FromQuery] SubscriptionSummaryRequest request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetReadinessAsync(request, _currentUser.UserId!.Value, ct));
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(SubscriptionOrderSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] SubscriptionSummaryRequest request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetOrderSummaryAsync(request, _currentUser.UserId!.Value, ct));
    }

    [HttpGet("payment-methods")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerPaymentMethodDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PaymentMethods([FromQuery] decimal amount, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return ToHttpResult(await _queryService.GetPaymentMethodsAsync(amount, ct));
    }

    private IActionResult ToHttpResult<T>(Result<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var error = result.Error;

        if (error.Code == "payment.gateway_unavailable")
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Payment methods unavailable",
                detail: "MyFatoorah did not return payment methods.");
        }

        if (error.Type == ErrorType.NotFound)
            return NotFound(new { error = error.Code });

        if (error.Type == ErrorType.Conflict)
        {
            if (error.Arguments is [ { } conflictPayload ])
                return Conflict(conflictPayload);
            return Conflict(new { error = error.Code });
        }

        if (error.Arguments is [ { } badPayload ])
            return BadRequest(badPayload);

        return BadRequest(new { error = error.Code });
    }

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;

    private sealed class ControllerSenderAdapter : ISender
    {
        private readonly CustomerSubscriptionFlowController _controller;
        public ControllerSenderAdapter(CustomerSubscriptionFlowController controller) => _controller = controller;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => _controller.Sender.Send(request, cancellationToken);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => _controller.Sender.Send(request, cancellationToken);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => _controller.Sender.Send(request, cancellationToken);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => _controller.Sender.CreateStream(request, cancellationToken);

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => _controller.Sender.CreateStream(request, cancellationToken);
    }
}
