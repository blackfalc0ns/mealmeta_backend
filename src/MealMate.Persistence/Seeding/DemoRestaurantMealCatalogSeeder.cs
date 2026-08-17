using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MealMate.Persistence.Seeding;

public static class DemoRestaurantMealCatalogSeeder
{
    private const string IngredientMedia = "https://ik.imagekit.io/f7h9cj23x/catalog/ingredient-photos/";
    private const string MealCodePrefix = "MM20";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoRestaurantMealCatalogSeeder");

        await ProgramsSubscriptionsSeeder.SeedProgramsAndBundlesAsync(db, logger, cancellationToken);
        await DemoRestaurantDataSeeder.EnsureSharedCatalogAsync(db, cancellationToken);

        var imageUrls = await UploadMealImagesAsync(db, storage, logger, cancellationToken);
        var programs = await db.Programs.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var bundles = await db.Bundles.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var emails = RestaurantEmails();
        var users = await db.Users.AsNoTracking().Where(x => emails.Contains(x.Email!)).ToListAsync(cancellationToken);
        var userIds = users.Select(x => x.Id).ToList();
        var restaurants = await db.RestaurantProfiles.Where(x => userIds.Contains(x.UserId)).ToListAsync(cancellationToken);

        var updated = 0;
        foreach (var restaurant in restaurants)
        {
            try
            {
                var user = users.First(x => x.Id == restaurant.UserId);
                var index = Math.Max(0, emails.ToList().IndexOf(user.Email!));
                await EnsureRestaurantMealsAsync(db, restaurant, index, programs, bundles, imageUrls, logger, cancellationToken);
                updated++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed catalog meals for {Name}", restaurant.TradeNameEn);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Attached 20 complete meals to {Count} restaurants.", updated);
    }

