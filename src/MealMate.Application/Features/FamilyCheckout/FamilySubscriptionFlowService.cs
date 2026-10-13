using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MealMate.Application.Features.FamilyCheckout;

public sealed class FamilySubscriptionFlowService : IFamilySubscriptionFlowService
{
    private readonly ISender _sender;
    private readonly IAppDbContext _db;
    private readonly IPaymentGatewayService _payments;
    private readonly IFamilyCheckoutService _checkout;
    private readonly IConfiguration? _configuration;
    private readonly IHostEnvironment? _environment;
    private readonly ISubscriptionFlowQueryService _subscriptionFlowQueries;

    public FamilySubscriptionFlowService(
        ISender sender,
        IAppDbContext db,
        IPaymentGatewayService payments,
        IFamilyCheckoutService checkout,
        ISubscriptionFlowQueryService subscriptionFlowQueries,
        IConfiguration? configuration = null,
        IHostEnvironment? environment = null)
    {
        _sender = sender;
        _db = db;
        _payments = payments;
        _checkout = checkout;
        _configuration = configuration;
        _environment = environment;
        _subscriptionFlowQueries = subscriptionFlowQueries;
    }

    public Task<Result<FamilyDraftMetadataDto>> GetMetadataAsync(CancellationToken ct = default)
        => _sender.Send(new GetFamilyDraftMetadataQuery(), ct);

    public Task<Result<IReadOnlyList<FamilyLookupDto>>> GetRelationshipsAsync(CancellationToken ct = default)
        => _sender.Send(new GetFamilyRelationshipsQuery(), ct);

    public Task<Result<IReadOnlyList<CustomerProgramCardDto>>> GetProgramsAsync(Guid customerUserId, CancellationToken ct = default)
        => _subscriptionFlowQueries.GetProgramsAsync(customerUserId, ct);

    public async Task<Result<IReadOnlyList<CustomerBundleCardDto>>> GetBundlesAsync(
        Guid programId,
        Guid customerUserId,
        CancellationToken ct = default)
    {
        var result = await _subscriptionFlowQueries.GetProgramBundlesAsync(programId, customerUserId, ct);
        if (result.IsFailure) return result.Error;
        return Result.Success<IReadOnlyList<CustomerBundleCardDto>>(result.Value.Bundles);
    }

    public Task<Result<IReadOnlyList<CustomerDurationOptionDto>>> GetDurationsAsync(
        Guid programId, Guid? bundleId, Guid customerUserId, bool includeFriday, CancellationToken ct = default)
    {
        return _subscriptionFlowQueries.GetDurationsAsync(programId, bundleId, customerUserId, includeFriday, ct);
    }

    public Task<Result<IReadOnlyList<CustomerTierOptionDto>>> GetTiersAsync(
        Guid programId, Guid? bundleId, Guid customerUserId, int? durationDays, bool includeFriday, Guid? familyMemberId = null, CancellationToken ct = default)
    {
        if (familyMemberId is null || familyMemberId == Guid.Empty)
        {
            return Task.FromResult<Result<IReadOnlyList<CustomerTierOptionDto>>>(new Error(
                "family.member_required",
                null,
                ErrorType.Conflict,
                [new
                {
                    error = "family.member_required",
                    missingStep = "FAMILY_MEMBER",
                    actionEndpoint = "Pass familyMemberId for the family member whose classification is being requested"
                }]));
        }

        return _subscriptionFlowQueries.GetTiersAsync(programId, bundleId, customerUserId, durationDays, includeFriday, familyMemberId, ct);
    }

    public Task<Result<FamilyGroupScreenDto>> GetCurrentAsync(CancellationToken ct = default)
        => _sender.Send(new GetCurrentFamilyGroupQuery(), ct);

    public Task<Result<FamilyGroupScreenDto>> UpsertGroupAsync(UpsertFamilyGroupRequest request, CancellationToken ct = default)
        => _sender.Send(new UpsertFamilyGroupCommand(request.Name, request.FamilyPackageId), ct);

    public Task<Result<FamilyGroupScreenDto>> ConvertFromIndividualAsync(ConvertFromIndividualRequest? request, CancellationToken ct = default)
        => _sender.Send(new ConvertIndividualToFamilyCommand(request?.Name, request?.FamilyPackageId), ct);

    public Task<Result<bool>> AbandonDraftAsync(CancellationToken ct = default)
        => _sender.Send(new AbandonFamilyDraftCommand(), ct);

