using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.DocumentTemplates;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using DomainProgram = MealMate.Domain.ProgramsSubscriptions.Program;

namespace MealMate.ApiTests.OrderLifecycle;

public sealed class TestClock : IDateTimeProvider
{
    private DateTime? _overrideUtcNow;
    public DateTime UtcNow => _overrideUtcNow ?? DateTime.UtcNow;
    public DateOnly TodayUtc => DateOnly.FromDateTime(UtcNow);

    public IDisposable AdvanceTo(DateTime utcNow)
    {
        var previous = _overrideUtcNow;
        _overrideUtcNow = utcNow;
        return new ClockScope(this, previous);
    }

    private sealed class ClockScope(TestClock clock, DateTime? previous) : IDisposable
    {
        public void Dispose() => clock._overrideUtcNow = previous;
    }
}

public static class OrderLifecycleTestDataSeeder
{
    public static async Task<FixtureEntities> SeedAsync(AppDbContext db, DateOnly startDate, CancellationToken ct = default)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // 1. Country & Tenant
        var country = await db.Countries.FirstOrDefaultAsync(c => c.Code == "KW", ct);
        if (country is null)
        {
            country = new Country
            {
                Code = "KW",
                NameAr = "الكويت",
                NameEn = "Kuwait",
                CurrencyCode = "KWD",
                DialCode = "+965",
                FlagEmoji = "🇰🇼",
                IsActive = true
            };
            db.Countries.Add(country);
            await db.SaveChangesAsync(ct);
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Code == "TEN-KW", ct);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Code = "TEN-KW",
                NameAr = "ميل ميت الكويت",
                NameEn = "MealMate Kuwait",
                CountryId = country.Id,
                Domain = "kw.mealmate.app",
                CurrencyCode = "KWD",
                TimeZoneId = "Asia/Kuwait",
                DefaultLanguage = "ar",
                Status = TenantStatus.Active,
                IsActive = true
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
        }

        // 2. Region
        var region = new Region
        {
            CountryId = country.Id,
            Code = "REG-" + suffix,
            NameAr = "منطقة العاصمة",
            NameEn = "Capital Region",
            IsActive = true
        };
        db.Regions.Add(region);

        // 3. PricingRuleVersion & Pricing Settings
        if (!await db.PricingRuleVersions.AnyAsync(ct))
        {
            db.PricingRuleVersions.Add(new PricingRuleVersion
            {
                RMax = Percentage.FromFraction(0.2m),
                RMin = Percentage.FromFraction(0.05m),
                DShort = 6,
                DLong = 26,
                Gamma = 1m,
                RoundingStepKwd = 0.05m,
                IsActive = true
            });
        }

        if (!await db.SystemSettings.AnyAsync(s => s.Key == SubscriptionPricingSettingsDefaults.Key, ct))
        {
            var pricingSettings = SubscriptionPricingSettingsDefaults.Create();
            db.SystemSettings.Add(new SystemSetting
            {
                Key = SubscriptionPricingSettingsDefaults.Key,
                ValueJson = JsonSerializer.Serialize(pricingSettings, SubscriptionPricingSettingsDefaults.JsonOptions),
                IsActive = true
            });
        }

        // 4. Gateway Config
        var gatewayConfig = await db.PayzahGatewayConfigs.FirstOrDefaultAsync(c => c.GatewayName == "Payzah", ct);
        if (gatewayConfig is null)
        {
            db.PayzahGatewayConfigs.Add(new PayzahGatewayConfig(sandbox: true, baseUrl: "https://development.payzah.net/ws/paymentgateway", encryptedPrivateKey: "test-api-token", maskedKey: "test...oken"));
        }

        // 5. Document Templates
        var templateHandler = new SeedDocumentTemplatesCommandHandler(db, new TestClock());
        await templateHandler.Handle(new SeedDocumentTemplatesCommand(), ct);

        // 6. Program & Bundle
        var program = new DomainProgram
        {
            Code = "PRG-" + suffix,
            NameAr = "برنامج الرشاقة",
            NameEn = "Fitness Program",
            Status = ProgramStatus.Active,
            IsActive = true
        };
        var bundle = new Bundle
        {
            Program = program,
            Code = "BND-" + suffix,
            NameAr = "باقة الغداء",
            NameEn = "Lunch Bundle",
            MealsPerDay = 1,
            LunchCount = 1,
            BreakfastCount = 0,
            DinnerCount = 0,
            SnackCount = 0,
            Status = ProgramStatus.Active,
            IsActive = true
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);

        // 7. Restaurants A and B
        var restOwnerA = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "ownerA_" + suffix,
            NormalizedUserName = ("ownerA_" + suffix).ToUpperInvariant(),
            Email = $"ownerA_{suffix}@mealmate.local",
            NormalizedEmail = $"ownerA_{suffix}@mealmate.local".ToUpperInvariant(),
            UserType = UserType.Restaurant,
            FullName = "مالك مطعم الشاطئ " + suffix,
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        var restOwnerB = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "ownerB_" + suffix,
            NormalizedUserName = ("ownerB_" + suffix).ToUpperInvariant(),
            Email = $"ownerB_{suffix}@mealmate.local",
            NormalizedEmail = $"ownerB_{suffix}@mealmate.local".ToUpperInvariant(),
            UserType = UserType.Restaurant,
            FullName = "مالك مطعم النخيل " + suffix,
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        db.Users.AddRange(restOwnerA, restOwnerB);

        var restA = new RestaurantProfile
        {
            UserId = restOwnerA.Id,
            TradeNameAr = "مطعم الشاطئ " + suffix,
            TradeNameEn = "Beach Restaurant " + suffix,
            LegalNameAr = "شركة الشاطئ",
            LegalNameEn = "Beach Co",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true
        };
        var restB = new RestaurantProfile
        {
            UserId = restOwnerB.Id,
            TradeNameAr = "مطعم النخيل " + suffix,
            TradeNameEn = "Palm Restaurant " + suffix,
            LegalNameAr = "شركة النخيل",
            LegalNameEn = "Palm Co",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true
        };
        restOwnerA.RestaurantId = restA.Id;
        restOwnerB.RestaurantId = restB.Id;
        db.RestaurantProfiles.AddRange(restA, restB);

        var branchA = new RestaurantBranch
        {
            RestaurantRegistrationId = restA.Id,
            BranchCode = "BRA-" + suffix,
            NameAr = "فرع الشاطئ",
            NameEn = "Beach Branch",
            GovernorateAr = "العاصمة",
            GovernorateEn = "Capital",
            AddressAr = "شارع الخليج",
            AddressEn = "Gulf Street",
            ContactPhone = "96590000010",
            IsActive = true,
            IsPrimary = true
        };
        var branchB = new RestaurantBranch
        {
            RestaurantRegistrationId = restB.Id,
            BranchCode = "BRB-" + suffix,
            NameAr = "فرع النخيل",
            NameEn = "Palm Branch",
            GovernorateAr = "العاصمة",
            GovernorateEn = "Capital",
            AddressAr = "شارع السور",
            AddressEn = "Sour Street",
            ContactPhone = "96590000020",
            IsActive = true,
            IsPrimary = true
        };
        db.RestaurantBranches.AddRange(branchA, branchB);

        db.RestaurantServiceRegions.AddRange(
            new RestaurantServiceRegion { RestaurantId = restA.Id, RegionId = region.Id, IsActive = true },
            new RestaurantServiceRegion { RestaurantId = restB.Id, RegionId = region.Id, IsActive = true }
        );

        foreach (DayOfWeek dow in Enum.GetValues<DayOfWeek>())
        {
            db.RestaurantOperatingSchedules.Add(new RestaurantOperatingSchedule { RestaurantId = restA.Id, DayOfWeek = dow, IsWorkingDay = true });
            db.RestaurantOperatingSchedules.Add(new RestaurantOperatingSchedule { RestaurantId = restB.Id, DayOfWeek = dow, IsWorkingDay = true });
        }

        for (int i = 0; i < 35; i++)
        {
            db.DailyCapacities.Add(new DailyCapacity { RestaurantId = restA.Id, ServiceDate = startDate.AddDays(i), CapacityType = CapacityType.Base, MaxBoxes = 100, ReservedBoxes = 0 });
            db.DailyCapacities.Add(new DailyCapacity { RestaurantId = restB.Id, ServiceDate = startDate.AddDays(i), CapacityType = CapacityType.Base, MaxBoxes = 100, ReservedBoxes = 0 });
        }

        // Menus and Meals
        var menuA = new Menu { RestaurantId = restA.Id, ProgramId = program.Id, NameAr = "قائمة الغداء", NameEn = "Lunch Menu", IsActive = true };
        var menuB = new Menu { RestaurantId = restB.Id, ProgramId = program.Id, NameAr = "قائمة الغداء", NameEn = "Lunch Menu", IsActive = true };
        db.Menus.AddRange(menuA, menuB);

        var ingA = new Ingredient { RestaurantId = restA.Id, Code = "ING-A-" + suffix, NameAr = "دجاج مشوي", NameEn = "Grilled Chicken", IsActive = true, OriginCountryCode = "KW" };
        var mealA = new Meal
        {
            RestaurantId = restA.Id,
            ProgramId = program.Id,
            Menu = menuA,
            Code = "MEAL-A-" + suffix,
            NameAr = "دجاج مشوي مع أرز",
            NameEn = "Grilled Chicken Rice",
            DescriptionAr = "وجبة صحية متوازنة",
            DescriptionEn = "Healthy balanced meal",
            Slot = "lunch",
            ApprovalStatus = VerificationStatus.Approved,
            IsAvailableForNewOrders = true,
            Calories = 500,
            ProteinG = 45,
            CarbsG = 50,
            NetCarbsG = 45,
            FatG = 12,
            FiberG = 5,
            BundleId = bundle.Id,
            AllergensReviewed = true,
            DataResponsibilityConfirmed = true
        };
        db.Ingredients.Add(ingA);
        db.Meals.Add(mealA);

        var ingB = new Ingredient { RestaurantId = restB.Id, Code = "ING-B-" + suffix, NameAr = "سمك مشوي", NameEn = "Grilled Fish", IsActive = true, OriginCountryCode = "KW" };
        var mealB = new Meal
        {
            RestaurantId = restB.Id,
            ProgramId = program.Id,
            Menu = menuB,
            Code = "MEAL-B-" + suffix,
            NameAr = "سمك مشوي مع بطاطا",
            NameEn = "Grilled Fish Potato",
            DescriptionAr = "وجبة بحرية صحية",
            DescriptionEn = "Healthy seafood meal",
            Slot = "lunch",
            ApprovalStatus = VerificationStatus.Approved,
            IsAvailableForNewOrders = true,
            Calories = 480,
            ProteinG = 40,
            CarbsG = 45,
            NetCarbsG = 40,
            FatG = 10,
            FiberG = 5,
            BundleId = bundle.Id,
            AllergensReviewed = true,
            DataResponsibilityConfirmed = true
        };
        db.Ingredients.Add(ingB);
        db.Meals.Add(mealB);

        var mealIngLinkA = new MealIngredient { MealId = mealA.Id, Meal = mealA, IngredientId = ingA.Id, Ingredient = ingA, PortionGrams = 150 };
        var mealIngLinkB = new MealIngredient { MealId = mealB.Id, Meal = mealB, IngredientId = ingB.Id, Ingredient = ingB, PortionGrams = 150 };
        db.MealIngredients.AddRange(mealIngLinkA, mealIngLinkB);

        var imgA = new MealImage { MealId = mealA.Id, Meal = mealA, ImageUrl = "/meal_a.png", IsPrimary = true };
        var imgB = new MealImage { MealId = mealB.Id, Meal = mealB, ImageUrl = "/meal_b.png", IsPrimary = true };
        db.MealImages.AddRange(imgA, imgB);

        var standard = new ProgramNutritionStandard
        {
            ProgramId = program.Id,
            MealCategory = "MAIN",
            KcalMin = 300,
            KcalMax = 800,
            ProteinGMin = 20,
            ProteinGMax = 70,
            CarbsGMin = 20,
            CarbsGMax = 80,
            FatGMin = 5,
            FatGMax = 40,
            FiberGMin = 2,
            CarbMode = "TOTAL",
            StandardVersion = "NUTRITION-v1.0",
            IsActive = true
        };
        db.ProgramNutritionStandards.Add(standard);

        db.RestaurantProgramBundleOffers.AddRange(
            new RestaurantProgramBundleOffer { RestaurantId = restA.Id, ProgramId = program.Id, BundleId = bundle.Id, IsActive = true },
            new RestaurantProgramBundleOffer { RestaurantId = restB.Id, ProgramId = program.Id, BundleId = bundle.Id, IsActive = true }
        );

        db.RestaurantBundlePricings.AddRange(
            new RestaurantBundlePricing
            {
                RestaurantId = restA.Id,
                ProgramId = program.Id,
                BundleId = bundle.Id,
                ReferencePrice26 = new Money(78m, "KWD"),
                ContractedSupplyPrice26 = new Money(60m, "KWD"),
                LockStatus = PriceLockStatus.Locked,
                EffectiveFromUtc = DateTime.UtcNow.AddDays(-10)
            },
            new RestaurantBundlePricing
            {
                RestaurantId = restB.Id,
                ProgramId = program.Id,
                BundleId = bundle.Id,
                ReferencePrice26 = new Money(78m, "KWD"),
                ContractedSupplyPrice26 = new Money(60m, "KWD"),
                LockStatus = PriceLockStatus.Locked,
                EffectiveFromUtc = DateTime.UtcNow.AddDays(-10)
            }
        );

        var run = new ClassificationRun
        {
            ProgramId = program.Id,
            BundleId = bundle.Id,
            RestaurantCount = 2,
            ConfidenceLevel = "95",
            RunAtUtc = DateTime.UtcNow
        };
        db.ClassificationRuns.Add(run);
        db.RestaurantClassifications.AddRange(
            new RestaurantClassification { ClassificationRun = run, RestaurantId = restA.Id, ProgramId = program.Id, BundleId = bundle.Id, Tier = RestaurantTier.Basic },
            new RestaurantClassification { ClassificationRun = run, RestaurantId = restB.Id, ProgramId = program.Id, BundleId = bundle.Id, Tier = RestaurantTier.Basic }
        );

        // MenuPlan & Versions
        var planA = new MenuPlan { RestaurantId = restA.Id, NameAr = "خطة الشاطئ", NameEn = "Beach Plan", Source = MenuPlanSource.RestaurantCreated, Mode = ScheduleMode.Cycle };
        var planLinkA = new MenuPlanProgram { MenuPlan = planA, ProgramId = program.Id, EffectiveFrom = startDate.AddDays(-10), EffectiveTo = startDate.AddDays(100) };
        var planVerA = new MenuPlanVersion { MenuPlan = planA, VersionNumber = 1, Status = VerificationStatus.Approved, EffectiveFrom = startDate.AddDays(-10), IsPaused = false };
        var dayA = new MenuDay { MenuPlanVersion = planVerA, DayNumber = 1, NameAr = "اليوم 1", NameEn = "Day 1", IsActiveInCycle = true };
        var menuMealA = new MenuDayMeal { MenuDay = dayA, MealId = mealA.Id, ProgramId = program.Id, Slot = "lunch", SortOrder = 1, IsPrimary = true };
        db.AddRange(planA, planLinkA, planVerA, dayA, menuMealA);

        var planB = new MenuPlan { RestaurantId = restB.Id, NameAr = "خطة النخيل", NameEn = "Palm Plan", Source = MenuPlanSource.RestaurantCreated, Mode = ScheduleMode.Cycle };
        var planLinkB = new MenuPlanProgram { MenuPlan = planB, ProgramId = program.Id, EffectiveFrom = startDate.AddDays(-10), EffectiveTo = startDate.AddDays(100) };
        var planVerB = new MenuPlanVersion { MenuPlan = planB, VersionNumber = 1, Status = VerificationStatus.Approved, EffectiveFrom = startDate.AddDays(-10), IsPaused = false };
        var dayB = new MenuDay { MenuPlanVersion = planVerB, DayNumber = 1, NameAr = "اليوم 1", NameEn = "Day 1", IsActiveInCycle = true };
        var menuMealB = new MenuDayMeal { MenuDay = dayB, MealId = mealB.Id, ProgramId = program.Id, Slot = "lunch", SortOrder = 1, IsPrimary = true };
        db.AddRange(planB, planLinkB, planVerB, dayB, menuMealB);

        // 8. Users & Profiles
        var customerUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "cust_" + suffix,
            NormalizedUserName = ("cust_" + suffix).ToUpperInvariant(),
            Email = $"cust_{suffix}@mealmate.local",
            NormalizedEmail = $"cust_{suffix}@mealmate.local".ToUpperInvariant(),
            UserType = UserType.Customer,
            FullName = "عميل تجريبي " + suffix,
            PhoneNumber = "965900" + suffix[..5],
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        db.Users.Add(customerUser);

        var customerProfile = new CustomerProfile
        {
            UserId = customerUser.Id,
            AllergiesCompletedAtUtc = DateTime.UtcNow,
            DietaryPreferencesCompletedAtUtc = DateTime.UtcNow
        };
        db.CustomerProfiles.Add(customerProfile);

        var customerAddress = new CustomerAddress
        {
            UserId = customerUser.Id,
            Label = "المنزل",
            Governorate = "العاصمة",
            Area = "القبلة",
            Block = "1",
            Street = "شارع الخليج",
            Building = "برج الساحل",
            AddressLine = "العاصمة، القبلة، شارع الخليج",
            RegionId = region.Id,
            IsDefault = true,
            Latitude = 29.3759m,
            Longitude = 47.9774m
        };
        db.CustomerAddresses.Add(customerAddress);
        customerProfile.ActiveDeliveryAddressId = customerAddress.Id;

        // Kitchen Staff
        var kitchenStaffA = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "kitchA_" + suffix,
            NormalizedUserName = ("kitchA_" + suffix).ToUpperInvariant(),
            Email = $"kitchA_{suffix}@mealmate.local",
            NormalizedEmail = $"kitchA_{suffix}@mealmate.local".ToUpperInvariant(),
            UserType = UserType.Restaurant,
            RestaurantId = restA.Id,
            FullName = "شيف مطعم الشاطئ",
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        var kitchenStaffB = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "kitchB_" + suffix,
            NormalizedUserName = ("kitchB_" + suffix).ToUpperInvariant(),
            Email = $"kitchB_{suffix}@mealmate.local",
            NormalizedEmail = $"kitchB_{suffix}@mealmate.local".ToUpperInvariant(),
            UserType = UserType.Restaurant,
            RestaurantId = restB.Id,
            FullName = "شيف مطعم النخيل",
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        db.Users.AddRange(kitchenStaffA, kitchenStaffB);

        // Dispatchers
        var dispatcherA = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "dispA_" + suffix,
            NormalizedUserName = ("dispA_" + suffix).ToUpperInvariant(),
            Email = $"dispA_{suffix}@mealmate.local",
            NormalizedEmail = $"dispA_{suffix}@mealmate.local".ToUpperInvariant(),
            UserType = UserType.DeliveryManager,
            RestaurantId = restA.Id,
            FullName = "مسؤول توصيل الشاطئ",
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        var dispatcherB = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "dispB_" + suffix,
            NormalizedUserName = ("dispB_" + suffix).ToUpperInvariant(),
            Email = $"dispB_{suffix}@mealmate.local",
            NormalizedEmail = $"dispB_{suffix}@mealmate.local".ToUpperInvariant(),
            UserType = UserType.DeliveryManager,
            RestaurantId = restB.Id,
            FullName = "مسؤول توصيل النخيل",
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        db.Users.AddRange(dispatcherA, dispatcherB);

        // Drivers
        var driverUserA = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "drvA_" + suffix,
            NormalizedUserName = ("drvA_" + suffix).ToUpperInvariant(),
            Email = $"drvA_{suffix}@mealmate.local",
            NormalizedEmail = $"drvA_{suffix}@mealmate.local".ToUpperInvariant(),
            UserType = UserType.Driver,
            RestaurantId = restA.Id,
            FullName = "سائق الشاطئ " + suffix,
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        var driverProfileA = new DriverProfile
        {
            UserId = driverUserA.Id,
            FullNameAr = "سائق الشاطئ " + suffix,
            FullNameEn = "Beach Driver " + suffix,
            NationalId = "290" + suffix[..8],
            Nationality = "Kuwaiti",
            VehicleType = "car",
            VehiclePlate = suffix[..5],
            RestaurantId = restA.Id,
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1),
            IsAvailable = true
        };
        var linkA = new RestaurantDriverLink { RestaurantId = restA.Id, DriverProfileId = driverProfileA.Id, IsActive = true };

        var driverUserB = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "drvB_" + suffix,
            NormalizedUserName = ("drvB_" + suffix).ToUpperInvariant(),
            Email = $"drvB_{suffix}@mealmate.local",
            NormalizedEmail = $"drvB_{suffix}@mealmate.local".ToUpperInvariant(),
            UserType = UserType.Driver,
            RestaurantId = restB.Id,
            FullName = "سائق النخيل " + suffix,
            SecurityStamp = Guid.NewGuid().ToString("D"),
            EmailConfirmed = true,
            AccountStatus = AccountStatus.Active
        };
        var driverProfileB = new DriverProfile
        {
            UserId = driverUserB.Id,
            FullNameAr = "سائق النخيل " + suffix,
            FullNameEn = "Palm Driver " + suffix,
            NationalId = "291" + suffix[..8],
            Nationality = "Kuwaiti",
            VehicleType = "car",
            VehiclePlate = suffix[..5],
            RestaurantId = restB.Id,
            VerificationStatus = VerificationStatus.Approved,
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(1),
            IsAvailable = true
        };
        var linkB = new RestaurantDriverLink { RestaurantId = restB.Id, DriverProfileId = driverProfileB.Id, IsActive = true };
        var linkB_A = new RestaurantDriverLink { RestaurantId = restA.Id, DriverProfileId = driverProfileB.Id, IsActive = true };

        db.Users.AddRange(driverUserA, driverUserB);
        db.DriverProfiles.AddRange(driverProfileA, driverProfileB);
        db.RestaurantDriverLinks.AddRange(linkA, linkB, linkB_A);

        await db.SaveChangesAsync(ct);

        return new FixtureEntities(
            country,
            tenant,
            region,
            program,
            bundle,
            restA,
            restB,
            mealA,
            customerUser,
            customerProfile,
            customerAddress,
            kitchenStaffA,
            kitchenStaffB,
            dispatcherA,
            dispatcherB,
            driverUserA,
            driverProfileA,
            driverUserB,
            driverProfileB,
            startDate);
    }
}
