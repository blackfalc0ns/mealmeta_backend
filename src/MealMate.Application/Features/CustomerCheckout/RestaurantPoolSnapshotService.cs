using System.Text.Json;
using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.CustomerCheckout;

public sealed record RestaurantPoolCaptureResult(bool IsSuccess, Guid? PoolSnapshotId, int RestaurantCount, string? ErrorCode = null);

public interface IRestaurantPoolSnapshotService
{
    Task<RestaurantPoolCaptureResult> CaptureAsync(Guid subscriptionId, IReadOnlyList<RestaurantEligibilityDto> eligibleRestaurants, Guid selectedRestaurantId, CancellationToken cancellationToken = default);
    Task<bool> CopyDayToFulfillmentSnapshotAsync(Guid subscriptionId, Guid subscriptionDayId, Guid restaurantId, DateOnly serviceDate, CancellationToken cancellationToken = default);
}

public sealed class RestaurantPoolSnapshotService : IRestaurantPoolSnapshotService
{
    private readonly IAppDbContext _db;
    private readonly IMenuPlanResolutionService _menus;
    private readonly IBundleSlotRequirementService _slots;
    private readonly IDateTimeProvider _clock;

    public RestaurantPoolSnapshotService(IAppDbContext db, IMenuPlanResolutionService menus, IBundleSlotRequirementService slots, IDateTimeProvider clock)
        => (_db, _menus, _slots, _clock) = (db, menus, slots, clock);

    public async Task<RestaurantPoolCaptureResult> CaptureAsync(Guid subscriptionId, IReadOnlyList<RestaurantEligibilityDto> eligibleRestaurants, Guid selectedRestaurantId, CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscriptionId, ct);
        if (subscription is null) return new(false, null, 0, "subscription.not_found");
        var candidates = eligibleRestaurants.Where(x => x.IsEligible).DistinctBy(x => x.RestaurantId).ToList();
        if (candidates.Count == 0 || candidates.All(x => x.RestaurantId != selectedRestaurantId))
            return new(false, null, 0, "checkout.selected_restaurant_not_in_pool");
        if (await _db.SubscriptionRestaurantPoolSnapshots.AsNoTracking().AnyAsync(x => x.SubscriptionId == subscriptionId, ct))
            return new(false, null, 0, "checkout.restaurant_pool_already_exists");

        var slotRequirements = await _slots.ResolveAsync(subscription.ProgramId, subscription.BundleId, ct);
        if (slotRequirements.Count == 0) return new(false, null, 0, "checkout.bundle_slots_invalid");
        var slotJson = JsonSerializer.Serialize(slotRequirements);
        var prefs = await MealPreferenceFilter.LoadAsync(_db, subscription.CustomerUserId, ct);
        var resolvedMenus = await _menus.ResolveManyAsync(candidates.Select(x => x.RestaurantId).ToList(), subscription.ProgramId,
            candidates.SelectMany(x => x.MenuPlanVersionsByDate).Select(x => x.ServiceDate).Distinct().ToList(), ct);

        var pool = new SubscriptionRestaurantPoolSnapshot
        {
            SubscriptionId = subscriptionId,
            EligibilityCheckedAtUtc = _clock.UtcNow,
            RestaurantCount = candidates.Count
        };
        _db.SubscriptionRestaurantPoolSnapshots.Add(pool);

