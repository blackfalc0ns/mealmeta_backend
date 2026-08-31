using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Text.Json;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class CustomerSubscriptionFlowControllerTests
{
    [Fact]
    public async Task Programs_ReturnsDescriptionsAndKeywordsWithoutNutritionRanges()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_WEIGHT_LOSS",
            ShortCode = "WL",
            NameAr = "خسارة الوزن",
            NameEn = "Weight Loss",
            DescriptionAr = "وجبات محسوبة السعرات.",
            DescriptionEn = "Calorie-controlled meals.",
            KeywordsAr = ["سعرات محسوبة", "نتائج مستدامة"],
            KeywordsEn = ["Calorie controlled", "Sustainable results"],
            ImageStorageKey = "uploads/admin/programs/wl.webp",
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "BUNDLE",
            NameAr = "باقة",
            NameEn = "Bundle",
            MealsPerDay = 1,
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم",
            TradeNameEn = "Restaurant",
            LegalNameAr = "مطعم",
            LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
        };
        var unlinkedProgram = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_UNLINKED",
            NameAr = "برنامج بدون مطعم",
            NameEn = "Unlinked Program",
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        var unlinkedBundle = new Bundle
        {
            ProgramId = unlinkedProgram.Id,
            Code = "UNLINKED_BUNDLE",
            NameAr = "باقة غير مرتبطة",
            NameEn = "Unlinked Bundle",
            MealsPerDay = 1,
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        db.AddRange(program, bundle, restaurant, unlinkedProgram, unlinkedBundle);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id,
            BundleId = bundle.Id,
            RestaurantId = restaurant.Id,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(await controller.Programs(CancellationToken.None));
        var item = Assert.Single(Assert.IsAssignableFrom<IEnumerable<CustomerProgramCardDto>>(result.Value));

        Assert.Equal("وجبات محسوبة السعرات.", item.DescriptionAr);
        Assert.Equal(["سعرات محسوبة", "نتائج مستدامة"], item.KeywordsAr);
        Assert.Equal(program.Id, item.ProgramId);
        Assert.Equal("/api/v1/uploads/content?key=uploads%2Fadmin%2Fprograms%2Fwl.webp", item.ImageUrl);
        Assert.Null(typeof(CustomerProgramCardDto).GetProperty("NutritionStandards"));
        Assert.DoesNotContain(typeof(CustomerProgramCardDto).GetProperties(), x => x.Name.Contains("Min") || x.Name.Contains("Max"));
    }

    [Fact]
    public async Task Bundles_ReturnsBilingualSlotsAndOnlyRestaurantPhotosFromActiveOffers()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_WEIGHT_LOSS",
            NameAr = "خسارة الوزن",
            NameEn = "Weight Loss",
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id,
            Code = "PKG-BL1S",
            NameAr = "باقة 3 وجبات",
            NameEn = "3 Meals Bundle",
            DescriptionAr = "فطور وغداء وسناك",
            DescriptionEn = "Breakfast, lunch and snack",
            ImageStorageKey = "uploads/admin/bundles/bl1s.webp",
            MealsPerDay = 3,
            BreakfastCount = 1,
            LunchCount = 1,
            SnackCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        var restaurantWithPhoto = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم بصورة",
            TradeNameEn = "Restaurant With Photo",
            LegalNameAr = "مطعم بصورة",
            LegalNameEn = "Restaurant With Photo",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
        };
        var restaurantWithoutPhoto = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم بدون صورة",
            TradeNameEn = "Restaurant Without Photo",
            LegalNameAr = "مطعم بدون صورة",
            LegalNameEn = "Restaurant Without Photo",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = true,
        };
        var stoppedRestaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم متوقف",
            TradeNameEn = "Stopped Restaurant",
            LegalNameAr = "مطعم متوقف",
            LegalNameEn = "Stopped Restaurant",
            VerificationStatus = VerificationStatus.Approved,
            IsOperationallyActive = false,
        };
        db.AddRange(program, bundle, restaurantWithPhoto, restaurantWithoutPhoto, stoppedRestaurant);
        db.RestaurantProgramBundleOffers.AddRange(
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurantWithPhoto.Id, IsActive = true },
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurantWithoutPhoto.Id, IsActive = true },
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = stoppedRestaurant.Id, IsActive = true });
        db.RestaurantBranches.Add(new MealMate.Domain.RestaurantOperations.RestaurantBranch
        {
            RestaurantRegistrationId = restaurantWithPhoto.Id,
            BranchCode = "MAIN",
            NameAr = "الرئيسي",
            NameEn = "Main",
            AddressAr = "الكويت",
            AddressEn = "Kuwait",
            GovernorateAr = "العاصمة",
            GovernorateEn = "Capital",
            ContactPhone = "50000000",
            IsActive = true,
            IsPrimary = true,
            PhotoStorageKey = "uploads/restaurant/branches/main.webp",
        });
        db.Subscriptions.AddRange(
            new Subscription
            {
                CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, BundleId = bundle.Id,
                SelectedTier = RestaurantTier.Basic, DurationDays = 6,
                StartDate = new DateOnly(2026, 8, 20), EndDate = new DateOnly(2026, 8, 25),
                Status = SubscriptionStatus.Active, PaidAmount = new Money(40, "KWD"),
            },
            new Subscription
            {
                CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, BundleId = bundle.Id,
                SelectedTier = RestaurantTier.Basic, DurationDays = 6,
                StartDate = new DateOnly(2026, 8, 20), EndDate = new DateOnly(2026, 8, 25),
                Status = SubscriptionStatus.PendingPayment, PaidAmount = Money.Zero("KWD"),
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(await controller.Bundles(program.Id, CancellationToken.None));
        var item = Assert.Single(Assert.IsAssignableFrom<IEnumerable<CustomerBundleCardDto>>(result.Value));

        Assert.Equal("فطور وغداء وسناك", item.DescriptionAr);
        Assert.Equal("Breakfast, lunch and snack", item.DescriptionEn);
        Assert.Equal("/api/v1/uploads/content?key=uploads%2Fadmin%2Fbundles%2Fbl1s.webp", item.ImageUrl);
        Assert.Collection(item.Slots,
            x => { Assert.Equal("breakfast", x.Code); Assert.Equal("الإفطار", x.NameAr); Assert.Equal(1, x.Quantity); },
            x => { Assert.Equal("lunch", x.Code); Assert.Equal("الغداء", x.NameAr); Assert.Equal(1, x.Quantity); },
            x => { Assert.Equal("snack", x.Code); Assert.Equal("سناك", x.NameAr); Assert.Equal(1, x.Quantity); });
        Assert.Equal(2, item.RestaurantCount);
        Assert.Equal("/api/v1/uploads/content?key=uploads%2Frestaurant%2Fbranches%2Fmain.webp",
            item.Restaurants.Single(x => x.RestaurantId == restaurantWithPhoto.Id).LogoUrl);
        Assert.Null(item.Restaurants.Single(x => x.RestaurantId == restaurantWithoutPhoto.Id).LogoUrl);
        Assert.DoesNotContain(item.Restaurants, x => x.RestaurantId == stoppedRestaurant.Id);
        Assert.Null(typeof(CustomerBundleRestaurantDto).GetProperty("ImageUrl"));
        Assert.Null(typeof(CustomerBundleRestaurantDto).GetProperty("NameAr"));
        Assert.Null(typeof(CustomerBundleRestaurantDto).GetProperty("NameEn"));
        Assert.Equal(1, item.SelectionCount);
        Assert.True(item.IsMostSelected);
        Assert.Equal("MOST_SELECTED", item.Badge?.Code);
        Assert.Equal("الأكثر اختيارًا", item.Badge?.NameAr);
    }

    [Fact]
    public async Task Types_ReturnsIndividualAndFamilyForCustomer()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out _);

        var result = Assert.IsType<OkObjectResult>(await controller.Types(CancellationToken.None));
        var items = Assert.IsAssignableFrom<IEnumerable<SubscriptionTypeOptionDto>>(result.Value).ToList();

        Assert.Equal(["INDIVIDUAL", "FAMILY"], items.Select(x => x.Code));
        Assert.True(items.Single(x => x.Code == "INDIVIDUAL").IsAvailable);
        Assert.False(items.Single(x => x.Code == "FAMILY").RequiresConversion);
    }

    [Fact]
    public async Task Types_FamilyIsConfiguredBeforePayment()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out _);

        var result = Assert.IsType<OkObjectResult>(await controller.Types(CancellationToken.None));
        var items = Assert.IsAssignableFrom<IEnumerable<SubscriptionTypeOptionDto>>(result.Value).ToList();

        Assert.False(items.Single(x => x.Code == "FAMILY").MembersConfiguredAfterPayment);
    }

    [Fact]
    public async Task Types_WithActiveIndividual_DisablesIndividual_AndMarksFamilyRequiresConversion()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, out _, out var currentUser);
        var customerId = currentUser.UserId!.Value;
        var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "P", NameAr = "ب", NameEn = "P", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "B", NameAr = "ب", NameEn = "B",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle, new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new MealMate.Domain.ValueObjects.Money(100m, "KWD"),
        });
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await controller.Types(CancellationToken.None));
        var items = Assert.IsAssignableFrom<IEnumerable<SubscriptionTypeOptionDto>>(result.Value).ToList();
        var individual = items.Single(x => x.Code == "INDIVIDUAL");
        var family = items.Single(x => x.Code == "FAMILY");

        Assert.False(individual.IsAvailable);
        Assert.Equal(CustomerIndividualSubscriptionGate.ExistingSubscriptionError, individual.DisabledReason);
        Assert.True(family.IsAvailable);
        Assert.True(family.RequiresConversion);
        Assert.Equal("/api/v1/customer/subscriptions/current/conversion-options", family.ConversionEndpoint);
    }

    [Fact]
    public async Task PaymentMethods_ReturnsOnlyConfiguredHostedMethodsAndWalletState()
    {
        await using var db = CreateDb();
        db.MyFatoorahGatewayConfigs.Add(new MyFatoorahGatewayConfig(
            "MyFatoorah", true, "https://apitest.myfatoorah.com", "token", "secret",
            isKnetEnabled: true, isVisaMasterEnabled: false, isApplePayEnabled: false,
            defaultCurrency: "KWD", countryCode: "KW", maskedToken: "tok***"));
        await db.SaveChangesAsync();
        var controller = CreateController(db, out var payment, out _);
        payment.GetPaymentMethodsAsync(41.6m, Arg.Any<CancellationToken>()).Returns([
            new MyFatoorahPaymentMethod(1, "KNET", "كي نت", "KNET", 0.1m, 41.7m, "KWD", false),
            new MyFatoorahPaymentMethod(2, "VM", "بطاقة", "VISA/MASTER", 0.5m, 42.1m, "KWD", false),
            new MyFatoorahPaymentMethod(8, "uaecc", "بطاقة إماراتية", "UAE Debit Cards", 0.5m, 42.1m, "KWD", false)
        ]);

        var result = Assert.IsType<OkObjectResult>(await controller.PaymentMethods(41.6m, CancellationToken.None));
        var items = Assert.IsAssignableFrom<IEnumerable<CustomerPaymentMethodDto>>(result.Value).ToList();

        Assert.Contains(items, x => x.PaymentMethodId == 1 && x.IsAvailable);
        Assert.DoesNotContain(items, x => x.PaymentMethodId == 2);
        Assert.DoesNotContain(items, x => x.PaymentMethodId == 8);
        Assert.Contains(items, x => x.Code == "MEALMATE_WALLET" && !x.IsAvailable && x.UnavailableReason == "wallet_not_implemented");
    }

    [Fact]
    public async Task DurationsAndTiers_ReturnSeparateMobileContracts()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_FLOW", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_FLOW", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var run = new ClassificationRun
        {
            ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
            RestaurantCount = 1, ConfidenceLevel = "95", RunAtUtc = DateTime.UtcNow,
        };
        var platinumRestaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم ٢", TradeNameEn = "Restaurant 2", LegalNameAr = "مطعم ٢", LegalNameEn = "Restaurant 2",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var eliteRestaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم ٣", TradeNameEn = "Restaurant 3", LegalNameAr = "مطعم ٣", LegalNameEn = "Restaurant 3",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant, platinumRestaurant, eliteRestaurant, run);
        // Daily reference prices 2 / 3 / 4 KWD classify the three restaurants as Basic / Platinum / Elite.
        foreach (var (owner, reference26) in new[] { (restaurant, 52m), (platinumRestaurant, 78m), (eliteRestaurant, 104m) })
        {
            db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = owner.Id, IsActive = true,
            });
            db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = owner.Id,
                ReferencePrice26 = new Money(reference26, "KWD"),
                ContractedSupplyPrice26 = new Money(reference26 * 0.8m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            });
        }
        db.RestaurantOperatingSchedules.Add(new MealMate.Domain.RestaurantOperations.RestaurantOperatingSchedule
        {
            RestaurantId = restaurant.Id, DayOfWeek = DayOfWeek.Friday, IsWorkingDay = true,
        });
        db.RestaurantClassifications.Add(new RestaurantClassification
        {
            ClassificationRunId = run.Id, ClassificationRun = run, RestaurantId = restaurant.Id,
            ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
            Tier = RestaurantTier.Basic,
        });
        db.RestaurantBranches.Add(new MealMate.Domain.RestaurantOperations.RestaurantBranch
        {
            RestaurantRegistrationId = restaurant.Id, BranchCode = "MAIN", NameAr = "الرئيسي", NameEn = "Main",
            AddressAr = "الكويت", AddressEn = "Kuwait", GovernorateAr = "العاصمة", GovernorateEn = "Capital",
            ContactPhone = "50000000", IsActive = true, IsPrimary = true,
            PhotoStorageKey = "uploads/restaurant/branches/tier.webp",
        });
        db.Subscriptions.Add(new Subscription
        {
            CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
            SelectedTier = RestaurantTier.Basic, DurationDays = 26, Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 20), EndDate = new DateOnly(2026, 9, 20), PaidAmount = new Money(260, "KWD"),
        });
        var pricingSettings = SubscriptionPricingSettingsDefaults.Create();
        pricingSettings.TierLabels.BasicLogoStorageKey = "uploads/admin/tiers/basic.webp";
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = JsonSerializer.Serialize(pricingSettings, SubscriptionPricingSettingsDefaults.JsonOptions),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        IReadOnlyList<CustomerSubscriptionCatalogItemDto> catalog =
        [
            new(program.Id, program.NameAr, program.NameEn, bundle.Id, bundle.NameAr, bundle.NameEn,
                RestaurantTier.Basic, "سيلكت", "Select",
                [new(6, 66, 11), new(12, 120, 10), new(26, 234, 9)]),
            new(program.Id, program.NameAr, program.NameEn, bundle.Id, bundle.NameAr, bundle.NameEn,
                RestaurantTier.Platinum, "بلس", "Plus",
                [new(6, 78, 13), new(12, 144, 12), new(26, 286, 11)]),
        ];
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetCustomerSubscriptionPricingCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(catalog));
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.ArgAt<PreviewCustomerPricingQuery>(0);
                var dailyPrice = query.Tier == RestaurantTier.Basic ? 9m : 11m;
                return Result.Success(PricingEngine.Calculate(new PricingEngineRequest(
                    dailyPrice, query.DurationDays, DurationRatesPct: new Dictionary<int, decimal> { [query.DurationDays] = 0m },
                    GatewayFeePct: 0m, GatewayFeeFixedKwd: 0m, RoundingStepKwd: 0.001m,
                    FridayDays: query.FridayDays, FridayRatePct: 0m)));
            });
        var clock = Substitute.For<IDateTimeProvider>();
        var schedule = new SubscriptionScheduleService(clock);
        var controller = CreateController(db, out _, out var currentUser, sender, schedule);
        await SeedCustomerDeliveryCoverageAsync(
            db, currentUser, restaurant.Id, platinumRestaurant.Id, eliteRestaurant.Id);

        var durationsResult = Assert.IsType<OkObjectResult>(await controller.Durations(program.Id, bundle.Id, false, CancellationToken.None));
        var durations = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(durationsResult.Value).ToList();
        Assert.Equal(new int?[] { 6, 12, 26, null }, durations.Select(x => x.DurationDays));
        Assert.True(durations.Single(x => x.DurationDays == 26).IsPopular);
        var presetDurations = durations.Where(x => !x.IsCustom).ToList();
        Assert.All(presetDurations, x =>
        {
            Assert.True(x.SupportsFriday);
            Assert.False(x.IncludeFriday);
            Assert.Equal(0, x.FridayDays);
            Assert.Equal(x.DurationDays, x.TotalServiceDays);
            Assert.Equal("INCLUDE_FRIDAY", x.FridayOption.Code);
        });
        var customDuration = Assert.Single(durations, x => x.IsCustom);
        Assert.Null(customDuration.DurationDays);
        Assert.Equal(7, customDuration.MinDays);
        Assert.Equal(25, customDuration.MaxDays);
        Assert.True(customDuration.SupportsFriday);
        Assert.Null(customDuration.FridayDays);
        Assert.Null(customDuration.TotalServiceDays);

        var tiersResult = Assert.IsType<OkObjectResult>(
            await controller.Tiers(program.Id, bundle.Id, 26, false, CancellationToken.None));
        var tiers = Assert.IsAssignableFrom<IEnumerable<CustomerTierOptionDto>>(tiersResult.Value).ToList();
        Assert.Equal(2, tiers.Count);
        Assert.True(tiers.Single(x => x.Tier == RestaurantTier.Basic).IsRecommended);
        Assert.Equal(52, tiers.Single(x => x.Tier == RestaurantTier.Platinum).PriceDifference);
        Assert.Contains("basic.webp", tiers.Single(x => x.Tier == RestaurantTier.Basic).LogoUrl);
        Assert.Null(tiers.Single(x => x.Tier == RestaurantTier.Platinum).LogoUrl);
        var preview = Assert.Single(tiers.Single(x => x.Tier == RestaurantTier.Basic).Restaurants);
        Assert.Equal(restaurant.Id, preview.RestaurantId);
        Assert.Contains("tier.webp", preview.LogoUrl);

        var cardResult = Assert.IsType<OkObjectResult>(
            await controller.RestaurantBrowseCard(program.Id, bundle.Id, restaurant.Id, CancellationToken.None));
        var card = Assert.IsType<CustomerRestaurantBrowseCardDto>(cardResult.Value);
        Assert.Equal(restaurant.Id, card.RestaurantId);
        Assert.Equal("مطعم", card.NameAr);
        Assert.Equal("Restaurant", card.NameEn);
        Assert.Contains("tier.webp", card.LogoUrl);
        Assert.Null(card.Rating);
        Assert.Equal(0, card.MealCount);
        Assert.Equal(0, card.BundleMealCount);
        Assert.Equal(RestaurantTier.Basic, card.Tier);
        Assert.Equal("أساسي", card.TierNameAr);
        Assert.Contains("basic.webp", card.TierLogoUrl);
        var classification = Assert.Single(card.Classifications);
        Assert.Contains("basic.webp", classification.LogoUrl);
        Assert.Equal(RestaurantTier.Basic, classification.Tier);
        Assert.Null(typeof(CustomerRestaurantClassificationDto).GetProperty("NameAr"));
        Assert.Null(typeof(CustomerRestaurantClassificationDto).GetProperty("NameEn"));

        var allBasicResult = Assert.IsType<OkObjectResult>(
            await controller.TierRestaurants(program.Id, bundle.Id, RestaurantTier.Basic, null, CancellationToken.None));
        var allBasic = Assert.IsAssignableFrom<IEnumerable<CustomerRestaurantBrowseCardDto>>(allBasicResult.Value).ToList();
        var listedBasic = Assert.Single(allBasic);
        Assert.Equal(card.RestaurantId, listedBasic.RestaurantId);
        Assert.Equal(card.NameAr, listedBasic.NameAr);
        Assert.Equal(card.LogoUrl, listedBasic.LogoUrl);
        Assert.Equal(card.MealCount, listedBasic.MealCount);

        var filteredResult = Assert.IsType<OkObjectResult>(
            await controller.TierRestaurants(program.Id, bundle.Id, RestaurantTier.Basic,
                [platinumRestaurant.Id, restaurant.Id, Guid.NewGuid()], CancellationToken.None));
        var filtered = Assert.IsAssignableFrom<IEnumerable<CustomerRestaurantBrowseCardDto>>(filteredResult.Value).ToList();
        Assert.Equal(restaurant.Id, Assert.Single(filtered).RestaurantId);

        var platinumListResult = Assert.IsType<OkObjectResult>(
            await controller.TierRestaurants(program.Id, bundle.Id, RestaurantTier.Platinum, null, CancellationToken.None));
        var platinumList = Assert.IsAssignableFrom<IEnumerable<CustomerRestaurantBrowseCardDto>>(platinumListResult.Value).ToList();
        Assert.Equal([restaurant.Id, platinumRestaurant.Id], platinumList.Select(x => x.RestaurantId));
        Assert.Equal("مطعم ٢", platinumList[1].NameAr);

        var unpricedResult = Assert.IsType<OkObjectResult>(
            await controller.Tiers(program.Id, bundle.Id, null, false, CancellationToken.None));
        var unpricedTiers = Assert.IsAssignableFrom<IEnumerable<CustomerTierOptionDto>>(unpricedResult.Value).ToList();
        Assert.Equal(2, unpricedTiers.Count);
        Assert.All(unpricedTiers, x =>
        {
            Assert.Null(x.DurationDays);
            Assert.Null(x.TotalPrice);
            Assert.Null(x.DailyPrice);
            Assert.Null(x.PriceDifference);
            Assert.Null(x.CurrencyCode);
            Assert.Null(x.FridayDays);
            Assert.Null(x.TotalServiceDays);
        });

        var fridayDurationsResult = Assert.IsType<OkObjectResult>(await controller.Durations(program.Id, bundle.Id, true,
            CancellationToken.None));
        var fridayDuration = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(fridayDurationsResult.Value)
            .Single(x => x.DurationDays == 6);
        Assert.True(fridayDuration.IncludeFriday);
        Assert.Equal(1, fridayDuration.FridayDays);
        Assert.Equal(7, fridayDuration.TotalServiceDays);
        var fridayMonthlyDuration = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(fridayDurationsResult.Value)
            .Single(x => x.DurationDays == 26);
        Assert.Equal(4, fridayMonthlyDuration.FridayDays);
        Assert.Equal(30, fridayMonthlyDuration.TotalServiceDays);

        var fridayTiersResult = Assert.IsType<OkObjectResult>(await controller.Tiers(program.Id, bundle.Id, 6, true,
            CancellationToken.None));
        var fridayTiers = Assert.IsAssignableFrom<IEnumerable<CustomerTierOptionDto>>(fridayTiersResult.Value).ToList();
        Assert.Equal(2, fridayTiers.Count);
        Assert.All(fridayTiers, x =>
        {
            Assert.True(x.IncludeFriday);
            Assert.Equal(1, x.FridayDays);
            Assert.Equal(7, x.TotalServiceDays);
            Assert.True(x.FridayAvailability.IsAvailable);
        });
        var basicFridayTier = fridayTiers.Single(x => x.Tier == RestaurantTier.Basic);
        Assert.Equal(0m, basicFridayTier.PriceDifference);
        Assert.True(basicFridayTier.IsRecommended);
    }

    [Fact]
    public async Task Summary_WithoutRestaurantId_ReturnsPackageSummaryForSelectedTier()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_SUMMARY", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
            ImageStorageKey = "uploads/admin/programs/keto.webp",
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_SUMMARY", NameAr = "باقة 4 وجبات", NameEn = "4-meal bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
        });
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
            ReferencePrice26 = new Money(104m, "KWD"),
            ContractedSupplyPrice26 = new Money(80m, "KWD"),
            LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
        });
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(PricingEngine.Calculate(new PricingEngineRequest(
                9m, 26, DurationRatesPct: new Dictionary<int, decimal> { [26] = 0m },
                GatewayFeePct: 0m, GatewayFeeFixedKwd: 0m, RoundingStepKwd: 0.001m))));
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns([new RestaurantEligibilityDto(restaurant.Id, restaurant.TradeNameAr, restaurant.TradeNameEn, true, [], [])]);
        var controller = CreateController(db, out _, out var currentUser, sender, new SubscriptionScheduleService(clock),
            new BundleSlotRequirementService(db), eligibility);
        var regionId = Guid.NewGuid();
        db.Add(new CustomerProfile
        {
            UserId = currentUser.UserId!.Value,
            AllergiesCompletedAtUtc = DateTime.UtcNow,
            DietaryPreferencesCompletedAtUtc = DateTime.UtcNow,
        });
        db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = currentUser.UserId!.Value, Label = "المنزل", AddressLine = "قطعة 4 شارع 12",
            Governorate = "العاصمة", Area = "الشرق", RegionId = regionId, IsDefault = true,
        });
        await db.SaveChangesAsync();
        var request = new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26, DateOnly.FromDateTime(DateTime.UtcNow));
        var chosenStart = request.StartDate!.Value;
        var expectedFirstDelivery = new SubscriptionScheduleService(clock).ResolveFirstDeliveryDate(chosenStart, false);

        var result = Assert.IsType<OkObjectResult>(await controller.Summary(request, CancellationToken.None));
        var summary = Assert.IsType<SubscriptionOrderSummaryDto>(result.Value);
        Assert.Equal(1, summary.RestaurantCount);
        Assert.Equal("المنزل", summary.Address!.Label);
        Assert.Equal(regionId, summary.Address.RegionId);
        var delivering = Assert.Single(summary.Restaurants!);
        Assert.Equal(restaurant.Id, delivering.RestaurantId);
        Assert.Equal("مطعم", delivering.NameAr);
        Assert.True(summary.CanCheckout);
        Assert.Equal(RestaurantTier.Basic, summary.Tier);
        Assert.False(string.IsNullOrWhiteSpace(summary.TierNameAr));
        Assert.False(string.IsNullOrWhiteSpace(summary.TierNameEn));
        Assert.Equal(26, summary.DurationDays);
        Assert.Equal(bundle.Id, summary.BundleId);
        Assert.Contains("keto.webp", summary.ProgramImageUrl);
        Assert.Equal(chosenStart, summary.ChosenStartDate);
        Assert.Equal(expectedFirstDelivery, summary.StartDate);
        Assert.NotNull(summary.RestaurantRepetition);
        Assert.Equal(1, summary.RestaurantRepetition!.RestaurantCount);
        Assert.Equal(26, summary.RestaurantRepetition.DurationDays);
        Assert.Equal(26, summary.RestaurantRepetition.Limit); // single restaurant → full duration
        Assert.True(summary.FinalPrice > 0m);
        var lunch = Assert.Single(summary.SlotRequirements);
        Assert.Equal("lunch", lunch.Slot);
        Assert.Equal("الغداء", lunch.NameAr);
        Assert.Equal("Lunch", lunch.NameEn);
        Assert.Equal(1, lunch.Count);
        Assert.Equal("يشمل اشتراكك", summary.Includes.TitleAr);
        Assert.Equal("Your subscription includes", summary.Includes.TitleEn);
        Assert.Equal(3, summary.Includes.Items.Count);
        Assert.Equal("DAILY_MEALS", summary.Includes.Items[0].Code);
        Assert.Contains("وجبة واحدة يوميًا", summary.Includes.Items[0].TextAr);
        Assert.Contains("الغداء", summary.Includes.Items[0].TextAr);
        Assert.Equal("RESTAURANTS", summary.Includes.Items[1].Code);
        Assert.Contains("مطعم واحد", summary.Includes.Items[1].TextAr);
        Assert.Equal("SERVICE_DAYS", summary.Includes.Items[2].Code);
    }

    [Fact]
    public async Task Readiness_WhenIncomplete_ReturnsAddressNextAction()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_READY", NameAr = "كيتو", NameEn = "Keto",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_READY", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var currentUser);
        db.Add(new CustomerProfile { UserId = currentUser.UserId!.Value });
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await controller.Readiness(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26), CancellationToken.None));
        var readiness = Assert.IsType<SubscriptionCheckoutReadinessDto>(result.Value);
        Assert.False(readiness.IsComplete);
        Assert.Equal(SubscriptionCheckoutGateCodes.Address, readiness.NextAction);
        Assert.Equal("كيتو", readiness.Program.NameAr);
    }

    [Fact]
    public async Task Readiness_WhenAddressOnly_ReturnsDietaryPreferencesNextAction()
    {
        await using var db = CreateDb();
        var (program, bundle, userId) = await SeedReadinessContextAsync(db);
        db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = userId, Label = "المنزل", AddressLine = "قطعة 4", RegionId = Guid.NewGuid(), IsDefault = true,
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var currentUser);
        currentUser.UserId.Returns(userId);

        var result = Assert.IsType<OkObjectResult>(await controller.Readiness(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26), CancellationToken.None));
        var readiness = Assert.IsType<SubscriptionCheckoutReadinessDto>(result.Value);
        Assert.Equal(SubscriptionCheckoutGateCodes.DietaryPreferences, readiness.NextAction);
        Assert.False(readiness.CanOpenSummary);
    }

    [Fact]
    public async Task Readiness_WhenAddressAndDietary_ReturnsAllergiesNextAction()
    {
        await using var db = CreateDb();
        var (program, bundle, userId) = await SeedReadinessContextAsync(db, dietary: true);
        db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = userId, Label = "المنزل", AddressLine = "قطعة 4", RegionId = Guid.NewGuid(), IsDefault = true,
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var currentUser);
        currentUser.UserId.Returns(userId);

        var result = Assert.IsType<OkObjectResult>(await controller.Readiness(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26), CancellationToken.None));
        var readiness = Assert.IsType<SubscriptionCheckoutReadinessDto>(result.Value);
        Assert.Equal(SubscriptionCheckoutGateCodes.Allergies, readiness.NextAction);
        Assert.False(readiness.CanOpenSummary);
    }

    [Fact]
    public async Task Readiness_WhenComplete_CanOpenSummary()
    {
        await using var db = CreateDb();
        var (program, bundle, userId) = await SeedReadinessContextAsync(db, allergies: true, dietary: true);
        db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = userId, Label = "المنزل", AddressLine = "قطعة 4", RegionId = Guid.NewGuid(), IsDefault = true,
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var currentUser);
        currentUser.UserId.Returns(userId);

        var result = Assert.IsType<OkObjectResult>(await controller.Readiness(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26), CancellationToken.None));
        var readiness = Assert.IsType<SubscriptionCheckoutReadinessDto>(result.Value);
        Assert.True(readiness.IsComplete);
        Assert.True(readiness.CanOpenSummary);
        Assert.Equal(SubscriptionCheckoutGateCodes.Summary, readiness.NextAction);
    }

    private static async Task<(MealMate.Domain.ProgramsSubscriptions.Program Program, Bundle Bundle, Guid UserId)> SeedReadinessContextAsync(
        AppDbContext db, bool allergies = false, bool dietary = false)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_READY", NameAr = "كيتو", NameEn = "Keto",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_READY", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var userId = Guid.NewGuid();
        db.AddRange(program, bundle, new CustomerProfile
        {
            UserId = userId,
            AllergiesCompletedAtUtc = allergies ? DateTime.UtcNow : null,
            DietaryPreferencesCompletedAtUtc = dietary ? DateTime.UtcNow : null,
        });
        await db.SaveChangesAsync();
        return (program, bundle, userId);
    }

    [Fact]
    public async Task Summary_WhenIncomplete_ReturnsConflictWithReadiness()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_INCOMPLETE", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_INCOMPLETE", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();
        var controller = CreateController(db, out _, out var currentUser);
        db.Add(new CustomerProfile { UserId = currentUser.UserId!.Value });
        await db.SaveChangesAsync();

        var result = Assert.IsType<ConflictObjectResult>(await controller.Summary(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26, DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None));
        var payload = result.Value!;
        var error = payload.GetType().GetProperty("error")?.GetValue(payload)?.ToString();
        Assert.Equal(SubscriptionCheckoutGateCodes.ProfileIncomplete, error);
    }

    [Fact]
    public async Task Summary_OmitsRestaurantsOutsideAddressCoverage()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_COVER", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
            ImageStorageKey = "uploads/admin/programs/cover.webp",
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_COVER", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var covered = new RestaurantProfile
        {
            TradeNameAr = "يوصل", TradeNameEn = "Covered", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var uncovered = new RestaurantProfile
        {
            TradeNameAr = "لا يوصل", TradeNameEn = "Uncovered", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, covered, uncovered);
        db.RestaurantProgramBundleOffers.AddRange(
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = covered.Id, IsActive = true },
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = uncovered.Id, IsActive = true });
        db.RestaurantBundlePricings.AddRange(
            new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = covered.Id,
                ReferencePrice26 = new Money(104m, "KWD"), ContractedSupplyPrice26 = new Money(80m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            },
            new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = uncovered.Id,
                ReferencePrice26 = new Money(104m, "KWD"), ContractedSupplyPrice26 = new Money(80m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            });
        await db.SaveChangesAsync();

        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(PricingEngine.Calculate(new PricingEngineRequest(
                9m, 26, DurationRatesPct: new Dictionary<int, decimal> { [26] = 0m },
                GatewayFeePct: 0m, GatewayFeeFixedKwd: 0m, RoundingStepKwd: 0.001m))));
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns([
                new RestaurantEligibilityDto(covered.Id, covered.TradeNameAr, covered.TradeNameEn, true, [], []),
                new RestaurantEligibilityDto(uncovered.Id, uncovered.TradeNameAr, uncovered.TradeNameEn, false, [EligibilityReasonCodes.RegionNotCovered], []),
            ]);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var controller = CreateController(db, out _, out var currentUser, sender, new SubscriptionScheduleService(clock),
            new BundleSlotRequirementService(db), eligibility);
        db.Add(new CustomerProfile
        {
            UserId = currentUser.UserId!.Value,
            AllergiesCompletedAtUtc = DateTime.UtcNow,
            DietaryPreferencesCompletedAtUtc = DateTime.UtcNow,
        });
        db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = currentUser.UserId!.Value, Label = "العمل", AddressLine = "شارع الخليج",
            RegionId = Guid.NewGuid(), IsDefault = true,
        });
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await controller.Summary(
            new SubscriptionSummaryRequest(program.Id, bundle.Id, RestaurantTier.Basic, 26, DateOnly.FromDateTime(DateTime.UtcNow)), CancellationToken.None));
        var summary = Assert.IsType<SubscriptionOrderSummaryDto>(result.Value);
        Assert.Equal(1, summary.RestaurantCount);
        Assert.Equal("العمل", summary.Address!.Label);
        Assert.Equal("يوصل", Assert.Single(summary.Restaurants!).NameAr);
        Assert.DoesNotContain(summary.Restaurants!, x => x.RestaurantId == uncovered.Id);
        Assert.Contains("مطعم واحد", summary.Includes.Items.Single(x => x.Code == "RESTAURANTS").TextAr);
    }

    [Fact]
    public async Task TierRestaurants_ReturnsLogoNameRatingMealsAndClassifications()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_BROWSE", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_BROWSE", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var first = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "دايت هاوس", TradeNameEn = "Diet House", LegalNameAr = "دايت هاوس", LegalNameEn = "Diet House",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var second = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطبخ البروتين", TradeNameEn = "The Protein Kitchen", LegalNameAr = "مطبخ البروتين", LegalNameEn = "The Protein Kitchen",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, first, second);
        foreach (var restaurant in new[] { first, second })
        {
            db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
            });
            db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
                ReferencePrice26 = new Money(52m, "KWD"),
                ContractedSupplyPrice26 = new Money(40m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
            });
        }
        db.RestaurantBranches.Add(new MealMate.Domain.RestaurantOperations.RestaurantBranch
        {
            RestaurantRegistrationId = first.Id, BranchCode = "MAIN", NameAr = "الرئيسي", NameEn = "Main",
            AddressAr = "الكويت", AddressEn = "Kuwait", GovernorateAr = "العاصمة", GovernorateEn = "Capital",
            ContactPhone = "50000000", IsActive = true, IsPrimary = true,
            PhotoStorageKey = "uploads/restaurant/branches/diet-house.webp",
        });
        var menu = new MealMate.Domain.RestaurantOperations.Menu
        {
            RestaurantId = first.Id, NameAr = "قائمة", NameEn = "Menu", IsActive = true,
        };
        db.Menus.Add(menu);
        db.Meals.AddRange(
            new MealMate.Domain.RestaurantOperations.Meal
            {
                RestaurantId = first.Id, MenuId = menu.Id, Menu = menu, Code = "M1",
                NameAr = "وجبة الباقة", NameEn = "Bundle meal",
                ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
                ProgramId = program.Id, BundleId = bundle.Id,
            },
            new MealMate.Domain.RestaurantOperations.Meal
            {
                RestaurantId = first.Id, MenuId = menu.Id, Menu = menu, Code = "M2",
                NameAr = "وجبة أخرى", NameEn = "Other meal",
                ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
            });
        db.RestaurantRatings.AddRange(
            new MealMate.Domain.RestaurantOperations.RestaurantRating
            {
                RestaurantId = first.Id, CustomerUserId = Guid.NewGuid(), Score = 5,
            },
            new MealMate.Domain.RestaurantOperations.RestaurantRating
            {
                RestaurantId = first.Id, CustomerUserId = Guid.NewGuid(), Score = 4,
            });
        var pricingSettings = SubscriptionPricingSettingsDefaults.Create();
        pricingSettings.TierLabels.BasicAr = "سيلكت";
        pricingSettings.TierLabels.BasicEn = "Select";
        pricingSettings.TierLabels.BasicLogoStorageKey = "uploads/admin/tiers/basic.webp";
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = JsonSerializer.Serialize(pricingSettings, SubscriptionPricingSettingsDefaults.JsonOptions),
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out var currentUser);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, first.Id, second.Id);
        var result = Assert.IsType<OkObjectResult>(
            await controller.TierRestaurants(program.Id, bundle.Id, RestaurantTier.Basic,
                [second.Id, first.Id], CancellationToken.None));
        var cards = Assert.IsAssignableFrom<IEnumerable<CustomerRestaurantBrowseCardDto>>(result.Value).ToList();
        Assert.Equal([second.Id, first.Id], cards.Select(x => x.RestaurantId));

        var dietHouse = cards.Single(x => x.RestaurantId == first.Id);
        Assert.Equal("دايت هاوس", dietHouse.NameAr);
        Assert.Equal("Diet House", dietHouse.NameEn);
        Assert.Contains("diet-house.webp", dietHouse.LogoUrl);
        Assert.Equal(4.5m, dietHouse.Rating);
        Assert.Equal(2, dietHouse.MealCount);
        Assert.Equal(1, dietHouse.BundleMealCount);
        Assert.Equal(RestaurantTier.Basic, dietHouse.Tier);
        Assert.Equal("سيلكت", dietHouse.TierNameAr);
        Assert.Equal("Select", dietHouse.TierNameEn);
        var classification = Assert.Single(dietHouse.Classifications);
        Assert.Contains("basic.webp", classification.LogoUrl);
        Assert.Equal(RestaurantTier.Basic, classification.Tier);
        Assert.Null(typeof(CustomerRestaurantClassificationDto).GetProperty("NameAr"));
        Assert.Null(typeof(CustomerRestaurantClassificationDto).GetProperty("NameEn"));

        var protein = cards.Single(x => x.RestaurantId == second.Id);
        Assert.Equal("مطبخ البروتين", protein.NameAr);
        Assert.Null(protein.LogoUrl);
        Assert.Null(protein.Rating);
        Assert.Equal(0, protein.MealCount);
        Assert.Equal(0, protein.BundleMealCount);
    }

    [Fact]
    public async Task RestaurantBrowseDetails_ReturnsLogoOriginsAndMealsGroupedBySlot()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_DETAILS", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_DETAILS", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطبخ البروتين", TradeNameEn = "The Protein Kitchen",
            LegalNameAr = "مطبخ البروتين", LegalNameEn = "The Protein Kitchen",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
        });
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
            ReferencePrice26 = new Money(52m, "KWD"),
            ContractedSupplyPrice26 = new Money(40m, "KWD"),
            LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
        });
        db.RestaurantBranches.Add(new MealMate.Domain.RestaurantOperations.RestaurantBranch
        {
            RestaurantRegistrationId = restaurant.Id, BranchCode = "MAIN", NameAr = "الرئيسي", NameEn = "Main",
            AddressAr = "الكويت", AddressEn = "Kuwait", GovernorateAr = "العاصمة", GovernorateEn = "Capital",
            ContactPhone = "50000000", IsActive = true, IsPrimary = true,
            PhotoStorageKey = "uploads/restaurant/branches/protein.webp",
        });
        db.Countries.AddRange(
            new Country { Code = "ES", NameAr = "اسبانيا", NameEn = "Spain", DialCode = "+34", FlagEmoji = "🇪🇸" },
            new Country { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait", DialCode = "+965", FlagEmoji = "🇰🇼" },
            new Country { Code = "NO", NameAr = "النرويج", NameEn = "Norway", DialCode = "+47", FlagEmoji = "🇳🇴" });
        var oil = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "OIL", NameAr = "زيت زيتون", NameEn = "Olive oil",
            Category = "oils", OriginCountryCode = "ES", IsLocal = false, IsOriginVisibleToCustomers = true,
            IsCore = true, RestaurantIconUrl = "uploads/restaurant/ingredients/oils.webp", IsActive = true,
        };
        var poultry = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "CHK", NameAr = "دجاج مشوي", NameEn = "Grilled chicken",
            Category = "poultry", OriginCountryCode = "KW", IsLocal = true, IsOriginVisibleToCustomers = true,
            IsCore = true, MealIconUrl = "uploads/restaurant/ingredients/chicken.webp", IsActive = true,
        };
        var hidden = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "HID", NameAr = "مخفي", NameEn = "Hidden",
            Category = "seafood", OriginCountryCode = "NO", IsLocal = false, IsOriginVisibleToCustomers = false,
            IsCore = true, IsActive = true,
        };
        var garnish = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "LEM", NameAr = "ليمون", NameEn = "Lemon",
            Category = "produce", OriginCountryCode = "KW", IsLocal = true, IsOriginVisibleToCustomers = true,
            IsCore = false, MealIconUrl = "uploads/restaurant/ingredients/lemon.webp", IsActive = true,
        };
        db.Ingredients.AddRange(oil, poultry, hidden, garnish);
        var menu = new MealMate.Domain.RestaurantOperations.Menu
        {
            RestaurantId = restaurant.Id, NameAr = "قائمة", NameEn = "Menu", IsActive = true,
        };
        db.Menus.Add(menu);
        var breakfast = new MealMate.Domain.RestaurantOperations.Meal
        {
            RestaurantId = restaurant.Id, MenuId = menu.Id, Menu = menu, Code = "B1",
            NameAr = "فطور بروتين", NameEn = "Protein breakfast", Slot = "breakfast",
            ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
            ProgramId = program.Id, BundleId = bundle.Id,
            Calories = 550, ProteinG = 35, CarbsG = 55, FatG = 18,
        };
        var lunch = new MealMate.Domain.RestaurantOperations.Meal
        {
            RestaurantId = restaurant.Id, MenuId = menu.Id, Menu = menu, Code = "L1",
            NameAr = "غداء دجاج", NameEn = "Chicken lunch", Slot = "lunch",
            ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
            ProgramId = program.Id, BundleId = bundle.Id,
            Calories = 620, ProteinG = 40, CarbsG = 48, FatG = 16,
        };
        var snack = new MealMate.Domain.RestaurantOperations.Meal
        {
            RestaurantId = restaurant.Id, MenuId = menu.Id, Menu = menu, Code = "S1",
            NameAr = "سناك", NameEn = "Snack", Slot = "snack",
            ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
            ProgramId = program.Id, BundleId = bundle.Id,
        };
        db.Meals.AddRange(breakfast, lunch, snack);
        db.MealImages.Add(new MealMate.Domain.RestaurantOperations.MealImage
        {
            MealId = breakfast.Id, ImageUrl = "uploads/restaurant/meals/breakfast.webp", IsPrimary = true, DisplayOrder = 1,
        });
        db.MealIngredients.AddRange(
            new MealMate.Domain.RestaurantOperations.MealIngredient
            {
                MealId = breakfast.Id, IngredientId = poultry.Id, PortionGrams = 120, SortOrder = 1,
            },
            new MealMate.Domain.RestaurantOperations.MealIngredient
            {
                MealId = breakfast.Id, IngredientId = oil.Id, PortionGrams = 10, SortOrder = 2,
            },
            new MealMate.Domain.RestaurantOperations.MealIngredient
            {
                MealId = breakfast.Id, IngredientId = garnish.Id, PortionGrams = 5, SortOrder = 3,
            });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(
            await controller.RestaurantBrowseDetails(program.Id, bundle.Id, restaurant.Id, CancellationToken.None));
        var details = Assert.IsType<CustomerRestaurantBrowseDetailsDto>(result.Value);
        Assert.Equal(restaurant.Id, details.RestaurantId);
        Assert.Equal("مطبخ البروتين", details.NameAr);
        Assert.Contains("protein.webp", details.LogoUrl);
        Assert.Contains("breakfast.webp", details.CoverImageUrl);
        Assert.Equal(3, details.CoreIngredients.Count);
        var oliveOil = details.CoreIngredients.Single(x => x.NameAr == "زيت زيتون");
        Assert.Contains("oils.webp", oliveOil.IconUrl);
        Assert.False(oliveOil.IsLocal);
        Assert.Equal("ES", oliveOil.OriginCountryCode);
        Assert.Equal("اسبانيا", oliveOil.OriginCountryNameAr);
        Assert.Equal("Spain", oliveOil.OriginCountryNameEn);
        Assert.Equal("🇪🇸", oliveOil.FlagEmoji);
        var chicken = details.CoreIngredients.Single(x => x.NameAr == "دجاج مشوي");
        Assert.Contains("chicken.webp", chicken.IconUrl);
        Assert.True(chicken.IsLocal);
        Assert.Equal("KW", chicken.OriginCountryCode);
        Assert.Equal("الكويت", chicken.OriginCountryNameAr);
        Assert.Equal("🇰🇼", chicken.FlagEmoji);
        var hiddenCore = details.CoreIngredients.Single(x => x.NameAr == "مخفي");
        Assert.Null(hiddenCore.OriginCountryCode);
        Assert.Null(hiddenCore.OriginCountryNameAr);
        Assert.Null(hiddenCore.FlagEmoji);
        Assert.DoesNotContain(details.CoreIngredients, x => x.NameAr == "ليمون");
        Assert.Equal(2, details.IngredientOrigins.Count);
        var oils = details.IngredientOrigins.Single(x => x.Category == "oils");
        Assert.Equal("الزيوت", oils.NameAr);
        Assert.False(oils.IsLocal);
        Assert.Equal("مستوردة", oils.SourceAr);
        Assert.Equal("ES", oils.OriginCountryCode);
        Assert.Equal("اسبانيا", oils.OriginCountryNameAr);
        Assert.Equal("🇪🇸", oils.FlagEmoji);
        Assert.Contains("oils.webp", oils.IconUrl);
        var poultryOrigin = details.IngredientOrigins.Single(x => x.Category == "poultry");
        Assert.Equal("الدواجن", poultryOrigin.NameAr);
        Assert.True(poultryOrigin.IsLocal);
        Assert.Equal("طازجة محلية", poultryOrigin.SourceAr);
        Assert.Equal("KW", poultryOrigin.OriginCountryCode);
        Assert.DoesNotContain(details.IngredientOrigins, x => x.Category == "seafood");
        Assert.DoesNotContain(details.IngredientOrigins, x => x.Category == "produce");
        Assert.Equal(["breakfast", "lunch", "snack"], details.MealGroups.Select(x => x.Slot));
        Assert.Equal("الإفطار", details.MealGroups[0].NameAr);
        var breakfastCard = Assert.Single(details.MealGroups[0].Meals);
        Assert.Equal("فطور بروتين", breakfastCard.NameAr);
        Assert.Equal(550, breakfastCard.Calories);
        Assert.Equal(35, breakfastCard.ProteinG);
        Assert.Equal(55, breakfastCard.CarbsG);
        Assert.Equal(18, breakfastCard.FatG);
        // Core ingredients live on restaurant details — meals expose non-core chips only.
        Assert.Equal(["ليمون"], breakfastCard.Ingredients.Select(x => x.NameAr));
        Assert.Equal("produce", breakfastCard.Ingredients[0].Category);
        Assert.False(breakfastCard.Ingredients[0].IsCore);
        Assert.Contains("lemon.webp", breakfastCard.Ingredients[0].IconUrl);
        Assert.Null(typeof(CustomerRestaurantBrowseMealDto).GetProperty("DescriptionAr"));
        Assert.Null(typeof(CustomerRestaurantBrowseMealDto).GetProperty("FiberG"));
        Assert.Equal("غداء دجاج", Assert.Single(details.MealGroups[1].Meals).NameAr);
        Assert.Equal("سناك", Assert.Single(details.MealGroups[2].Meals).NameAr);
    }

    [Fact]
    public async Task RestaurantBrowseMealDetails_ReturnsDisplayPageWithShortAllergens()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_MEAL", NameAr = "خسارة الوزن", NameEn = "Weight Loss",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_MEAL", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطبخ البروتين", TradeNameEn = "The Protein Kitchen",
            LegalNameAr = "مطبخ البروتين", LegalNameEn = "The Protein Kitchen",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(program, bundle, restaurant);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
        });
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
            ReferencePrice26 = new Money(52m, "KWD"),
            ContractedSupplyPrice26 = new Money(40m, "KWD"),
            LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
        });
        db.RestaurantBranches.Add(new MealMate.Domain.RestaurantOperations.RestaurantBranch
        {
            RestaurantRegistrationId = restaurant.Id, BranchCode = "MAIN", NameAr = "الرئيسي", NameEn = "Main",
            AddressAr = "الكويت", AddressEn = "Kuwait", GovernorateAr = "العاصمة", GovernorateEn = "Capital",
            ContactPhone = "50000000", IsActive = true, IsPrimary = true,
            PhotoStorageKey = "uploads/restaurant/branches/protein.webp",
        });
        db.AllergenDefinitions.AddRange(
            new MealMate.Domain.RestaurantOperations.AllergenDefinition { Code = "GLUTEN", NameAr = "جلوتين", NameEn = "Gluten", IsActive = true, SortOrder = 1 },
            new MealMate.Domain.RestaurantOperations.AllergenDefinition { Code = "MILK", NameAr = "لاكتوز", NameEn = "Lactose", IsActive = true, SortOrder = 2 },
            new MealMate.Domain.RestaurantOperations.AllergenDefinition { Code = "TREE_NUTS", NameAr = "مكسرات", NameEn = "Nuts", IsActive = true, SortOrder = 3 });
        var chicken = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "CHK", NameAr = "دجاج مشوي", NameEn = "Grilled chicken",
            Category = "poultry", IsActive = true,
        };
        var rice = new MealMate.Domain.RestaurantOperations.Ingredient
        {
            RestaurantId = restaurant.Id, Code = "RICE", NameAr = "أرز بني", NameEn = "Brown rice",
            Category = "grains", IsActive = true,
        };
        db.Ingredients.AddRange(chicken, rice);
        var menu = new MealMate.Domain.RestaurantOperations.Menu
        {
            RestaurantId = restaurant.Id, NameAr = "قائمة", NameEn = "Menu", IsActive = true,
        };
        db.Menus.Add(menu);
        var meal = new MealMate.Domain.RestaurantOperations.Meal
        {
            RestaurantId = restaurant.Id, MenuId = menu.Id, Menu = menu, Code = "L1",
            NameAr = "دجاج مشوي مع الأرز البني", NameEn = "Grilled chicken with brown rice",
            DescriptionAr = "وجبة متكاملة عالية البروتين.", DescriptionEn = "A high-protein complete meal.",
            Slot = "lunch", ApprovalStatus = VerificationStatus.Approved, IsAvailableForNewOrders = true,
            ProgramId = program.Id, BundleId = bundle.Id,
            Calories = 550, ProteinG = 65, CarbsG = 60, FatG = 15, FiberG = 7,
            PresentAllergenCodes = "GLUTEN,MILK,TREE_NUTS",
        };
        db.Meals.Add(meal);
        db.MealImages.Add(new MealMate.Domain.RestaurantOperations.MealImage
        {
            MealId = meal.Id, ImageUrl = "uploads/restaurant/meals/chicken.webp", IsPrimary = true, DisplayOrder = 1,
        });
        db.MealIngredients.AddRange(
            new MealMate.Domain.RestaurantOperations.MealIngredient { MealId = meal.Id, IngredientId = chicken.Id, PortionGrams = 180, SortOrder = 1 },
            new MealMate.Domain.RestaurantOperations.MealIngredient { MealId = meal.Id, IngredientId = rice.Id, PortionGrams = 120, SortOrder = 2 });
        db.RestaurantRatings.AddRange(
            new MealMate.Domain.RestaurantOperations.RestaurantRating { RestaurantId = restaurant.Id, CustomerUserId = Guid.NewGuid(), Score = 5 },
            new MealMate.Domain.RestaurantOperations.RestaurantRating { RestaurantId = restaurant.Id, CustomerUserId = Guid.NewGuid(), Score = 5 },
            new MealMate.Domain.RestaurantOperations.RestaurantRating { RestaurantId = restaurant.Id, CustomerUserId = Guid.NewGuid(), Score = 4 });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(
            await controller.RestaurantBrowseMealDetails(program.Id, bundle.Id, restaurant.Id, meal.Id, CancellationToken.None));
        var page = Assert.IsType<CustomerRestaurantBrowseMealDetailsDto>(result.Value);
        Assert.Equal("دجاج مشوي مع الأرز البني", page.NameAr);
        Assert.Equal("وجبة متكاملة عالية البروتين.", page.DescriptionAr);
        Assert.Contains("chicken.webp", page.ImageUrl);
        Assert.Equal("lunch", page.Slot);
        Assert.Equal("مطبخ البروتين", page.RestaurantNameAr);
        Assert.Contains("protein.webp", page.RestaurantLogoUrl);
        Assert.Equal(4.7m, page.RestaurantRating);
        Assert.Equal(3, page.RestaurantRatingCount);
        Assert.Null(typeof(CustomerRestaurantBrowseMealDetailsDto).GetProperty("ProgramNameAr"));
        Assert.Null(page.DeliveryWindowStart);
        Assert.Equal(["دجاج مشوي", "أرز بني"], page.Ingredients.Select(x => x.NameAr));
        Assert.Equal(550, page.Nutrition.Calories);
        Assert.Equal(65, page.Nutrition.ProteinG);
        Assert.Equal(60, page.Nutrition.CarbsG);
        Assert.Equal(15, page.Nutrition.FatG);
        Assert.Equal(7, page.Nutrition.FiberG);
        Assert.Equal(100, page.Nutrition.CarbsPercent + page.Nutrition.ProteinPercent + page.Nutrition.FatPercent);
        Assert.Equal(["GLUTEN", "MILK", "TREE_NUTS"], page.Allergens.Select(x => x.Code));
        Assert.Equal(["جلوتين", "لاكتوز", "مكسرات"], page.Allergens.Select(x => x.NameAr));
        Assert.Equal("Gluten", page.Allergens[0].NameEn);
        Assert.DoesNotContain(page.Allergens, x => x.NameAr.Contains("حبوب") || x.NameAr.Contains("مشتقات"));
    }

    [Fact]
    public async Task FridayToggle_IgnoresUnclassifiedRestaurants_AndConflictsWithUnavailablePayload()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "PROG_FRIDAY", NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_FRIDAY", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var classified = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم أساسي", TradeNameEn = "Classified", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var unclassifiedFriday = new MealMate.Domain.IdentityAccess.RestaurantProfile
        {
            TradeNameAr = "مطعم غير مصنف", TradeNameEn = "Unclassified Friday", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var run = new ClassificationRun
        {
            ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
            RestaurantCount = 1, ConfidenceLevel = "95", RunAtUtc = DateTime.UtcNow,
        };
        db.AddRange(program, bundle, classified, unclassifiedFriday, run);
        db.RestaurantProgramBundleOffers.AddRange(
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = classified.Id, IsActive = true },
            new RestaurantProgramBundleOffer { ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = unclassifiedFriday.Id, IsActive = true });
        db.RestaurantOperatingSchedules.AddRange(
            new MealMate.Domain.RestaurantOperations.RestaurantOperatingSchedule
            {
                RestaurantId = classified.Id, DayOfWeek = DayOfWeek.Friday, IsWorkingDay = false,
            },
            new MealMate.Domain.RestaurantOperations.RestaurantOperatingSchedule
            {
                RestaurantId = unclassifiedFriday.Id, DayOfWeek = DayOfWeek.Friday, IsWorkingDay = true,
            });
        db.RestaurantClassifications.Add(new RestaurantClassification
        {
            ClassificationRunId = run.Id, ClassificationRun = run, RestaurantId = classified.Id,
            ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
            Tier = RestaurantTier.Basic,
        });
        await db.SaveChangesAsync();

        IReadOnlyList<CustomerSubscriptionCatalogItemDto> catalog =
        [
            new(program.Id, program.NameAr, program.NameEn, bundle.Id, bundle.NameAr, bundle.NameEn,
                RestaurantTier.Basic, "سيلكت", "Select",
                [new(6, 66, 11), new(12, 120, 10), new(26, 234, 9)]),
        ];
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetCustomerSubscriptionPricingCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(catalog));
        var controller = CreateController(db, out _, out var currentUser, sender);
        await SeedCustomerDeliveryCoverageAsync(db, currentUser, classified.Id, unclassifiedFriday.Id);

        var durationsResult = Assert.IsType<OkObjectResult>(await controller.Durations(program.Id, bundle.Id, false, CancellationToken.None));
        var durations = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(durationsResult.Value).ToList();
        Assert.All(durations, x =>
        {
            Assert.False(x.SupportsFriday);
            Assert.False(x.FridayOption.IsEnabled);
            Assert.False(x.FridayAvailability.IsAvailable);
            Assert.Equal("DISABLED", x.FridayAvailability.DisplayType);
        });

        var fridayTiers = Assert.IsType<ConflictObjectResult>(
            await controller.Tiers(program.Id, bundle.Id, null, true, CancellationToken.None));
        var payload = fridayTiers.Value!;
        var error = payload.GetType().GetProperty("error")?.GetValue(payload)?.ToString()
            ?? payload.GetType().GetProperty("Error")?.GetValue(payload)?.ToString();
        Assert.Equal("subscription_flow.friday_unavailable", error);
        var availability = payload.GetType().GetProperty("fridayAvailability")?.GetValue(payload)
            ?? payload.GetType().GetProperty("FridayAvailability")?.GetValue(payload);
        Assert.NotNull(availability);
        var isAvailable = Assert.IsType<bool>(availability!.GetType().GetProperty("IsAvailable")!.GetValue(availability));
        Assert.False(isAvailable);
    }

    private static CustomerSubscriptionFlowController CreateController(AppDbContext db, out IMyFatoorahPaymentService payment,
        out ICurrentUser currentUser, ISender? sender = null, ISubscriptionScheduleService? schedule = null,
        IBundleSlotRequirementService? slots = null, IRestaurantEligibilityService? eligibility = null)
    {
        payment = Substitute.For<IMyFatoorahPaymentService>();
        currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Customer);
        var httpContext = new DefaultHttpContext();
        if (sender is not null)
            httpContext.RequestServices = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        slots ??= Substitute.For<IBundleSlotRequirementService>();
        if (slots is not BundleSlotRequirementService)
        {
            slots.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<SlotRequirementDto>>([]));
        }
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        eligibility ??= Substitute.For<IRestaurantEligibilityService>();
        var resolvedSchedule = schedule ?? new SubscriptionScheduleService(clock);
        var tiers = new RestaurantTierClassificationService(db, clock);
        var readiness = new SubscriptionCheckoutReadinessService(
            db, clock, eligibility, new AllowedSubscriptionDurationService(db), tiers,
            new CustomerIndividualSubscriptionGate(db));
        return new CustomerSubscriptionFlowController(db, currentUser,
            eligibility, Substitute.For<IMenuPlanResolutionService>(),
            resolvedSchedule, slots, payment,
            new AllowedSubscriptionDurationService(db), tiers, readiness,
            new CustomerIndividualSubscriptionGate(db),
            new RestaurantDiversificationCalculator())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static async Task<Guid> SeedCustomerDeliveryCoverageAsync(
        AppDbContext db,
        ICurrentUser currentUser,
        params Guid[] restaurantIds)
    {
        var userId = currentUser.UserId!.Value;
        var regionId = Guid.NewGuid();
        if (!await db.CustomerProfiles.AnyAsync(x => x.UserId == userId))
        {
            db.CustomerProfiles.Add(new CustomerProfile { UserId = userId });
        }

        var address = new CustomerAddress
        {
            UserId = userId,
            Label = "المنزل",
            AddressLine = "السالمية",
            RegionId = regionId,
            IsDefault = true,
            Latitude = 29.34m,
            Longitude = 48.08m,
        };
        db.CustomerAddresses.Add(address);
        foreach (var restaurantId in restaurantIds.Distinct())
        {
            db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
            {
                RestaurantId = restaurantId,
                RegionId = regionId,
                IsActive = true,
            });
        }

        await db.SaveChangesAsync();
        return address.Id;
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
