using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;

var db = new AppDbContextFactory().CreateDbContext([]);
var now = DateTime.UtcNow;

if (args.FirstOrDefault() == "audit-payment")
{
    var subscriptionId = Guid.Parse("ccfb8bcc-2010-47fc-b743-4f3fd6377a42");
    var subscription = await db.Subscriptions.IgnoreQueryFilters().SingleAsync(x => x.Id == subscriptionId);
    Console.WriteLine($"SUBSCRIPTION status={subscription.Status} paid={subscription.PaidAmount} fulfillmentAt={subscription.FulfillmentCompletedAtUtc:o} error={subscription.LastFulfillmentErrorCode}");
    foreach (var hold in await db.CapacityHolds.IgnoreQueryFilters().Where(x => x.SubscriptionId == subscriptionId).OrderBy(x => x.ServiceDate).ToListAsync())
        Console.WriteLine($"HOLD date={hold.ServiceDate} status={hold.Status} expires={hold.ExpiresAtUtc:o}");
    Console.WriteLine($"POOL={await db.SubscriptionRestaurantPoolSnapshots.IgnoreQueryFilters().CountAsync(x => x.SubscriptionId == subscriptionId)} RESTAURANTS={await db.SubscriptionRestaurantSnapshots.IgnoreQueryFilters().CountAsync(x => x.SubscriptionId == subscriptionId)} RESTAURANT_DAYS={await db.SubscriptionRestaurantDaySnapshots.IgnoreQueryFilters().CountAsync(x => x.SubscriptionId == subscriptionId)}");
    Console.WriteLine($"FULFILLMENT_DAYS={await db.SubscriptionDays.IgnoreQueryFilters().CountAsync(x => x.SubscriptionId == subscriptionId)} MEAL_SNAPSHOTS={await db.SubscriptionDayMealSnapshots.IgnoreQueryFilters().CountAsync(x => x.SubscriptionId == subscriptionId)} WEBHOOK_RECEIPTS={await db.PaymentWebhookReceipts.IgnoreQueryFilters().CountAsync(x => x.SubscriptionId == subscriptionId)}");
    foreach (var operation in await db.SubscriptionOperations.IgnoreQueryFilters().Where(x => x.SubscriptionId == subscriptionId).OrderBy(x => x.TimestampUtc).ToListAsync())
        Console.WriteLine($"OP {operation.TimestampUtc:o} {operation.OperationType} {operation.PreviousStatus}->{operation.NewStatus} reason={operation.Reason}");
    return;
}

if (args.FirstOrDefault() is "contact-valid" or "contact-restore")
{
    var customer = await db.Users.SingleAsync(x => x.Id == Guid.Parse("49d159ac-5025-4acd-a990-ab52f0f83837"));
    if (args[0] == "contact-valid")
    {
        customer.PhoneNumber = "96555546681";
        customer.Email = "codex.checkout@mealmate.app";
        customer.NormalizedEmail = "CODEX.CHECKOUT@MEALMATE.APP";
    }
    else
    {
        customer.PhoneNumber = "+96555546681";
    }
    await db.SaveChangesAsync();
    Console.WriteLine(args[0]);
    return;
}

var programId = Guid.Parse("b59a2251-ba7f-4a78-a6bb-d43defa2e4d2");
var bundleId = Guid.Parse("a6497534-efb2-4744-b190-9ac81a5b5102");
var regionId = Guid.Parse("6590e7e9-5951-4977-895c-fc586b6a2952");
var restaurantId = Guid.Parse("97db2bbb-b652-4aaa-8584-3414e6966792");

var program = await db.Programs.IgnoreQueryFilters().SingleAsync(x => x.Id == programId);
var bundle = await db.Bundles.IgnoreQueryFilters().SingleAsync(x => x.Id == bundleId);
bundle.MealsPerDay = bundle.BreakfastCount + bundle.LunchCount + bundle.DinnerCount + bundle.SnackCount;

foreach (var slot in new[] { "lunch", "snack" })
{
    var requirement = await db.ProgramMealSlotRequirements.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.ProgramId == programId && x.Slot == slot);
    if (requirement is null)
        db.ProgramMealSlotRequirements.Add(new ProgramMealSlotRequirement { ProgramId = programId, Slot = slot, IsRequired = true, CreatedAtUtc = now });
    else
        requirement.IsRequired = true;
}

