using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/customer/family-subscription-flow")]
public sealed class CustomerFamilySubscriptionFlowController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IFamilySubscriptionFlowService _familyFlow;

    [ActivatorUtilitiesConstructor]
    public CustomerFamilySubscriptionFlowController(
        ICurrentUser currentUser,
        IFamilySubscriptionFlowService familyFlow)
    {
        _currentUser = currentUser;
        _familyFlow = familyFlow;
    }

    /// <summary>Backward-compatible constructor for existing tests and call sites.</summary>
    public CustomerFamilySubscriptionFlowController(
        ICurrentUser currentUser,
        IAppDbContext db,
        IPaymentGatewayService payments,
        IFamilyCheckoutService checkout,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        IFamilySubscriptionFlowService? familyFlow = null,
        ISubscriptionFlowQueryService? queryService = null)
    {
        _currentUser = currentUser;
        _familyFlow = familyFlow ?? new LazyFamilySubscriptionFlowService(
            () => Sender, db, payments, checkout, configuration, environment,
            () => queryService ?? HttpContext?.RequestServices?.GetService<ISubscriptionFlowQueryService>());
    }

    [HttpGet("metadata")]
    [ProducesResponseType(typeof(FamilyDraftMetadataDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Metadata(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetMetadataAsync(cancellationToken));
    }

    [HttpPut("members/{id:guid}/checkout-selection")]
    [ProducesResponseType(typeof(FamilyCheckoutSelectionDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetCheckoutSelection(Guid id, [FromBody] SetFamilyCheckoutSelectionRequest request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new SetFamilyCheckoutSelectionCommand(id, request.SubscribeNow), ct));
    }

    [HttpGet("relationships")]
    [ProducesResponseType(typeof(IReadOnlyList<FamilyLookupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Relationships(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetRelationshipsAsync(cancellationToken));
    }

    [HttpGet("programs")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerProgramCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Programs(CancellationToken ct = default, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _familyFlow.GetProgramsAsync(_currentUser.UserId!.Value, ct);
        if (result.IsFailure) return HandleResult(result);
        var items = result.Value;
        if (page is > 0 && pageSize is > 0)
        {
            items = items.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value).ToList();
        }
        return Ok(items);
    }

    [HttpGet("programs/{programId:guid}/bundles")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerBundleCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Bundles(Guid programId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetBundlesAsync(programId, _currentUser.UserId!.Value, ct));
    }

    [HttpGet("programs/{programId:guid}/durations")]
    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/durations")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerDurationOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Durations(
        Guid programId,
        Guid? bundleId,
        [FromQuery] bool includeFriday,
        CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetDurationsAsync(
            programId, bundleId, _currentUser.UserId ?? Guid.Empty, includeFriday, ct));
    }

    [HttpGet("programs/{programId:guid}/tiers")]
    [HttpGet("programs/{programId:guid}/bundles/{bundleId:guid}/tiers")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerTierOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Tiers(
        Guid programId,
        Guid? bundleId,
        [FromQuery] int? durationDays,
        [FromQuery] bool includeFriday,
        [FromQuery] Guid? familyMemberId,
        CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetTiersAsync(
            programId, bundleId, _currentUser.UserId ?? Guid.Empty, durationDays, includeFriday, familyMemberId, ct));
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Current(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetCurrentAsync(cancellationToken));
    }

    [HttpPut("group")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertGroup([FromBody] UpsertFamilyGroupRequest request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.UpsertGroupAsync(request, cancellationToken));
    }

    [HttpPost("convert-from-individual")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConvertFromIndividual(
        [FromBody] ConvertFromIndividualRequest? request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.ConvertFromIndividualAsync(request, cancellationToken));
    }

    [HttpPost("abandon-draft")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AbandonDraft(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _familyFlow.AbandonDraftAsync(cancellationToken);
        if (result.IsFailure) return HandleResult(result);
        return NoContent();
    }

    [HttpGet("packages")]
    [ProducesResponseType(typeof(IReadOnlyList<FamilySizePackageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Packages(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetPackagesAsync(cancellationToken));
    }

    [HttpPost("capacity-upgrade")]
    [ProducesResponseType(typeof(FamilyCapacityUpgradeDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpgradeCapacity([FromBody] FamilyCapacityUpgradeRequest request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.UpgradeCapacityAsync(request, cancellationToken));
    }

    [HttpPost("members/lookup")]
    [ProducesResponseType(typeof(FamilyMemberLookupDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupMember(
        [FromBody] FamilyMemberLookupRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.LookupMemberAsync(request, cancellationToken));
    }

    [HttpPut("members/{memberId:guid}/allergies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveMemberAllergies(
        Guid memberId,
        [FromBody] SaveFamilyMemberAllergiesRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.SaveMemberAllergiesAsync(memberId, request, cancellationToken));
    }

    [HttpGet("members/{memberId:guid}/allergies")]
    [ProducesResponseType(typeof(FamilyMemberAllergiesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberAllergies(Guid memberId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetMemberAllergiesAsync(memberId, cancellationToken));
    }

    [HttpPut("members/{memberId:guid}/disliked-ingredients")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveMemberDislikedFoods(
        Guid memberId,
        [FromBody] SaveFamilyMemberDislikedFoodsRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.SaveMemberDislikedFoodsAsync(memberId, request, cancellationToken));
    }

    [HttpGet("members/{memberId:guid}/disliked-ingredients")]
    [ProducesResponseType(typeof(FamilyMemberDislikedFoodsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberDislikedFoods(Guid memberId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetMemberDislikedFoodsAsync(memberId, cancellationToken));
    }

    [HttpPut("members/{memberId:guid}/dietary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveMemberDietary(
        Guid memberId,
        [FromBody] SaveFamilyMemberDietaryRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.SaveMemberDietaryAsync(memberId, request, cancellationToken));
    }

    [HttpGet("members/{memberId:guid}/dietary")]
    [ProducesResponseType(typeof(FamilyMemberDietaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberDietary(Guid memberId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetMemberDietaryAsync(memberId, cancellationToken));
    }

    [HttpGet("allergies")]
    [ProducesResponseType(typeof(CustomerAllergiesScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllergies([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetCustomerAllergiesQuery(page, pageSize, search), cancellationToken));
    }

    [HttpGet("disliked-ingredients")]
    [ProducesResponseType(typeof(CustomerDislikedIngredientsScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDislikedIngredients([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetCustomerDislikedIngredientsQuery(page, pageSize, search), cancellationToken));
    }

    [HttpGet("dietary")]
    [ProducesResponseType(typeof(CustomerDietaryPreferencesScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDietaryPreferences([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetCustomerDietaryPreferencesQuery(page, pageSize, search), cancellationToken));
    }

    [HttpGet("food-preferences/catalog")]
    [ProducesResponseType(typeof(CustomerFoodPreferenceCatalogDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFoodPreferenceCatalog(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetCustomerFoodPreferenceCatalogQuery(), cancellationToken));
    }

    [HttpGet("members/{memberId:guid}/addresses")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerAddressDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMemberAddresses(Guid memberId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetMemberAddressesAsync(memberId, cancellationToken));
    }

    [HttpPost("members/{memberId:guid}/addresses")]
    [ProducesResponseType(typeof(CustomerAddressDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateMemberAddress(
        Guid memberId,
        [FromBody] CustomerAddressRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.CreateMemberAddressAsync(memberId, request, cancellationToken));
    }

    [HttpPut("members/{memberId:guid}/addresses/{addressId:guid}")]
    [ProducesResponseType(typeof(CustomerAddressDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMemberAddress(
        Guid memberId,
        Guid addressId,
        [FromBody] CustomerAddressRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.UpdateMemberAddressAsync(memberId, addressId, request, cancellationToken));
    }

    [HttpPost("members/{memberId:guid}/addresses/preview-coverage")]
    [ProducesResponseType(typeof(AddressCoveragePreviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewMemberAddressCoverage(
        Guid memberId,
        [FromBody] PreviewAddressCoverageRequest body,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.PreviewMemberAddressCoverageAsync(memberId, body, cancellationToken));
    }

    [HttpGet("members/{memberId:guid}/addresses/{addressId:guid}/preview-coverage")]
    [ProducesResponseType(typeof(AddressCoveragePreviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewMemberAddressCoverageById(
        Guid memberId,
        Guid addressId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.PreviewMemberAddressCoverageByIdAsync(memberId, addressId, page, pageSize, cancellationToken));
    }

    [HttpPost("members/{memberId:guid}/addresses/{addressId:guid}/set-default")]
    [ProducesResponseType(typeof(CustomerAddressDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMemberDefaultAddress(
        Guid memberId,
        Guid addressId,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.SetMemberDefaultAddressAsync(memberId, addressId, cancellationToken));
    }

    [HttpPost("members")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddMember([FromBody] FamilyMemberWriteDto request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.AddMemberAsync(request, cancellationToken));
    }

    [HttpPut("members/{memberId:guid}")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMember(Guid memberId, [FromBody] FamilyMemberWriteDto request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.UpdateMemberAsync(memberId, request, cancellationToken));
    }

    [HttpDelete("members/{memberId:guid}")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteMember(Guid memberId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.DeleteMemberAsync(memberId, cancellationToken));
    }

    [HttpPut("members/{memberId:guid}/plan")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMemberPlan(
        Guid memberId,
        [FromBody] SetFamilyMemberPlanRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.SetMemberPlanAsync(memberId, request, cancellationToken));
    }

    [HttpPut("members/{memberId:guid}/start-date")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMemberStartDate(
        Guid memberId,
        [FromBody] SetFamilyMemberStartDateRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.SetMemberStartDateAsync(memberId, request.StartDate, cancellationToken));
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(FamilyCheckoutSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] Guid? memberId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetSummaryAsync(memberId, cancellationToken));
    }

    [HttpGet("payment-methods")]
    public async Task<IActionResult> PaymentMethods([FromQuery] decimal amount, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await _familyFlow.GetPaymentMethodsAsync(amount, ct));
    }

    [HttpPost("checkout")]
    [ProducesResponseType(typeof(FamilyCheckoutStartedDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Checkout([FromBody] FamilyStartCheckoutRequest request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _familyFlow.StartCheckoutAsync(request, cancellationToken);
        if (result.IsFailure && result.Error.Code == "payment.initialization_failed")
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: Messages.GetMessage("payment.initialization_failed", "Payment initialization failed."), detail: result.Error.Description);
        return HandleResult(result);
    }

    [HttpPost("renewals/{renewalRequestId:guid}/checkout")]
    [ProducesResponseType(typeof(FamilyCheckoutStartedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RenewalCheckout(Guid renewalRequestId,
        [FromBody] FamilyRenewalCheckoutRequest request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        var result = await _familyFlow.StartRenewalCheckoutAsync(renewalRequestId, request, cancellationToken);
        if (result.IsFailure && result.Error.Code == "payment.initialization_failed")
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: Messages.GetMessage("payment.initialization_failed", "Payment initialization failed."), detail: result.Error.Description);
        return HandleResult(result);
    }

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;

    private sealed class LazyFamilySubscriptionFlowService : IFamilySubscriptionFlowService
    {
        private readonly Func<ISender> _senderProvider;
        private readonly IAppDbContext _db;
        private readonly IPaymentGatewayService _payments;
        private readonly IFamilyCheckoutService _checkout;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly Func<ISubscriptionFlowQueryService?>? _queryServiceProvider;
        private FamilySubscriptionFlowService? _inner;

        public LazyFamilySubscriptionFlowService(
            Func<ISender> senderProvider,
            IAppDbContext db,
            IPaymentGatewayService payments,
            IFamilyCheckoutService checkout,
            IConfiguration configuration,
            IWebHostEnvironment environment,
            Func<ISubscriptionFlowQueryService?>? queryServiceProvider = null)
        {
            _senderProvider = senderProvider;
            _db = db;
            _payments = payments;
            _checkout = checkout;
            _configuration = configuration;
            _environment = environment;
            _queryServiceProvider = queryServiceProvider;
        }

        private FamilySubscriptionFlowService Service =>
            _inner ??= new FamilySubscriptionFlowService(
                _senderProvider(), _db, _payments, _checkout,
                _queryServiceProvider?.Invoke() ?? throw new InvalidOperationException("Subscription flow query service is required."),
                _configuration, _environment);

        public Task<Result<FamilyDraftMetadataDto>> GetMetadataAsync(CancellationToken ct = default) => Service.GetMetadataAsync(ct);
        public Task<Result<IReadOnlyList<FamilyLookupDto>>> GetRelationshipsAsync(CancellationToken ct = default) => Service.GetRelationshipsAsync(ct);
        public Task<Result<IReadOnlyList<CustomerProgramCardDto>>> GetProgramsAsync(Guid customerUserId, CancellationToken ct = default) => Service.GetProgramsAsync(customerUserId, ct);
        public Task<Result<IReadOnlyList<CustomerBundleCardDto>>> GetBundlesAsync(Guid programId, Guid customerUserId, CancellationToken ct = default) => Service.GetBundlesAsync(programId, customerUserId, ct);
        public Task<Result<IReadOnlyList<CustomerDurationOptionDto>>> GetDurationsAsync(Guid programId, Guid? bundleId, Guid customerUserId, bool includeFriday, CancellationToken ct = default) => Service.GetDurationsAsync(programId, bundleId, customerUserId, includeFriday, ct);
        public Task<Result<IReadOnlyList<CustomerTierOptionDto>>> GetTiersAsync(Guid programId, Guid? bundleId, Guid customerUserId, int? durationDays, bool includeFriday, Guid? familyMemberId = null, CancellationToken ct = default) => Service.GetTiersAsync(programId, bundleId, customerUserId, durationDays, includeFriday, familyMemberId, ct);
        public Task<Result<FamilyGroupScreenDto>> GetCurrentAsync(CancellationToken ct = default) => Service.GetCurrentAsync(ct);
        public Task<Result<FamilyGroupScreenDto>> UpsertGroupAsync(UpsertFamilyGroupRequest request, CancellationToken ct = default) => Service.UpsertGroupAsync(request, ct);
        public Task<Result<FamilyGroupScreenDto>> ConvertFromIndividualAsync(ConvertFromIndividualRequest? request, CancellationToken ct = default) => Service.ConvertFromIndividualAsync(request, ct);
        public Task<Result<bool>> AbandonDraftAsync(CancellationToken ct = default) => Service.AbandonDraftAsync(ct);
        public Task<Result<IReadOnlyList<FamilySizePackageDto>>> GetPackagesAsync(CancellationToken ct = default) => Service.GetPackagesAsync(ct);
        public Task<Result<FamilyCapacityUpgradeDto>> UpgradeCapacityAsync(FamilyCapacityUpgradeRequest request, CancellationToken ct = default) => Service.UpgradeCapacityAsync(request, ct);
        public Task<Result<FamilyMemberLookupDto>> LookupMemberAsync(FamilyMemberLookupRequest request, CancellationToken ct = default) => Service.LookupMemberAsync(request, ct);
        public Task<Result<bool>> SaveMemberAllergiesAsync(Guid memberId, SaveFamilyMemberAllergiesRequest request, CancellationToken ct = default) => Service.SaveMemberAllergiesAsync(memberId, request, ct);
        public Task<Result<FamilyMemberAllergiesDto>> GetMemberAllergiesAsync(Guid memberId, CancellationToken ct = default) => Service.GetMemberAllergiesAsync(memberId, ct);
        public Task<Result<bool>> SaveMemberDislikedFoodsAsync(Guid memberId, SaveFamilyMemberDislikedFoodsRequest request, CancellationToken ct = default) => Service.SaveMemberDislikedFoodsAsync(memberId, request, ct);
        public Task<Result<FamilyMemberDislikedFoodsDto>> GetMemberDislikedFoodsAsync(Guid memberId, CancellationToken ct = default) => Service.GetMemberDislikedFoodsAsync(memberId, ct);
        public Task<Result<bool>> SaveMemberDietaryAsync(Guid memberId, SaveFamilyMemberDietaryRequest request, CancellationToken ct = default) => Service.SaveMemberDietaryAsync(memberId, request, ct);
        public Task<Result<FamilyMemberDietaryDto>> GetMemberDietaryAsync(Guid memberId, CancellationToken ct = default) => Service.GetMemberDietaryAsync(memberId, ct);
        public Task<Result<IReadOnlyList<CustomerAddressDto>>> GetMemberAddressesAsync(Guid memberId, CancellationToken ct = default) => Service.GetMemberAddressesAsync(memberId, ct);
        public Task<Result<CustomerAddressDto>> CreateMemberAddressAsync(Guid memberId, CustomerAddressRequest request, CancellationToken ct = default) => Service.CreateMemberAddressAsync(memberId, request, ct);
        public Task<Result<CustomerAddressDto>> UpdateMemberAddressAsync(Guid memberId, Guid addressId, CustomerAddressRequest request, CancellationToken ct = default) => Service.UpdateMemberAddressAsync(memberId, addressId, request, ct);
        public Task<Result<AddressCoveragePreviewDto>> PreviewMemberAddressCoverageAsync(Guid memberId, PreviewAddressCoverageRequest request, CancellationToken ct = default) => Service.PreviewMemberAddressCoverageAsync(memberId, request, ct);
        public Task<Result<AddressCoveragePreviewDto>> PreviewMemberAddressCoverageByIdAsync(Guid memberId, Guid addressId, int? page = null, int? pageSize = null, CancellationToken ct = default) => Service.PreviewMemberAddressCoverageByIdAsync(memberId, addressId, page, pageSize, ct);
        public Task<Result<CustomerAddressDto>> SetMemberDefaultAddressAsync(Guid memberId, Guid addressId, CancellationToken ct = default) => Service.SetMemberDefaultAddressAsync(memberId, addressId, ct);
        public Task<Result<FamilyGroupScreenDto>> AddMemberAsync(FamilyMemberWriteDto request, CancellationToken ct = default) => Service.AddMemberAsync(request, ct);
        public Task<Result<FamilyGroupScreenDto>> UpdateMemberAsync(Guid memberId, FamilyMemberWriteDto request, CancellationToken ct = default) => Service.UpdateMemberAsync(memberId, request, ct);
        public Task<Result<FamilyGroupScreenDto>> DeleteMemberAsync(Guid memberId, CancellationToken ct = default) => Service.DeleteMemberAsync(memberId, ct);
        public Task<Result<FamilyGroupScreenDto>> SetMemberPlanAsync(Guid memberId, SetFamilyMemberPlanRequest request, CancellationToken ct = default) => Service.SetMemberPlanAsync(memberId, request, ct);
        public Task<Result<FamilyGroupScreenDto>> SetMemberStartDateAsync(Guid memberId, DateOnly startDate, CancellationToken ct = default) => Service.SetMemberStartDateAsync(memberId, startDate, ct);
        public Task<Result<FamilyCheckoutSummaryDto>> GetSummaryAsync(Guid? memberId, CancellationToken ct = default) => Service.GetSummaryAsync(memberId, ct);
        public Task<Result<IReadOnlyList<CustomerPaymentMethodDto>>> GetPaymentMethodsAsync(decimal amount, CancellationToken ct = default) => Service.GetPaymentMethodsAsync(amount, ct);
        public Task<Result<FamilyCheckoutStartedDto>> StartCheckoutAsync(FamilyStartCheckoutRequest request, CancellationToken ct = default) => Service.StartCheckoutAsync(request, ct);
        public Task<Result<FamilyCheckoutStartedDto>> StartRenewalCheckoutAsync(Guid renewalRequestId, FamilyRenewalCheckoutRequest request, CancellationToken ct = default) => Service.StartRenewalCheckoutAsync(renewalRequestId, request, ct);
    }
}
