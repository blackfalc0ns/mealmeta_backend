using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
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

public static class DemoRestaurantDataSeeder
{
    private const string EmailPrefix = "demo.restaurant.";
    private const string Password = "DemoRestaurant#2026";

    public static async Task SeedAsync(IServiceProvider services, int restaurantCount, CancellationToken cancellationToken = default)
    {
        if (restaurantCount is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(restaurantCount));

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoRestaurantDataSeeder");
        var bundles = await db.Bundles.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);
        if (bundles.Count < 9) throw new InvalidOperationException("Seed the program catalog before demo restaurants.");

        var restaurantNames = new[] { "صحة وهناء", "مذاق الميزان", "بيت البروتين", "وجبتك الصحية", "نبات ونعمة", "مطبخ الرشاقة", "وقود الرياضي", "توازن دايت", "صحنك اليومي", "أكل نظيف" };
        var ingredientTemplates = new[]
        {
            ("chicken", "صدر دجاج", "Chicken breast", "poultry", 165m, 31m, 0m, 3.6m),
            ("rice", "أرز بسمتي", "Basmati rice", "grains", 130m, 2.7m, 28m, 0.3m),
            ("salmon", "سلمون", "Salmon", "seafood", 208m, 20m, 0m, 13m),
            ("broccoli", "بروكلي", "Broccoli", "produce", 34m, 2.8m, 7m, 0.4m),
            ("yogurt", "زبادي يوناني", "Greek yogurt", "dairy", 59m, 10m, 3.6m, 0.4m),
            ("oats", "شوفان", "Oats", "grains", 389m, 17m, 66m, 7m),
            ("eggs", "بيض", "Eggs", "dairy", 155m, 13m, 1.1m, 11m),
            ("avocado", "أفوكادو", "Avocado", "produce", 160m, 2m, 9m, 15m),
            ("beef", "لحم بقري قليل الدهن", "Lean beef", "meat", 250m, 26m, 0m, 15m),
            ("quinoa", "كينوا", "Quinoa", "grains", 120m, 4.4m, 21m, 1.9m),
        };
        var mealTemplates = new[]
        {
            ("breakfast", "فطور البيض والأفوكادو", "Egg & avocado breakfast", 420m, 25m, 20m, 25m),
            ("lunch", "دجاج مشوي مع أرز", "Grilled chicken with rice", 560m, 46m, 55m, 14m),
            ("dinner", "سلمون وخضار موسمية", "Salmon with seasonal vegetables", 510m, 38m, 24m, 28m),
            ("snack", "زبادي يوناني بالشوفان", "Greek yogurt with oats", 260m, 18m, 30m, 8m),
            ("lunch", "لحم قليل الدهن مع كينوا", "Lean beef with quinoa", 590m, 44m, 48m, 22m),
            ("dinner", "سلطة بروتين طازجة", "Fresh protein salad", 380m, 34m, 18m, 18m),
        };

        var created = 0;
        for (var index = 1; index <= restaurantCount; index++)
        {
            var email = $"{EmailPrefix}{index:D2}@mealmate.demo";
            var existingUser = await db.Users.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
            if (existingUser is not null)
            {
                var existingRestaurant = await db.RestaurantProfiles.FirstOrDefaultAsync(x => x.UserId == existingUser.Id, cancellationToken);
                if (existingRestaurant is not null)
                    await SeedPreferenceIngredientsAsync(db, existingRestaurant.Id, index, cancellationToken);
                continue;
            }

            var name = $"{restaurantNames[(index - 1) % restaurantNames.Length]} {index:D2}";
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = $"مالك {name}",
                UserType = UserType.Restaurant,
                AccountStatus = AccountStatus.Active,
                CreatedAtUtc = DateTime.UtcNow,
            };
            var result = await userManager.CreateAsync(user, Password);
            if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
            await userManager.AddToRoleAsync(user, AppRoles.RestaurantOwner);

            var restaurant = new RestaurantProfile
            {
                UserId = user.Id,
                LegalNameAr = name,
                LegalNameEn = $"{restaurantNames[(index - 1) % restaurantNames.Length]} Restaurant {index:D2}",
                TradeNameAr = name,
                TradeNameEn = $"MealMate Demo Restaurant {index:D2}",
                CommercialLicenseNumber = $"DEMO-{index:D5}",
                ContactPhone = $"+965 5000 {index:D4}",
                ContactEmail = email,
                VerificationStatus = VerificationStatus.Approved,
                IsOperationallyActive = true,
                ActivatedAtUtc = DateTime.UtcNow,
            };
            db.RestaurantProfiles.Add(restaurant);
            user.RestaurantId = restaurant.Id;