var offer = await db.RestaurantProgramBundleOffers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.ProgramId == programId && x.BundleId == bundleId);
if (offer is null)
    db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer { RestaurantId = restaurantId, ProgramId = programId, BundleId = bundleId, IsActive = true, CreatedAtUtc = now });
else
    offer.IsActive = true;

var pricing = await db.RestaurantBundlePricings.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.ProgramId == programId && x.BundleId == bundleId && x.EffectiveToUtc == null);
if (pricing is null)
{
    db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
    {
        RestaurantId = restaurantId,
        ProgramId = programId,
        BundleId = bundleId,
        ReferencePrice26 = new Money(260m, "KWD"),
        ContractedSupplyPrice26 = new Money(208m, "KWD"),
        LockStatus = PriceLockStatus.Locked,
        EffectiveFromUtc = now.AddDays(-1),
        CreatedAtUtc = now
    });
}
else
{
    pricing.ReferencePrice26 = new Money(260m, "KWD");
    pricing.ContractedSupplyPrice26 = new Money(208m, "KWD");
    pricing.LockStatus = PriceLockStatus.Locked;
    pricing.EffectiveFromUtc = now.AddDays(-1);
}

if (!await db.PricingRuleVersions.IgnoreQueryFilters().AnyAsync(x => x.IsActive))
{
    db.PricingRuleVersions.Add(new PricingRuleVersion
    {
        RMax = Percentage.FromFraction(.30m),
        RMin = Percentage.FromFraction(.05m),
        DShort = 6,
        DLong = 26,
        Gamma = 1m,
        RoundingStepKwd = .1m,
        IsActive = true,
        CreatedAtUtc = now
    });
}

var latestRun = await db.ClassificationRuns.IgnoreQueryFilters()
    .Where(x => x.ProgramId == programId && x.BundleId == bundleId)
    .OrderByDescending(x => x.RunAtUtc).FirstOrDefaultAsync();
if (latestRun is null || !await db.RestaurantClassifications.IgnoreQueryFilters().AnyAsync(x => x.ClassificationRunId == latestRun.Id && x.RestaurantId == restaurantId && x.Tier == RestaurantTier.Basic))
{
    var run = new ClassificationRun
    {
        ProgramId = programId,
        BundleId = bundleId,
        MeanReferenceDaily = 10m,
        SdReferenceDaily = 0m,
        LowerBound = 10m,
        UpperBound = 10m,
        RestaurantCount = 1,
        ConfidenceLevel = "Sandbox",
        RunAtUtc = now,
        Notes = "Codex sandbox checkout seed",
        CreatedAtUtc = now
    };
    db.ClassificationRuns.Add(run);
    db.RestaurantClassifications.Add(new RestaurantClassification
    {
        ClassificationRunId = run.Id,
        RestaurantId = restaurantId,
        ProgramId = programId,
        BundleId = bundleId,
        Tier = RestaurantTier.Basic,
        ReferenceDailyBox = 10m,
        CreatedAtUtc = now
    });
}

var serviceRegion = await db.RestaurantServiceRegions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.RegionId == regionId);
if (serviceRegion is null)
    db.RestaurantServiceRegions.Add(new RestaurantServiceRegion { RestaurantId = restaurantId, RegionId = regionId, IsActive = true, DeliveryWindowStart = new TimeOnly(8), DeliveryWindowEnd = new TimeOnly(14), CreatedAtUtc = now });
else
    serviceRegion.IsActive = true;

foreach (var day in Enum.GetValues<DayOfWeek>())
{
    var schedule = await db.RestaurantOperatingSchedules.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.DayOfWeek == day);
    if (schedule is null)
        db.RestaurantOperatingSchedules.Add(new RestaurantOperatingSchedule { RestaurantId = restaurantId, DayOfWeek = day, IsWorkingDay = true, CreatedAtUtc = now });
    else
        schedule.IsWorkingDay = true;
}

var baseCapacity = await db.DailyCapacities.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.CapacityType == CapacityType.Base && x.CampaignId == null);
if (baseCapacity is null)
    db.DailyCapacities.Add(new DailyCapacity { RestaurantId = restaurantId, ServiceDate = new DateOnly(2026, 8, 1), CapacityType = CapacityType.Base, MaxBoxes = 50, ReservedBoxes = 0, CreatedAtUtc = now });
else
{
    baseCapacity.ServiceDate = new DateOnly(2026, 8, 1);
    baseCapacity.MaxBoxes = Math.Max(baseCapacity.MaxBoxes, 50);
}