    public Task<Result<IReadOnlyList<FamilySizePackageDto>>> GetPackagesAsync(CancellationToken ct = default)
        => _sender.Send(new GetFamilySizePackagesQuery(), ct);

    public Task<Result<FamilyCapacityUpgradeDto>> UpgradeCapacityAsync(FamilyCapacityUpgradeRequest request, CancellationToken ct = default)
        => _sender.Send(new UpgradeFamilyCapacityCommand(request), ct);

    public Task<Result<FamilyMemberLookupDto>> LookupMemberAsync(FamilyMemberLookupRequest request, CancellationToken ct = default)
        => _sender.Send(new LookupFamilyMemberByPhoneQuery(request.Phone), ct);

    public Task<Result<bool>> SaveMemberAllergiesAsync(Guid memberId, SaveFamilyMemberAllergiesRequest request, CancellationToken ct = default)
    {
        var disliked = ResolveDislikedFoods(request.DislikedFoods, request.IngredientCodes);
        // Empty shotgun arrays mean "not specified": only non-empty cross-field lists may
        // replace, otherwise an allergies save would wipe saved dislikes/dietary.
        return _sender.Send(
            new SaveFamilyMemberAllergiesCommand(
                memberId,
                ResolveAllergyItems(request),
                disliked,
                ReplaceDislikedFoods: disliked is { Count: > 0 },
                request.DietaryPreferenceCodes,
                ReplaceDietary: request.DietaryPreferenceCodes is { Count: > 0 }),
            ct);
    }

    public Task<Result<FamilyMemberAllergiesDto>> GetMemberAllergiesAsync(Guid memberId, CancellationToken ct = default)
        => _sender.Send(new GetFamilyMemberAllergiesQuery(memberId), ct);

    public Task<Result<bool>> SaveMemberDislikedFoodsAsync(Guid memberId, SaveFamilyMemberDislikedFoodsRequest request, CancellationToken ct = default)
    {
        // Prefer any non-empty candidate field so an empty alias cannot shadow real data;
        // only when every field is empty (or absent) does the empty list clear the rows.
        var candidates = new[]
        {
            ResolveDislikedFoods(request.DislikedFoods, request.IngredientCodes),
            ResolveDislikedFoods(request.DislikedIngredients, request.Ingredients),
            ResolveDislikedFoods(request.Selected, request.SelectedCodes),
            ResolveDislikedFoods(request.Items, null)
        };
        var resolved = candidates.FirstOrDefault(x => x is { Count: > 0 })
            ?? candidates.FirstOrDefault(x => x is not null)
            ?? [];

        return _sender.Send(
            new SaveFamilyMemberDislikedFoodsCommand(memberId, resolved),
            ct);
    }

    public Task<Result<FamilyMemberDislikedFoodsDto>> GetMemberDislikedFoodsAsync(Guid memberId, CancellationToken ct = default)
        => _sender.Send(new GetFamilyMemberDislikedFoodsQuery(memberId), ct);

    public Task<Result<bool>> SaveMemberDietaryAsync(Guid memberId, SaveFamilyMemberDietaryRequest request, CancellationToken ct = default)
    {
        IReadOnlyList<string>?[] candidates = [request.DietaryPreferenceCodes, request.SelectedCodes, request.Selected];
        var codes = candidates.FirstOrDefault(x => x is { Count: > 0 })
            ?? candidates.FirstOrDefault(x => x is not null)
            ?? [];
        return _sender.Send(new SaveFamilyMemberDietaryCommand(memberId, codes), ct);
    }

    public Task<Result<FamilyMemberDietaryDto>> GetMemberDietaryAsync(Guid memberId, CancellationToken ct = default)
        => _sender.Send(new GetFamilyMemberDietaryQuery(memberId), ct);

    public Task<Result<IReadOnlyList<CustomerAddressDto>>> GetMemberAddressesAsync(Guid memberId, CancellationToken ct = default)
        => _sender.Send(new GetFamilyMemberAddressesQuery(memberId), ct);

    public Task<Result<CustomerAddressDto>> CreateMemberAddressAsync(Guid memberId, CustomerAddressRequest request, CancellationToken ct = default)
    {
        return _sender.Send(
            new CreateFamilyMemberAddressCommand(
                memberId,
                request.Label,
                request.AddressLine,
                request.RegionId,
                request.Latitude,
                request.Longitude,
                request.Notes,
                request.IsDefault,
                request.NotifyOnDelivery,
                request.Governorate,
                request.Area,
                request.Block,
                request.Street,
                request.Avenue,
                request.Building,
                request.Unit,
                request.Floor,
                request.BlockRegionId),
            ct);
    }

