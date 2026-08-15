using MealMate.Application.Common.Security;
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

public static class DemoRestaurantDataSeeder
{
    public const string Password = "Restaurant#2026";
    private const string Media = "https://ik.imagekit.io/f7h9cj23x/catalog/restaurants/";
    private const string IngredientMedia = "https://ik.imagekit.io/f7h9cj23x/catalog/ingredient-photos/";

    public static async Task SeedAsync(IServiceProvider services, int restaurantCount, CancellationToken cancellationToken = default)
    {
        if (restaurantCount is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(restaurantCount));

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoRestaurantDataSeeder");

        await ProgramsSubscriptionsSeeder.SeedProgramsAndBundlesAsync(db, logger, cancellationToken);
        await EnsureSharedCatalogAsync(db, cancellationToken);

        var bundles = await db.Bundles.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);
        if (bundles.Count == 0) throw new InvalidOperationException("Seed the program catalog before demo restaurants.");

        var programs = await db.Programs.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var country = await db.Countries.AsNoTracking().FirstOrDefaultAsync(x => x.Code == "KW", cancellationToken);
        var regions = country is null
            ? []
            : await db.Regions.AsNoTracking().Where(x => x.CountryId == country.Id && x.IsActive).OrderBy(x => x.NameEn).Take(12).ToListAsync(cancellationToken);

