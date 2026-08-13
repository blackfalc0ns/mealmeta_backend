using MealMate.Api.Common;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Api.Controllers.V1;

/// <summary>Mobile-first read endpoints for every screen before subscription checkout.</summary>
[Authorize]
[Route("api/v1/customer/subscription-flow")]
public sealed class CustomerSubscriptionFlowController : ApiControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IRestaurantEligibilityService _eligibility;
    private readonly IMenuPlanResolutionService _menus;
    private readonly ISubscriptionScheduleService _schedule;
    private readonly IBundleSlotRequirementService _slots;
    private readonly IMyFatoorahPaymentService _payments;

    public CustomerSubscriptionFlowController(IAppDbContext db, ICurrentUser currentUser, IRestaurantEligibilityService eligibility,
        IMenuPlanResolutionService menus, ISubscriptionScheduleService schedule, IBundleSlotRequirementService slots, IMyFatoorahPaymentService payments)
        => (_db, _currentUser, _eligibility, _menus, _schedule, _slots, _payments) = (db, currentUser, eligibility, menus, schedule, slots, payments);

    [HttpGet("types")]
    public IActionResult Types()
    {
        if (!IsCustomer()) return Forbid();
        return Ok(new[]
        {
            new SubscriptionTypeOptionDto("INDIVIDUAL", "اشتراك فردي", "Individual subscription", "خطة غذائية لشخص واحد", "A meal plan for one person", 1, true, false),
            new SubscriptionTypeOptionDto("FAMILY", "اشتراك عائلي", "Family subscription", "اشتراك أساسي مع إضافة أفراد العائلة بعد التفعيل", "A primary subscription with family members added after activation", 4, true, true)
        });
    }

    [HttpGet("programs")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerProgramCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Programs(CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var programs = await _db.Programs.AsNoTracking()
            .Where(x => x.IsActive && x.Status == ProgramStatus.Active)
            .OrderBy(x => x.SortOrder).Select(x => new { x.Id, x.Code, x.ShortCode, x.NameAr, x.NameEn, x.DescriptionAr, x.DescriptionEn, x.KeywordsAr, x.KeywordsEn }).ToListAsync(ct);
        var ids = programs.Select(x => x.Id).ToList();
        var bundleCounts = await _db.Bundles.AsNoTracking().Where(x => ids.Contains(x.ProgramId) && x.IsActive && x.Status == ProgramStatus.Active)
            .GroupBy(x => x.ProgramId).Select(x => new { ProgramId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.ProgramId, x => x.Count, ct);
        var slots = await _db.ProgramMealSlotRequirements.AsNoTracking().Where(x => ids.Contains(x.ProgramId) && x.IsRequired).ToListAsync(ct);
        return Ok(programs.Where(x => bundleCounts.GetValueOrDefault(x.Id) > 0).Select(x => new CustomerProgramCardDto(
            x.Id, x.Code, x.ShortCode, x.NameAr, x.NameEn, x.DescriptionAr, x.DescriptionEn,
            x.KeywordsAr, x.KeywordsEn, null, bundleCounts.GetValueOrDefault(x.Id),
            slots.Where(s => s.ProgramId == x.Id).Select(s => s.Slot).OrderBy(s => s).ToList())));
    }

    [HttpGet("programs/{programId:guid}/bundles")]
    [ProducesResponseType(typeof(IReadOnlyList<CustomerBundleCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Bundles(Guid programId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var program = await _db.Programs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == programId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        if (program is null) return NotFound(new { error = "subscription_flow.program_not_found" });
        var bundles = await _db.Bundles.AsNoTracking().Where(x => x.ProgramId == programId && x.IsActive && x.Status == ProgramStatus.Active)
            .OrderBy(x => x.SortOrder).ToListAsync(ct);
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
            .Select(x => new { x.Id, x.TradeNameAr, x.TradeNameEn })
            .ToListAsync(ct);
        var activeRestaurantIds = restaurantProfiles.Select(x => x.Id).ToList();
        var branchPhotos = await _db.RestaurantBranches.AsNoTracking()
            .Where(x => activeRestaurantIds.Contains(x.RestaurantRegistrationId) && x.IsActive)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.NameAr)
            .Select(x => new { RestaurantId = x.RestaurantRegistrationId, x.PhotoStorageKey })
            .ToListAsync(ct);

        var restaurantCards = restaurantProfiles.ToDictionary(
            x => x.Id,
            x => new CustomerBundleRestaurantDto(
                x.Id,
                x.TradeNameAr,
                x.TradeNameEn,
                ToPublicRestaurantImageUrl(branchPhotos.FirstOrDefault(p => p.RestaurantId == x.Id)?.PhotoStorageKey)));

        return Ok(bundles.Where(IsBundleSlotConfigurationValid).Select(x =>
        {
            var restaurants = offers.Where(o => o.BundleId == x.Id)
                .Select(o => restaurantCards.GetValueOrDefault(o.RestaurantId))
                .Where(r => r is not null)
                .DistinctBy(r => r!.RestaurantId)
                .OrderBy(r => r!.NameAr)
                .Select(r => r!)
                .ToList();
            return new CustomerBundleCardDto(x.Id, x.ProgramId, x.Code, x.NameAr, x.NameEn,
                x.Description ?? x.DescriptionEn ?? x.DescriptionAr, null, x.MealsPerDay, x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount,
                ToSlotRequirements(x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount), x.SortOrder,
                x.DescriptionAr ?? x.NameAr, x.DescriptionEn ?? x.Description ?? x.NameEn,
                ToDisplaySlots(x.BreakfastCount, x.LunchCount, x.DinnerCount, x.SnackCount),
                restaurants.Count, restaurants.Take(8).ToList());
        }).ToList());
    }

    [HttpGet("configuration")]
    public async Task<IActionResult> Configuration([FromQuery] Guid programId, [FromQuery] Guid bundleId, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var bundle = await _db.Bundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bundleId && x.ProgramId == programId && x.IsActive && x.Status == ProgramStatus.Active, ct);
        if (bundle is null || !IsBundleSlotConfigurationValid(bundle)) return NotFound(new { error = "subscription_flow.bundle_not_found" });
        var catalog = await Sender.Send(new GetCustomerSubscriptionPricingCatalogQuery(), ct);
        if (catalog.IsFailure) return HandleResult(catalog);
        var tierCounts = await _db.RestaurantClassifications.AsNoTracking().Where(x => x.ProgramId == programId && x.BundleId == bundleId)
            .GroupBy(x => x.Tier).Select(x => new { Tier = x.Key, Count = x.Select(v => v.RestaurantId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Tier, x => x.Count, ct);
        var options = catalog.Value.Where(x => x.ProgramId == programId && x.BundleId == bundleId)
            .GroupBy(x => x.Tier).OrderBy(x => x.Key).Select(group => new TierConfigurationDto(group.Key, group.First().TierNameAr, group.First().TierNameEn,
                tierCounts.GetValueOrDefault(group.Key), group.SelectMany(x => x.Prices).DistinctBy(x => x.DurationDays).OrderBy(x => x.DurationDays)
                    .Select(x => new DurationPriceOptionDto(x.DurationDays, DurationNameAr(x.DurationDays), $"{x.DurationDays} days", x.TotalPriceKwd, x.DailyPriceKwd, "KWD")).ToList())).ToList();
        return Ok(new SubscriptionConfigurationDto(programId, bundle.Id, bundle.NameAr, bundle.NameEn, bundle.Description, bundle.MealsPerDay,
            ToSlotRequirements(bundle.BreakfastCount, bundle.LunchCount, bundle.DinnerCount, bundle.SnackCount), options));
    }

    [HttpGet("restaurants")]
    public async Task<IActionResult> Restaurants([FromQuery] SubscriptionFlowContext request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var validation = ValidateContext(request);
        if (validation is not null) return BadRequest(new { error = validation });
        var pricing = await Sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays, IncludeFriday: request.IncludeFriday), ct);
        if (pricing.IsFailure) return HandleResult(pricing);
        var rows = await _eligibility.EvaluateAsync(ToEligibility(request, _currentUser.UserId), ct);
        var eligible = rows.Where(x => x.IsEligible).ToList();
        var dates = _schedule.GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
        var resolved = await _menus.ResolveManyAsync(eligible.Select(x => x.RestaurantId).ToList(), request.ProgramId, dates, ct);
        var mealIdsByRestaurant = resolved.GroupBy(x => x.Key.RestaurantId).ToDictionary(x => x.Key, x => x.SelectMany(v => v.Value.Assignments).Select(a => a.MealId).Distinct().ToList());
        var allMealIds = mealIdsByRestaurant.Values.SelectMany(x => x).Distinct().ToList();
        var images = await _db.MealImages.AsNoTracking().Where(x => allMealIds.Contains(x.MealId)).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder).ToListAsync(ct);
        var regions = await _db.RestaurantServiceRegions.AsNoTracking().Where(x => eligible.Select(e => e.RestaurantId).Contains(x.RestaurantId) && x.RegionId == request.RegionId && x.IsActive).ToListAsync(ct);
        return Ok(eligible.Select(x =>
        {
            var ids = mealIdsByRestaurant.GetValueOrDefault(x.RestaurantId) ?? [];
            var window = regions.FirstOrDefault(r => r.RestaurantId == x.RestaurantId);
            return new CustomerRestaurantCardDto(x.RestaurantId, x.NameAr, x.NameEn, images.FirstOrDefault(i => ids.Contains(i.MealId))?.ImageUrl,
                request.Tier, pricing.Value.FinalCustomerPrice, "KWD", ids.Count, window?.DeliveryWindowStart, window?.DeliveryWindowEnd,
                x.MenuPlanVersionsByDate);
        }));
    }

    [HttpGet("restaurants/{restaurantId:guid}")]
    public async Task<IActionResult> Restaurant(Guid restaurantId, [FromQuery] SubscriptionFlowContext request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var details = await ResolveRestaurantDetailsAsync(restaurantId, request, ct);
        return details.ErrorResult ?? Ok(details.Value);
    }

    [HttpGet("restaurants/{restaurantId:guid}/meals/{mealId:guid}")]
    public async Task<IActionResult> Meal(Guid restaurantId, Guid mealId, [FromQuery] SubscriptionFlowContext request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var details = await ResolveRestaurantDetailsAsync(restaurantId, request, ct);
        if (details.ErrorResult is not null) return details.ErrorResult;
        var meal = details.Value!.MealGroups.SelectMany(x => x.Meals).FirstOrDefault(x => x.MealId == mealId);
        if (meal is null) return NotFound(new { error = "subscription_flow.meal_not_in_resolved_menu" });
        return Ok(meal);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] Guid restaurantId, [FromQuery] SubscriptionFlowContext request, CancellationToken ct)
    {
        if (!IsCustomer()) return Forbid();
        var validation = ValidateContext(request);
        if (validation is not null) return BadRequest(new { error = validation });
        var eligible = await _eligibility.EvaluateAsync(ToEligibility(request, _currentUser.UserId, restaurantId), ct);
        var restaurant = eligible.SingleOrDefault(x => x.IsEligible);
        if (restaurant is null) return BadRequest(new { error = "restaurant.not_eligible" });
        var program = await _db.Programs.AsNoTracking().SingleAsync(x => x.Id == request.ProgramId, ct);
        var bundle = await _db.Bundles.AsNoTracking().SingleAsync(x => x.Id == request.BundleId, ct);
        var price = await Sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays, IncludeFriday: request.IncludeFriday), ct);
        if (price.IsFailure) return HandleResult(price);
        var dates = _schedule.GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
        var slots = await _slots.ResolveAsync(request.ProgramId, request.BundleId, ct);
        return Ok(new SubscriptionOrderSummaryDto(program.Id, program.NameAr, program.NameEn, bundle.Id, bundle.NameAr, bundle.NameEn, null,
            restaurant.RestaurantId, restaurant.NameAr, restaurant.NameEn, request.Tier, request.DurationDays, request.StartDate, dates.Last(), dates,
            slots, price.Value.UnroundedCorePrice, price.Value.CampaignDiscountApplied, price.Value.PromoCodeDiscountApplied,
            price.Value.WalletCreditApplied, price.Value.GatewayCostRecoveryAmount, price.Value.FinalCustomerPrice, price.Value.CurrencyCode,
            true, "POST /api/v1/customer/subscriptions/checkout"));
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

    private async Task<ResolvedRestaurantDetails> ResolveRestaurantDetailsAsync(Guid restaurantId, SubscriptionFlowContext request, CancellationToken ct)
    {
        var validation = ValidateContext(request);
        if (validation is not null) return new(null, BadRequest(new { error = validation }));
        var eligibleRows = await _eligibility.EvaluateAsync(ToEligibility(request, _currentUser.UserId, restaurantId), ct);
        var eligible = eligibleRows.SingleOrDefault(x => x.IsEligible);
        if (eligible is null) return new(null, BadRequest(new { error = "restaurant.not_eligible" }));
        var dates = _schedule.GetServiceDates(request.StartDate, request.DurationDays, request.IncludeFriday);
        var resolved = await _menus.ResolveManyAsync([restaurantId], request.ProgramId, dates, ct);
        var assignments = resolved.Values.SelectMany(x => x.Assignments).DistinctBy(x => x.MealId).OrderBy(x => x.Slot).ThenBy(x => x.SortOrder).ToList();
        var cards = await BuildMealCardsAsync(assignments, restaurantId, ct);
        var window = await _db.RestaurantServiceRegions.AsNoTracking().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.RegionId == request.RegionId && x.IsActive, ct);
        var pricing = await Sender.Send(new PreviewCustomerPricingQuery(request.ProgramId, request.BundleId, request.Tier, request.DurationDays, IncludeFriday: request.IncludeFriday), ct);
        if (pricing.IsFailure) return new(null, HandleResult(pricing));
        var groups = cards.GroupBy(x => x.Slot, StringComparer.OrdinalIgnoreCase).OrderBy(x => SlotOrder(x.Key))
            .Select(x => new CustomerMealGroupDto(x.Key, SlotNameAr(x.Key), SlotNameEn(x.Key), x.OrderBy(m => m.SortOrder).ToList())).ToList();
        return new(new CustomerRestaurantDetailsDto(restaurantId, eligible.NameAr, eligible.NameEn, cards.FirstOrDefault()?.ImageUrl, request.Tier,
            pricing.Value.FinalCustomerPrice, pricing.Value.CurrencyCode, window?.DeliveryWindowStart, window?.DeliveryWindowEnd,
            eligible.MenuPlanVersionsByDate, groups), null);
    }

    private async Task<List<CustomerMealDetailsDto>> BuildMealCardsAsync(IReadOnlyList<ResolvedMenuAssignment> assignments, Guid restaurantId, CancellationToken ct)
    {
        var mealIds = assignments.Select(x => x.MealId).Distinct().ToList();
        var meals = await _db.Meals.AsNoTracking().Where(x => mealIds.Contains(x.Id) && x.RestaurantId == restaurantId && x.ApprovalStatus == VerificationStatus.Approved && x.IsAvailableForNewOrders).ToListAsync(ct);
        var images = await _db.MealImages.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder).ToListAsync(ct);
        var links = await _db.MealIngredients.AsNoTracking().Where(x => mealIds.Contains(x.MealId))
            .Join(_db.Ingredients.AsNoTracking(), x => x.IngredientId, x => x.Id, (link, ingredient) => new { link.MealId, link.PortionGrams, link.SortOrder, Ingredient = ingredient }).ToListAsync(ct);
        var mealPreferences = await _db.MealFoodPreferences.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).ToListAsync(ct);
        var choices = await _db.CustomerAllergyPreferences.AsNoTracking().Where(x => x.CustomerUserId == _currentUser.UserId).ToListAsync(ct);
        var blocked = choices.Where(x => !x.IsDislike).Select(x => FoodCodeNormalizer.Normalize(x.AllergenCode)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disliked = choices.Where(x => x.IsDislike).Select(x => x.AllergenCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dietary = await _db.CustomerDietaryPreferences.AsNoTracking().Where(x => x.CustomerUserId == _currentUser.UserId).Select(x => x.PreferenceCode).ToListAsync(ct);
        var result = new List<CustomerMealDetailsDto>();
        foreach (var assignment in assignments)
        {
            var meal = meals.FirstOrDefault(x => x.Id == assignment.MealId);
            if (meal is null) continue;
            var ingredients = links.Where(x => x.MealId == meal.Id).OrderBy(x => x.SortOrder).ToList();
            var effective = FoodCodeNormalizer.Split(meal.PresentAllergenCodes).Concat(FoodCodeNormalizer.Split(meal.MayContainAllergenCodes))
                .Concat(ingredients.SelectMany(x => FoodCodeNormalizer.Split(x.Ingredient.AllergenCodes))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tags = FoodCodeNormalizer.Split(meal.NutritionTagCodes).Concat(mealPreferences.Where(x => x.MealId == meal.Id).Select(x => x.PreferenceCode)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var reason = effective.Overlaps(blocked) ? "allergy_conflict"
                : ingredients.Any(x => disliked.Contains($"INGREDIENT:{x.Ingredient.Id}")) ? "ingredient_preference_conflict"
                : !dietary.All(x => tags.Contains(x, StringComparer.OrdinalIgnoreCase)) ? "dietary_preference_conflict" : null;
            result.Add(new CustomerMealDetailsDto(meal.Id, meal.NameAr, meal.NameEn, meal.DescriptionAr, meal.DescriptionEn, assignment.Slot, assignment.SortOrder,
                images.FirstOrDefault(x => x.MealId == meal.Id)?.ImageUrl, meal.Calories, meal.ProteinG, meal.CarbsG, meal.NetCarbsG, meal.FatG, meal.FiberG,
                meal.ServingWeightG, FoodCodeNormalizer.Split(meal.PresentAllergenCodes), FoodCodeNormalizer.Split(meal.MayContainAllergenCodes), tags,
                ingredients.Select(x => new CustomerIngredientDetailsDto(x.Ingredient.Id, x.Ingredient.NameAr, x.Ingredient.NameEn, x.PortionGrams,
                    x.Ingredient.IsOriginVisibleToCustomers ? x.Ingredient.OriginCountryCode : null,
                    x.Ingredient.IsOriginVisibleToCustomers ? x.Ingredient.SupplierName : null, x.Ingredient.IsLocal, x.Ingredient.IsHalalCertified,
                    FoodCodeNormalizer.Split(x.Ingredient.AllergenCodes))).ToList(), reason is null, reason));
        }
        return result;
    }

    private bool IsCustomer() => _currentUser.UserType == UserType.Customer && _currentUser.UserId.HasValue;
    private static RestaurantEligibilityRequest ToEligibility(SubscriptionFlowContext r, Guid? userId, Guid? restaurantId = null)
        => new(r.ProgramId, r.BundleId, r.Tier, r.RegionId, r.StartDate, r.DurationDays, r.IncludeFriday, userId, restaurantId);
    private static string? ValidateContext(SubscriptionFlowContext r)
        => r.ProgramId == Guid.Empty || r.BundleId == Guid.Empty || r.RegionId == Guid.Empty || r.DurationDays < 6 || r.StartDate < DateOnly.FromDateTime(DateTime.UtcNow)
            ? "subscription_flow.invalid_context" : null;
    private static bool IsBundleSlotConfigurationValid(MealMate.Domain.ProgramsSubscriptions.Bundle x)
        => x.MealsPerDay > 0 && x.MealsPerDay == x.BreakfastCount + x.LunchCount + x.DinnerCount + x.SnackCount;
    private static List<SlotRequirementDto> ToSlotRequirements(int breakfast, int lunch, int dinner, int snack)
        => new[] { new SlotRequirementDto("breakfast", breakfast), new SlotRequirementDto("lunch", lunch), new SlotRequirementDto("dinner", dinner), new SlotRequirementDto("snack", snack) }.Where(x => x.Count > 0).ToList();
    private static List<CustomerBundleSlotDto> ToDisplaySlots(int breakfast, int lunch, int dinner, int snack)
        => ToSlotRequirements(breakfast, lunch, dinner, snack)
            .Select(x => new CustomerBundleSlotDto(x.Slot, SlotNameAr(x.Slot), SlotNameEn(x.Slot), x.Count))
            .ToList();
    private static string? ToPublicRestaurantImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace('\\', '/').Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out _)) return normalized;
        if (normalized.StartsWith('/')) return normalized;
        return $"/api/v1/uploads/content?key={Uri.EscapeDataString(normalized.TrimStart('/'))}";
    }
    private static string DurationNameAr(int days) => days == 6 ? "6 أيام" : days == 12 ? "12 يومًا" : days == 26 ? "26 يومًا" : $"{days} يومًا";
    private static string SlotNameAr(string slot) => slot.ToLowerInvariant() switch { "breakfast" => "الإفطار", "lunch" => "الغداء", "dinner" => "العشاء", "snack" => "سناك", _ => slot };
    private static string SlotNameEn(string slot) => slot.ToLowerInvariant() switch { "breakfast" => "Breakfast", "lunch" => "Lunch", "dinner" => "Dinner", "snack" => "Snack", _ => slot };
    private static int SlotOrder(string slot) => slot.ToLowerInvariant() switch { "breakfast" => 1, "lunch" => 2, "dinner" => 3, "snack" => 4, _ => 99 };
    private static bool IsConfiguredMethod(MyFatoorahPaymentMethod method, bool knet, bool card, bool apple)
    {
        var code = method.Code.Trim().ToUpperInvariant();
        var name = method.NameEn.Trim().ToUpperInvariant();
        if (code == "KN" || name.Contains("KNET")) return knet;
        if (code == "AP" || name.Contains("APPLE PAY")) return apple;
        if (code == "VM" || name.Contains("VISA/MASTER") || name.Contains("VISA / MASTER")) return card;
        return false;
    }
    private sealed record ResolvedRestaurantDetails(CustomerRestaurantDetailsDto? Value, IActionResult? ErrorResult);
}

