using MealMate.Api.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

[Authorize]
[Route("api/v1/customer/family-subscription-flow")]
public sealed class CustomerFamilySubscriptionFlowController : ApiControllerBase
{
    private readonly ICurrentUser _currentUser;
    private readonly IAppDbContext _db;
    private readonly IMyFatoorahPaymentService _payments;
    private readonly IFamilyCheckoutService _checkout;
    private readonly IConfiguration _configuration;

    public CustomerFamilySubscriptionFlowController(
        ICurrentUser currentUser,
        IAppDbContext db,
        IMyFatoorahPaymentService payments,
        IFamilyCheckoutService checkout,
        IConfiguration configuration)
    {
        _currentUser = currentUser;
        _db = db;
        _payments = payments;
        _checkout = checkout;
        _configuration = configuration;
    }

    [HttpGet("metadata")]
    [ProducesResponseType(typeof(FamilyDraftMetadataDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Metadata(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetFamilyDraftMetadataQuery(), cancellationToken));
    }

    [HttpGet("relationships")]
    [ProducesResponseType(typeof(IReadOnlyList<FamilyLookupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Relationships(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetFamilyRelationshipsQuery(), cancellationToken));
    }

    [HttpGet("programs")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerProgramCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Programs(CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();

        var programs = await _db.Programs.AsNoTracking()
            .Where(x => x.IsActive && x.Status == ProgramStatus.Active && x.AvailableForFamily)
            .OrderBy(x => x.SortOrder)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.ShortCode,
                x.NameAr,
                x.NameEn,
                x.DescriptionAr,
                x.DescriptionEn,
                x.KeywordsAr,
                x.KeywordsEn,
                x.ImageStorageKey,
            })
            .ToListAsync(ct);

        var ids = programs.Select(x => x.Id).ToList();
        var bundleCounts = await (
            from bundle in _db.Bundles.AsNoTracking()
            join offer in _db.RestaurantProgramBundleOffers.AsNoTracking() on bundle.Id equals offer.BundleId
            join restaurant in _db.RestaurantProfiles.AsNoTracking() on offer.RestaurantId equals restaurant.Id
            where ids.Contains(bundle.ProgramId)
                && bundle.IsActive
                && bundle.Status == ProgramStatus.Active
                && bundle.AvailableForFamily
                && offer.ProgramId == bundle.ProgramId
                && offer.IsActive
                && restaurant.VerificationStatus == VerificationStatus.Approved
                && restaurant.IsOperationallyActive
            group bundle by bundle.ProgramId into programBundles
            select new
            {
                ProgramId = programBundles.Key,
                Count = programBundles.Select(x => x.Id).Distinct().Count(),
            })
            .ToDictionaryAsync(x => x.ProgramId, x => x.Count, ct);

        var slots = await _db.ProgramMealSlotRequirements.AsNoTracking()
            .Where(x => ids.Contains(x.ProgramId) && x.IsRequired)
            .ToListAsync(ct);

        return Ok(programs
            .Where(x => bundleCounts.GetValueOrDefault(x.Id) > 0)
            .Select(x => new CustomerProgramCardDto(
                x.Id,
                x.Code,
                x.ShortCode,
                x.NameAr,
                x.NameEn,
                x.DescriptionAr,
                x.DescriptionEn,
                x.KeywordsAr,
                x.KeywordsEn,
                CatalogImageStorage.ToContentUrl(x.ImageStorageKey),
                bundleCounts.GetValueOrDefault(x.Id),
                slots.Where(s => s.ProgramId == x.Id).Select(s => s.Slot).OrderBy(s => s).ToList())));
    }

    [HttpGet("programs/{programId:guid}/bundles")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerBundleCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Bundles(Guid programId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();

        var program = await _db.Programs.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == programId && x.IsActive && x.Status == ProgramStatus.Active && x.AvailableForFamily,
                ct);
        if (program is null)
            return NotFound(new { error = "family.catalog_not_available" });

        var bundles = await _db.Bundles.AsNoTracking()
            .Where(x => x.ProgramId == programId
                && x.IsActive
                && x.Status == ProgramStatus.Active
                && x.AvailableForFamily)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        var bundleIds = bundles.Select(x => x.Id).ToList();
        var offers = await _db.RestaurantProgramBundleOffers.AsNoTracking()
            .Where(x => x.ProgramId == programId && bundleIds.Contains(x.BundleId) && x.IsActive)
            .Select(x => new { x.BundleId, x.RestaurantId })
            .ToListAsync(ct);
        var offeredRestaurantIds = offers.Select(x => x.RestaurantId).Distinct().ToList();
        var restaurantProfiles = await _db.RestaurantProfiles.AsNoTracking()
            .Where(x => offeredRestaurantIds.Contains(x.Id)
                && x.VerificationStatus == VerificationStatus.Approved
                && x.IsOperationallyActive)
            .OrderBy(x => x.TradeNameAr)
            .Select(x => new { x.Id })
            .ToListAsync(ct);
        var activeRestaurantIds = restaurantProfiles.Select(x => x.Id).ToList();
        var restaurantCards = restaurantProfiles.ToDictionary(
            x => x.Id,
            x => new CustomerBundleRestaurantDto(x.Id, null));

        return Ok(bundles.Select(x =>
        {
            var restaurants = offers.Where(o => o.BundleId == x.Id)
                .Select(o => restaurantCards.GetValueOrDefault(o.RestaurantId))
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();
            return new CustomerBundleCardDto(
                x.Id,
                x.ProgramId,
                x.Code,
                x.NameAr,
                x.NameEn,
                x.Description ?? x.DescriptionEn ?? x.DescriptionAr,
                CatalogImageStorage.ToContentUrl(x.ImageStorageKey),
                x.MealsPerDay,
                x.BreakfastCount,
                x.LunchCount,
                x.DinnerCount,
                x.SnackCount,
                ToSlotRequirements(x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount),
                x.SortOrder,
                x.DescriptionAr ?? x.NameAr,
                x.DescriptionEn ?? x.Description ?? x.NameEn,
                ToDisplaySlots(x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount),
                restaurants.Count,
                restaurants.Take(8).ToList(),
                0,
                false,
                null);
        }).ToList());
    }

    // Durations/tiers: use individual subscription-flow endpoints with the same programId/bundleId
    // after selecting from family programs/bundles. SetMemberPlan enforces AvailableForFamily.

    private static List<SlotRequirementDto> ToSlotRequirements(int breakfast, int lunch, int dinner, int snack)
        => new[]
            {
                new SlotRequirementDto("breakfast", breakfast),
                new SlotRequirementDto("lunch", lunch),
                new SlotRequirementDto("dinner", dinner),
                new SlotRequirementDto("snack", snack),
            }
            .Where(x => x.Count > 0)
            .ToList();

    private static List<CustomerBundleSlotDto> ToDisplaySlots(int breakfast, int lunch, int dinner, int snack)
        => ToSlotRequirements(breakfast, lunch, dinner, snack)
            .Select(x => new CustomerBundleSlotDto(x.Slot, SlotNameAr(x.Slot), SlotNameEn(x.Slot), x.Count))
            .ToList();

    private static string SlotNameAr(string slot) => slot.ToLowerInvariant() switch
    {
        "breakfast" => "الإفطار",
        "lunch" => "الغداء",
        "dinner" => "العشاء",
        "snack" => "سناك",
        _ => slot,
    };

    private static string SlotNameEn(string slot) => slot.ToLowerInvariant() switch
    {
        "breakfast" => "Breakfast",
        "lunch" => "Lunch",
        "dinner" => "Dinner",
        "snack" => "Snack",
        _ => slot,
    };

    [HttpGet("current")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Current(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetCurrentFamilyGroupQuery(), cancellationToken));
    }