        var brands = Brands().Take(Math.Min(restaurantCount, 10)).ToList();
        var created = 0;
        for (var index = 0; index < brands.Count; index++)
        {
            var brand = brands[index];
            var existingUser = await db.Users.FirstOrDefaultAsync(x => x.Email == brand.Email, cancellationToken);
            if (existingUser is not null && await db.RestaurantProfiles.AnyAsync(x => x.UserId == existingUser.Id, cancellationToken))
            {
                logger.LogInformation("Skipped existing restaurant {Email}", brand.Email);
                continue;
            }

            ApplicationUser user;
            if (existingUser is not null)
            {
                user = existingUser;
            }
            else
            {
                user = new ApplicationUser
                {
                    UserName = brand.Email,
                    Email = brand.Email,
                    EmailConfirmed = true,
                    PhoneNumber = brand.Phone,
                    PhoneNumberConfirmed = true,
                    FullName = brand.OwnerName,
                    UserType = UserType.Restaurant,
                    AccountStatus = AccountStatus.Active,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                var result = await userManager.CreateAsync(user, Password);
                if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
                await userManager.AddToRoleAsync(user, AppRoles.RestaurantOwner);
            }

            var restaurant = new RestaurantProfile
            {
                UserId = user.Id,
                LegalNameAr = brand.LegalAr,
                LegalNameEn = brand.LegalEn,
                TradeNameAr = brand.NameAr,
                TradeNameEn = brand.NameEn,
                CommercialLicenseNumber = brand.License,
                CountryId = country?.Id,
                PrimaryRegionId = regions.ElementAtOrDefault(index % Math.Max(regions.Count, 1))?.Id,
                ContactPhone = brand.Phone,
                ContactEmail = brand.Email,
                VerificationStatus = VerificationStatus.Approved,
                IsOperationallyActive = true,
                ActivatedAtUtc = DateTime.UtcNow,
            };
            db.RestaurantProfiles.Add(restaurant);
            user.RestaurantId = restaurant.Id;
            await userManager.UpdateAsync(user);

            var registration = new RestaurantRegistration
            {
                SubmittedByUserId = user.Id,
                LegalNameAr = brand.LegalAr,
                LegalNameEn = brand.LegalEn,
                TradeNameAr = brand.NameAr,
                TradeNameEn = brand.NameEn,
                ContactPhone = brand.Phone,
                ContactEmail = brand.Email,
                CommercialRegistrationNumber = brand.Cr,
                CommercialRegistrationExpiry = new DateOnly(2028, 3, 15),
                CommercialLicenseNumber = brand.License,
                CommercialLicenseExpiry = new DateOnly(2027, 8, 1),
                FoodLicenseNumber = $"FL-{index + 1:D5}",
                FoodLicenseExpiry = new DateOnly(2027, 6, 30),
                LegalEntityType = "W.L.L",
                CompanyCivilId = brand.CivilId,
                Website = brand.Website,
                InstagramHandle = brand.Instagram,
                RestaurantCode = brand.Code,
                OwnerName = brand.OwnerName,
                AuthorizedSignatoryCivilId = brand.CivilId,
                AuthorizedSignatoryTitle = "General Manager",
                PrimaryContactName = brand.OwnerName,
                PrimaryContactTitle = "Operations Manager",
                PrimaryContactPhone = brand.Phone,
                PrimaryContactEmail = brand.Email,
                BankName = "National Bank of Kuwait",
                BankBeneficiaryName = brand.LegalEn,
                Iban = brand.Iban,
                BankAccountNumber = $"12345678{index + 1:D2}",
                SwiftCode = "NBKKKWKK",
                SettlementCurrency = "KWD",
                Address = brand.AddressEn,
                CountryId = country?.Id,
                RegionId = restaurant.PrimaryRegionId,
                ServiceRegionIdsJson = regions.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(regions.Select(x => x.Id)),
                Status = VerificationStatus.Approved,
                ResultingRestaurantId = restaurant.Id,
                DeliveryByRestaurantConfirmed = true,
                DataResponsibilityConfirmed = true,
                SubmissionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                ReviewedAtUtc = DateTime.UtcNow,
            };
            db.RestaurantRegistrations.Add(registration);

            var logo = new Document
            {
                OwnerType = DocumentOwnerType.Restaurant,
                OwnerId = restaurant.Id,
                DocumentType = DocumentType.ProfileImage,
                FileName = $"logo-{brand.Slug}.png",
                ContentType = "image/png",
                StoragePath = brand.LogoUrl,
                SizeBytes = 80_000,
            };
            var cover = new Document
            {
                OwnerType = DocumentOwnerType.Restaurant,
                OwnerId = restaurant.Id,
                DocumentType = DocumentType.BranchPhoto,
                FileName = $"photo-{brand.Slug}.jpg",
                ContentType = "image/jpeg",
                StoragePath = brand.PhotoUrl,
                SizeBytes = 220_000,
            };
            db.Documents.AddRange(logo, cover);
            db.DocumentVerifications.AddRange(
                new DocumentVerification { DocumentId = logo.Id, Status = VerificationStatus.Approved, ReviewedAtUtc = DateTime.UtcNow },
                new DocumentVerification { DocumentId = cover.Id, Status = VerificationStatus.Approved, ReviewedAtUtc = DateTime.UtcNow });

            db.RestaurantBranches.Add(new RestaurantBranch
            {
                RestaurantRegistrationId = registration.Id,
                BranchCode = $"{brand.Code}-HQ",
                NameAr = $"{brand.NameAr} - الفرع الرئيسي",
                NameEn = $"{brand.NameEn} - Main branch",
                AddressAr = brand.AddressAr,
                AddressEn = brand.AddressEn,
                GovernorateAr = "العاصمة",
                GovernorateEn = "Capital",
                ContactPhone = brand.Phone,
                IsActive = true,
                IsPrimary = true,
                PhotoStorageKey = brand.PhotoUrl,
            });

            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                db.RestaurantOperatingSchedules.Add(new RestaurantOperatingSchedule
                {
                    RestaurantId = restaurant.Id,
                    DayOfWeek = day,
                    IsWorkingDay = true,
                });
            }

            foreach (var region in regions)
            {
                db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
                {
                    RestaurantId = restaurant.Id,
                    RegionId = region.Id,
                    IsActive = true,
                    DeliveryWindowStart = new TimeOnly(8, 0),
                    DeliveryWindowEnd = new TimeOnly(16, 0),
                    PreparationLeadTimeMinutes = 90,
                });
            }

            db.DailyCapacities.Add(new DailyCapacity
            {
                RestaurantId = restaurant.Id,
                ServiceDate = new DateOnly(2020, 1, 1),
                CapacityType = CapacityType.Base,
                MaxBoxes = 80 + (index * 5),
            });

            var ingredients = CreateIngredients(restaurant.Id, index);
            db.Ingredients.AddRange(ingredients);

