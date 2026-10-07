using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class CustomerCheckoutPaymentStatusTests
{
    [Fact]
    public async Task PaymentStatus_FamilyGroupId_DoesNot404_AndSyncsByFamilyCustomerReference()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDb();
        var (group, subscription) = await SeedFamilyPendingAsync(db, userId);

        var payments = Substitute.For<IPaymentGatewayService>();
        payments.GetPaymentStatusAsync(group.Id.ToString(), "CustomerReference", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse(
                "INV-F", "Paid", 270m, "Customer", "c@example.com", "50000000", "TX-F", "VISA/MASTER",
                DateTime.UtcNow, group.Id.ToString(), "KWD", "PAY-SYNC"));

        var processor = Substitute.For<ISubscriptionPaymentProcessor>();
        processor.VerifyAndProcessAsync(
                Arg.Any<Guid>(), "PAY-SYNC", "INV-F", 270m, "KWD", group.Id.ToString(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                subscription.Status = SubscriptionStatus.Paid;
                group.Status = FamilyGroupStatus.Active;
                await db.SaveChangesAsync();
                return new PaymentProcessingResult(true, false, null, SubscriptionStatus.Paid, "Completed");
            });

        var controller = CreateController(db, userId, payments, processor);

        var result = await controller.PaymentStatus(group.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SubscriptionPaymentStatusDto>(ok.Value);
        Assert.True(dto.IsPaid);
        Assert.Equal(270m, dto.Amount);
        await payments.Received(1).GetPaymentStatusAsync(group.Id.ToString(), "CustomerReference", Arg.Any<CancellationToken>());
        await payments.DidNotReceive().GetPaymentStatusAsync(subscription.Id.ToString(), "CustomerReference", Arg.Any<CancellationToken>());
        await processor.Received(1).VerifyAndProcessAsync(
            Arg.Any<Guid>(), "PAY-SYNC", "INV-F", 270m, "KWD", group.Id.ToString(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PaymentStatus_FamilyAnchorSubscription_UsesFamilyGroupIdWhenPaymentIdMissing()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDb();
        var (group, subscription) = await SeedFamilyPendingAsync(db, userId);

        var payments = Substitute.For<IPaymentGatewayService>();
        payments.GetPaymentStatusAsync(group.Id.ToString(), "CustomerReference", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse(
                "INV-F", "Paid", 270m, "Customer", "c@example.com", "50000000", "TX-F", "VISA/MASTER",
                DateTime.UtcNow, group.Id.ToString(), "KWD", "PAY-ANCHOR"));

        var processor = Substitute.For<ISubscriptionPaymentProcessor>();
        processor.VerifyAndProcessAsync(
                Arg.Any<Guid>(), "PAY-ANCHOR", "INV-F", 270m, "KWD", group.Id.ToString(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                subscription.Status = SubscriptionStatus.Paid;
                group.Status = FamilyGroupStatus.Active;
                await db.SaveChangesAsync();
                return new PaymentProcessingResult(true, false, null, SubscriptionStatus.Paid, "Completed");
            });

        var controller = CreateController(db, userId, payments, processor);

        var result = await controller.PaymentStatus(subscription.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SubscriptionPaymentStatusDto>(ok.Value);
        Assert.True(dto.IsPaid);
        await payments.Received(1).GetPaymentStatusAsync(group.Id.ToString(), "CustomerReference", Arg.Any<CancellationToken>());
        await payments.DidNotReceive().GetPaymentStatusAsync(subscription.Id.ToString(), "CustomerReference", Arg.Any<CancellationToken>());
        await processor.Received(1).VerifyAndProcessAsync(
            Arg.Any<Guid>(), "PAY-ANCHOR", "INV-F", 270m, "KWD", group.Id.ToString(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PaymentStatus_WhenVerifyThrows_Returns200WithCurrentPendingState()
    {
        var userId = Guid.NewGuid();
        await using var db = CreateDb();
        var (group, subscription) = await SeedFamilyPendingAsync(db, userId);

        var payments = Substitute.For<IPaymentGatewayService>();
        payments.GetPaymentStatusAsync(group.Id.ToString(), "CustomerReference", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse(
                "INV-F", "Paid", 270m, "Customer", "c@example.com", "50000000", "TX-F", "VISA/MASTER",
                DateTime.UtcNow, group.Id.ToString(), "KWD", "PAY-THROW"));

        var processor = Substitute.For<ISubscriptionPaymentProcessor>();
        processor.VerifyAndProcessAsync(
                Arg.Any<Guid>(), "PAY-THROW", "INV-F", 270m, "KWD", group.Id.ToString(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<PaymentProcessingResult>>(_ => throw new InvalidOperationException("IX_Subscriptions_PaymentId"));

        var controller = CreateController(db, userId, payments, processor);

        var result = await controller.PaymentStatus(subscription.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SubscriptionPaymentStatusDto>(ok.Value);
        Assert.False(dto.IsPaid);
        Assert.Equal(nameof(SubscriptionStatus.PendingPayment), dto.PaymentStatus);
    }

    private static async Task<(FamilyGroup Group, Subscription Subscription)> SeedFamilyPendingAsync(
        AppDbContext db, Guid userId)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "P_FAM_PAY", ShortCode = "PFP", NameAr = "برنامج", NameEn = "Program",
            DescriptionAr = "وصف", DescriptionEn = "Desc", KeywordsAr = ["كلمات"], KeywordsEn = ["Keys"],
            IsActive = true, Status = ProgramStatus.Active
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Code = "B_FAM_PAY", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 2, IsActive = true, Status = ProgramStatus.Active
        };
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = userId,
            Name = "Family",
            Status = FamilyGroupStatus.CheckoutPending,
            QuotedFinalPrice = 270m,
            CurrencyCode = "KWD",
            DiscountPercentSnapshot = 0.10m,
        };
        var startDate = new DateOnly(2026, 9, 10);
        var subscription = new Subscription
        {
            CustomerUserId = userId,
            ProgramId = program.Id,
            BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 12,
            IncludeFriday = false,
            StartDate = startDate,
            EndDate = startDate.AddDays(11),
            Status = SubscriptionStatus.PendingPayment,
            PaidAmount = new Money(200m, "KWD"),
            FamilyGroupId = group.Id,
            PaymentId = null,
        };
        db.AddRange(program, bundle, group, subscription);
        await db.SaveChangesAsync();
        group.PaymentAnchorSubscriptionId = subscription.Id;
        await db.SaveChangesAsync();
        return (group, subscription);
    }

    private static AppDbContext CreateDb()
        => new(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private static CustomerCheckoutController CreateController(
        AppDbContext db,
        Guid userId,
        IPaymentGatewayService payments,
        ISubscriptionPaymentProcessor processor)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(new DateOnly(2026, 9, 8));

        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var users = Substitute.For<UserManager<ApplicationUser>>(
            store, Substitute.For<IOptions<IdentityOptions>>(), Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(), new IdentityErrorDescriber(), Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        return new CustomerCheckoutController(
            db, currentUser, payments, clock,
            users, Substitute.For<IRestaurantEligibilityService>(), Substitute.For<ICapacityConcurrencyLock>(),
            Substitute.For<ICheckoutTransactionExecutor>(), Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IRestaurantPoolSnapshotService>(), new ConfigurationBuilder().Build(),
            Substitute.For<IAllowedSubscriptionDurationService>(), processor,
            Substitute.For<ISubscriptionCheckoutReadinessService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }
}
