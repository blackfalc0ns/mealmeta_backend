using MealMate.Application.Common.Geography;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
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
        var fallbackRegions = country is null
            ? []
            : await db.Regions.AsNoTracking().Where(x => x.CountryId == country.Id && x.IsActive).OrderBy(x => x.NameEn).Take(12).ToListAsync(cancellationToken);
        var areas = country is null
            ? []
            : await db.Regions.AsNoTracking()
                .Where(x => x.CountryId == country.Id && x.IsActive && x.Type == RegionType.Area && x.CentroidLatitude != null && x.CentroidLongitude != null)
                .ToListAsync(cancellationToken);

        var brands = DemoRestaurantBrandCatalog.All.Take(restaurantCount).ToList();
        var created = 0;
        var refreshed = 0;
        for (var index = 0; index < brands.Count; index++)
        {
            var brand = brands[index];
            var nearby = NearbyAreas(brand.Latitude, brand.Longitude, areas);
            if (nearby.Count == 0) nearby = fallbackRegions;
            var existingUser = await db.Users.FirstOrDefaultAsync(x => x.Email == brand.Email, cancellationToken);
            if (existingUser is not null && await db.RestaurantProfiles.AnyAsync(x => x.UserId == existingUser.Id, cancellationToken))
            {
                await RefreshExistingRestaurantAsync(db, existingUser.Id, brand, bundles, nearby, cancellationToken);
                refreshed++;
                logger.LogInformation("Refreshed location and pricing for {Email}", brand.Email);
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
                PrimaryRegionId = nearby.ElementAtOrDefault(0)?.Id,
                ContactPhone = brand.Phone,
                ContactEmail = brand.Email,
                Latitude = brand.Latitude,
                Longitude = brand.Longitude,
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
                Latitude = brand.Latitude,
                Longitude = brand.Longitude,
                ServiceRegionIdsJson = nearby.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(nearby.Select(x => x.Id)),
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
                GovernorateAr = brand.GovernorateAr,
                GovernorateEn = brand.GovernorateEn,
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

            foreach (var region in nearby)
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

            var price26 = brand.Price26;
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

            var weekdayDays = new (int Number, DayOfWeek Day, string Ar, string En)[]
            {
                (1, DayOfWeek.Saturday, "السبت", "Saturday"),
                (2, DayOfWeek.Sunday, "الأحد", "Sunday"),
                (3, DayOfWeek.Monday, "الاثنين", "Monday"),
                (4, DayOfWeek.Tuesday, "الثلاثاء", "Tuesday"),
                (5, DayOfWeek.Wednesday, "الأربعاء", "Wednesday"),
                (6, DayOfWeek.Thursday, "الخميس", "Thursday"),
            };
            var menuDays = weekdayDays.Select(day => new MenuDay
            {
                MenuPlanVersionId = version.Id,
                DayNumber = day.Number,
                NameAr = day.Ar,
                NameEn = day.En,
            }).ToList();
            db.MenuDays.AddRange(menuDays);

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

            foreach (var menuDay in menuDays)
            {
                foreach (var meal in meals)
                {
                    db.MenuDayMeals.Add(new MenuDayMeal
                    {
                        MenuDayId = menuDay.Id,
                        MealId = meal.Id,
                        ProgramId = meal.ProgramId!.Value,
                        Slot = meal.Slot!,
                        SortOrder = 1,
                        IsPrimary = true,
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            created++;
            logger.LogInformation("Created restaurant {Email} / {Phone} at {Lat},{Lng}", brand.Email, brand.Phone, brand.Latitude, brand.Longitude);
        }

        await RestaurantDeliveryCoverageBackfill.SeedAsync(db, logger, cancellationToken);
        await SeedClassificationsAsync(scope.ServiceProvider, logger, cancellationToken);
        logger.LogInformation(
            "Created {Count} complete restaurants and refreshed {Refreshed}. Password: {Password}",
            created,
            refreshed,
            Password);
    }

    public static async Task SeedElevenDayCycleAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoRestaurantDataSeeder");

        await ProgramsSubscriptionsSeeder.SeedProgramsAndBundlesAsync(db, logger, cancellationToken);
        await EnsureSharedCatalogAsync(db, cancellationToken);

        const string email = "cycle11@mealmate.kw";
        const string phone = "+96550001021";
        var bundles = await db.Bundles.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);
        var programs = await db.Programs.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var country = await db.Countries.AsNoTracking().FirstOrDefaultAsync(x => x.Code == "KW", cancellationToken);
        var regions = country is null
            ? []
            : await db.Regions.AsNoTracking().Where(x => x.CountryId == country.Id && x.IsActive).OrderBy(x => x.NameEn).Take(12).ToListAsync(cancellationToken);

        var existing = await db.RestaurantProfiles.FirstOrDefaultAsync(x => x.ContactEmail == email, cancellationToken);
        if (existing is not null)
        {
            await CompleteElevenDayCycleRestaurantAsync(db, existing, bundles, regions, cancellationToken);
            logger.LogInformation("Completed logo, pricing, and profile for {Email}", email);
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            PhoneNumber = phone,
            PhoneNumberConfirmed = true,
            FullName = "يوسف الدورة",
            UserType = UserType.Restaurant,
            AccountStatus = AccountStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
        };
        var createdUser = await userManager.CreateAsync(user, Password);
        if (!createdUser.Succeeded) throw new InvalidOperationException(string.Join("; ", createdUser.Errors.Select(x => x.Description)));
        await userManager.AddToRoleAsync(user, AppRoles.RestaurantOwner);

        var restaurant = new RestaurantProfile
        {
            UserId = user.Id,
            LegalNameAr = "إيليفن سايكل للوجبات ذ.م.م",
            LegalNameEn = "Eleven Cycle Meals W.L.L",
            TradeNameAr = "إيليفن سايكل",
            TradeNameEn = "Eleven Cycle",
            CommercialLicenseNumber = "LIC-110011",
            CountryId = country?.Id,
            PrimaryRegionId = regions.FirstOrDefault()?.Id,
            ContactPhone = phone,
            ContactEmail = email,
            Latitude = 29.3875m,
            Longitude = 47.9952m,
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
            LegalNameAr = restaurant.LegalNameAr,
            LegalNameEn = restaurant.LegalNameEn,
            TradeNameAr = restaurant.TradeNameAr,
            TradeNameEn = restaurant.TradeNameEn,
            ContactPhone = phone,
            ContactEmail = email,
            CommercialRegistrationNumber = "KW-CR-110011",
            CommercialRegistrationExpiry = new DateOnly(2028, 3, 15),
            CommercialLicenseNumber = "LIC-110011",
            CommercialLicenseExpiry = new DateOnly(2027, 8, 1),
            FoodLicenseNumber = "FL-11011",
            FoodLicenseExpiry = new DateOnly(2027, 6, 30),
            LegalEntityType = "W.L.L",
            CompanyCivilId = "285101234599",
            Website = "https://elevencycle.kw",
            InstagramHandle = "@elevencycle.kw",
            RestaurantCode = "C11",
            OwnerName = "يوسف الدورة",
            AuthorizedSignatoryCivilId = "285101234599",
            AuthorizedSignatoryTitle = "General Manager",
            PrimaryContactName = "يوسف الدورة",
            PrimaryContactTitle = "Operations Manager",
            PrimaryContactPhone = phone,
            PrimaryContactEmail = email,
            BankName = "National Bank of Kuwait",
            BankBeneficiaryName = "Eleven Cycle Meals W.L.L",
            Iban = "KW89NBKK00000001234567890121",
            BankAccountNumber = "1234567821",
            SwiftCode = "NBKKKWKK",
            SettlementCurrency = "KWD",
            Address = "Kuwait City, Sharq",
            Latitude = 29.3875m,
            Longitude = 47.9952m,
            Status = VerificationStatus.Approved,
            ResultingRestaurantId = restaurant.Id,
            DeliveryByRestaurantConfirmed = true,
            DataResponsibilityConfirmed = true,
            CountryId = country?.Id,
            RegionId = restaurant.PrimaryRegionId,
            ServiceRegionIdsJson = regions.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(regions.Select(x => x.Id)),
            SubmissionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
            ReviewedAtUtc = DateTime.UtcNow,
        };
        db.RestaurantRegistrations.Add(registration);
        AddCycleRestaurantMedia(db, restaurant.Id);
        db.RestaurantBranches.Add(new RestaurantBranch
        {
            RestaurantRegistrationId = registration.Id,
            BranchCode = "C11-HQ",
            NameAr = "إيليفن سايكل - الرئيسي",
            NameEn = "Eleven Cycle - Main",
            AddressAr = "مدينة الكويت، الشرق",
            AddressEn = "Kuwait City, Sharq",
            GovernorateAr = "العاصمة",
            GovernorateEn = "Capital",
            ContactPhone = phone,
            IsActive = true,
            IsPrimary = true,
            PhotoStorageKey = $"{Media}photo-aura-meals.jpg",
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
            MaxBoxes = 90,
        });

        var ingredients = CreateIngredients(restaurant.Id, 10);
        db.Ingredients.AddRange(ingredients);
        var menus = programs.ToDictionary(
            program => program.Id,
            program => new Menu
            {
                RestaurantId = restaurant.Id,
                ProgramId = program.Id,
                NameAr = $"قائمة إيليفن - {program.NameAr}",
                NameEn = $"Eleven - {program.NameEn}",
                IsActive = true,
            });
        db.Menus.AddRange(menus.Values);

        var meals = new List<Meal>();
        for (var dayNumber = 1; dayNumber <= 11; dayNumber++)
        {
            foreach (var program in programs)
            {
                var macros = MacrosFor(program.ShortCode);
                foreach (var slot in new[] { "breakfast", "lunch", "dinner", "snack" })
                {
                    var meal = CreateMeal(restaurant.Id, menus[program.Id].Id, program.Id, $"C11D{dayNumber:D2}", program.ShortCode ?? program.Code, slot, macros[slot]);
                    meal.NameAr = $"{CycleSlotNameAr(slot)} — منيو {dayNumber}";
                    meal.NameEn = $"{CycleSlotNameEn(slot)} — Menu {dayNumber}";
                    meals.Add(meal);
                }
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
                ReferencePrice26 = new Money(90m + (10 * 12m) + (bundle.SortOrder * 4m), "KWD"),
                ContractedSupplyPrice26 = new Money((90m + (10 * 12m) + (bundle.SortOrder * 4m)) * 0.78m, "KWD"),
                LockStatus = PriceLockStatus.Approved,
                EffectiveFromUtc = DateTime.UtcNow.AddDays(-2),
            });
        }

        var start = DateOnly.FromDateTime(DateTime.UtcNow);
        var plan = new MenuPlan
        {
            RestaurantId = restaurant.Id,
            NameAr = "دورة 11 منيو",
            NameEn = "11-menu cycle",
            Source = MenuPlanSource.RestaurantCreated,
            IsTemplate = false,
            Mode = ScheduleMode.Cycle,
        };
        db.MenuPlans.Add(plan);
        var version = new MenuPlanVersion
        {
            MenuPlanId = plan.Id,
            VersionNumber = 1,
            EffectiveFrom = start,
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
                EffectiveFrom = start,
                IsPrimaryOwner = program.ShortCode == "LS",
            });
        }

        for (var dayNumber = 1; dayNumber <= 11; dayNumber++)
        {
            var menuDay = new MenuDay
            {
                MenuPlanVersionId = version.Id,
                DayNumber = dayNumber,
                NameAr = $"منيو {dayNumber}",
                NameEn = $"Menu {dayNumber}",
            };
            db.MenuDays.Add(menuDay);
            foreach (var meal in meals.Where(x => x.Code.StartsWith($"C11D{dayNumber:D2}-", StringComparison.Ordinal)))
            {
                db.MenuDayMeals.Add(new MenuDayMeal
                {
                    MenuDayId = menuDay.Id,
                    MealId = meal.Id,
                    ProgramId = meal.ProgramId!.Value,
                    Slot = meal.Slot!,
                    SortOrder = 1,
                    IsPrimary = true,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created 11-day cycle restaurant {Email}. Password: {Password}. Cycle starts {Start}.", email, Password, start);
    }

    private static async Task CompleteElevenDayCycleRestaurantAsync(
        AppDbContext db,
        RestaurantProfile restaurant,
        IReadOnlyList<Bundle> bundles,
        IReadOnlyList<Region> regions,
        CancellationToken ct)
    {
        var logoUrl = $"{Media}logo-aura-meals.png";
        var photoUrl = $"{Media}photo-aura-meals.jpg";
        restaurant.Latitude = 29.3875m;
        restaurant.Longitude = 47.9952m;

        if (!await db.Documents.AnyAsync(x => x.OwnerId == restaurant.Id && x.OwnerType == DocumentOwnerType.Restaurant && x.DocumentType == DocumentType.ProfileImage, ct))
        {
            AddCycleRestaurantMedia(db, restaurant.Id);
        }
        else
        {
            var logo = await db.Documents.FirstAsync(x => x.OwnerId == restaurant.Id && x.DocumentType == DocumentType.ProfileImage, ct);
            logo.StoragePath = logoUrl;
            logo.FileName = "logo-eleven-cycle.png";
            if (!await db.DocumentVerifications.AnyAsync(x => x.DocumentId == logo.Id, ct))
            {
                db.DocumentVerifications.Add(new DocumentVerification
                {
                    DocumentId = logo.Id,
                    Status = VerificationStatus.Approved,
                    ReviewedAtUtc = DateTime.UtcNow,
                });
            }

            if (!await db.Documents.AnyAsync(x => x.OwnerId == restaurant.Id && x.DocumentType == DocumentType.BranchPhoto, ct))
            {
                var cover = new Document
                {
                    OwnerType = DocumentOwnerType.Restaurant,
                    OwnerId = restaurant.Id,
                    DocumentType = DocumentType.BranchPhoto,
                    FileName = "photo-eleven-cycle.jpg",
                    ContentType = "image/jpeg",
                    StoragePath = photoUrl,
                    SizeBytes = 220_000,
                };
                db.Documents.Add(cover);
                db.DocumentVerifications.Add(new DocumentVerification
                {
                    DocumentId = cover.Id,
                    Status = VerificationStatus.Approved,
                    ReviewedAtUtc = DateTime.UtcNow,
                });
            }
        }

        var registration = await db.RestaurantRegistrations.FirstOrDefaultAsync(x => x.ResultingRestaurantId == restaurant.Id, ct);
        if (registration is not null)
        {
            registration.Website ??= "https://elevencycle.kw";
            registration.InstagramHandle ??= "@elevencycle.kw";
            registration.BankName ??= "National Bank of Kuwait";
            registration.BankBeneficiaryName ??= restaurant.LegalNameEn;
            registration.Iban ??= "KW89NBKK00000001234567890121";
            registration.BankAccountNumber ??= "1234567821";
            registration.SwiftCode ??= "NBKKKWKK";
            registration.SettlementCurrency ??= "KWD";
            registration.Address ??= "Kuwait City, Sharq";
            registration.PrimaryContactName ??= registration.OwnerName;
            registration.PrimaryContactEmail ??= restaurant.ContactEmail;
            registration.PrimaryContactPhone ??= restaurant.ContactPhone;
            registration.AuthorizedSignatoryTitle ??= "General Manager";
        }

        if (registration is not null)
        {
            var branch = await db.RestaurantBranches.FirstOrDefaultAsync(x => x.RestaurantRegistrationId == registration.Id, ct);
            if (branch is not null)
                branch.PhotoStorageKey = photoUrl;
        }

        var existingOffers = await db.RestaurantProgramBundleOffers
            .Where(x => x.RestaurantId == restaurant.Id)
            .Select(x => x.BundleId)
            .ToListAsync(ct);
        var existingPrices = await db.RestaurantBundlePricings
            .Where(x => x.RestaurantId == restaurant.Id)
            .Select(x => x.BundleId)
            .ToListAsync(ct);
        foreach (var bundle in bundles)
        {
            if (!existingOffers.Contains(bundle.Id))
            {
                db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
                {
                    RestaurantId = restaurant.Id,
                    ProgramId = bundle.ProgramId,
                    BundleId = bundle.Id,
                    IsActive = true,
                });
            }

            var price26 = 90m + (10 * 12m) + (bundle.SortOrder * 4m);
            if (!existingPrices.Contains(bundle.Id))
            {
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
            else
            {
                var pricing = await db.RestaurantBundlePricings
                    .Where(x => x.RestaurantId == restaurant.Id && x.BundleId == bundle.Id)
                    .OrderByDescending(x => x.EffectiveFromUtc)
                    .FirstAsync(ct);
                pricing.ReferencePrice26 = new Money(price26, "KWD");
                pricing.ContractedSupplyPrice26 = new Money(decimal.Round(price26 * 0.78m, 3), "KWD");
                pricing.LockStatus = PriceLockStatus.Approved;
                pricing.EffectiveFromUtc = DateTime.UtcNow.AddDays(-2);
                pricing.EffectiveToUtc = null;
            }
        }

        if (!await db.RestaurantServiceRegions.AnyAsync(x => x.RestaurantId == restaurant.Id && x.IsActive, ct))
        {
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
        }

        if (!await db.DailyCapacities.AnyAsync(x => x.RestaurantId == restaurant.Id, ct))
        {
            db.DailyCapacities.Add(new DailyCapacity
            {
                RestaurantId = restaurant.Id,
                ServiceDate = new DateOnly(2020, 1, 1),
                CapacityType = CapacityType.Base,
                MaxBoxes = 90,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static void AddCycleRestaurantMedia(AppDbContext db, Guid restaurantId)
    {
        var logo = new Document
        {
            OwnerType = DocumentOwnerType.Restaurant,
            OwnerId = restaurantId,
            DocumentType = DocumentType.ProfileImage,
            FileName = "logo-eleven-cycle.png",
            ContentType = "image/png",
            StoragePath = $"{Media}logo-aura-meals.png",
            SizeBytes = 80_000,
        };
        var cover = new Document
        {
            OwnerType = DocumentOwnerType.Restaurant,
            OwnerId = restaurantId,
            DocumentType = DocumentType.BranchPhoto,
            FileName = "photo-eleven-cycle.jpg",
            ContentType = "image/jpeg",
            StoragePath = $"{Media}photo-aura-meals.jpg",
            SizeBytes = 220_000,
        };
        db.Documents.AddRange(logo, cover);
        db.DocumentVerifications.AddRange(
            new DocumentVerification { DocumentId = logo.Id, Status = VerificationStatus.Approved, ReviewedAtUtc = DateTime.UtcNow },
            new DocumentVerification { DocumentId = cover.Id, Status = VerificationStatus.Approved, ReviewedAtUtc = DateTime.UtcNow });
    }

    private static string CycleSlotNameAr(string slot) => slot switch
    {
        "breakfast" => "فطور",
        "lunch" => "غداء",
        "dinner" => "عشاء",
        _ => "سناك",
    };

    private static string CycleSlotNameEn(string slot) => slot switch
    {
        "breakfast" => "Breakfast",
        "lunch" => "Lunch",
        "dinner" => "Dinner",
        _ => "Snack",
    };

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

    private static async Task RefreshExistingRestaurantAsync(
        AppDbContext db,
        Guid userId,
        DemoRestaurantBrand brand,
        IReadOnlyList<Bundle> bundles,
        IReadOnlyList<Region> nearby,
        CancellationToken cancellationToken)
    {
        var restaurant = await db.RestaurantProfiles.FirstAsync(x => x.UserId == userId, cancellationToken);
        restaurant.Latitude = brand.Latitude;
        restaurant.Longitude = brand.Longitude;
        restaurant.PrimaryRegionId = nearby.FirstOrDefault()?.Id ?? restaurant.PrimaryRegionId;
        restaurant.ContactPhone = brand.Phone;

        var registration = await db.RestaurantRegistrations.FirstOrDefaultAsync(
            x => x.ResultingRestaurantId == restaurant.Id,
            cancellationToken);
        if (registration is not null)
        {
            registration.Latitude = brand.Latitude;
            registration.Longitude = brand.Longitude;
            registration.Address = brand.AddressEn;
            registration.RegionId = restaurant.PrimaryRegionId;
            registration.ServiceRegionIdsJson = nearby.Count == 0
                ? registration.ServiceRegionIdsJson
                : System.Text.Json.JsonSerializer.Serialize(nearby.Select(x => x.Id));

            var branch = await db.RestaurantBranches.FirstOrDefaultAsync(
                x => x.RestaurantRegistrationId == registration.Id && x.IsPrimary,
                cancellationToken);
            if (branch is not null)
            {
                branch.AddressAr = brand.AddressAr;
                branch.AddressEn = brand.AddressEn;
                branch.GovernorateAr = brand.GovernorateAr;
                branch.GovernorateEn = brand.GovernorateEn;
            }
        }

        foreach (var bundle in bundles)
        {
            var price26 = brand.Price26 + (bundle.SortOrder * 4m);
            var pricing = await db.RestaurantBundlePricings
                .Where(x => x.RestaurantId == restaurant.Id && x.BundleId == bundle.Id)
                .OrderByDescending(x => x.EffectiveFromUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (pricing is null) continue;
            pricing.ReferencePrice26 = new Money(price26, "KWD");
            pricing.ContractedSupplyPrice26 = new Money(decimal.Round(price26 * 0.78m, 3), "KWD");
            pricing.LockStatus = PriceLockStatus.Approved;
            pricing.EffectiveFromUtc = DateTime.UtcNow.AddDays(-2);
            pricing.EffectiveToUtc = null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedClassificationsAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var classifier = services.GetRequiredService<IRestaurantTierClassificationService>();
        var pairs = await db.Bundles.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.Id, x.ProgramId })
            .ToListAsync(cancellationToken);

        var runs = 0;
        foreach (var pair in pairs)
        {
            var resolved = await classifier.ResolveAsync(pair.ProgramId, pair.Id, eligibleOnly: false, cancellationToken);
            if (resolved.Assignments.Count == 0) continue;

            var run = new ClassificationRun
            {
                ProgramId = pair.ProgramId,
                BundleId = pair.Id,
                MeanReferenceDaily = resolved.MeanReferenceDaily,
                SdReferenceDaily = resolved.SdReferenceDaily,
                LowerBound = resolved.LowerBound,
                UpperBound = resolved.UpperBound,
                RestaurantCount = resolved.Assignments.Count,
                ConfidenceLevel = "95%",
                RunAtUtc = DateTime.UtcNow,
            };
            db.ClassificationRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var assignment in resolved.Assignments)
            {
                db.RestaurantClassifications.Add(new RestaurantClassification
                {
                    ClassificationRunId = run.Id,
                    RestaurantId = assignment.RestaurantId,
                    ProgramId = pair.ProgramId,
                    BundleId = pair.Id,
                    Tier = assignment.Tier,
                    ReferenceDailyBox = assignment.ReferenceDailyBox,
                    IsOutlierFlagged = assignment.IsOutlierFlagged,
                    OutlierReason = assignment.OutlierReason,
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            runs++;
        }

        logger.LogInformation("Persisted {Count} restaurant classification runs.", runs);
    }

    private static List<Region> NearbyAreas(decimal latitude, decimal longitude, IReadOnlyList<Region> areas)
        => areas
            .Where(x => x.CentroidLatitude is not null && x.CentroidLongitude is not null)
            .Select(x => new
            {
                Area = x,
                DistanceKm = KuwaitGeoBounds.HaversineKm(latitude, longitude, x.CentroidLatitude!.Value, x.CentroidLongitude!.Value),
            })
            .Where(x => x.DistanceKm <= KuwaitGeoBounds.MaxSuggestionDistanceKm)
            .OrderBy(x => x.DistanceKm)
            .Select(x => x.Area)
            .Take(RestaurantDeliveryCoverageBackfill.NearbyAreaLimit)
            .ToList();
}