            var menuByProgram = programs.ToDictionary(
                program => program.Id,
                program => new Menu
                {
                    RestaurantId = restaurant.Id,
                    ProgramId = program.Id,
                    NameAr = $"قائمة {brand.NameAr} - {program.NameAr}",
                    NameEn = $"{brand.NameEn} - {program.NameEn}",
                    IsActive = true,
                });
            db.Menus.AddRange(menuByProgram.Values);

            var meals = new List<Meal>();
            foreach (var program in programs)
            {
                var macros = MacrosFor(program.ShortCode);
                foreach (var slot in new[] { "breakfast", "lunch", "dinner", "snack" })
                {
                    var meal = CreateMeal(restaurant.Id, menuByProgram[program.Id].Id, program.Id, brand.Code, program.ShortCode ?? program.Code, slot, macros[slot]);
                    meals.Add(meal);
                }
            }
            db.Meals.AddRange(meals);
            db.MealImages.AddRange(meals.Select(meal => new MealImage
            {
                MealId = meal.Id,
                ImageUrl = MealImageUrl(meal.Slot),
                DisplayOrder = 1,
                IsPrimary = true,
                AltTextAr = meal.NameAr,
                AltTextEn = meal.NameEn,
            }));
            db.MealIngredients.AddRange(meals.SelectMany((meal, mealIndex) => Enumerable.Range(0, 3).Select(offset => new MealIngredient
            {
                MealId = meal.Id,
                IngredientId = ingredients[(mealIndex + offset) % ingredients.Count].Id,
                PortionGrams = 80 + (offset * 30),
                SortOrder = offset + 1,
            })));

