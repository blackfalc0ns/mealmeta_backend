using MealMate.Application.Common;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.ProgramsSubscriptions.Flow;

public interface ISubscriptionCatalogPreparationService
{
    Task<IReadOnlyDictionary<Guid, HashSet<Guid>>> ResolveAsync(
        Guid customerUserId, Guid regionId, IReadOnlyCollection<Guid> bundleIds, CancellationToken ct = default);
}

/// <summary>Date-free preparation, never a promise of capacity or menus on selected dates.
/// A restaurant must offer a covered, priced bundle and own enough safe, approved meals
/// assigned to its program. Full-period eligibility remains authoritative after date selection.</summary>
public sealed class SubscriptionCatalogPreparationService(IAppDbContext db, IDateTimeProvider clock)
    : ISubscriptionCatalogPreparationService
{
    public async Task<IReadOnlyDictionary<Guid, HashSet<Guid>>> ResolveAsync(
        Guid customerUserId, Guid regionId, IReadOnlyCollection<Guid> bundleIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, HashSet<Guid>>();
        if (customerUserId == Guid.Empty || regionId == Guid.Empty || bundleIds.Count == 0) return result;
        var bundles = await db.Bundles.AsNoTracking()
            .Where(x => bundleIds.Contains(x.Id) && x.IsActive && x.Status == ProgramStatus.Active
                && x.Program.IsActive && x.Program.Status == ProgramStatus.Active).ToListAsync(ct);
        var now = clock.UtcNow;
        if (!await db.PricingRuleVersions.AsNoTracking().AnyAsync(x => x.IsActive, ct)) return result;
        var candidates = await (from offer in db.RestaurantProgramBundleOffers.AsNoTracking()
            join bundle in db.Bundles.AsNoTracking() on offer.BundleId equals bundle.Id
            join restaurant in db.RestaurantProfiles.AsNoTracking() on offer.RestaurantId equals restaurant.Id
            where bundleIds.Contains(bundle.Id) && offer.ProgramId == bundle.ProgramId && offer.IsActive
                && restaurant.VerificationStatus == VerificationStatus.Approved && restaurant.IsOperationallyActive
                && db.RestaurantServiceRegions.Any(x => x.RestaurantId == restaurant.Id && x.RegionId == regionId && x.IsActive)
                && db.RestaurantBundlePricings.Any(x => x.RestaurantId == restaurant.Id && x.ProgramId == bundle.ProgramId
                    && x.BundleId == bundle.Id && x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || x.EffectiveToUtc > now)
                    && (x.LockStatus == PriceLockStatus.Locked || x.LockStatus == PriceLockStatus.Approved
                        || x.LockStatus == PriceLockStatus.ApprovedWithEffectiveDate)
                    && x.ReferencePrice26.Amount > 0 && x.ContractedSupplyPrice26.Amount > 0)
            select new { BundleId = bundle.Id, bundle.ProgramId, RestaurantId = restaurant.Id }).Distinct().ToListAsync(ct);
        var restaurantIds = candidates.Select(x => x.RestaurantId).Distinct().ToList();
        var meals = await db.Meals.AsNoTracking().Where(x => restaurantIds.Contains(x.RestaurantId)
                && x.ApprovalStatus == VerificationStatus.Approved && x.IsAvailableForNewOrders)
            .Select(x => new { x.Id, x.RestaurantId, x.Slot }).ToListAsync(ct);
        var mealIds = meals.Select(x => x.Id).ToList();
        var preferences = await MealPreferenceFilter.LoadAsync(db, customerUserId, ct);
        foreach (var programGroup in bundles.GroupBy(x => x.ProgramId))
        {
            var safeIds = await SubscriptionMealSafety.ResolveAsync(db, mealIds, programGroup.Key, preferences, ct);
            foreach (var bundle in programGroup)
            {
                var required = new[] { ("breakfast", bundle.BreakfastCount), ("lunch", bundle.LunchCount),
                    ("dinner", bundle.DinnerCount), ("snack", bundle.SnackCount) };
                if (required.Any(x => x.Item2 < 0) || bundle.MealsPerDay <= 0
                    || required.Sum(x => x.Item2) != bundle.MealsPerDay) continue;
                var eligible = candidates.Where(x => x.BundleId == bundle.Id).Select(x => x.RestaurantId)
                    .Where(id => required.All(slot => meals.Where(x => x.RestaurantId == id && safeIds.Contains(x.Id)
                        && string.Equals(x.Slot, slot.Item1, StringComparison.OrdinalIgnoreCase))
                        .Select(x => x.Id).Distinct().Count() >= slot.Item2)).ToHashSet();
                result[bundle.Id] = eligible;
            }
        }
        return result;
    }
}