    [HttpPut("group")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpsertGroup([FromBody] UpsertFamilyGroupRequest request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new UpsertFamilyGroupCommand(request.Name, request.FamilyPackageId), cancellationToken));
    }

    [HttpGet("packages")]
    [ProducesResponseType(typeof(IReadOnlyList<FamilySizePackageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Packages(CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetFamilySizePackagesQuery(), cancellationToken));
    }

    [HttpPost("members")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddMember([FromBody] FamilyMemberWriteDto request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new AddFamilyDraftMemberCommand(request), cancellationToken));
    }

    [HttpPut("members/{memberId:guid}")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMember(Guid memberId, [FromBody] FamilyMemberWriteDto request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new UpdateFamilyMemberCommand(memberId, request), cancellationToken));
    }

    [HttpDelete("members/{memberId:guid}")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteMember(Guid memberId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new DeleteFamilyMemberCommand(memberId), cancellationToken));
    }

    [HttpPut("members/{memberId:guid}/plan")]
    [ProducesResponseType(typeof(FamilyGroupScreenDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetMemberPlan(
        Guid memberId,
        [FromBody] SetFamilyMemberPlanRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new SetFamilyMemberPlanCommand(memberId, request), cancellationToken));
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(FamilyCheckoutSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] Guid? memberId, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        return HandleResult(await Sender.Send(new GetFamilyCheckoutSummaryQuery(memberId), cancellationToken));
    }

    [HttpGet("payment-methods")]
    public async Task<IActionResult> PaymentMethods([FromQuery] decimal amount, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        if (amount <= 0) return BadRequest(new { error = "subscription_flow.invalid_amount" });
        var config = await _db.MyFatoorahGatewayConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.GatewayName == "MyFatoorah", ct);
        if (config is null) return Conflict(new { error = "payment.gateway_not_configured" });
        IReadOnlyList<MyFatoorahPaymentMethod> methods;
        try { methods = await _payments.GetPaymentMethodsAsync(amount, ct); }
        catch { return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Payment methods unavailable", detail: "MyFatoorah did not return payment methods."); }
        var result = methods.Where(x => IsConfiguredMethod(x, config.IsKnetEnabled, config.IsVisaMasterEnabled, config.IsApplePayEnabled))
            .Select(x => new CustomerPaymentMethodDto(x.PaymentMethodId, x.Code, x.NameAr, x.NameEn, "MYFATOORAH_HOSTED", x.ServiceCharge, x.TotalAmount, x.CurrencyCode, true, null)).ToList();
        result.Add(new CustomerPaymentMethodDto(null, "MEALMATE_WALLET", "محفظة MealMate", "MealMate Wallet", "WALLET", 0m, amount, config.DefaultCurrency, false, "wallet_not_implemented"));
        return Ok(result);
    }

    [HttpPost("checkout")]
    [ProducesResponseType(typeof(FamilyCheckoutStartedDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Checkout([FromBody] FamilyStartCheckoutRequest request, CancellationToken cancellationToken)
    {
        if (!IsCustomer()) return Forbid();
        if (!IsAllowedRedirectUrl(request.CallbackUrl) || !IsAllowedRedirectUrl(request.ErrorUrl))
            return BadRequest(new { error = "checkout.redirect_url_not_allowed" });

        var result = await _checkout.StartAsync(request, cancellationToken);
        if (result.IsFailure && result.Error.Code == "payment.initialization_failed")
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Payment initialization failed", detail: result.Error.Description);
        return HandleResult(result);
    }

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;

    private bool IsAllowedRedirectUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var schemes = _configuration.GetSection("Checkout:AllowedRedirectSchemes").Get<string[]>() ?? ["https", "mealmate"];
        if (!schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)) return false;
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) return true;
        var hosts = _configuration.GetSection("Checkout:AllowedRedirectHosts").Get<string[]>() ?? ["maelmate.runasp.net", "localhost"];
        return hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsConfiguredMethod(MyFatoorahPaymentMethod method, bool knet, bool card, bool apple)
    {
        var code = method.Code.Trim().ToUpperInvariant();
        var name = method.NameEn.Trim().ToUpperInvariant();
        if (code == "KN" || name.Contains("KNET")) return knet;
        if (code == "AP" || name.Contains("APPLE PAY")) return apple;
        if (code == "VM" || name.Contains("VISA/MASTER") || name.Contains("VISA / MASTER")) return card;
        return false;
    }
}
