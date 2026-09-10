using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Persistence.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace MealMate.Api.Controllers.V1;

/// <summary>
/// Customer checkout is deliberately backed by one eligibility predicate.  A restaurant
/// returned by this controller is the only kind that may be reserved at payment time.
/// </summary>
[Authorize]
[Route("api/v1/customer/checkout")]
public sealed class CustomerCheckoutController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerCheckoutService _checkoutService;

    public CustomerCheckoutController(
        ICurrentUser currentUser,
        ICustomerCheckoutService checkoutService)
    {
        _currentUser = currentUser;
        _checkoutService = checkoutService;
    }

    /// <summary>Backward-compatible constructor for existing tests and call sites.</summary>
    public CustomerCheckoutController(
        IAppDbContext db,
        ICurrentUser currentUser,
        IMyFatoorahPaymentService payments,
        IDateTimeProvider clock,
        UserManager<ApplicationUser> users,
        IRestaurantEligibilityService eligibility,
        ICapacityConcurrencyLock capacityLock,
        ICheckoutTransactionExecutor transactionExecutor,
        ISubscriptionScheduleService schedule,
        IRestaurantPoolSnapshotService restaurantPool,
        IConfiguration configuration,
        IAllowedSubscriptionDurationService allowedDurations,
        ISubscriptionPaymentProcessor paymentProcessor,
        ISubscriptionCheckoutReadinessService readiness,
        ICustomerCheckoutService? checkoutService = null)
    {
        _currentUser = currentUser;
        _checkoutService = checkoutService ?? new CustomerCheckoutService(
            db, payments, clock, new UserManagerAccountAdapter(users),
            eligibility, capacityLock, transactionExecutor, schedule, restaurantPool,
            configuration, allowedDurations, paymentProcessor, readiness,
            new ControllerSenderAdapter(this));
    }

    [HttpGet("catalog")]
    [ProducesResponseType(typeof(IReadOnlyList<CheckoutCatalogItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        if (_currentUser.UserType != UserType.Customer) return Forbid();
        return ToHttpResult(await _checkoutService.GetCatalogAsync(ct));
    }

    [HttpGet("restaurants")]
    [ProducesResponseType(typeof(IReadOnlyList<EligibleRestaurantDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Restaurants([FromQuery] CheckoutAvailabilityRequest request, CancellationToken ct)
    {
        if (_currentUser.UserType != UserType.Customer) return Forbid();
        return ToHttpResult(await _checkoutService.GetRestaurantsAsync(request, _currentUser.UserId, ct));
    }

    [HttpPost("/api/v1/customer/subscriptions/checkout")]
    [ProducesResponseType(typeof(CheckoutStartedDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Checkout([FromBody] StartCheckoutRequest request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();
        if (_currentUser.UserType != UserType.Customer) return Forbid();
        return ToHttpResult(await _checkoutService.StartCheckoutAsync(request, userId, ct));
    }

    [HttpGet("/api/v1/customer/subscriptions/{id:guid}/payment")]
    [ProducesResponseType(typeof(SubscriptionPaymentStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PaymentStatus(Guid id, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();
        return ToHttpResult(await _checkoutService.GetPaymentStatusAsync(id, userId, ct));
    }

    [HttpGet("/api/v1/customer/subscriptions/{id:guid}/resume")]
    [ProducesResponseType(typeof(CustomerSubscriptionResumePreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResumePreview(Guid id, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();
        if (_currentUser.UserType != UserType.Customer) return Forbid();
        return ToHttpResult(await _checkoutService.GetResumePreviewAsync(id, userId, ct));
    }

    [HttpPost("/api/v1/customer/subscriptions/{id:guid}/resume")]
    [ProducesResponseType(typeof(CheckoutStartedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResumeCheckout(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ResumeCheckoutRequest? request = null,
        CancellationToken ct = default)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();
        if (_currentUser.UserType != UserType.Customer) return Forbid();
        return ToHttpResult(await _checkoutService.ResumeCheckoutAsync(id, request, userId, ct));
    }

    [HttpGet("/api/v1/customer/subscriptions/{id:guid}/restaurants")]
    [ProducesResponseType(typeof(SubscriptionRestaurantPoolDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubscriptionRestaurants(
        Guid id,
        [FromQuery] string? query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (_currentUser.UserId is not { } userId) return Unauthorized();
        var lang = HttpContext.Request.Headers.AcceptLanguage.FirstOrDefault();
        return ToHttpResult(await _checkoutService.GetSubscriptionRestaurantsAsync(id, userId, query, page, pageSize, lang, ct));
    }

    private IActionResult ToHttpResult<T>(Result<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var error = result.Error;

        if (error.Code == "checkout.payment_init_failed")
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Payment initialization failed", detail: error.Description);

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

    private sealed class ControllerSenderAdapter : ISender
    {
        private readonly CustomerCheckoutController _controller;
        public ControllerSenderAdapter(CustomerCheckoutController controller) => _controller = controller;

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

    private sealed class UserManagerAccountAdapter : IUserAccountService
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public UserManagerAccountAdapter(UserManager<ApplicationUser> userManager) => _userManager = userManager;

        public async Task<UserAccountDto?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var u = await _userManager.FindByIdAsync(userId.ToString());
            return u is null ? null : new UserAccountDto(
                u.Id, u.Email, u.PhoneNumber, u.FullName, u.UserType, u.AccountStatus,
                u.RestaurantId, u.EmailConfirmed, u.PhoneNumberConfirmed, u.CreatedAtUtc, u.LastLoginAtUtc);
        }

        public Task<UserAccountDto?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<UserAccountDto?> FindByPhoneAsync(string phone, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserIdentityDto>> GetUserIdentitiesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Result<UserAccountDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> VerifyPasswordOrDummyAsync(Guid? userId, string password, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task RegisterFailedLoginAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResetAccessFailedCountAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Result> ResetPasswordAsync(Guid userId, string resetToken, string newPassword, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string> GeneratePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateAccountStatusAsync(Guid userId, AccountStatus status, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetEmailConfirmedAsync(Guid userId, bool confirmed, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetPhoneConfirmedAsync(Guid userId, bool confirmed, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateLastLoginAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetRestaurantIdAsync(Guid userId, Guid? restaurantId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetUserTypeAsync(Guid userId, UserType userType, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateFullNameAsync(Guid userId, string fullName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Result> AddToRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Result> RemoveFromRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Result> EnsureRoleExistsAsync(string roleName, string? description = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RoleDto>> ListRolesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RoleDto?> FindRoleByIdAsync(Guid roleId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RoleDto?> FindRoleByNameAsync(string roleName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Result<RoleDto>> CreateRoleAsync(string name, string? description, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Result> UpdateRoleAsync(Guid roleId, string? description, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserAccountDto>> ListUsersByTypeAsync(UserType userType, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserAccountDto>> ListAllUsersAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserAccountDto>> SearchUsersAsync(string? search = null, UserType? userType = null, AccountStatus? status = null, string? role = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserAccountDto>> ListUsersInRoleAsync(string roleName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