    private static async Task EnsureRestaurantMealsAsync(
        AppDbContext db,
        RestaurantProfile restaurant,
        int index,
        IReadOnlyList<Program> programs,
        IReadOnlyList<Bundle> bundles,
        IReadOnlyDictionary<string, string> imageUrls,
        ILogger logger,
        CancellationToken ct)
    {
        var ingredients = await EnsureIngredientsAsync(db, restaurant.Id, index, ct);
        var menus = await EnsureMenusAsync(db, restaurant, programs, ct);
        var existingMeals = await db.Meals.Where(x => x.RestaurantId == restaurant.Id).ToListAsync(ct);
        var createdMeals = new List<Meal>();

        foreach (var spec in MealSpecs())
        {
            var program = programs.FirstOrDefault(x => string.Equals(x.ShortCode, spec.Program, StringComparison.OrdinalIgnoreCase));
            if (program is null || !menus.TryGetValue(program.Id, out var menu))
            {
                continue;
            }

            var code = $"{MealCodePrefix}-{spec.Number:00}";
            var meal = existingMeals.FirstOrDefault(x => x.Code == code);
            if (meal is null)
            {
                meal = new Meal
                {
                    MenuId = menu.Id,
                    RestaurantId = restaurant.Id,
                    ProgramId = program.Id,
                    Code = code,
                };
                db.Meals.Add(meal);
                existingMeals.Add(meal);
            }

            var macros = spec.Macros;
            meal.NameAr = spec.NameAr;
            meal.NameEn = spec.NameEn;
            meal.Slot = spec.Slot;
            meal.DescriptionAr = spec.DescriptionAr;
            meal.DescriptionEn = spec.DescriptionEn;
            meal.Calories = macros.Kcal;
            meal.ProteinG = macros.P;
            meal.CarbsG = macros.C;
            meal.NetCarbsG = macros.Net;
            meal.FatG = macros.F;
            meal.FiberG = macros.Fiber;
            meal.ServingWeightG = 380;
            meal.IsAvailableForNewOrders = true;
            meal.ApprovalStatus = VerificationStatus.Approved;
            meal.ReviewedAtUtc = DateTime.UtcNow;
            meal.AllergensReviewed = true;
            meal.DataResponsibilityConfirmed = true;
            meal.PresentAllergenCodes = spec.Allergens;
            meal.NutritionSource = "lab";
            meal.RecipeVersion = "MM20";
            createdMeals.Add(meal);
        }

        await db.SaveChangesAsync(ct);

        var mealIds = createdMeals.Select(x => x.Id).ToList();
        var images = await db.MealImages.Where(x => mealIds.Contains(x.MealId)).ToListAsync(ct);
        var links = await db.MealIngredients.Where(x => mealIds.Contains(x.MealId)).ToListAsync(ct);

        foreach (var spec in MealSpecs())
        {
            var meal = createdMeals.FirstOrDefault(x => x.Code == $"{MealCodePrefix}-{spec.Number:00}");
            if (meal is null)
            {
                continue;
            }

            if (!images.Any(x => x.MealId == meal.Id))
            {
                db.MealImages.Add(new MealImage
                {
                    MealId = meal.Id,
                    ImageUrl = imageUrls[spec.File],
                    DisplayOrder = 1,
                    IsPrimary = true,
                    AltTextAr = spec.NameAr,
                    AltTextEn = spec.NameEn,
                });
            }

            if (!links.Any(x => x.MealId == meal.Id))
            {
                var sort = 1;
                foreach (var slug in spec.Ingredients)
                {
                    if (!ingredients.TryGetValue(slug, out var ingredient))
                    {
                        continue;
                    }

                    db.MealIngredients.Add(new MealIngredient
                    {
                        MealId = meal.Id,
                        IngredientId = ingredient.Id,
                        PortionGrams = 70 + (sort * 20),
                        SortOrder = sort,
                    });
                    sort++;
                }
            }
        }

        foreach (var bundle in bundles)
        {
            if (!await db.RestaurantProgramBundleOffers.AnyAsync(x => x.RestaurantId == restaurant.Id && x.BundleId == bundle.Id, ct))
            {
                db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
                {
                    RestaurantId = restaurant.Id,
                    ProgramId = bundle.ProgramId,
                    BundleId = bundle.Id,
                    IsActive = true,
                });
            }

            if (!await db.RestaurantBundlePricings.AnyAsync(x =>
                    x.RestaurantId == restaurant.Id && x.BundleId == bundle.Id && x.ReferencePrice26.Amount > 0, ct))
            {
                var price26 = 90m + (index * 12m) + (bundle.MealsPerDay * 8m);
                db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
                {
                    RestaurantId = restaurant.Id,
                    ProgramId = bundle.ProgramId,
                    BundleId = bundle.Id,
                    ReferencePrice26 = new Money(price26, "KWD"),
                    ContractedSupplyPrice26 = new Money(decimal.Round(price26 * 0.78m, 3), "KWD"),
                    LockStatus = PriceLockStatus.Approved,
                    EffectiveFromUtc = DateTime.UtcNow.AddDays(-2),
                });
            }
        }

        var plan = await db.MenuPlans.FirstOrDefaultAsync(x => x.RestaurantId == restaurant.Id && !x.IsTemplate && !x.IsDeleted, ct);
        if (plan is null)
        {
            plan = new MenuPlan
            {
                RestaurantId = restaurant.Id,
                NameAr = $"خطة {restaurant.TradeNameAr}",
                NameEn = $"{restaurant.TradeNameEn} weekly plan",
                Source = MenuPlanSource.RestaurantCreated,
                Mode = ScheduleMode.Weekday,
            };
            db.MenuPlans.Add(plan);
            await db.SaveChangesAsync(ct);
        }

        var version = await db.MenuPlanVersions.FirstOrDefaultAsync(x => x.MenuPlanId == plan.Id && x.Status == VerificationStatus.Approved, ct);
        if (version is null)
        {
            version = new MenuPlanVersion
            {
                MenuPlanId = plan.Id,
                VersionNumber = 1,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                Status = VerificationStatus.Approved,
                ReviewedAtUtc = DateTime.UtcNow,
            };
            db.MenuPlanVersions.Add(version);
            await db.SaveChangesAsync(ct);
        }

        foreach (var program in programs)
        {
            if (!await db.MenuPlanPrograms.AnyAsync(x => x.MenuPlanId == plan.Id && x.ProgramId == program.Id, ct))
            {
                db.MenuPlanPrograms.Add(new MenuPlanProgram
                {
                    MenuPlanId = plan.Id,
                    ProgramId = program.Id,
                    EffectiveFrom = new DateOnly(2026, 1, 1),
                    IsPrimaryOwner = program.ShortCode == "LS",
                });
            }
        }

        var assignments = await db.WeekdayMealAssignments
            .Where(x => x.MenuPlanVersionId == version.Id)
            .ToListAsync(ct);
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            foreach (var meal in createdMeals.Where(x => x.ProgramId.HasValue))
            {
                if (assignments.Any(x => x.DayOfWeek == day && x.ProgramId == meal.ProgramId && x.MealId == meal.Id))
                {
                    continue;
                }

                var hasPrimary = assignments.Any(x =>
                    x.DayOfWeek == day && x.ProgramId == meal.ProgramId && x.Slot == meal.Slot && x.IsPrimary);
                db.WeekdayMealAssignments.Add(new WeekdayMealAssignment
                {
                    MenuPlanVersionId = version.Id,
                    DayOfWeek = day,
                    ProgramId = meal.ProgramId!.Value,
                    MealId = meal.Id,
                    Slot = meal.Slot!,
                    SortOrder = hasPrimary ? 2 : 1,
                    IsPrimary = !hasPrimary,
                });
            }
        }

        var menuDays = await db.MenuDays.Where(x => x.MenuPlanVersionId == version.Id).ToListAsync(ct);
        if (menuDays.Count == 0)
        {
            menuDays =
            [
                new MenuDay { MenuPlanVersionId = version.Id, DayNumber = 1, NameAr = "السبت", NameEn = "Saturday" },
                new MenuDay { MenuPlanVersionId = version.Id, DayNumber = 2, NameAr = "الأحد", NameEn = "Sunday" },
                new MenuDay { MenuPlanVersionId = version.Id, DayNumber = 3, NameAr = "الاثنين", NameEn = "Monday" },
                new MenuDay { MenuPlanVersionId = version.Id, DayNumber = 4, NameAr = "الثلاثاء", NameEn = "Tuesday" },
                new MenuDay { MenuPlanVersionId = version.Id, DayNumber = 5, NameAr = "الأربعاء", NameEn = "Wednesday" },
                new MenuDay { MenuPlanVersionId = version.Id, DayNumber = 6, NameAr = "الخميس", NameEn = "Thursday" },
            ];
            db.MenuDays.AddRange(menuDays);
            await db.SaveChangesAsync(ct);
        }

        var dayLinks = await db.MenuDayMeals
            .Where(x => menuDays.Select(d => d.Id).Contains(x.MenuDayId))
            .ToListAsync(ct);
        foreach (var menuDay in menuDays)
        {
            foreach (var meal in createdMeals.Where(x => x.ProgramId.HasValue))
            {
                if (dayLinks.Any(x => x.MenuDayId == menuDay.Id && x.ProgramId == meal.ProgramId && x.MealId == meal.Id))
                {
                    continue;
                }

                var hasPrimary = dayLinks.Any(x =>
                    x.MenuDayId == menuDay.Id && x.ProgramId == meal.ProgramId && x.Slot == meal.Slot && x.IsPrimary);
                var link = new MenuDayMeal
                {
                    MenuDayId = menuDay.Id,
                    MealId = meal.Id,
                    ProgramId = meal.ProgramId!.Value,
                    Slot = meal.Slot!,
                    SortOrder = hasPrimary ? 2 : 1,
                    IsPrimary = !hasPrimary,
                };
                db.MenuDayMeals.Add(link);
                dayLinks.Add(link);
            }
        }

        logger.LogInformation("Restaurant {Name} now has 20 complete catalog meals.", restaurant.TradeNameEn);
    }

    private static async Task<Dictionary<string, Ingredient>> EnsureIngredientsAsync(
        AppDbContext db, Guid restaurantId, int index, CancellationToken ct)
    {
        var existing = await db.Ingredients.Where(x => x.RestaurantId == restaurantId).ToListAsync(ct);
        foreach (var item in IngredientSpecs())
        {
            if (existing.Any(x => x.Code == item.Code || string.Equals(x.NameEn, item.En, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var ingredient = new Ingredient
            {
                RestaurantId = restaurantId,
                Code = item.Code,
                NameAr = item.Ar,
                NameEn = item.En,
                Category = item.Category,
                DefaultCaloriesPer100g = item.Kcal,
                DefaultProteinPer100g = item.P,
                DefaultCarbsPer100g = item.C,
                DefaultFatPer100g = item.F,
                DefaultWeightGrams = 100,
                OriginCountryCode = item.Origin,
                SupplierName = "MealMate Supply",
                IsLocal = item.Origin == "KW",
                IsHalalCertified = true,
                IsOriginVisibleToCustomers = true,
                IsCore = item.Core,
                RestaurantIconUrl = item.Core ? $"{IngredientMedia}{item.Slug}.webp" : null,
                MealIconUrl = $"{IngredientMedia}{item.Slug}.webp",
                AllergenCodes = item.Allergens,
                IsActive = true,
            };
            db.Ingredients.Add(ingredient);
            existing.Add(ingredient);
        }

        await db.SaveChangesAsync(ct);
        return existing
            .GroupBy(x => NormalizeSlug(x.NameEn), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<Guid, Menu>> EnsureMenusAsync(
        AppDbContext db, RestaurantProfile restaurant, IReadOnlyList<Program> programs, CancellationToken ct)
    {
        var menus = await db.Menus.Where(x => x.RestaurantId == restaurant.Id).ToListAsync(ct);
        foreach (var program in programs)
        {
            if (menus.Any(x => x.ProgramId == program.Id))
            {
                continue;
            }

            var menu = new Menu
            {
                RestaurantId = restaurant.Id,
                ProgramId = program.Id,
                NameAr = $"قائمة {restaurant.TradeNameAr} - {program.NameAr}",
                NameEn = $"{restaurant.TradeNameEn} - {program.NameEn}",
                IsActive = true,
            };
            db.Menus.Add(menu);
            menus.Add(menu);
        }

        await db.SaveChangesAsync(ct);
        return menus.Where(x => x.ProgramId.HasValue).ToDictionary(x => x.ProgramId!.Value);
    }

    private static async Task<Dictionary<string, string>> UploadMealImagesAsync(
        AppDbContext db, IFileStorage storage, ILogger logger, CancellationToken ct)
    {
        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var existing = await db.MealImages.AsNoTracking()
            .Where(x => x.AltTextEn != null)
            .Select(x => new { x.AltTextEn, x.ImageUrl })
            .ToListAsync(ct);
        foreach (var spec in MealSpecs())
        {
            var reused = existing.FirstOrDefault(x =>
                string.Equals(x.AltTextEn, spec.NameEn, StringComparison.OrdinalIgnoreCase)
                && x.ImageUrl.Contains("imagekit.io", StringComparison.OrdinalIgnoreCase));
            if (reused is not null)
            {
                urls[spec.File] = reused.ImageUrl;
                continue;
            }

            var root = new[]
            {
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".tmp-catalog-images")),
                @"F:\MealMate\backend\.tmp-catalog-images",
            }.FirstOrDefault(Directory.Exists) ?? throw new DirectoryNotFoundException("Meal image folder was not found.");
            var path = Path.Combine(root, spec.File);
            await using var stream = File.OpenRead(path);
            var stored = await storage.SaveAsync(stream, spec.File, "image/jpeg", "uploads/admin/meals", ct);
            urls[spec.File] = stored.StorageKey.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? stored.StorageKey
                : $"https://ik.imagekit.io/f7h9cj23x/{stored.StorageKey.TrimStart('/')}";
            logger.LogInformation("Uploaded meal photo {File}", spec.File);
        }

        return urls;
    }

    private static string NormalizeSlug(string name) => name.ToLowerInvariant()
        .Replace("breast", "breast")
        .Replace("white rice", "white-rice")
        .Replace("sweet potato", "sweet-potato")
        .Replace("green beans", "green-beans")
        .Replace(" ", "-");

    private static IReadOnlyList<string> RestaurantEmails() =>
    [
        "dietcare.hq@mealmate.kw",
        "greenbowl@mealmate.kw",
        "proteinlab@mealmate.kw",
        "fitplate@mealmate.kw",
        "oliveherb@mealmate.kw",
        "balancebox@mealmate.kw",
        "harvest@mealmate.kw",
        "leanflame@mealmate.kw",
        "noon@mealmate.kw",
        "aura@mealmate.kw",
    ];

    private static IEnumerable<(string Code, string Slug, string Ar, string En, string Category, decimal Kcal, decimal P, decimal C, decimal F, string Origin, bool Core, string? Allergens)> IngredientSpecs()
    {
        yield return ("MM-ING-01", "chicken-breast", "صدور دجاج", "Chicken breast", "poultry", 165, 31, 0, 3.6m, "KW", true, null);
        yield return ("MM-ING-02", "turkey", "ديك رومي", "Turkey", "poultry", 135, 29, 0, 2, "KW", true, null);
        yield return ("MM-ING-03", "beef", "لحم بقري", "Beef", "meat", 250, 26, 0, 15, "AU", true, null);
        yield return ("MM-ING-04", "lamb", "لحم ضاني", "Lamb", "meat", 258, 25, 0, 17, "NZ", true, null);
        yield return ("MM-ING-05", "salmon", "سالمون", "Salmon", "seafood", 208, 20, 0, 13, "NO", true, "FISH");
        yield return ("MM-ING-06", "shrimp", "جمبري", "Shrimp", "seafood", 99, 24, 0.2m, 0.3m, "IN", true, "CRUSTACEANS");
        yield return ("MM-ING-07", "tuna", "تونة", "Tuna", "seafood", 132, 28, 0, 1.3m, "TH", true, "FISH");
        yield return ("MM-ING-08", "egg", "بيض", "Egg", "dairy", 155, 13, 1.1m, 11, "KW", false, "EGGS");
        yield return ("MM-ING-09", "yogurt", "زبادي", "Yogurt", "dairy", 59, 10, 3.6m, 0.4m, "KW", false, "MILK");
        yield return ("MM-ING-10", "cheddar", "جبنة شيدر", "Cheddar", "dairy", 403, 25, 1.3m, 33, "NL", false, "MILK");
        yield return ("MM-ING-11", "avocado", "أفوكادو", "Avocado", "oils", 160, 2, 9, 15, "MX", true, null);
        yield return ("MM-ING-12", "broccoli", "بروكلي", "Broccoli", "produce", 34, 2.8m, 7, 0.4m, "NL", false, null);
        yield return ("MM-ING-13", "spinach", "سبانخ", "Spinach", "produce", 23, 2.9m, 3.6m, 0.4m, "KW", false, null);
        yield return ("MM-ING-14", "zucchini", "كوسة", "Zucchini", "produce", 17, 1.2m, 3.1m, 0.3m, "KW", false, null);
        yield return ("MM-ING-15", "cauliflower", "قرنبيط", "Cauliflower", "produce", 25, 1.9m, 5, 0.3m, "IN", false, null);
        yield return ("MM-ING-16", "cucumber", "خيار", "Cucumber", "produce", 16, 0.7m, 3.6m, 0.1m, "KW", false, null);
        yield return ("MM-ING-17", "tomato", "طماطم", "Tomato", "produce", 18, 0.9m, 3.9m, 0.2m, "KW", false, null);
        yield return ("MM-ING-18", "white-rice", "رز أبيض", "White rice", "grains", 130, 2.7m, 28, 0.3m, "IN", false, null);
        yield return ("MM-ING-19", "sweet-potato", "بطاطا حلوة", "Sweet potato", "produce", 86, 1.6m, 20, 0.1m, "EG", false, null);
        yield return ("MM-ING-20", "oats", "شوفان", "Oats", "grains", 389, 17, 66, 7, "AU", false, "GLUTEN");
        yield return ("MM-ING-21", "walnut", "عين جمل", "Walnut", "oils", 654, 15, 14, 65, "US", false, "TREE_NUTS");
        yield return ("MM-ING-22", "asparagus", "هليون", "Asparagus", "produce", 20, 2.2m, 3.9m, 0.1m, "PE", false, null);
        yield return ("MM-ING-23", "potato", "بطاطس", "Potato", "produce", 77, 2, 17, 0.1m, "EG", false, null);
        yield return ("MM-ING-24", "green-beans", "فاصوليا خضراء", "Green beans", "produce", 31, 1.8m, 7, 0.1m, "KE", false, null);
        yield return ("MM-ING-25", "lemon", "ليمون", "Lemon", "produce", 29, 1.1m, 9, 0.3m, "TR", false, null);
    }

    private static IEnumerable<MealSpec> MealSpecs()
    {
        yield return new(1, "WL", "breakfast", "meal-01-omelette.jpg", "أومليت السبانخ", "Spinach omelette", "بيض بياض مع سبانخ وطماطم طازجة.", "Egg-white omelette with spinach and tomato.", "EGGS", ["egg", "spinach", "tomato"], (420, 34, 38, 38, 14, 6));
        yield return new(2, "WL", "lunch", "meal-02-chicken-salad.jpg", "سلطة دجاج كينوا", "Chicken quinoa salad", "صدور دجاج مشوي مع خضار وليمون.", "Grilled chicken salad with greens and lemon.", "", ["chicken-breast", "cucumber", "tomato", "lemon"], (450, 36, 42, 42, 15, 6));
        yield return new(3, "WL", "dinner", "meal-03-baked-fish.jpg", "سمك مشوي مع كوسة", "Baked fish with zucchini", "فيليه أبيض مخبوز مع كوسة وأعشاب.", "Baked white fish with zucchini and herbs.", "FISH", ["tuna", "zucchini", "lemon"], (430, 35, 36, 36, 16, 6));
        yield return new(4, "WL", "snack", "meal-04-yogurt-snack.jpg", "زبادي وخيار", "Yogurt and cucumber", "زبادي يوناني مع خيار ونعناع.", "Greek yogurt with cucumber.", "MILK", ["yogurt", "cucumber"], (180, 14, 18, 18, 6, 3));
        yield return new(5, "LS", "breakfast", "meal-05-avocado-eggs.jpg", "بيض وأفوكادو", "Eggs and avocado", "بيض طازج مع أفوكادو وخضار ورقية.", "Eggs with avocado and greens.", "EGGS", ["egg", "avocado", "spinach"], (520, 36, 52, 52, 18, 6));
        yield return new(6, "LS", "lunch", "meal-06-salmon-rice.jpg", "سالمون مع رز", "Salmon rice bowl", "سالمون مشوي مع رز وبروكلي.", "Seared salmon with rice and broccoli.", "FISH", ["salmon", "white-rice", "broccoli"], (560, 38, 58, 58, 20, 6));
        yield return new(7, "LS", "dinner", "meal-07-herb-chicken.jpg", "دجاج بالأعشاب", "Herb roasted chicken", "دجاج مشوي مع خضار مشوية.", "Herb chicken with roasted vegetables.", "", ["chicken-breast", "broccoli", "tomato"], (540, 37, 54, 54, 19, 6));
        yield return new(8, "LS", "snack", "meal-08-oat-yogurt.jpg", "شوفان وزبادي", "Oats and yogurt", "كوب شوفان مع زبادي.", "Oats with yogurt.", "MILK,GLUTEN", ["oats", "yogurt"], (220, 14, 26, 26, 8, 3));
        yield return new(9, "AT", "breakfast", "meal-09-turkey-hash.jpg", "تركي وبطاطا حلوة", "Turkey sweet potato hash", "ديك رومي مع بطاطا حلوة وبيض.", "Turkey hash with sweet potato and egg.", "EGGS", ["turkey", "sweet-potato", "egg"], (620, 40, 78, 78, 18, 6));
        yield return new(10, "AT", "lunch", "meal-10-power-bowl.jpg", "بول الدجاج الرياضي", "Athlete chicken power bowl", "دجاج ورز وبروكلي ليوم التمرين.", "Chicken, rice and broccoli for training days.", "", ["chicken-breast", "white-rice", "broccoli"], (680, 44, 82, 82, 20, 6));
        yield return new(11, "AT", "dinner", "meal-11-beef-sweet-potato.jpg", "لحم وبطاطا حلوة", "Beef and sweet potato", "شرائح لحم مع بطاطا حلوة وفاصوليا.", "Beef with sweet potato and green beans.", "", ["beef", "sweet-potato", "green-beans"], (640, 42, 76, 76, 19, 6));
        yield return new(12, "AT", "snack", "meal-12-yogurt-walnut.jpg", "زبادي وعين جمل", "Yogurt and walnuts", "زبادي كثيف مع عين جمل.", "Thick yogurt with walnuts.", "MILK,TREE_NUTS", ["yogurt", "walnut"], (280, 20, 36, 36, 9, 3));
        yield return new(13, "MG", "breakfast", "meal-13-steak-eggs.jpg", "ستيك وبيض", "Steak and eggs", "شرائح لحم مع بيض وسبانخ.", "Steak slices with eggs and spinach.", "EGGS", ["beef", "egg", "spinach"], (720, 48, 88, 88, 22, 6));
        yield return new(14, "MG", "lunch", "meal-14-chicken-rice-xl.jpg", "دجاج ورز كبير", "Large chicken rice bowl", "حصة أكبر من الدجاج والرز لبناء العضلات.", "Larger chicken and rice portion for muscle gain.", "", ["chicken-breast", "white-rice", "broccoli"], (780, 52, 96, 96, 24, 6));
        yield return new(15, "MG", "dinner", "meal-15-lamb-potato.jpg", "لحم ضاني وبطاطس", "Lamb and potatoes", "ضاني مشوي مع بطاطس وبقدونس.", "Roast lamb with potatoes.", "", ["lamb", "potato", "tomato"], (740, 50, 90, 90, 23, 6));
        yield return new(16, "MG", "snack", "meal-16-tuna-yogurt.jpg", "تونة وزبادي", "Tuna yogurt cup", "تونة مع زبادي وخيار.", "Tuna with yogurt and cucumber.", "FISH,MILK", ["tuna", "yogurt", "cucumber"], (340, 26, 42, 42, 12, 3));
        yield return new(17, "KT", "breakfast", "meal-17-keto-salmon.jpg", "سالمون كيتو", "Keto salmon plate", "سالمون وأفوكادو وهليون بدون كارب.", "Salmon, avocado and asparagus with almost no carbs.", "FISH", ["salmon", "avocado", "asparagus"], (580, 36, 6, 5, 46, 5));
        yield return new(18, "KT", "lunch", "meal-18-keto-beef.jpg", "لحم كيتو", "Keto beef bowl", "لحم مع قرنبيط وسبانخ.", "Beef with cauliflower and spinach.", "", ["beef", "cauliflower", "spinach"], (620, 38, 5, 4, 50, 5));
        yield return new(19, "KT", "dinner", "meal-19-keto-shrimp.jpg", "جمبري كيتو", "Keto garlic shrimp", "جمبري مع نودلز كوسة.", "Shrimp over zucchini noodles.", "CRUSTACEANS", ["shrimp", "zucchini", "lemon"], (600, 37, 6, 5, 48, 5));
        yield return new(20, "KT", "snack", "meal-20-keto-cheese.jpg", "جبنة كيتو", "Keto cheese snack", "شيدر وعين جمل وخيار.", "Cheddar, walnuts and cucumber.", "MILK,TREE_NUTS", ["cheddar", "walnut", "cucumber"], (210, 14, 3, 2, 16, 3));
    }

    private sealed record MealSpec(
        int Number,
        string Program,
        string Slot,
        string File,
        string NameAr,
        string NameEn,
        string DescriptionAr,
        string DescriptionEn,
        string Allergens,
        string[] Ingredients,
        (decimal Kcal, decimal P, decimal C, decimal Net, decimal F, decimal Fiber) Macros);
}