public sealed record SubscriptionTypeOptionDto(string Code, string NameAr, string NameEn, string DescriptionAr, string DescriptionEn, int MaxMembers, bool IsAvailable, bool MembersConfiguredAfterPayment);
public sealed record CustomerProgramCardDto(Guid ProgramId, string Code, string? ShortCode, string NameAr, string NameEn,
    string? DescriptionAr, string? DescriptionEn, IReadOnlyList<string> KeywordsAr, IReadOnlyList<string> KeywordsEn,
    string? ImageUrl, int BundleCount, IReadOnlyList<string> SupportedSlots);
public sealed record CustomerBundleCardDto(Guid BundleId, Guid ProgramId, string Code, string NameAr, string NameEn, string? Description, string? ImageUrl,
    int MealsPerDay, int BreakfastCount, int LunchCount, int DinnerCount, int SnackCount, IReadOnlyList<SlotRequirementDto> SlotRequirements, int SortOrder,
    string? DescriptionAr, string? DescriptionEn, IReadOnlyList<CustomerBundleSlotDto> Slots, int RestaurantCount,
    IReadOnlyList<CustomerBundleRestaurantDto> Restaurants);
public sealed record CustomerBundleSlotDto(string Code, string NameAr, string NameEn, int Quantity);
public sealed record CustomerBundleRestaurantDto(Guid RestaurantId, string NameAr, string NameEn, string? ImageUrl);
public sealed record DurationPriceOptionDto(int DurationDays, string NameAr, string NameEn, decimal TotalPrice, decimal DailyPrice, string CurrencyCode);
public sealed record TierConfigurationDto(RestaurantTier Tier, string NameAr, string NameEn, int RestaurantCount, IReadOnlyList<DurationPriceOptionDto> Durations);
public sealed record SubscriptionConfigurationDto(Guid ProgramId, Guid BundleId, string BundleNameAr, string BundleNameEn, string? Description, int MealsPerDay, IReadOnlyList<SlotRequirementDto> SlotRequirements, IReadOnlyList<TierConfigurationDto> Tiers);
public sealed record SubscriptionFlowContext(Guid ProgramId, Guid BundleId, RestaurantTier Tier, int DurationDays, DateOnly StartDate, Guid RegionId, bool IncludeFriday = false);
public sealed record CustomerRestaurantCardDto(Guid RestaurantId, string NameAr, string NameEn, string? ImageUrl, RestaurantTier Tier, decimal FinalPrice, string CurrencyCode, int MealCount, TimeOnly? DeliveryWindowStart, TimeOnly? DeliveryWindowEnd, IReadOnlyList<EligibleMenuDateDto> MenuPlanVersionsByDate);
public sealed record CustomerMealGroupDto(string Slot, string NameAr, string NameEn, IReadOnlyList<CustomerMealDetailsDto> Meals);
public sealed record CustomerRestaurantDetailsDto(Guid RestaurantId, string NameAr, string NameEn, string? CoverImageUrl, RestaurantTier Tier, decimal FinalPrice, string CurrencyCode, TimeOnly? DeliveryWindowStart, TimeOnly? DeliveryWindowEnd, IReadOnlyList<EligibleMenuDateDto> MenuPlanVersionsByDate, IReadOnlyList<CustomerMealGroupDto> MealGroups);
public sealed record CustomerIngredientDetailsDto(Guid IngredientId, string NameAr, string NameEn, decimal PortionGrams, string? OriginCountryCode, string? SupplierName, bool IsLocal, bool IsHalalCertified, IReadOnlyList<string> AllergenCodes);
public sealed record CustomerMealDetailsDto(Guid MealId, string NameAr, string NameEn, string? DescriptionAr, string? DescriptionEn, string Slot, int SortOrder, string? ImageUrl, decimal? Calories, decimal? ProteinG, decimal? CarbsG, decimal? NetCarbsG, decimal? FatG, decimal? FiberG, decimal? ServingWeightG, IReadOnlyList<string> PresentAllergenCodes, IReadOnlyList<string> MayContainAllergenCodes, IReadOnlyList<string> DietaryPreferenceCodes, IReadOnlyList<CustomerIngredientDetailsDto> Ingredients, bool IsSelectable, string? BlockReason);
public sealed record SubscriptionOrderSummaryDto(Guid ProgramId, string ProgramNameAr, string ProgramNameEn, Guid BundleId, string BundleNameAr, string BundleNameEn, string? ImageUrl, Guid RestaurantId, string RestaurantNameAr, string RestaurantNameEn, RestaurantTier Tier, int DurationDays, DateOnly StartDate, DateOnly EndDate, IReadOnlyList<DateOnly> ServiceDates, IReadOnlyList<SlotRequirementDto> SlotRequirements, decimal BasePrice, decimal CampaignDiscount, decimal PromoDiscount, decimal WalletCredit, decimal GatewayFee, decimal FinalPrice, string CurrencyCode, bool TermsAcceptanceRequired, string CheckoutEndpoint);
public sealed record CustomerPaymentMethodDto(int? PaymentMethodId, string Code, string NameAr, string NameEn, string IntegrationType, decimal ServiceCharge, decimal TotalAmount, string CurrencyCode, bool IsAvailable, string? UnavailableReason);
