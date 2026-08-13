using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class RestaurantEligibilityServiceTests
{
    [Fact]
    public async Task Ingredient_only_allergen_hard_blocks_restaurant()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);
        var serviceDate = new DateOnly(2026, 8, 17);
        var customerId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(), LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            TradeNameAr = "مطعم", TradeNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true
        };
        var program = new Program { Code = "P", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Code = "B", NameAr = "باقة", NameEn = "Bundle", Status = ProgramStatus.Active, IsActive = true, MealsPerDay = 1 };
        var menu = new Menu { RestaurantId = restaurant.Id, ProgramId = program.Id, NameAr = "وجبات", NameEn = "Meals", IsActive = true };
        var ingredient = new Ingredient { RestaurantId = restaurant.Id, Code = "MILK", NameAr = "حليب", NameEn = "Milk", AllergenCodes = "MILK", OriginCountryCode = "KW", IsActive = true };
        var meal = new Meal
        {
            RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, MenuId = menu.Id, Menu = menu,
            Code = "M", NameAr = "وجبة", NameEn = "Meal", DescriptionAr = "وصف", DescriptionEn = "Description", Slot = "lunch", ApprovalStatus = VerificationStatus.Approved,
            Calories = 500, ProteinG = 40, CarbsG = 50, NetCarbsG = 50, FatG = 20, FiberG = 8,
            IsAvailableForNewOrders = true, AllergensReviewed = true, DataResponsibilityConfirmed = true
        };
        var ingredientLink = new MealIngredient { MealId = meal.Id, Meal = meal, IngredientId = ingredient.Id, Ingredient = ingredient, PortionGrams = 100 };
        var image = new MealImage { MealId = meal.Id, Meal = meal, ImageUrl = "/meal.png", IsPrimary = true };
        var plan = new MenuPlan { RestaurantId = restaurant.Id, NameAr = "خطة", NameEn = "Plan", Source = MenuPlanSource.RestaurantCreated, Mode = ScheduleMode.Cycle };
        var version = new MenuPlanVersion { MenuPlanId = plan.Id, MenuPlan = plan, VersionNumber = 1, EffectiveFrom = serviceDate, Status = VerificationStatus.Approved };
        var day = new MenuDay { MenuPlanVersionId = version.Id, MenuPlanVersion = version, DayNumber = 1, NameAr = "اليوم", NameEn = "Day" };
        var dayMeal = new MenuDayMeal { MenuDayId = day.Id, MenuDay = day, MealId = meal.Id, Meal = meal, ProgramId = program.Id, Slot = "lunch", IsPrimary = true };
        var planProgram = new MenuPlanProgram { MenuPlanId = plan.Id, MenuPlan = plan, ProgramId = program.Id, EffectiveFrom = serviceDate, IsPrimaryOwner = true };
        var run = new ClassificationRun { ProgramId = program.Id, BundleId = bundle.Id, ConfidenceLevel = "high", RunAtUtc = now };
        var existingSubscription = new Subscription { CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, BundleId = bundle.Id, SelectedTier = RestaurantTier.Basic, DurationDays = 1, StartDate = serviceDate, EndDate = serviceDate, Status = SubscriptionStatus.Active, PaidAmount = new Money(10, "KWD") };

        db.AddRange(restaurant, program, bundle, menu, ingredient, meal, ingredientLink, image, plan, version, day, dayMeal, planProgram, run,
            new RestaurantClassification { ClassificationRunId = run.Id, ClassificationRun = run, RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, Tier = RestaurantTier.Basic },
            new RestaurantProgramBundleOffer { RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, IsActive = true },
            new RestaurantServiceRegion { RestaurantId = restaurant.Id, RegionId = regionId, IsActive = true },
            new RestaurantBundlePricing { RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, ReferencePrice26 = new Money(130, "KWD"), ContractedSupplyPrice26 = new Money(100, "KWD"), LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = now.AddDays(-1) },
            new PricingRuleVersion { IsActive = true, RMax = Percentage.FromFraction(.3m), RMin = Percentage.FromFraction(.05m), DShort = 6, DLong = 26, Gamma = 1, RoundingStepKwd = .1m },
            new ProgramMealSlotRequirement { ProgramId = program.Id, Slot = "lunch", IsRequired = true },
            new ProgramNutritionStandard { ProgramId = program.Id, MealCategory = "MAIN", KcalMin = 400, KcalMax = 600, ProteinGMin = 30, ProteinGMax = 50, CarbsGMin = 40, CarbsGMax = 60, FatGMin = 15, FatGMax = 25, FiberGMin = 5 },
            new DailyCapacity { RestaurantId = restaurant.Id, ServiceDate = serviceDate, CapacityType = CapacityType.DateOverride, MaxBoxes = 2 },
            existingSubscription,
            new SubscriptionDay { SubscriptionId = existingSubscription.Id, RestaurantId = restaurant.Id, ServiceDate = serviceDate, ResolvedMenuPlanVersionId = version.Id, Status = SubscriptionDayStatus.Open },
            new CapacityHold { SubscriptionId = existingSubscription.Id, RestaurantId = restaurant.Id, ServiceDate = serviceDate, MenuPlanVersionId = version.Id, Quantity = 1, Status = CapacityHoldStatus.Confirmed, ExpiresAtUtc = now.AddMinutes(10) },
            new CustomerAllergyPreference { CustomerUserId = customerId, AllergenCode = "MILK", IsDislike = false });
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var service = new RestaurantEligibilityService(db, clock);
        var result = await service.EvaluateAsync(new RestaurantEligibilityRequest(program.Id, bundle.Id, RestaurantTier.Basic, regionId, serviceDate, 1, true, customerId));

        var candidate = Assert.Single(result);
        Assert.False(candidate.IsEligible);
        Assert.Equal([EligibilityReasonCodes.NoSafeMeals], candidate.ReasonCodes);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }
}
