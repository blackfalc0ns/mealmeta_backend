using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

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
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        db.Programs.Add(program);
        db.Bundles.Add(new Bundle
        {
            ProgramId = program.Id,
            Code = "BUNDLE",
            NameAr = "باقة",
            NameEn = "Bundle",
            MealsPerDay = 1,
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, out _, out _);
        var result = Assert.IsType<OkObjectResult>(await controller.Programs(CancellationToken.None));
        var item = Assert.Single(Assert.IsAssignableFrom<IEnumerable<CustomerProgramCardDto>>(result.Value));

        Assert.Equal("وجبات محسوبة السعرات.", item.DescriptionAr);
        Assert.Equal(["سعرات محسوبة", "نتائج مستدامة"], item.KeywordsAr);
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
    public void Types_ReturnsIndividualAndFamilyForCustomer()
    {
        using var db = CreateDb();
        var controller = CreateController(db, out _, out _);

        var result = Assert.IsType<OkObjectResult>(controller.Types());
        var items = Assert.IsAssignableFrom<IEnumerable<SubscriptionTypeOptionDto>>(result.Value).ToList();

        Assert.Equal(["INDIVIDUAL", "FAMILY"], items.Select(x => x.Code));
        Assert.True(items.Single(x => x.Code == "FAMILY").MembersConfiguredAfterPayment);
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
        db.AddRange(program, bundle, restaurant, run);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
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
        var controller = CreateController(db, out _, out _, sender, schedule);

        var durationsResult = Assert.IsType<OkObjectResult>(await controller.Durations(program.Id, bundle.Id, false, CancellationToken.None));
        var durations = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(durationsResult.Value).ToList();
        Assert.Equal([6, 12, 26], durations.Select(x => x.DurationDays));
        Assert.True(durations.Single(x => x.DurationDays == 26).IsPopular);
        Assert.Equal(234, durations.Single(x => x.DurationDays == 26).PriceFrom);
        Assert.All(durations, x =>
        {
            Assert.True(x.SupportsFriday);
            Assert.False(x.IncludeFriday);
            Assert.Equal(0, x.FridayDays);
            Assert.Equal(x.DurationDays, x.TotalServiceDays);
            Assert.Equal("INCLUDE_FRIDAY", x.FridayOption.Code);
        });

        var tiersResult = Assert.IsType<OkObjectResult>(await controller.Tiers(program.Id, bundle.Id, 26, false, CancellationToken.None));
        var tiers = Assert.IsAssignableFrom<IEnumerable<CustomerTierOptionDto>>(tiersResult.Value).ToList();
        Assert.Equal(2, tiers.Count);
        Assert.True(tiers.Single(x => x.Tier == RestaurantTier.Basic).IsRecommended);
        Assert.Equal(52, tiers.Single(x => x.Tier == RestaurantTier.Platinum).PriceDifference);
        var preview = Assert.Single(tiers.Single(x => x.Tier == RestaurantTier.Basic).Restaurants);
        Assert.Equal(restaurant.Id, preview.RestaurantId);
        Assert.Contains("tier.webp", preview.LogoUrl);

        var fridayDurationsResult = Assert.IsType<OkObjectResult>(await controller.Durations(program.Id, bundle.Id, true,
            CancellationToken.None));
        var fridayDuration = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(fridayDurationsResult.Value)
            .Single(x => x.DurationDays == 6);
        Assert.True(fridayDuration.IncludeFriday);
        Assert.Equal(1, fridayDuration.FridayDays);
        Assert.Equal(7, fridayDuration.TotalServiceDays);
        Assert.Equal(63m, fridayDuration.PriceFrom);
        var fridayMonthlyDuration = Assert.IsAssignableFrom<IEnumerable<CustomerDurationOptionDto>>(fridayDurationsResult.Value)
            .Single(x => x.DurationDays == 26);
        Assert.Equal(4, fridayMonthlyDuration.FridayDays);
        Assert.Equal(30, fridayMonthlyDuration.TotalServiceDays);

        var fridayTiersResult = Assert.IsType<OkObjectResult>(await controller.Tiers(program.Id, bundle.Id, 6, true,
            CancellationToken.None));
        var fridayTiers = Assert.IsAssignableFrom<IEnumerable<CustomerTierOptionDto>>(fridayTiersResult.Value).ToList();
        Assert.Single(fridayTiers);
        Assert.All(fridayTiers, x =>
        {
            Assert.True(x.IncludeFriday);
            Assert.Equal(1, x.FridayDays);
            Assert.Equal(7, x.TotalServiceDays);
        });
        Assert.Equal(RestaurantTier.Basic, fridayTiers[0].Tier);
        Assert.Equal(0m, fridayTiers[0].PriceDifference);
        Assert.True(fridayTiers[0].FridayAvailability.IsAvailable);
    }

    private static CustomerSubscriptionFlowController CreateController(AppDbContext db, out IMyFatoorahPaymentService payment,
        out ICurrentUser currentUser, ISender? sender = null, ISubscriptionScheduleService? schedule = null)
    {
        payment = Substitute.For<IMyFatoorahPaymentService>();
        currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Customer);
        var httpContext = new DefaultHttpContext();
        if (sender is not null)
            httpContext.RequestServices = new ServiceCollection().AddSingleton(sender).BuildServiceProvider();
        return new CustomerSubscriptionFlowController(db, currentUser,
            Substitute.For<IRestaurantEligibilityService>(), Substitute.For<IMenuPlanResolutionService>(),
            schedule ?? Substitute.For<ISubscriptionScheduleService>(), Substitute.For<IBundleSlotRequirementService>(), payment)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