        foreach (var candidate in candidates)
        {
            var restaurant = new SubscriptionRestaurantSnapshot
            {
                PoolSnapshotId = pool.Id,
                SubscriptionId = subscriptionId,
                RestaurantId = candidate.RestaurantId,
                ProgramId = subscription.ProgramId,
                BundleId = subscription.BundleId,
                RegionId = subscription.RegionId!.Value,
                Tier = subscription.SelectedTier,
                NameAr = candidate.NameAr,
                NameEn = candidate.NameEn,
                IsInitiallySelected = candidate.RestaurantId == selectedRestaurantId,
                OfferingSnapshotJson = JsonSerializer.Serialize(new { subscription.ProgramId, subscription.BundleId, subscription.SelectedTier }),
                CapturedAtUtc = _clock.UtcNow
            };
            _db.SubscriptionRestaurantSnapshots.Add(restaurant);
            foreach (var expected in candidate.MenuPlanVersionsByDate.OrderBy(x => x.ServiceDate))
            {
                var resolved = resolvedMenus.GetValueOrDefault((candidate.RestaurantId, expected.ServiceDate));
                if (resolved is null || resolved.MenuPlanVersionId != expected.MenuPlanVersionId)
                    return new(false, null, 0, "checkout.menu_resolution_changed");
                var payload = await BuildFrozenMealsAsync(resolved.Assignments, candidate.RestaurantId, ct);
                if (slotRequirements.Any(slot => payload.Count(x => string.Equals(x.Slot, slot.Slot, StringComparison.OrdinalIgnoreCase) && MealPreferenceFilter.BlockReason(x, prefs) is null) < slot.Count))
                    return new(false, null, 0, "checkout.safe_meal_count_insufficient");
                var mealsJson = JsonSerializer.Serialize(payload);
                _db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot
                {
                    RestaurantSnapshotId = restaurant.Id,
                    SubscriptionId = subscriptionId,
                    RestaurantId = candidate.RestaurantId,
                    ServiceDate = expected.ServiceDate,
                    MenuPlanVersionId = resolved.MenuPlanVersionId,
                    SlotRequirementsJson = slotJson,
                    MealsJson = mealsJson,
                    ContentHash = FrozenSnapshotIntegrity.ComputeContentHash(slotJson, mealsJson),
                    CapturedAtUtc = _clock.UtcNow
                });
            }
        }
        await _db.SaveChangesAsync(ct);
        return new(true, pool.Id, candidates.Count);
    }

    public async Task<bool> CopyDayToFulfillmentSnapshotAsync(Guid subscriptionId, Guid subscriptionDayId, Guid restaurantId, DateOnly serviceDate, CancellationToken ct = default)
    {
        if (await _db.SubscriptionDayMealSnapshots.AsNoTracking().AnyAsync(x => x.SubscriptionDayId == subscriptionDayId, ct)) return true;
        var source = await _db.SubscriptionRestaurantDaySnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.SubscriptionId == subscriptionId && x.RestaurantId == restaurantId && x.ServiceDate == serviceDate, ct);
        if (source is null) return false;
        var subscription = await _db.Subscriptions.AsNoTracking().FirstAsync(x => x.Id == subscriptionId, ct);
        _db.SubscriptionDayMealSnapshots.Add(new MealMate.Domain.CalendarMeals.SubscriptionDayMealSnapshot
        {
            SubscriptionId = subscriptionId,
            SubscriptionDayId = subscriptionDayId,
            ServiceDate = serviceDate,
            RestaurantId = restaurantId,
            ProgramId = subscription.ProgramId,
            MenuPlanVersionId = source.MenuPlanVersionId,
            RequiredSlotsJson = source.SlotRequirementsJson,
            MealsJson = source.MealsJson,
            CapturedAtUtc = _clock.UtcNow
        });
        return true;
    }

    private async Task<List<MealSnapshotItem>> BuildFrozenMealsAsync(IReadOnlyList<ResolvedMenuAssignment> assignments, Guid restaurantId, CancellationToken ct)
    {
        var mealIds = assignments.Select(x => x.MealId).Distinct().ToList();
        var meals = await _db.Meals.AsNoTracking().Where(x => mealIds.Contains(x.Id) && x.RestaurantId == restaurantId && x.ApprovalStatus == VerificationStatus.Approved && x.IsAvailableForNewOrders && x.AllergensReviewed && x.DataResponsibilityConfirmed).ToListAsync(ct);
        var images = await _db.MealImages.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.DisplayOrder).ToListAsync(ct);
        var links = await _db.MealIngredients.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).Join(_db.Ingredients.AsNoTracking(), x => x.IngredientId, x => x.Id, (link, ingredient) => new { link.MealId, ingredient }).ToListAsync(ct);
        var mealPrefs = await _db.MealFoodPreferences.AsNoTracking().Where(x => mealIds.Contains(x.MealId)).ToListAsync(ct);
        var ingredientIds = links.Select(x => x.ingredient.Id).Distinct().ToList();
        var ingredientPrefs = (await _db.IngredientFoodPreferences.AsNoTracking().Where(x => ingredientIds.Contains(x.IngredientId)).ToListAsync(ct))
            .Select(x => (x.IngredientId, x.PreferenceCode)).ToList();
        var result = new List<MealSnapshotItem>();
        foreach (var assignment in assignments)
        {
            var meal = meals.FirstOrDefault(x => x.Id == assignment.MealId);
            if (meal is null || images.All(x => x.MealId != meal.Id)) continue;
            var ingredients = links.Where(x => x.MealId == meal.Id).Select(x => x.ingredient).ToList();
            if (ingredients.Count == 0 || ingredients.Any(x => !x.IsActive || string.IsNullOrWhiteSpace(x.OriginCountryCode))) continue;
            var named = ingredients.Select(x => new MealPreferenceFilter.NamedIngredient(x.Id, x.NameAr, x.NameEn, FoodCodeNormalizer.Split(x.AllergenCodes))).ToList();
            var tags = MealPreferenceFilter.DeriveDietaryTags(meal.NutritionTagCodes, mealPrefs.Where(x => x.MealId == meal.Id).Select(x => x.PreferenceCode), named, ingredientPrefs);
            var frozenIngredients = ingredients.Select(x => new MealSnapshotIngredient(
                x.Id, x.NameAr, x.NameEn, x.OriginCountryCode, x.SupplierName, x.IsLocal, x.IsHalalCertified,
                FoodCodeNormalizer.Split(x.AllergenCodes), x.IsOriginVisibleToCustomers, x.IsCore, x.MealIconUrl)).ToList();
            result.Add(new MealSnapshotItem(meal.Id, meal.NameAr, meal.NameEn, meal.DescriptionAr, meal.DescriptionEn, assignment.Slot, assignment.SortOrder,
                images.First(x => x.MealId == meal.Id).ImageUrl, meal.Calories, meal.ProteinG, meal.CarbsG, meal.FatG,
                FoodCodeNormalizer.Split(meal.PresentAllergenCodes), FoodCodeNormalizer.Split(meal.MayContainAllergenCodes), tags, frozenIngredients));
        }
        return result;
    }
}

