using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    private static CustomerSubscriptionFlowController CreateController(AppDbContext db, out IMyFatoorahPaymentService payment, out ICurrentUser currentUser)
    {
        payment = Substitute.For<IMyFatoorahPaymentService>();
        currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.UserType.Returns(UserType.Customer);
        return new CustomerSubscriptionFlowController(db, currentUser,
            Substitute.For<IRestaurantEligibilityService>(), Substitute.For<IMenuPlanResolutionService>(),
            Substitute.For<ISubscriptionScheduleService>(), Substitute.For<IBundleSlotRequirementService>(), payment)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
