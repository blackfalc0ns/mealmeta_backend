using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Storage;
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
    private readonly IMyFatoorahPaymentService _payments;
    private readonly IFamilyCheckoutService _checkout;
    private readonly IConfiguration? _configuration;
    private readonly IHostEnvironment? _environment;
    private readonly ISubscriptionFlowQueryService? _subscriptionFlowQueries;

    public FamilySubscriptionFlowService(
        ISender sender,
        IAppDbContext db,
        IMyFatoorahPaymentService payments,
        IFamilyCheckoutService checkout,
        IConfiguration? configuration = null,
        IHostEnvironment? environment = null,
        ISubscriptionFlowQueryService? subscriptionFlowQueries = null)
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

    public async Task<Result<IReadOnlyList<CustomerProgramCardDto>>> GetProgramsAsync(Guid customerUserId, CancellationToken ct = default)
    {
        if (_subscriptionFlowQueries is not null)
            return await _subscriptionFlowQueries.GetProgramsAsync(customerUserId, ct);

        var programs = await _db.Programs.AsNoTracking()
            .Where(x => x.IsActive && x.Status == ProgramStatus.Active)
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

        IReadOnlyList<CustomerProgramCardDto> result = programs
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
                slots.Where(s => s.ProgramId == x.Id).Select(s => s.Slot).OrderBy(s => s).ToList()))
            .ToList();

        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<CustomerBundleCardDto>>> GetBundlesAsync(Guid programId, CancellationToken ct = default)
    {
        var program = await _db.Programs.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == programId && x.IsActive && x.Status == ProgramStatus.Active,
                ct);
        if (program is null)
            return Error.NotFound("family.catalog_not_available");

        var bundles = await _db.Bundles.AsNoTracking()
            .Where(x => x.ProgramId == programId
                && x.IsActive
                && x.Status == ProgramStatus.Active)
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
        var restaurantCards = restaurantProfiles.ToDictionary(
            x => x.Id,
            x => new CustomerBundleRestaurantDto(x.Id, null));

        IReadOnlyList<CustomerBundleCardDto> result = bundles.Select(x =>
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
        }).ToList();

        return Result.Success(result);
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

    public Task<Result<FamilyMemberLookupDto>> LookupMemberAsync(FamilyMemberLookupRequest request, CancellationToken ct = default)
        => _sender.Send(new LookupFamilyMemberByPhoneQuery(request.Phone), ct);

    public Task<Result<bool>> SaveMemberAllergiesAsync(Guid memberId, SaveFamilyMemberAllergiesRequest request, CancellationToken ct = default)
    {
        var disliked = ResolveDislikedFoods(request.DislikedFoods, request.IngredientCodes);
        return _sender.Send(
            new SaveFamilyMemberAllergiesCommand(
                memberId,
                ResolveAllergyItems(request),
                disliked,
                ReplaceDislikedFoods: disliked is not null || request.IngredientCodes is not null,
                request.DietaryPreferenceCodes,
                ReplaceDietary: request.DietaryPreferenceCodes is not null),
            ct);
    }

    public Task<Result<FamilyMemberAllergiesDto>> GetMemberAllergiesAsync(Guid memberId, CancellationToken ct = default)
        => _sender.Send(new GetFamilyMemberAllergiesQuery(memberId), ct);

    public Task<Result<bool>> SaveMemberDislikedFoodsAsync(Guid memberId, SaveFamilyMemberDislikedFoodsRequest request, CancellationToken ct = default)
    {
        return _sender.Send(
            new SaveFamilyMemberDislikedFoodsCommand(
                memberId,
                ResolveDislikedFoods(request.DislikedFoods, request.IngredientCodes)
                    ?? ResolveDislikedFoods(request.Selected, request.SelectedCodes)
                    ?? []),
            ct);
    }

    public Task<Result<FamilyMemberDislikedFoodsDto>> GetMemberDislikedFoodsAsync(Guid memberId, CancellationToken ct = default)
        => _sender.Send(new GetFamilyMemberDislikedFoodsQuery(memberId), ct);

    public Task<Result<bool>> SaveMemberDietaryAsync(Guid memberId, SaveFamilyMemberDietaryRequest request, CancellationToken ct = default)
        => _sender.Send(new SaveFamilyMemberDietaryCommand(memberId, request.DietaryPreferenceCodes ?? []), ct);

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
                request.IsDefault),
            ct);
    }

    public Task<Result<AddressCoveragePreviewDto>> PreviewMemberAddressCoverageByIdAsync(Guid memberId, Guid addressId, CancellationToken ct = default)
        => _sender.Send(new PreviewFamilyMemberAddressCoverageCommand(memberId, AddressId: addressId), ct);

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

    public Task<Result<FamilyCheckoutSummaryDto>> GetSummaryAsync(Guid? memberId, CancellationToken ct = default)
        => _sender.Send(new GetFamilyCheckoutSummaryQuery(memberId), ct);

    public async Task<Result<IReadOnlyList<CustomerPaymentMethodDto>>> GetPaymentMethodsAsync(decimal amount, CancellationToken ct = default)
    {
        if (_subscriptionFlowQueries is not null)
            return await _subscriptionFlowQueries.GetPaymentMethodsAsync(amount, ct);

        if (amount <= 0) return Error.Validation("subscription_flow.invalid_amount");

        var config = await _db.MyFatoorahGatewayConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.GatewayName == "MyFatoorah", ct);
        if (config is null)
        {
            if (IsMockEnabled())
                return Result.Success<IReadOnlyList<CustomerPaymentMethodDto>>(BuildMock(amount));
            return Error.Conflict("payment.gateway_not_configured");
        }

        IReadOnlyList<MyFatoorahPaymentMethod> methods;
        try
        {
            methods = await _payments.GetPaymentMethodsAsync(amount, ct);
        }
        catch
        {
            return new Error("payment.gateway_unavailable", "MyFatoorah did not return payment methods.", ErrorType.Failure);
        }

        var result = methods
            .Where(x => IsConfiguredMethod(x, config.IsKnetEnabled, config.IsVisaMasterEnabled, config.IsApplePayEnabled))
            .Select(x => new CustomerPaymentMethodDto(x.PaymentMethodId, x.Code, x.NameAr, x.NameEn, "MYFATOORAH_HOSTED", x.ServiceCharge, x.TotalAmount, x.CurrencyCode, true, null, x.ImageUrl))
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
            .Select(x =>
            {
                var (ar, en) = SubscriptionFlowLocalization.SlotName(x.Slot);
                return new CustomerBundleSlotDto(x.Slot, ar, en, x.Count);
            })
            .ToList();

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

    private static IReadOnlyList<FoodPreferenceItemDto>? ResolveDislikedFoods(
        IReadOnlyList<FoodPreferenceItemDto>? dislikedFoods,
        IReadOnlyList<string>? ingredientCodes)
    {
        if (dislikedFoods is not null)
            return dislikedFoods;
        if (ingredientCodes is null)
            return null;
        return ingredientCodes.Select(code => new FoodPreferenceItemDto(code, null, null, null)).ToList();
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

    private bool IsMockEnabled()
        => _environment?.IsDevelopment() == true
            && string.Equals(_configuration?["Payments:AllowMockPaymentMethods"], "true", StringComparison.OrdinalIgnoreCase);

    private static List<CustomerPaymentMethodDto> BuildMock(decimal amount) =>
    [
        new CustomerPaymentMethodDto(1, "KNET", "كي نت", "KNET", "MYFATOORAH_HOSTED", 0m, amount, "KWD", true, null),
        new CustomerPaymentMethodDto(2, "VM", "فيزا / ماستر", "VISA / MASTER", "MYFATOORAH_HOSTED", 0m, amount, "KWD", true, null),
        new CustomerPaymentMethodDto(null, "MEALMATE_WALLET", "محفظة MealMate", "MealMate Wallet", "WALLET", 0m, amount, "KWD", false, "wallet_not_implemented", null),
    ];

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
