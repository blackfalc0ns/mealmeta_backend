using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.ValueObjects;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;

namespace MealMate.UnitTests.ProgramsSubscriptions;

internal static class FreezeTestFixture
{
    internal static SubscriptionOperationsHandlers CreateHandler(MealMate.Persistence.AppDbContext db, IDateTimeProvider clock)
    {
        var schedule = new SubscriptionScheduleService(clock);
        var menus = new MenuPlanResolutionService(db);
        var slots = new BundleSlotRequirementService(db);
        var audit = NSubstitute.Substitute.For<IAuditLogger>();
        var policies = new MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicyService(db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), audit, clock);
        var reader = new RestaurantCapacityReader(db, clock);
        var eligibility = new RestaurantEligibilityService(db, clock, schedule, slots, menus, new MealMate.Application.Features.ProgramsSubscriptions.Pricing.RestaurantTierClassificationService(db, clock), reader);
        var prep = new MealMate.Application.Features.ProgramsSubscriptions.Freeze.SubscriptionFreezePreparationService(db, clock, schedule, policies, eligibility, new RestaurantPoolSnapshotService(db, menus, slots, clock), NSubstitute.Substitute.For<ICapacityConcurrencyLock>(), NSubstitute.Substitute.For<IApplicationLockManager>(), reader);
        return new SubscriptionOperationsHandlers(db, audit, clock, NSubstitute.Substitute.For<MealMate.Application.Features.ProgramsSubscriptions.ISubscriptionTierUpgradeQuoteBuilder>(), schedule: schedule, freezePreparation: prep);
    }
    internal static async Task<Subscription> SeedMenuPoolAsync(MealMate.Persistence.AppDbContext db, DateTime now, DateOnly date, bool capacity = true)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "FREEZE", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Code = "FREEZE", NameAr = "باقة", NameEn = "Bundle", Status = ProgramStatus.Active, IsActive = true, MealsPerDay = 1, LunchCount = 1 };
        var region = Guid.NewGuid();
        var restaurant = new RestaurantProfile { UserId = Guid.NewGuid(), LegalNameAr = "مطعم", LegalNameEn = "Restaurant", TradeNameAr = "مطعم", TradeNameEn = "Restaurant", VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true };
        var sub = new Subscription { CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, BundleId = bundle.Id, SelectedTier = RestaurantTier.Basic, RegionId = region, StartDate = date, EndDate = date, IncludeFriday = true, Status = SubscriptionStatus.Active, DurationDays = 1, PaidAmount = new Money(50m, "KWD") };
        var menu = new Menu { RestaurantId = restaurant.Id, ProgramId = program.Id, NameAr = "قائمة", NameEn = "Menu", IsActive = true };
        var ingredient = new Ingredient { RestaurantId = restaurant.Id, Code = "RICE", NameAr = "أرز", NameEn = "Rice", OriginCountryCode = "KW", IsActive = true };
        var meal = new Meal { RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, MenuId = menu.Id, Code = "FRESH", NameAr = "وجبة جديدة", NameEn = "Fresh meal", DescriptionAr = "وصف", DescriptionEn = "Description", Slot = "lunch", ApprovalStatus = VerificationStatus.Approved, Calories = 500, ProteinG = 40, CarbsG = 50, NetCarbsG = 50, FatG = 20, FiberG = 8, IsAvailableForNewOrders = true, AllergensReviewed = true, DataResponsibilityConfirmed = true };
        var plan = new MenuPlan { RestaurantId = restaurant.Id, NameAr = "خطة", NameEn = "Plan", Source = MenuPlanSource.RestaurantCreated, Mode = ScheduleMode.Cycle };
        var version = new MenuPlanVersion { MenuPlanId = plan.Id, VersionNumber = 2, EffectiveFrom = date.AddDays(1), Status = VerificationStatus.Approved };
        var menuDay = new MenuDay { MenuPlanVersionId = version.Id, DayNumber = 1, NameAr = "يوم", NameEn = "Day" };
        var run = new ClassificationRun { ProgramId = program.Id, BundleId = bundle.Id, ConfidenceLevel = "high", RunAtUtc = now };
        var pool = new SubscriptionRestaurantPoolSnapshot { SubscriptionId = sub.Id, RestaurantCount = 1, EligibilityCheckedAtUtc = now };
        var captured = new SubscriptionRestaurantSnapshot { SubscriptionId = sub.Id, PoolSnapshotId = pool.Id, RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, RegionId = region, Tier = RestaurantTier.Basic, NameAr = "مطعم", NameEn = "Restaurant", IsInitiallySelected = true };
        var staleVersion = Guid.NewGuid();
        db.AddRange(program, bundle, restaurant, sub, menu, ingredient, meal, plan, version, menuDay, run, pool, captured,
            new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = date, RestaurantId = restaurant.Id, Status = SubscriptionDayStatus.CustomerSelected, SelectionDeadlineUtc = now.AddDays(5), DeliveryRegionId = region, ResolvedMenuPlanVersionId = staleVersion },
            new CapacityHold { SubscriptionId = sub.Id, RestaurantId = restaurant.Id, ServiceDate = date, MenuPlanVersionId = staleVersion, Status = CapacityHoldStatus.Confirmed, ConfirmedAtUtc = now, ExpiresAtUtc = now },
            new SubscriptionRestaurantDaySnapshot { SubscriptionId = sub.Id, RestaurantSnapshotId = captured.Id, RestaurantId = restaurant.Id, ServiceDate = date, MenuPlanVersionId = staleVersion, SlotRequirementsJson = "[]", MealsJson = "[]", ContentHash = "old" },
            new MealIngredient { MealId = meal.Id, IngredientId = ingredient.Id, PortionGrams = 100 }, new MealImage { MealId = meal.Id, ImageUrl = "/meal.png", IsPrimary = true },
            new MenuDayMeal { MenuDayId = menuDay.Id, MealId = meal.Id, ProgramId = program.Id, Slot = "lunch", IsPrimary = true },
            new MenuPlanProgram { MenuPlanId = plan.Id, ProgramId = program.Id, EffectiveFrom = date.AddDays(1), IsPrimaryOwner = true },
            new RestaurantClassification { ClassificationRunId = run.Id, RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, Tier = RestaurantTier.Basic },
            new RestaurantProgramBundleOffer { RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, IsActive = true }, new RestaurantServiceRegion { RestaurantId = restaurant.Id, RegionId = region, IsActive = true },
            new RestaurantBundlePricing { RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, ReferencePrice26 = new Money(110m, "KWD"), ContractedSupplyPrice26 = new Money(100m, "KWD"), LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = now.AddDays(-1) },
            new PricingRuleVersion { IsActive = true, RMax = Percentage.FromFraction(.3m), RMin = Percentage.FromFraction(.05m), DShort = 6, DLong = 26, Gamma = 1, RoundingStepKwd = .1m },
            new ProgramMealSlotRequirement { ProgramId = program.Id, Slot = "lunch", IsRequired = true },
            new ProgramNutritionStandard { ProgramId = program.Id, MealCategory = "MAIN", KcalMin = 400, KcalMax = 600, ProteinGMin = 30, ProteinGMax = 50, CarbsGMin = 40, CarbsGMax = 60, FatGMin = 15, FatGMax = 25, FiberGMin = 5 });
        foreach (var dow in Enum.GetValues<DayOfWeek>()) db.RestaurantOperatingSchedules.Add(new RestaurantOperatingSchedule { RestaurantId = restaurant.Id, DayOfWeek = dow, IsWorkingDay = true });
        if (capacity) db.DailyCapacities.Add(new DailyCapacity { RestaurantId = restaurant.Id, ServiceDate = date.AddDays(1), CapacityType = CapacityType.DateOverride, MaxBoxes = 1 });
        await db.SaveChangesAsync();
        return sub;
    }
}