            var price26 = 90m + (index * 12m);
            foreach (var bundle in bundles)
            {
                db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
                {
                    RestaurantId = restaurant.Id,
                    ProgramId = bundle.ProgramId,
                    BundleId = bundle.Id,
                    IsActive = true,
                });
                db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
                {
                    RestaurantId = restaurant.Id,
                    ProgramId = bundle.ProgramId,
                    BundleId = bundle.Id,
                    ReferencePrice26 = new Money(price26 + (bundle.SortOrder * 4m), "KWD"),
                    ContractedSupplyPrice26 = new Money((price26 + (bundle.SortOrder * 4m)) * 0.78m, "KWD"),
                    LockStatus = PriceLockStatus.Approved,
                    EffectiveFromUtc = DateTime.UtcNow.AddDays(-2),
                });
            }

            var plan = new MenuPlan
            {
                RestaurantId = restaurant.Id,
                NameAr = $"خطة {brand.NameAr}",
                NameEn = $"{brand.NameEn} weekly plan",
                Source = MenuPlanSource.RestaurantCreated,
                IsTemplate = false,
                Mode = ScheduleMode.Weekday,
            };
            db.MenuPlans.Add(plan);
            var version = new MenuPlanVersion
            {
                MenuPlanId = plan.Id,
                VersionNumber = 1,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                Status = VerificationStatus.Approved,
                ReviewedAtUtc = DateTime.UtcNow,
            };
            db.MenuPlanVersions.Add(version);
            foreach (var program in programs)
            {
                db.MenuPlanPrograms.Add(new MenuPlanProgram
                {
                    MenuPlanId = plan.Id,
                    ProgramId = program.Id,
                    EffectiveFrom = new DateOnly(2026, 1, 1),
                    IsPrimaryOwner = program.ShortCode == "LS",
                });
            }

            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                foreach (var meal in meals)
                {
                    db.WeekdayMealAssignments.Add(new WeekdayMealAssignment
                    {
                        MenuPlanVersionId = version.Id,
                        DayOfWeek = day,
                        ProgramId = meal.ProgramId!.Value,
                        MealId = meal.Id,
                        Slot = meal.Slot!,
                        SortOrder = 1,
                        IsPrimary = true,
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            created++;
            logger.LogInformation("Created restaurant {Email} / {Phone}", brand.Email, brand.Phone);
        }

        logger.LogInformation("Created {Count} complete restaurants. Password: {Password}", created, Password);
    }

    public static async Task EnsureSharedCatalogAsync(AppDbContext db, CancellationToken ct)
    {
        await EnsurePricingRuleAsync(db, ct);
        await AlignBundleMealCountsAsync(db, ct);
    }

    private static async Task EnsurePricingRuleAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.PricingRuleVersions.AnyAsync(x => x.IsActive, ct)) return;
        db.PricingRuleVersions.Add(new PricingRuleVersion
        {
            RMax = Percentage.FromPercent(30m),
            RMin = Percentage.FromPercent(15m),
            DShort = 1,
            DLong = 26,
            Gamma = 1m,
            RoundingStepKwd = 0.05m,
            IsActive = true,
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task AlignBundleMealCountsAsync(AppDbContext db, CancellationToken ct)
    {
        var bundles = await db.Bundles.ToListAsync(ct);
        var changed = false;
        foreach (var bundle in bundles)
        {
            var sum = bundle.BreakfastCount + bundle.LunchCount + bundle.DinnerCount + bundle.SnackCount;
            if (sum > 0 && bundle.MealsPerDay != sum)
            {
                bundle.MealsPerDay = sum;
                changed = true;
            }
        }
        if (changed) await db.SaveChangesAsync(ct);
    }

    private static List<Ingredient> CreateIngredients(Guid restaurantId, int index)
    {
        var templates = new (string Slug, string Ar, string En, string Category, decimal Kcal, decimal P, decimal C, decimal F, bool Core)[]
        {
            ("chicken-breast", "صدور دجاج", "Chicken breast", "poultry", 165, 31, 0, 3.6m, true),
            ("beef", "لحم بقري", "Beef", "meat", 250, 26, 0, 15, true),
            ("salmon", "سالمون", "Salmon", "seafood", 208, 20, 0, 13, true),
            ("avocado", "أفوكادو", "Avocado", "oils", 160, 2, 9, 15, true),
            ("white-rice", "رز أبيض", "White rice", "grains", 130, 2.7m, 28, 0.3m, false),
            ("broccoli", "بروكلي", "Broccoli", "produce", 34, 2.8m, 7, 0.4m, false),
            ("yogurt", "زبادي", "Yogurt", "dairy", 59, 10, 3.6m, 0.4m, false),
            ("oats", "شوفان", "Oats", "grains", 389, 17, 66, 7, false),
            ("egg", "بيض", "Eggs", "dairy", 155, 13, 1.1m, 11, false),
            ("tomato", "طماطم", "Tomato", "produce", 18, 0.9m, 3.9m, 0.2m, false),
        };
        return templates.Select((item, i) => new Ingredient
        {
            RestaurantId = restaurantId,
            Code = $"R{index + 1:D2}-ING-{i + 1:D2}",
            NameAr = item.Ar,
            NameEn = item.En,
            Category = item.Category,
            DefaultCaloriesPer100g = item.Kcal,
            DefaultProteinPer100g = item.P,
            DefaultCarbsPer100g = item.C,
            DefaultFatPer100g = item.F,
            DefaultWeightGrams = 100,
            OriginCountryCode = item.Core ? "KW" : "IN",
            SupplierName = "MealMate Demo Supply",
            IsLocal = item.Core,
            IsHalalCertified = true,
            IsOriginVisibleToCustomers = true,
            IsCore = item.Core,
            RestaurantIconUrl = item.Core ? $"{IngredientMedia}{item.Slug}.webp" : null,
            MealIconUrl = $"{IngredientMedia}{item.Slug}.webp",
            IsActive = true,
        }).ToList();
    }

    private static Meal CreateMeal(
        Guid restaurantId,
        Guid menuId,
        Guid programId,
        string brandCode,
        string programCode,
        string slot,
        (decimal Kcal, decimal P, decimal C, decimal F, decimal Fiber) macros)
    {
        var names = slot switch
        {
            "breakfast" => ("فطور البيض والأفوكادو", "Egg & avocado breakfast"),
            "lunch" => ("دجاج مشوي مع أرز", "Grilled chicken with rice"),
            "dinner" => ("سلمون وخضار موسمية", "Salmon with seasonal vegetables"),
            _ => ("زبادي يوناني بالشوفان", "Greek yogurt with oats"),
        };
        return new Meal
        {
            MenuId = menuId,
            RestaurantId = restaurantId,
            ProgramId = programId,
            Code = $"{brandCode}-{programCode}-{slot}",
            NameAr = names.Item1,
            NameEn = names.Item2,
            Slot = slot,
            DescriptionAr = "وجبة طازجة محسوبة القيم الغذائية ومعدة يوميًا.",
            DescriptionEn = "Fresh daily meal with calculated nutritional values.",
            Calories = macros.Kcal,
            ProteinG = macros.P,
            CarbsG = macros.C,
            NetCarbsG = macros.C,
            FatG = macros.F,
            FiberG = macros.Fiber,
            IsAvailableForNewOrders = true,
            ApprovalStatus = VerificationStatus.Approved,
            ReviewedAtUtc = DateTime.UtcNow,
            AllergensReviewed = true,
            DataResponsibilityConfirmed = true,
            PresentAllergenCodes = string.Empty,
            NutritionSource = "lab",
        };
    }

    private static string MealImageUrl(string? slot) => slot switch
    {
        "breakfast" => $"{Media}meal-breakfast.jpg",
        "lunch" => $"{Media}meal-lunch.jpg",
        "dinner" => $"{Media}meal-dinner.jpg",
        _ => $"{Media}meal-snack.jpg",
    };

    private static Dictionary<string, (decimal Kcal, decimal P, decimal C, decimal F, decimal Fiber)> MacrosFor(string? shortCode) =>
        shortCode switch
        {
            "WL" => new()
            {
                ["breakfast"] = (420, 34, 38, 14, 6),
                ["lunch"] = (450, 36, 42, 15, 6),
                ["dinner"] = (430, 35, 36, 16, 6),
                ["snack"] = (180, 14, 18, 6, 3),
            },
            "AT" => new()
            {
                ["breakfast"] = (620, 40, 78, 18, 6),
                ["lunch"] = (680, 44, 82, 20, 6),
                ["dinner"] = (640, 42, 76, 19, 6),
                ["snack"] = (280, 20, 36, 9, 3),
            },
            "MG" => new()
            {
                ["breakfast"] = (720, 48, 88, 22, 6),
                ["lunch"] = (780, 52, 96, 24, 6),
                ["dinner"] = (740, 50, 90, 23, 6),
                ["snack"] = (340, 26, 42, 12, 3),
            },
            "KT" => new()
            {
                ["breakfast"] = (580, 36, 6, 46, 5),
                ["lunch"] = (620, 38, 5, 50, 5),
                ["dinner"] = (600, 37, 6, 48, 5),
                ["snack"] = (210, 14, 3, 16, 3),
            },
            "LC" => new()
            {
                ["breakfast"] = (500, 38, 22, 26, 6),
                ["lunch"] = (540, 40, 24, 28, 6),
                ["dinner"] = (520, 39, 20, 27, 6),
                ["snack"] = (210, 16, 10, 12, 3),
            },
            _ => new()
            {
                ["breakfast"] = (520, 36, 52, 18, 6),
                ["lunch"] = (560, 38, 58, 20, 6),
                ["dinner"] = (540, 37, 54, 19, 6),
                ["snack"] = (220, 14, 26, 8, 3),
            },
        };

    private static IEnumerable<Brand> Brands()
    {
        yield return new("diet-care", "DC01", "مطبخ دايت كير", "Diet Care", "شركة دايت كير الغذائية ذ.م.م", "Diet Care Food Company W.L.L", "dietcare.hq@mealmate.kw", "+96550001011", "باسل الشمري", "KW-CR-100001", "LIC-100001", "285101234567", "KW89NBKK00000001234567890101", "https://dietcare.kw", "@dietcare.kw", "العاصمة، برج الحمراء، الدور 15", "Kuwait City, Al Hamra Tower, Floor 15");
        yield return new("green-bowl", "GB02", "جرين بول", "Green Bowl", "جرين بول للوجبات الصحية ذ.م.م", "Green Bowl Healthy Meals W.L.L", "greenbowl@mealmate.kw", "+96550001002", "نورة العتيبي", "KW-CR-100002", "LIC-100002", "285101234568", "KW89NBKK00000001234567890102", "https://greenbowl.kw", "@greenbowl.kw", "السالمية، شارع سالم المبارك", "Salmiya, Salem Al Mubarak St");
        yield return new("protein-lab", "PL03", "بروتين لاب", "Protein Lab", "بروتين لاب للمطاعم ذ.م.م", "Protein Lab Restaurants W.L.L", "proteinlab@mealmate.kw", "+96550001003", "خالد العنزي", "KW-CR-100003", "LIC-100003", "285101234569", "KW89NBKK00000001234567890103", "https://proteinlab.kw", "@proteinlab.kw", "حولي، شارع تونس", "Hawally, Tunisia St");
        yield return new("fit-plate", "FP04", "فيت بليت", "Fit Plate", "فيت بليت للتغذية ذ.م.م", "Fit Plate Nutrition W.L.L", "fitplate@mealmate.kw", "+96550001004", "سارة العلي", "KW-CR-100004", "LIC-100004", "285101234570", "KW89NBKK00000001234567890104", "https://fitplate.kw", "@fitplate.kw", "الجابرية، قطعة 1أ", "Jabriya, Block 1A");
        yield return new("olive-herb", "OH05", "زيتون وأعشاب", "Olive Herb", "زيتون وأعشاب للمواد الغذائية", "Olive Herb Food Establishment", "oliveherb@mealmate.kw", "+96550001005", "ليان المطيري", "KW-CR-100005", "LIC-100005", "285101234571", "KW89NBKK00000001234567890105", "https://oliveherb.kw", "@oliveherb.kw", "الشويخ، القطعة 4", "Shuwaikh, Block 4");
        yield return new("balance-box", "BB06", "بالانس بوكس", "Balance Box", "بالانس بوكس للوجبات المعلبة ذ.م.م", "Balance Box Meal Prep W.L.L", "balancebox@mealmate.kw", "+96550001006", "عبدالرحمن المطيري", "KW-CR-100006", "LIC-100006", "285101234572", "KW89NBKK00000001234567890106", "https://balancebox.kw", "@balancebox.kw", "الفروانية، شارع حبيب مناور", "Farwaniya, Habib Munawer St");
        yield return new("harvest-kitchen", "HK07", "هارفست كيتشن", "Harvest Kitchen", "هارفست كيتشن للمطابخ المركزية", "Harvest Kitchen Central Kitchens", "harvest@mealmate.kw", "+96550001007", "مريم الشمري", "KW-CR-100007", "LIC-100007", "285101234573", "KW89NBKK00000001234567890107", "https://harvest.kw", "@harvest.kw", "الري، المنطقة الصناعية", "Rai, Industrial Area");
        yield return new("lean-flame", "LF08", "لين فليم", "Lean Flame", "لين فليم للمشاوي الصحية ذ.م.م", "Lean Flame Healthy Grill W.L.L", "leanflame@mealmate.kw", "+96550001008", "فهد الدوسري", "KW-CR-100008", "LIC-100008", "285101234574", "KW89NBKK00000001234567890108", "https://leanflame.kw", "@leanflame.kw", "الأحمدي، الفحيحيل", "Ahmadi, Fahaheel");
        yield return new("noon-nutrition", "NN09", "نون نيوتريشن", "Noon Nutrition", "نون نيوتريشن للحمية ذ.م.م", "Noon Nutrition Diet W.L.L", "noon@mealmate.kw", "+96550001009", "هند الرشيد", "KW-CR-100009", "LIC-100009", "285101234575", "KW89NBKK00000001234567890109", "https://noon.kw", "@noon.kw", "الجهراء، سوق الجهراء", "Jahra, Jahra Souq");
        yield return new("aura-meals", "AM10", "أورا ميلز", "Aura Meals", "أورا ميلز للوجبات الفاخرة ذ.م.م", "Aura Meals Premium W.L.L", "aura@mealmate.kw", "+96550001010", "دانة الخالد", "KW-CR-100010", "LIC-100010", "285101234576", "KW89NBKK00000001234567890110", "https://aura.kw", "@aura.kw", "المسيلة، شارع الخليج", "Messila, Gulf Road");
    }

    private sealed record Brand(
        string Slug,
        string Code,
        string NameAr,
        string NameEn,
        string LegalAr,
        string LegalEn,
        string Email,
        string Phone,
        string OwnerName,
        string Cr,
        string License,
        string CivilId,
        string Iban,
        string Website,
        string Instagram,
        string AddressAr,
        string AddressEn)
    {
        public string LogoUrl => $"{Media}logo-{Slug}.png";
        public string PhotoUrl => $"{Media}photo-{Slug}.jpg";
    }
}
