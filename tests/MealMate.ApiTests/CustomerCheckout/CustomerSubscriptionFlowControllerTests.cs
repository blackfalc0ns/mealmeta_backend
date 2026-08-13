using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
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