var menu = await db.Menus.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.NameEn == "Codex Sandbox Checkout Menu");
if (menu is null)
{
    menu = new Menu { RestaurantId = restaurantId, ProgramId = programId, NameAr = "قائمة اختبار الاشتراك", NameEn = "Codex Sandbox Checkout Menu", IsActive = true, CreatedAtUtc = now };
    db.Menus.Add(menu);
}
else
    menu.IsActive = true;

async Task<Meal> EnsureMeal(string code, string slot, string nameAr, string nameEn, decimal calories, decimal protein, decimal carbs, decimal fat, decimal fiber)
{
    var meal = await db.Meals.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.Code == code);
    if (meal is null)
    {
        meal = new Meal
        {
            RestaurantId = restaurantId,
            MenuId = menu.Id,
            ProgramId = programId,
            BundleId = bundleId,
            Code = code,
            NameAr = nameAr,
            NameEn = nameEn,
            DescriptionAr = "وجبة تجريبية آمنة لاختبار فلو الاشتراك",
            DescriptionEn = "Safe sandbox meal for subscription checkout testing",
            Slot = slot,
            IsAvailableForNewOrders = true,
            ApprovalStatus = VerificationStatus.Approved,
            ReviewedAtUtc = now,
            Calories = calories,
            ProteinG = protein,
            CarbsG = carbs,
            NetCarbsG = carbs,
            FatG = fat,
            FiberG = fiber,
            ServingWeightG = slot == "snack" ? 160m : 420m,
            NutritionSource = "Sandbox verified test data",
            VerificationDate = DateOnly.FromDateTime(now),
            VerifiedBy = "MealMate QA",
            RecipeVersion = "SANDBOX-v1",
            AllergensReviewed = true,
            PresentAllergenCodes = string.Empty,
            MayContainAllergenCodes = string.Empty,
            DataResponsibilityConfirmed = true,
            CreatedAtUtc = now
        };
        db.Meals.Add(meal);
    }
    else
    {
        meal.MenuId = menu.Id;
        meal.ProgramId = programId;
        meal.BundleId = bundleId;
        meal.Slot = slot;
        meal.IsAvailableForNewOrders = true;
        meal.ApprovalStatus = VerificationStatus.Approved;
        meal.AllergensReviewed = true;
        meal.DataResponsibilityConfirmed = true;
    }

    var ingredientCode = code + "-ING";
    var ingredient = await db.Ingredients.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.Code == ingredientCode);
    if (ingredient is null)
    {
        ingredient = new Ingredient
        {
            RestaurantId = restaurantId,
            Code = ingredientCode,
            NameAr = slot == "snack" ? "شوفان وفاكهة تجريبية" : "دجاج وأرز وخضار تجريبي",
            NameEn = slot == "snack" ? "Sandbox oats and fruit" : "Sandbox chicken, rice and vegetables",
            Category = slot == "snack" ? "grains" : "poultry",
            OriginCountryCode = "KWT",
            SupplierName = "MealMate Sandbox Supplier",
            IsLocal = true,
            IsHalalCertified = true,
            TraceabilityNote = "Sandbox batch 2026-08",
            IsOriginVisibleToCustomers = true,
            IsActive = true,
            AllergenCodes = string.Empty,
            CreatedAtUtc = now
        };
        db.Ingredients.Add(ingredient);
    }
    else
    {
        ingredient.IsActive = true;
        ingredient.OriginCountryCode = "KWT";
        ingredient.AllergenCodes = string.Empty;
    }

    if (!await db.MealIngredients.IgnoreQueryFilters().AnyAsync(x => x.MealId == meal.Id && x.IngredientId == ingredient.Id))
        db.MealIngredients.Add(new MealIngredient { MealId = meal.Id, IngredientId = ingredient.Id, PortionGrams = slot == "snack" ? 160m : 420m, SortOrder = 1, CreatedAtUtc = now });
    if (!await db.MealImages.IgnoreQueryFilters().AnyAsync(x => x.MealId == meal.Id))
        db.MealImages.Add(new MealImage { MealId = meal.Id, ImageUrl = "https://images.unsplash.com/photo-1547592180-85f173990554?auto=format&fit=crop&w=1200&q=80", DisplayOrder = 1, IsPrimary = true, AltTextAr = nameAr, AltTextEn = nameEn, CreatedAtUtc = now });
    return meal;
}