    public Task<Result<CustomerAddressDto>> UpdateMemberAddressAsync(Guid memberId, Guid addressId, CustomerAddressRequest request, CancellationToken ct = default)
    {
        return _sender.Send(
            new UpdateFamilyMemberAddressCommand(
                memberId,
                addressId,
                request.Label,
                request.AddressLine,
                request.RegionId,
                request.Latitude,
                request.Longitude,
                request.Notes,
                request.IsDefault,
                request.NotifyOnDelivery,
                request.Governorate,
                request.Area,
                request.Block,
                request.Street,
                request.Avenue,
                request.Building,
                request.Unit,
                request.Floor,
                request.BlockRegionId),
            ct);
    }

    public Task<Result<AddressCoveragePreviewDto>> PreviewMemberAddressCoverageAsync(Guid memberId, PreviewAddressCoverageRequest request, CancellationToken ct = default)
    {
        return _sender.Send(
            new PreviewFamilyMemberAddressCoverageCommand(
                memberId,
                request.AddressId,
                request.RegionId,
                request.BlockRegionId,
                request.Latitude,
                request.Longitude,
                request.Governorate,
                request.Area,
                request.Block,
                request.IsDefault,
                request.Page,
                request.PageSize),
            ct);
    }

    public Task<Result<AddressCoveragePreviewDto>> PreviewMemberAddressCoverageByIdAsync(Guid memberId, Guid addressId, int? page = null, int? pageSize = null, CancellationToken ct = default)
        => _sender.Send(new PreviewFamilyMemberAddressCoverageCommand(memberId, AddressId: addressId, Page: page, PageSize: pageSize), ct);

    public Task<Result<CustomerAddressDto>> SetMemberDefaultAddressAsync(Guid memberId, Guid addressId, CancellationToken ct = default)
        => _sender.Send(new SetFamilyMemberDefaultAddressCommand(memberId, addressId), ct);

    public Task<Result<FamilyGroupScreenDto>> AddMemberAsync(FamilyMemberWriteDto request, CancellationToken ct = default)
        => _sender.Send(new AddFamilyDraftMemberCommand(request), ct);

    public Task<Result<FamilyGroupScreenDto>> UpdateMemberAsync(Guid memberId, FamilyMemberWriteDto request, CancellationToken ct = default)
        => _sender.Send(new UpdateFamilyMemberCommand(memberId, request), ct);

    public Task<Result<FamilyGroupScreenDto>> DeleteMemberAsync(Guid memberId, CancellationToken ct = default)
        => _sender.Send(new DeleteFamilyMemberCommand(memberId), ct);

    public Task<Result<FamilyGroupScreenDto>> SetMemberPlanAsync(Guid memberId, SetFamilyMemberPlanRequest request, CancellationToken ct = default)
        => _sender.Send(new SetFamilyMemberPlanCommand(memberId, request), ct);

    public Task<Result<FamilyGroupScreenDto>> SetMemberStartDateAsync(Guid memberId, DateOnly startDate, CancellationToken ct = default)
        => _sender.Send(new SetFamilyMemberStartDateCommand(memberId, startDate), ct);

    public Task<Result<FamilyCheckoutSummaryDto>> GetSummaryAsync(Guid? memberId, CancellationToken ct = default)
        => _sender.Send(new GetFamilyCheckoutSummaryQuery(memberId), ct);

    public async Task<Result<IReadOnlyList<CustomerPaymentMethodDto>>> GetPaymentMethodsAsync(decimal amount, CancellationToken ct = default)
    {
        if (_subscriptionFlowQueries is not null)
            return await _subscriptionFlowQueries.GetPaymentMethodsAsync(amount, ct, allowSandboxCompletion: false);

        if (amount <= 0) return Error.Validation("subscription_flow.invalid_amount");

        var config = await _db.PayzahGatewayConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.GatewayName == "Payzah", ct);
        if (config is null)
        {
            return Error.Conflict("payment.gateway_not_configured");
        }

        IReadOnlyList<PaymentGatewayMethod> methods;
        try
        {
            methods = await _payments.GetPaymentMethodsAsync(amount, ct);
        }
        catch
        {
            return new Error("payment.gateway_unavailable", "Payzah did not return payment methods.", ErrorType.Failure);
        }

        var result = methods
            .Select(x => new CustomerPaymentMethodDto(x.PaymentMethodId, x.Code, x.NameAr, x.NameEn, "PAYZAH_HOSTED", x.ServiceCharge, x.TotalAmount, x.CurrencyCode, true, null, x.ImageUrl))
            .ToList();

