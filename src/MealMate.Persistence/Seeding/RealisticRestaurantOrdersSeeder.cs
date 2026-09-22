using System.Text.Json;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class RealisticRestaurantOrdersSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RealisticRestaurantOrdersSeeder");

        logger.LogInformation("Starting Realistic Restaurant Orders Seeding for Diet Care and other kitchens...");

        // 1. Ensure Diet Care Profile exists
        var dietCareUser = await db.Users.FirstOrDefaultAsync(x => x.Email == "dietcare.hq@mealmate.kw", cancellationToken);
        if (dietCareUser == null || dietCareUser.RestaurantId == null)
        {
            logger.LogWarning("Diet Care user not found. Seeding demo restaurants first...");
            await DemoRestaurantDataSeeder.SeedAsync(services, 10, cancellationToken);
            dietCareUser = await db.Users.FirstOrDefaultAsync(x => x.Email == "dietcare.hq@mealmate.kw", cancellationToken);
        }

        if (dietCareUser?.RestaurantId == null)
        {
            logger.LogError("Could not find or create Diet Care restaurant profile.");
            return;
        }

        var restaurantId = dietCareUser.RestaurantId.Value;
        var restaurant = await db.RestaurantProfiles.FindAsync([restaurantId], cancellationToken);
        if (restaurant == null)
        {
            logger.LogError("Diet Care profile not found.");
            return;
        }

        var programs = await db.Programs.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var bundles = await db.Bundles.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var meals = await db.Meals.AsNoTracking().Where(x => x.RestaurantId == restaurantId).ToListAsync(cancellationToken);

        if (programs.Count == 0 || bundles.Count == 0 || meals.Count == 0)
        {
            logger.LogWarning("Catalog not ready for Diet Care. Seeding catalog first...");
            await DemoRestaurantMealCatalogSeeder.SeedAsync(services, cancellationToken);
            programs = await db.Programs.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
            bundles = await db.Bundles.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
            meals = await db.Meals.AsNoTracking().Where(x => x.RestaurantId == restaurantId).ToListAsync(cancellationToken);
        }

        // 2. Create Realistic Customers
        var customerDemos = new[]
        {
            (Name: "فاطمة الكندري", Email: "customer.fatima@mealmate.kw", Phone: "+96550000001", Area: "السالمية، قطعة 4، جادة 12"),
            (Name: "محمد الهاجري", Email: "customer.mohammed@mealmate.kw", Phone: "+96550000002", Area: "حولي، شارع تونس، مجمع 8"),
            (Name: "جاسم العتيبي", Email: "customer.jassem@mealmate.kw", Phone: "+96550000003", Area: "الجابرية، قطعة 1أ، شارع 105"),
            (Name: "ريم الدوسري", Email: "customer.reem@mealmate.kw", Phone: "+96550000004", Area: "مدينة الكويت، شرق، برج الراية"),
            (Name: "عبدالله الشمري", Email: "customer.abdullah@mealmate.kw", Phone: "+96550000005", Area: "بيان، قطعة 7، شارع 1"),
            (Name: "دلال السبيعي", Email: "customer.dalal@mealmate.kw", Phone: "+96550000006", Area: "مشرف، قطعة 5، شارع 22"),
            (Name: "يوسف المطيري", Email: "customer.yousef@mealmate.kw", Phone: "+96550000007", Area: "الروضة، قطعة 2، شارع النخيل"),
            (Name: "نور الصباح", Email: "customer.nour@mealmate.kw", Phone: "+96550000008", Area: "الفروانية، قطعة 1، شارع حبيب مناور")
        };

        var customerUsers = new List<ApplicationUser>();
        foreach (var c in customerDemos)
        {
            var user = await db.Users.FirstOrDefaultAsync(x => x.Email == c.Email, cancellationToken);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = c.Email,
                    Email = c.Email,
                    EmailConfirmed = true,
                    PhoneNumber = c.Phone,
                    PhoneNumberConfirmed = true,
                    FullName = c.Name,
                    UserType = UserType.Customer,
                    AccountStatus = AccountStatus.Active,
                    CreatedAtUtc = DateTime.UtcNow
                };
                var createRes = await userManager.CreateAsync(user, "Customer#2026");
                if (createRes.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, "Customer");
                }
            }

            var existingProfile = await db.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);
            if (existingProfile == null)
            {
                db.CustomerProfiles.Add(new CustomerProfile
                {
                    UserId = user.Id,
                    DeliveryAddressLine = c.Area,
                    OnboardingCompleted = true,
                    PreferredLanguage = "ar",
                    DietaryPattern = "Balanced Diet",
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            customerUsers.Add(user);
        }

        await db.SaveChangesAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now.Date);
        var tomorrow = today.AddDays(1);
        var inThreeDays = today.AddDays(3);
        var inTwoDays = today.AddDays(2);

        // Delete existing realistic demo subscription days for Diet Care to ensure fresh seed
        var customerIds = customerUsers.Select(u => u.Id).ToList();
        if (customerIds.Count > 0)
        {
            var idsParam = string.Join(",", customerIds.Select(id => $"'{id}'"));
            await db.Database.ExecuteSqlRawAsync($@"
                DELETE FROM ""DailyMealSelections"" WHERE ""SubscriptionDayId"" IN (
                    SELECT sd.""Id"" FROM ""SubscriptionDays"" sd JOIN ""Subscriptions"" s ON sd.""SubscriptionId"" = s.""Id"" WHERE s.""CustomerUserId"" IN ({idsParam})
                );
                DELETE FROM ""SubscriptionDayMealSnapshots"" WHERE ""SubscriptionDayId"" IN (
                    SELECT sd.""Id"" FROM ""SubscriptionDays"" sd JOIN ""Subscriptions"" s ON sd.""SubscriptionId"" = s.""Id"" WHERE s.""CustomerUserId"" IN ({idsParam})
                );
                DELETE FROM ""SubscriptionDays"" WHERE ""SubscriptionId"" IN (
                    SELECT s.""Id"" FROM ""Subscriptions"" s WHERE s.""CustomerUserId"" IN ({idsParam})
                );
                DELETE FROM ""Subscriptions"" WHERE ""CustomerUserId"" IN ({idsParam});
            ", cancellationToken);
        }

        // 3. Create realistic Subscriptions & Days
        var menuPlans = await db.MenuPlans.AsNoTracking().Where(x => x.RestaurantId == restaurantId).ToListAsync(cancellationToken);
        var menuPlanVersions = await db.MenuPlanVersions.AsNoTracking().Where(x => menuPlans.Select(mp => mp.Id).Contains(x.MenuPlanId)).ToListAsync(cancellationToken);
        var defaultVersionId = menuPlanVersions.FirstOrDefault()?.Id ?? Guid.NewGuid();

        for (int i = 0; i < customerUsers.Count; i++)
        {
            var user = customerUsers[i];
            var program = programs[i % programs.Count];
            var bundle = bundles[i % bundles.Count];

            var sub = new Subscription
            {
                CustomerUserId = user.Id,
                ProgramId = program.Id,
                BundleId = bundle.Id,
                DurationDays = 30,
                StartDate = today.AddDays(-5),
                EndDate = today.AddDays(25),
                Status = SubscriptionStatus.Active,
                SelectedTier = RestaurantTier.Platinum,
                PaidAmount = new Money(185.000m, "KWD"),
                FulfillmentCompletedAtUtc = now.AddDays(-5),
                CreatedAtUtc = now.AddDays(-5),
                IncludeFriday = false
            };
            db.Subscriptions.Add(sub);
            await db.SaveChangesAsync(cancellationToken);

            var programMeals = meals.Where(m => m.ProgramId == program.Id).ToList();
            if (programMeals.Count == 0) programMeals = meals;

            // Scenario 1: Pending Confirmation Orders (In 3 days - Lock window active)
            if (i < 4)
            {
                var lockOffsetHours = i switch
                {
                    0 => 22, // Critical (< 4h left -> 2h left)
                    1 => 18, // Warning (< 8h left -> 6h left)
                    2 => 8,  // Normal (16h left)
                    _ => 26  // Overdue (2h overdue)
                };

                var serviceDate = i == 3 ? inTwoDays : inThreeDays;
                var day = new SubscriptionDay
                {
                    SubscriptionId = sub.Id,
                    RestaurantId = restaurantId,
                    ServiceDate = serviceDate,
                    Status = SubscriptionDayStatus.Locked72h,
                    LockAtUtc = now.AddHours(-lockOffsetHours),
                    SelectionDeadlineUtc = now.AddHours(-lockOffsetHours),
                    ResolvedMenuPlanVersionId = defaultVersionId,
                    CreatedAtUtc = now.AddHours(-lockOffsetHours)
                };
                db.SubscriptionDays.Add(day);
                await db.SaveChangesAsync(cancellationToken);

                AddSnapshotAndSelections(db, day, sub, programMeals, now);
            }

            // Scenario 2: Upcoming 24h Orders (Tomorrow)
            if (i >= 2 && i < 6)
            {
                var day = new SubscriptionDay
                {
                    SubscriptionId = sub.Id,
                    RestaurantId = restaurantId,
                    ServiceDate = tomorrow,
                    Status = SubscriptionDayStatus.ConfirmedByRestaurant,
                    LockAtUtc = now.AddHours(-36),
                    ConfirmedAtUtc = now.AddHours(-18),
                    ResolvedMenuPlanVersionId = defaultVersionId,
                    CreatedAtUtc = now.AddHours(-36)
                };
                db.SubscriptionDays.Add(day);
                await db.SaveChangesAsync(cancellationToken);

                AddSnapshotAndSelections(db, day, sub, programMeals, now);
            }

            // Scenario 3: Daily Orders (Today)
            if (i >= 1 && i < 7)
            {
                var status = i switch
                {
                    1 => SubscriptionDayStatus.Preparing,
                    2 => SubscriptionDayStatus.Preparing,
                    3 => SubscriptionDayStatus.Preparing,
                    4 => SubscriptionDayStatus.OutForDelivery,
                    5 => SubscriptionDayStatus.Delivered,
                    _ => SubscriptionDayStatus.Preparing
                };

                var day = new SubscriptionDay
                {
                    SubscriptionId = sub.Id,
                    RestaurantId = restaurantId,
                    ServiceDate = today,
                    Status = status,
                    LockAtUtc = now.AddHours(-60),
                    ConfirmedAtUtc = now.AddHours(-40),
                    ResolvedMenuPlanVersionId = defaultVersionId,
                    CreatedAtUtc = now.AddHours(-60)
                };
                db.SubscriptionDays.Add(day);
                await db.SaveChangesAsync(cancellationToken);

                AddSnapshotAndSelections(db, day, sub, programMeals, now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Successfully seeded rich realistic orders for Diet Care restaurant!");

        await SupportIssuesSeeder.SeedAsync(db, logger, cancellationToken);
    }

    private static void AddSnapshotAndSelections(AppDbContext db, SubscriptionDay day, Subscription sub, List<Meal> meals, DateTime now)
    {
        var slots = new[] { "breakfast", "lunch", "dinner", "snack" };
        var selectedMeals = new List<Meal>();

        foreach (var slot in slots)
        {
            var slotMeal = meals.FirstOrDefault(m => string.Equals(m.Slot, slot, StringComparison.OrdinalIgnoreCase)) ?? meals.First();
            selectedMeals.Add(slotMeal);
        }

        var snapshotItems = selectedMeals.Select(m => new
        {
            MealId = m.Id,
            NameAr = m.NameAr,
            NameEn = m.NameEn,
            Slot = m.Slot,
            Calories = m.Calories ?? 450,
            ProteinGrams = m.ProteinG ?? 35,
            CarbsGrams = m.CarbsG ?? 45,
            FatGrams = m.FatG ?? 15,
            FiberGrams = m.FiberG ?? 6,
            PresentAllergenCodes = new string[0],
            MayContainAllergenCodes = new string[0],
            DietaryPreferenceCodes = new string[0],
            Ingredients = new object[0]
        }).ToList();

        var requiredSlots = slots.Select(s => new { Slot = s, Count = 1 }).ToList();

        var snapshot = new SubscriptionDayMealSnapshot
        {
            SubscriptionId = sub.Id,
            SubscriptionDayId = day.Id,
            ServiceDate = day.ServiceDate,
            RestaurantId = day.RestaurantId!.Value,
            ProgramId = sub.ProgramId,
            MenuPlanVersionId = day.ResolvedMenuPlanVersionId ?? Guid.NewGuid(),
            RequiredSlotsJson = JsonSerializer.Serialize(requiredSlots),
            MealsJson = JsonSerializer.Serialize(snapshotItems),
            CapturedAtUtc = now
        };
        db.SubscriptionDayMealSnapshots.Add(snapshot);

        for (int idx = 0; idx < selectedMeals.Count; idx++)
        {
            db.DailyMealSelections.Add(new DailyMealSelection
            {
                SubscriptionDayId = day.Id,
                MealId = selectedMeals[idx].Id,
                SortOrder = idx,
                SelectedAtUtc = now,
                ResolvedMenuPlanVersionId = day.ResolvedMenuPlanVersionId,
                ResolvedProgramId = sub.ProgramId
            });
        }
    }
}