            var ingredients = ingredientTemplates.Select((item, ingredientIndex) => new Ingredient
            {
                RestaurantId = restaurant.Id,
                Code = $"R{index:D2}-ING-{ingredientIndex + 1:D2}",
                NameAr = item.Item2,
                NameEn = item.Item3,
                Category = item.Item4,
                DefaultCaloriesPer100g = item.Item5,
                DefaultProteinPer100g = item.Item6,
                DefaultCarbsPer100g = item.Item7,
                DefaultFatPer100g = item.Item8,
                IsActive = true,
            }).ToList();
            db.Ingredients.AddRange(ingredients);
            await SeedPreferenceIngredientsAsync(db, restaurant.Id, index, cancellationToken);

            var menus = bundles.Select(bundle => new Menu
            {
                RestaurantId = restaurant.Id,
                ProgramId = bundle.ProgramId,
                NameAr = $"قائمة {name} - {bundle.NameAr}",
                NameEn = $"{name} - {bundle.NameEn}",
                IsActive = true,
            }).ToList();
            db.Menus.AddRange(menus);

            var meals = new List<Meal>();
            foreach (var menu in menus.Take(3))
            {
                foreach (var template in mealTemplates)
                {
                    meals.Add(new Meal
                    {
                        MenuId = menu.Id,
                        RestaurantId = restaurant.Id,
                        Code = $"R{index:D2}-MEAL-{meals.Count + 1:D2}",
                        NameAr = template.Item2,
                        NameEn = template.Item3,
                        Slot = template.Item1,
                        DescriptionAr = "وجبة طازجة محسوبة القيم الغذائية ومعدة يوميًا.",
                        DescriptionEn = "Fresh daily meal with calculated nutritional values.",
                        Calories = template.Item4,
                        ProteinG = template.Item5,
                        CarbsG = template.Item6,
                        FatG = template.Item7,
                        IsAvailableForNewOrders = true,
                        ApprovalStatus = VerificationStatus.Approved,
                        ReviewedAtUtc = DateTime.UtcNow,
                    });
                }
            }
            db.Meals.AddRange(meals);
            db.MealIngredients.AddRange(meals.SelectMany((meal, mealIndex) => Enumerable.Range(0, 3).Select(offset => new MealIngredient
            {
                MealId = meal.Id,
                IngredientId = ingredients[(mealIndex + offset) % ingredients.Count].Id,
                PortionGrams = 70 + (offset * 35),
                SortOrder = offset + 1,
            })));

            foreach (var bundle in bundles)
            {
                db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
                {
                    RestaurantId = restaurant.Id,
                    ProgramId = bundle.ProgramId,
                    BundleId = bundle.Id,
                    IsActive = true,
                });
                var price = 95m + (index * 1.5m) + (bundle.SortOrder * 7m);
                db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
                {
                    RestaurantId = restaurant.Id,
                    ProgramId = bundle.ProgramId,
                    BundleId = bundle.Id,
                    ReferencePrice26 = new Money(price, "KWD"),
                    ContractedSupplyPrice26 = new Money(price * 0.78m, "KWD"),
                    LockStatus = PriceLockStatus.Approved,
                    EffectiveFromUtc = DateTime.UtcNow,
                });
            }

            db.DailyCapacities.Add(new DailyCapacity
            {
                RestaurantId = restaurant.Id,
                ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow),
                MaxBoxes = 40 + (index % 30),
                ReservedBoxes = index % 12,
            });

            created++;
            if (index % 10 == 0) await db.SaveChangesAsync(cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created {Count} connected demo restaurants. Login password: {Password}", created, Password);
    }

    private static async Task SeedPreferenceIngredientsAsync(AppDbContext db, Guid restaurantId, int index, CancellationToken ct)
    {
        var templates = new[]
        {
            ("MILK", "حليب", "Milk", "dairy"),
            ("GLUTEN", "قمح وجلوتين", "Wheat and gluten", "grains"),
            ("EGGS", "بيض", "Eggs", "dairy"),
            ("PEANUTS", "فول سوداني", "Peanuts", "nuts"),
            ("TREE_NUTS", "مكسرات شجرية", "Tree nuts", "nuts"),
            ("FISH", "سمك", "Fish", "seafood")
        };
        foreach (var item in templates)
        {
            var code = $"R{index:D2}-ALLERGEN-{item.Item1}";
            if (await db.Ingredients.AnyAsync(x => x.RestaurantId == restaurantId && x.Code == code, ct)) continue;
            db.Ingredients.Add(new Ingredient
            {
                RestaurantId = restaurantId,
                Code = code,
                NameAr = item.Item2,
                NameEn = item.Item3,
                Category = item.Item4,
                AllergenCodes = item.Item1,
                IsActive = true
            });
        }
    }
}