var lunch = await EnsureMeal("CODEX-CHK-LUNCH", "lunch", "دجاج متوازن تجريبي", "Sandbox Balanced Chicken", 520m, 36m, 55m, 18m, 8m);
var snack = await EnsureMeal("CODEX-CHK-SNACK", "snack", "شوفان وفاكهة تجريبي", "Sandbox Oats and Fruit", 220m, 12m, 25m, 9m, 3m);

var plan = await db.MenuPlans.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.RestaurantId == restaurantId && x.NameEn == "Codex Sandbox Subscription Plan");
if (plan is null)
{
    plan = new MenuPlan { RestaurantId = restaurantId, NameAr = "خطة اختبار الاشتراك", NameEn = "Codex Sandbox Subscription Plan", Source = MenuPlanSource.RestaurantCreated, IsTemplate = false, Mode = ScheduleMode.Weekday, CreatedAtUtc = now };
    db.MenuPlans.Add(plan);
}
else
    plan.Mode = ScheduleMode.Weekday;

var planProgram = await db.MenuPlanPrograms.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.MenuPlanId == plan.Id && x.ProgramId == programId);
if (planProgram is null)
    db.MenuPlanPrograms.Add(new MenuPlanProgram { MenuPlanId = plan.Id, ProgramId = programId, EffectiveFrom = new DateOnly(2026, 8, 1), IsPrimaryOwner = true, CreatedAtUtc = now });
else
{
    planProgram.EffectiveFrom = new DateOnly(2026, 8, 1);
    planProgram.EffectiveTo = null;
    planProgram.IsDeleted = false;
}

var version = await db.MenuPlanVersions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.MenuPlanId == plan.Id && x.VersionNumber == 1);
if (version is null)
{
    version = new MenuPlanVersion { MenuPlanId = plan.Id, VersionNumber = 1, EffectiveFrom = new DateOnly(2026, 8, 1), Status = VerificationStatus.Approved, ReviewedAtUtc = now, ReviewNotes = "Codex sandbox checkout seed", CreatedAtUtc = now };
    db.MenuPlanVersions.Add(version);
}
else
{
    version.Status = VerificationStatus.Approved;
    version.IsPaused = false;
    version.EffectiveFrom = new DateOnly(2026, 8, 1);
}

foreach (var day in Enum.GetValues<DayOfWeek>())
{
    foreach (var item in new[] { (Meal: lunch, Slot: "lunch", Sort: 1), (Meal: snack, Slot: "snack", Sort: 1) })
    {
        if (!await db.WeekdayMealAssignments.IgnoreQueryFilters().AnyAsync(x => x.MenuPlanVersionId == version.Id && x.DayOfWeek == day && x.ProgramId == programId && x.MealId == item.Meal.Id))
            db.WeekdayMealAssignments.Add(new WeekdayMealAssignment { MenuPlanVersionId = version.Id, DayOfWeek = day, ProgramId = programId, MealId = item.Meal.Id, Slot = item.Slot, SortOrder = item.Sort, IsPrimary = true, CreatedAtUtc = now });
    }
}

const string sandboxToken = "SK_KWT_vVZlnnAqu8jRByOWaRPNId4ShzEDNt256dvnjebuyzo52dXjAfRx2ixW5umjWSUx";
const string sandboxWebhookSecret = "MealMateSandboxWebhookV2-2026";
var gateway = await db.MyFatoorahGatewayConfigs.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.GatewayName == "MyFatoorah");
if (gateway is null)
{
    gateway = new MyFatoorahGatewayConfig("MyFatoorah", true, "https://apitest.myfatoorah.com", sandboxToken, sandboxWebhookSecret, true, true, true, "KWD", "KW", "SK_KWT_****");
    gateway.CreatedAtUtc = now;
    db.MyFatoorahGatewayConfigs.Add(gateway);
}
else
    gateway.Update(true, "https://apitest.myfatoorah.com", sandboxToken, sandboxWebhookSecret, true, true, true, "KWD", "KW", "SK_KWT_****");

await db.SaveChangesAsync();
Console.WriteLine($"SEEDED restaurant={restaurantId} program={programId} bundle={bundleId} region={regionId} menuVersion={version.Id} meals={lunch.Id},{snack.Id}");
Console.WriteLine($"BUNDLE_COUNTS mealsPerDay={bundle.MealsPerDay} lunch={bundle.LunchCount} snack={bundle.SnackCount}");