        result.Add(new CustomerPaymentMethodDto(null, "MEALMATE_WALLET", "محفظة MealMate", "MealMate Wallet", "WALLET", 0m, amount, config.DefaultCurrency, false, "wallet_not_implemented", null));
        return Result.Success<IReadOnlyList<CustomerPaymentMethodDto>>(result);
    }

    public async Task<Result<FamilyCheckoutStartedDto>> StartCheckoutAsync(FamilyStartCheckoutRequest request, CancellationToken ct = default)
    {
        if (!IsAllowedRedirectUrl(request.CallbackUrl) || !IsAllowedRedirectUrl(request.ErrorUrl))
            return Error.Validation("checkout.redirect_url_not_allowed");

        return await _checkout.StartAsync(request, ct);
    }

    public async Task<Result<FamilyCheckoutStartedDto>> StartRenewalCheckoutAsync(Guid renewalRequestId,
        FamilyRenewalCheckoutRequest request, CancellationToken ct = default)
    {
        if (!IsAllowedRedirectUrl(request.CallbackUrl) || !IsAllowedRedirectUrl(request.ErrorUrl))
            return Error.Validation("checkout.redirect_url_not_allowed");
        return await _checkout.StartRenewalAsync(renewalRequestId, request, ct);
    }

    private static IReadOnlyList<FoodPreferenceItemDto> ResolveAllergyItems(SaveFamilyMemberAllergiesRequest request)
    {
        var fromAllergens = (request.Allergens ?? [])
            .Where(x => x.IsSelected && !string.IsNullOrWhiteSpace(x.Code))
            .Select(x => new FoodPreferenceItemDto(x.Code, x.NameAr, x.NameEn, x.Severity))
            .ToList();
        var fromCodes = (request.SelectedCodes ?? request.Codes ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new FoodPreferenceItemDto(x, null, null, null))
            .ToList();
        if (request.Allergies is { Count: > 0 }) return request.Allergies;
        if (request.Selected is { Count: > 0 }) return request.Selected;
        if (fromAllergens.Count > 0) return fromAllergens;
        if (fromCodes.Count > 0) return fromCodes;
        return request.Allergies ?? request.Selected ?? fromAllergens;
    }

    private static string NormalizeIngredientCode(string code)
    {
        if (code.StartsWith("INGREDIENT:", StringComparison.OrdinalIgnoreCase))
            return code;
        var trimmed = code.Trim();
        if (Guid.TryParse(trimmed, out _))
            return $"INGREDIENT:{trimmed.ToLowerInvariant()}";
        return code;
    }

    private static IReadOnlyList<FoodPreferenceItemDto>? ResolveDislikedFoods(
        IReadOnlyList<FoodPreferenceItemDto>? dislikedFoods,
        IReadOnlyList<string>? ingredientCodes)
    {
        if (dislikedFoods is not null)
            return dislikedFoods
                .Select(x => x with { Code = NormalizeIngredientCode(x.Code) })
                .ToList();
        if (ingredientCodes is null)
            return null;
        return ingredientCodes
            .Select(code => new FoodPreferenceItemDto(NormalizeIngredientCode(code), null, null, null))
            .ToList();
    }

    private bool IsAllowedRedirectUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var schemes = _configuration?.GetSection("Checkout:AllowedRedirectSchemes").GetChildren().Select(x => x.Value).Where(x => !string.IsNullOrEmpty(x)).ToList();
        var allowedSchemes = schemes is { Count: > 0 } ? schemes : ["https", "mealmate"];
        if (!allowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)) return false;
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) return true;
        var hosts = _configuration?.GetSection("Checkout:AllowedRedirectHosts").GetChildren().Select(x => x.Value).Where(x => !string.IsNullOrEmpty(x)).ToList();
        var allowedHosts = hosts is { Count: > 0 } ? hosts : ["maelmate.runasp.net", "localhost"];
        return allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsConfiguredMethod(PaymentGatewayMethod method, bool knet, bool card, bool apple)
    {
        var code = method.Code.Trim().ToUpperInvariant();
        var name = method.NameEn.Trim().ToUpperInvariant();
        if (code == "KN" || code == "KNET" || name.Contains("KNET")) return knet;
        if (code == "AP" || name.Contains("APPLE PAY") || name.Contains("APPLEPAY")) return apple;
        if (code == "VM" || name.Contains("VISA/MASTER") || name.Contains("VISA / MASTER") || name.Contains("VISA") || name.Contains("MASTER")) return card;
        return false;
    }
}
