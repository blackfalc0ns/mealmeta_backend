using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using MealMate.Application.Features.ProgramsSubscriptions.CustomerSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Freeze;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.ProgramsSubscriptions.Renewal;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.Enums;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

/// <summary>Customer-facing subscription management, lifecycle actions and payment history.</summary>
[Authorize]
[Route("api/v1/customer/subscriptions")]
public sealed class CustomerSubscriptionManagementController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly ISubscriptionCancellationService _cancellationService;
    private readonly ISubscriptionFreezeService _freezeService;
    private readonly ISubscriptionUpgradeService _upgradeService;
    private readonly ISubscriptionRenewalService _renewalService;
    private readonly ICustomerSubscriptionQueryService _queryService;

    public CustomerSubscriptionManagementController(
        IAppDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ISubscriptionTierUpgradeQuoteBuilder upgradeQuotes,
        IPaymentGatewayService payments,
        UserManager<ApplicationUser> users,
        IConfiguration? configuration = null,
        IWebHostEnvironment? environment = null,
        ISubscriptionPaymentProcessor? paymentProcessor = null,
        IRestaurantTierClassificationService? classification = null,
        IRestaurantDiversificationCalculator? diversification = null,
        ICapacityHoldExpiryService? holdExpiry = null,
        ISubscriptionCancellationService? cancellationService = null,
        ISubscriptionFreezeService? freezeService = null,
        ISubscriptionUpgradeService? upgradeService = null,
        ISubscriptionRenewalService? renewalService = null,
        ICustomerSubscriptionQueryService? queryService = null)
    {
        _currentUser = currentUser;
        _cancellationService = cancellationService ?? throw new ArgumentNullException(nameof(cancellationService));
        _freezeService = freezeService ?? throw new ArgumentNullException(nameof(freezeService));
        _upgradeService = upgradeService ?? throw new ArgumentNullException(nameof(upgradeService));
        _renewalService = renewalService ?? throw new ArgumentNullException(nameof(renewalService));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
    }

    [HttpPut("{id:guid}/delivery-address")]
    public async Task<IActionResult> AmendDeliveryAddress(Guid id, [FromBody] FamilyDeliveryAddressRequest body,
        [FromServices] IFamilySubscriptionDeliveryAmendments amendments, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await amendments.AmendAsync(id, _currentUser.UserId!.Value, body, ct));
    }

    [HttpGet("{id:guid}/delivery-address")]
    public async Task<IActionResult> GetDeliveryAddress(Guid id,
        [FromServices] IFamilySubscriptionDeliveryAmendments amendments, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await amendments.GetAsync(id, _currentUser.UserId!.Value, ct));
    }

    /// <summary>Chooser when the customer already has an individual subscription.</summary>
    [HttpGet("current/conversion-options")]
    [ProducesResponseType(typeof(SubscriptionConversionOptionsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConversionOptions(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetSubscriptionConversionOptionsQuery(), ct));
    }

    /// <summary>Complete data for the subscriptions and invoices screen.</summary>
    [HttpGet("management")]
    [ProducesResponseType(typeof(CustomerSubscriptionManagementScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Management([FromQuery] bool includeHistory = true, CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _queryService.GetManagementScreenAsync(_currentUser.UserId!.Value, includeHistory, ct));
    }

    /// <summary>Current active subscription card for the home/dashboard screen.</summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(CustomerCurrentSubscriptionScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Current(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _queryService.GetCurrentSubscriptionAsync(_currentUser.UserId!.Value, ct));
    }

    /// <summary>Slim list of the caller subscriptions, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerSubscriptionSlimDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeHistory = true, CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _queryService.ListSubscriptionsAsync(_currentUser.UserId!.Value, includeHistory, ct));
    }

    /// <summary>Uncompleted or draft subscriptions owned by current customer.</summary>
    [HttpGet("uncompleted")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerUncompletedSubscriptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UncompletedSubscriptions(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _queryService.GetUncompletedSubscriptionsAsync(_currentUser.UserId!.Value, ct));
    }

    /// <summary>Archived historical subscriptions (completed, expired, cancelled, upgraded), with explicit INDIVIDUAL and FAMILY kinds.</summary>
    [HttpGet("archived")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerSubscriptionSlimDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ArchivedSubscriptions(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _queryService.GetArchivedSubscriptionsAsync(_currentUser.UserId!.Value, ct));
    }

    /// <summary>Delete or cancel an uncompleted/draft subscription, releasing holds and removing blocks.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteUncompletedSubscription(Guid id, CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _cancellationService.CancelUncompletedSubscriptionAsync(id, _currentUser.UserId!.Value, ct);
        if (result.IsFailure)
        {
            if (result.Error.Type == ErrorType.NotFound) return NotFound(new { error = "subscription.not_found" });
            if (result.Error.Type == ErrorType.Conflict) return Conflict(new { error = result.Error.Code });
            return BadRequest(new { error = result.Error.Code });
        }
        return NoContent();
    }

    /// <summary>All verified payment receipts for subscriptions owned by the current customer.</summary>
    [HttpGet("/api/v1/customer/invoices")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerSubscriptionInvoiceListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoices(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _queryService.GetInvoicesAsync(_currentUser.UserId!.Value, ct));
    }

    /// <summary>One owned verified payment receipt.</summary>
    [HttpGet("/api/v1/customer/invoices/{receiptId:guid}")]
    [ProducesResponseType(typeof(CustomerSubscriptionInvoiceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> InvoiceDetail(Guid receiptId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _queryService.GetInvoiceDetailAsync(receiptId, _currentUser.UserId!.Value, ct);
        if (result.IsFailure && result.Error.Type == ErrorType.NotFound)
            return NotFound(new { error = "invoice.not_found" });
        return HandleResult(result);
    }

    /// <summary>The customer's default payment method summary.</summary>
    [HttpGet("/api/v1/customer/payment-methods/default")]
    [ProducesResponseType(typeof(CustomerDefaultPaymentMethodDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DefaultPaymentMethod(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _queryService.GetDefaultPaymentMethodAsync(_currentUser.UserId!.Value, ct));
    }

    /// <summary>Freeze policy configured by operations.</summary>
    [HttpGet("freeze-policy")]
    [ProducesResponseType(typeof(CustomerSubscriptionFreezePolicyDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> FreezePolicy(CancellationToken ct = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _freezeService.GetPolicyAsync(ct));
    }

    /// <summary>Available tier upgrade options for one owned subscription.</summary>
    [HttpGet("{id:guid}/upgrade-options")]
    [ProducesResponseType(typeof(CustomerUpgradeOptionsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpgradeOptions(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _upgradeService.GetUpgradeOptionsAsync(id, _currentUser.UserId!.Value, ct);
        if (result.IsFailure && result.Error.Type == ErrorType.NotFound)
            return NotFound(new { error = "subscription.not_found" });
        return HandleResult(result);
    }

    /// <summary>Full summary and restaurant comparison for upgrading to a target tier.</summary>
    [HttpGet("{id:guid}/upgrade-summary")]
    [ProducesResponseType(typeof(CustomerUpgradeSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpgradeSummary(Guid id, [FromQuery] RestaurantTier targetTier, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _upgradeService.GetUpgradeSummaryAsync(id, _currentUser.UserId!.Value, targetTier, ct);
        if (result.IsFailure)
        {
            if (result.Error.Type == ErrorType.NotFound) return NotFound(new { error = "subscription.not_found" });
            if (result.Error.Type == ErrorType.Conflict) return Conflict(new { error = result.Error.Code });
            return BadRequest(new { error = result.Error.Code });
        }
        return HandleResult(result);
    }

    /// <summary>Available payment methods for subscription upgrade.</summary>
    [HttpGet("{id:guid}/upgrade-payment-methods")]
    [HttpGet("{id:guid}/payment-methods")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerPaymentMethodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpgradePaymentMethods(Guid id, [FromQuery] decimal? amount, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _upgradeService.GetUpgradePaymentMethodsAsync(id, _currentUser.UserId!.Value, amount, ct);
        if (result.IsFailure && result.Error.Type == ErrorType.NotFound)
            return NotFound(new { error = "subscription.not_found" });
        return HandleResult(result);
    }

    /// <summary>Request a subscription tier upgrade and start Payzah payment when due.</summary>
    [HttpPost("{id:guid}/upgrade-requests")]
    [ProducesResponseType(typeof(CustomerUpgradeStartedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestUpgrade(Guid id, [FromBody] CustomerUpgradeRequest body, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _upgradeService.RequestUpgradeAsync(id, _currentUser.UserId!.Value, body, ct);
        if (result.IsFailure)
        {
            if (result.Error.Type == ErrorType.NotFound) return NotFound(new { error = "subscription.not_found" });
            if (result.Error.Type == ErrorType.Conflict) return Conflict(new { error = result.Error.Code });
            if (result.Error.Code == "upgrade.payment_init_failed")
                return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Payment initialization failed", detail: result.Error.Description);
            return BadRequest(new { error = result.Error.Code });
        }
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>Status of one owned upgrade request (for return from payment gateway).</summary>
    [HttpGet("{id:guid}/upgrade-requests/{upgradeId:guid}")]
    [ProducesResponseType(typeof(CustomerUpgradeStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpgradeRequestStatus(Guid id, Guid upgradeId, CancellationToken ct = default, [FromQuery] string? paymentId = null)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _upgradeService.GetUpgradeStatusAsync(id, upgradeId, _currentUser.UserId!.Value, paymentId, ct);
        if (result.IsFailure)
        {
            if (result.Error.Code == MessageCodes.Common.NotFound) return NotFound(new { error = "subscription.not_found" });
            return NotFound(new { error = "upgrade.not_found" });
        }
        return HandleResult(result);
    }

    /// <summary>Management details for one owned subscription.</summary>
    [HttpGet("{id:guid}/management")]
    [ProducesResponseType(typeof(CustomerManagedSubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ManagementDetails(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _queryService.GetManagementDetailsAsync(id, _currentUser.UserId!.Value, ct);
        if (result.IsFailure) return NotFound(new { error = "subscription.not_found" });
        return HandleResult(result);
    }

    /// <summary>Verified payment receipts belonging to one owned subscription.</summary>
    [HttpGet("{id:guid}/invoices")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerSubscriptionInvoiceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoices(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _queryService.GetSubscriptionInvoicesAsync(id, _currentUser.UserId!.Value, ct);
        if (result.IsFailure) return NotFound(new { error = "subscription.not_found" });
        return HandleResult(result);
    }

    /// <summary>Full freeze details, active dates, compensation days, and history for the current customer subscription.</summary>
    [HttpGet("current/freeze-details")]
    [ProducesResponseType(typeof(CustomerSubscriptionFreezeDetailsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CurrentFreezeDetails(CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _freezeService.GetCurrentFreezeDetailsAsync(_currentUser.UserId!.Value, ct));
    }

    /// <summary>Full freeze details, active dates, compensation days, and history for a specific owned subscription.</summary>
    [HttpGet("{id:guid}/freeze-details")]
    [ProducesResponseType(typeof(CustomerSubscriptionFreezeDetailsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> FreezeDetails(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _freezeService.GetFreezeDetailsAsync(id, _currentUser.UserId!.Value, ct));
    }

    /// <summary>Submit a freeze request under the active admin policy.</summary>
    [HttpPost("{id:guid}/freeze-requests")]
    [ProducesResponseType(typeof(CustomerSubscriptionFreezeDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestFreeze(Guid id, [FromBody] CustomerFreezeRequest body, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _freezeService.RequestFreezeAsync(id, _currentUser.UserId!.Value, body, ct));
    }

    /// <summary>Return saved customer consent and the activation prerequisites for automatic renewal.</summary>
    [HttpGet("{id:guid}/auto-renewal")]
    public async Task<IActionResult> GetAutoRenewal(Guid id, [FromServices] IAppDbContext db,
        [FromServices] IFamilySubscriptionAccess access, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var entity = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { error = "subscription.not_found" });
        var authorized = await access.AuthorizeAsync(entity, _currentUser.UserId!.Value, FamilyAction.RequestRenewal, ct);
        if (authorized.IsFailure) return HandleResult(authorized.Error);
        var subscription = new
        {
            entity.AutoRenewConsentAtUtc, entity.AutoRenewConsentWithdrawnAtUtc,
            entity.AutoRenewConsentVersion, entity.AutoRenewConsentAmount, entity.AutoRenewConsentCurrency,
            entity.AutoRenewConsentActorUserId, entity.AutoRenewConsentWithdrawnByUserId
        };
        var consentActive = subscription.AutoRenewConsentAtUtc.HasValue && !subscription.AutoRenewConsentWithdrawnAtUtc.HasValue;
        return Ok(new { subscriptionId = id, consentActive, consentVersion = subscription.AutoRenewConsentVersion,
            consentAmount = subscription.AutoRenewConsentAmount, currency = subscription.AutoRenewConsentCurrency,
            consentActorUserId = subscription.AutoRenewConsentActorUserId,
            withdrawnByUserId = subscription.AutoRenewConsentWithdrawnByUserId,
            consentedAtUtc = subscription.AutoRenewConsentAtUtc, withdrawnAtUtc = subscription.AutoRenewConsentWithdrawnAtUtc,
            billingStatus = "NotConnected", requiresPaymentSetup = consentActive,
            autoChargeEnabled = false });
    }

    /// <summary>Save explicit consent to renew at the displayed price; does not enroll or charge the customer.</summary>
    [HttpPut("{id:guid}/auto-renewal/consent")]
    public async Task<IActionResult> RecordAutoRenewalConsent(Guid id, [FromBody] AutoRenewalConsentRequest body,
        [FromServices] IAppDbContext db, [FromServices] IFamilySubscriptionAccess access,
        IDateTimeProvider clock, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        if (body is null || !body.Accepted || string.IsNullOrWhiteSpace(body.ConsentVersion) || body.ConsentVersion.Length > 40 ||
            body.Amount <= 0 || decimal.Round(body.Amount, 3) != body.Amount || string.IsNullOrWhiteSpace(body.Currency) || body.Currency.Length != 3)
            return BadRequest(new { error = "auto_renewal.invalid_consent" });

        var subscription = await db.Subscriptions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });
        var authorized = await access.AuthorizeAsync(subscription, _currentUser.UserId!.Value, FamilyAction.RequestRenewal, ct);
        if (authorized.IsFailure) return HandleResult(authorized.Error);
        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Scheduled))
            return Conflict(new { error = "auto_renewal.subscription_not_eligible" });
        var currentAmount = subscription.PaidAmount.Amount;
        var currentCurrency = subscription.PaidAmount.CurrencyCode;
        if (body.Amount != currentAmount || !body.Currency.Equals(currentCurrency, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "auto_renewal.price_changed", amount = currentAmount, currency = currentCurrency });

        subscription.AutoRenewConsentAtUtc = clock.UtcNow;
        subscription.AutoRenewConsentWithdrawnAtUtc = null;
        subscription.AutoRenewConsentVersion = body.ConsentVersion.Trim();
        subscription.AutoRenewConsentAmount = body.Amount;
        subscription.AutoRenewConsentCurrency = currentCurrency;
        subscription.AutoRenewConsentActorUserId = _currentUser.UserId.Value;
        subscription.AutoRenewConsentWithdrawnByUserId = null;
        await db.SaveChangesAsync(ct);
        return Accepted(new { subscriptionId = id, consentActive = true, consentVersion = subscription.AutoRenewConsentVersion,
            consentAmount = subscription.AutoRenewConsentAmount, currency = subscription.AutoRenewConsentCurrency,
            consentedAtUtc = subscription.AutoRenewConsentAtUtc, billingStatus = "NotConnected", requiresPaymentSetup = true,
            autoChargeEnabled = false });
    }

    /// <summary>Withdraw customer consent. Provider-side recurring cancellation must be completed before automatic billing is enabled.</summary>
    [HttpDelete("{id:guid}/auto-renewal/consent")]
    public async Task<IActionResult> WithdrawAutoRenewalConsent(Guid id, [FromServices] IAppDbContext db,
        [FromServices] IFamilySubscriptionAccess access, IDateTimeProvider clock, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (subscription is null) return NotFound(new { error = "subscription.not_found" });
        var authorized = await access.AuthorizeAsync(subscription, _currentUser.UserId!.Value, FamilyAction.RequestRenewal, ct);
        if (authorized.IsFailure) return HandleResult(authorized.Error);
        subscription.AutoRenewConsentWithdrawnAtUtc = clock.UtcNow;
        subscription.AutoRenewConsentWithdrawnByUserId = _currentUser.UserId.Value;
        await db.SaveChangesAsync(ct);
        return Ok(new { subscriptionId = id, consentActive = false, withdrawnAtUtc = subscription.AutoRenewConsentWithdrawnAtUtc,
            billingStatus = "NotConnected", autoChargeEnabled = false });
    }

    /// <summary>Create an idempotent renewal intent and return the frozen context used to reopen subscription flow.</summary>
    [HttpPost("{id:guid}/renewal-requests")]
    [ProducesResponseType(typeof(CustomerRenewalRequestDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestRenewal(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _renewalService.RequestRenewalAsync(id, _currentUser.UserId!.Value, ct);
        if (result.IsFailure)
        {
            if (result.Error.Type == ErrorType.NotFound) return NotFound(new { error = "subscription.not_found" });
            if (result.Error.Type == ErrorType.Conflict) return Conflict(new { error = result.Error.Code });
            return BadRequest(new { error = result.Error.Code });
        }
        return Accepted(result.Value);
    }

    /// <summary>Get a quote / preview of the cancellation refund and effective dates prior to confirming cancellation.</summary>
    [HttpGet("{id:guid}/cancellation-quote")]
    [ProducesResponseType(typeof(CustomerCancellationQuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetCancellationQuote(Guid id, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _cancellationService.GetQuoteAsync(id, _currentUser.UserId!.Value, _currentUser.UserType ?? UserType.Customer, ct));
    }

    /// <summary>Cancel subscription, calculate refund for remaining days after kitchen lock window, cancel remaining days, and update status.</summary>
    [HttpPost("{id:guid}/cancellation-requests")]
    [ProducesResponseType(typeof(CustomerCancellationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestCancellation(Guid id, [FromBody] CustomerCancellationRequest body, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _cancellationService.RequestCancellationAsync(id, _currentUser.UserId!.Value, _currentUser.UserType ?? UserType.Customer, body.Reason, ct));
    }

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;

    private sealed class UserManagerAccountAdapter : IUserAccountService
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public UserManagerAccountAdapter(UserManager<ApplicationUser> userManager) => _userManager = userManager;

        public async Task<UserAccountDto?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var u = await _userManager.FindByIdAsync(userId.ToString());
            return u is null ? null : new UserAccountDto(
                u.Id, u.Email, u.PhoneNumber, u.FullName, u.UserType, u.AccountStatus,
                u.RestaurantId, u.EmailConfirmed, u.PhoneNumberConfirmed, u.CreatedAtUtc, u.LastLoginAtUtc, u.HasSetPassword);
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
        public Task SetHasPasswordAsync(Guid userId, bool hasSetPassword, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetRestaurantIdAsync(Guid userId, Guid? restaurantId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetUserTypeAsync(Guid userId, UserType userType, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateFullNameAsync(Guid userId, string fullName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public async Task UpdatePhoneAsync(Guid userId, string phone, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException("User not found.");
            var result = await _userManager.SetPhoneNumberAsync(user, phone);
            if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        }
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

public sealed record AutoRenewalConsentRequest(bool Accepted, string ConsentVersion, decimal Amount, string Currency);
