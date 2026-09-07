using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Persistence.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using MealMate.Domain.Foundation;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class CustomerCheckoutGateControllerTests
{
    [Fact]
    public async Task Checkout_WithoutAddress_ReturnsAddressRequired()
    {
        var result = await CheckoutWithItemsAsync(
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Address, false, "عنوان", "Address", "ADDRESS", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Allergies, false, "حساسية", "Allergies", "ALLERGIES", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.DietaryPreferences, false, "تفضيلات", "Dietary", "DIETARY_PREFERENCES", "e"));

        AssertConflict(result, "checkout.address_required");
    }

    [Fact]
    public async Task Checkout_WithoutAllergiesConfirmation_ReturnsAllergiesRequired()
    {
        var result = await CheckoutWithItemsAsync(
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Address, true, "عنوان", "Address", "ADDRESS", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Allergies, false, "حساسية", "Allergies", "ALLERGIES", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.DietaryPreferences, true, "تفضيلات", "Dietary", "DIETARY_PREFERENCES", "e"));

        AssertConflict(result, "checkout.allergies_required");
    }

    [Fact]
    public async Task Checkout_WithoutDietaryConfirmation_ReturnsDietaryRequired()
    {
        var result = await CheckoutWithItemsAsync(
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Address, true, "عنوان", "Address", "ADDRESS", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Allergies, true, "حساسية", "Allergies", "ALLERGIES", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.DietaryPreferences, false, "تفضيلات", "Dietary", "DIETARY_PREFERENCES", "e"));

        AssertConflict(result, "checkout.dietary_preferences_required");
    }

    [Fact]
    public async Task Checkout_WithExistingSubscriptionBlock_ReturnsConflict()
    {
        var result = await CheckoutWithExistingSubscriptionAsync();

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var value = conflict.Value!;
        var error = value.GetType().GetProperty("error")?.GetValue(value)?.ToString();
        var subscriptionId = value.GetType().GetProperty("subscriptionId")?.GetValue(value);
        Assert.Equal("checkout.existing_subscription", error);
        Assert.NotNull(subscriptionId);
    }

    [Fact]
    public async Task ResumePreview_WhenUncompletedSubscriptionWithActiveHold_ReturnsCanPayDirectlyTrue()
    {
        var userId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        var clock = Substitute.For<IDateTimeProvider>();
        var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        clock.UtcNow.Returns(now);
        clock.TodayUtc.Returns(new DateOnly(2026, 9, 7));

        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var startDate = new DateOnly(2026, 9, 10);
        DateOnly resolvedDate = startDate;
        string resolvedErr = string.Empty;
        schedule.TryResolveCheckoutStart(startDate, false, out Arg.Any<DateOnly>(), out Arg.Any<string>())
            .Returns(x => { x[2] = startDate; x[3] = string.Empty; return true; });
        schedule.TryResolveCheckoutStart(Arg.Any<DateOnly>(), false, out Arg.Any<DateOnly>(), out Arg.Any<string>())
            .Returns(x => { x[2] = startDate; x[3] = string.Empty; return true; });

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "P1", ShortCode = "P1", NameAr = "برنامج", NameEn = "Program",
            DescriptionAr = "وصف", DescriptionEn = "Desc", KeywordsAr = ["كلمات"], KeywordsEn = ["Keys"],
            IsActive = true, Status = ProgramStatus.Active
        };
        var bundle = new MealMate.Domain.ProgramsSubscriptions.Bundle
        {
            ProgramId = program.Id, Code = "B1", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 2, IsActive = true, Status = ProgramStatus.Active
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();

        var subscription = new MealMate.Domain.ProgramsSubscriptions.Subscription
        {
            CustomerUserId = userId, ProgramId = program.Id, BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic, DurationDays = 12, IncludeFriday = false,
            StartDate = startDate, EndDate = startDate.AddDays(12), Status = SubscriptionStatus.PendingPayment,
            PaidAmount = new MealMate.Domain.ValueObjects.Money(50m, "KWD")
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var hold = new MealMate.Domain.RestaurantOperations.CapacityHold
        {
            SubscriptionId = subscription.Id, RestaurantId = Guid.NewGuid(),
            MenuPlanVersionId = Guid.NewGuid(), ServiceDate = startDate, Quantity = 1,
            Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = now.AddMinutes(10)
        };
        db.CapacityHolds.Add(hold);
        await db.SaveChangesAsync();

        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var users = Substitute.For<UserManager<ApplicationUser>>(
            store, Substitute.For<IOptions<IdentityOptions>>(), Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(), new IdentityErrorDescriber(), Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        var controller = new CustomerCheckoutController(
            db, currentUser, Substitute.For<IMyFatoorahPaymentService>(), clock,
            users, Substitute.For<IRestaurantEligibilityService>(), Substitute.For<ICapacityConcurrencyLock>(),
            Substitute.For<ICheckoutTransactionExecutor>(), schedule,
            Substitute.For<IRestaurantPoolSnapshotService>(), new ConfigurationBuilder().Build(),
            Substitute.For<IAllowedSubscriptionDurationService>(), Substitute.For<ISubscriptionPaymentProcessor>(),
            Substitute.For<ISubscriptionCheckoutReadinessService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.ResumePreview(subscription.Id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var preview = Assert.IsType<CustomerSubscriptionResumePreviewDto>(ok.Value);

        Assert.True(preview.CanPayDirectly);
        Assert.Null(preview.Reason);
        Assert.Equal(600, preview.HoldRemainingSeconds);
        Assert.NotNull(preview.HoldExpiresAtUtc);
    }

    [Fact]
    public async Task ResumePreview_WhenHoldExpired_ReturnsCanPayDirectlyFalse()
    {
        var userId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        var clock = Substitute.For<IDateTimeProvider>();
        var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        clock.UtcNow.Returns(now);
        clock.TodayUtc.Returns(new DateOnly(2026, 9, 7));

        var schedule = Substitute.For<ISubscriptionScheduleService>();
        var startDate = new DateOnly(2026, 9, 10);
        schedule.TryResolveCheckoutStart(startDate, false, out Arg.Any<DateOnly>(), out Arg.Any<string>())
            .Returns(x => { x[2] = startDate; x[3] = string.Empty; return true; });
        schedule.TryResolveCheckoutStart(Arg.Any<DateOnly>(), false, out Arg.Any<DateOnly>(), out Arg.Any<string>())
            .Returns(x => { x[2] = startDate; x[3] = string.Empty; return true; });

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "P1", ShortCode = "P1", NameAr = "برنامج", NameEn = "Program",
            DescriptionAr = "وصف", DescriptionEn = "Desc", KeywordsAr = ["كلمات"], KeywordsEn = ["Keys"],
            IsActive = true, Status = ProgramStatus.Active
        };
        var bundle = new MealMate.Domain.ProgramsSubscriptions.Bundle
        {
            ProgramId = program.Id, Code = "B1", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 2, IsActive = true, Status = ProgramStatus.Active
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();

        var subscription = new MealMate.Domain.ProgramsSubscriptions.Subscription
        {
            CustomerUserId = userId, ProgramId = program.Id, BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic, DurationDays = 12, IncludeFriday = false,
            StartDate = startDate, EndDate = startDate.AddDays(12), Status = SubscriptionStatus.PendingPayment,
            PaidAmount = new MealMate.Domain.ValueObjects.Money(50m, "KWD")
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var hold = new MealMate.Domain.RestaurantOperations.CapacityHold
        {
            SubscriptionId = subscription.Id, RestaurantId = Guid.NewGuid(),
            MenuPlanVersionId = Guid.NewGuid(), ServiceDate = startDate, Quantity = 1,
            Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = now.AddMinutes(-5)
        };
        db.CapacityHolds.Add(hold);
        await db.SaveChangesAsync();

        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var users = Substitute.For<UserManager<ApplicationUser>>(
            store, Substitute.For<IOptions<IdentityOptions>>(), Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(), new IdentityErrorDescriber(), Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        var controller = new CustomerCheckoutController(
            db, currentUser, Substitute.For<IMyFatoorahPaymentService>(), clock,
            users, Substitute.For<IRestaurantEligibilityService>(), Substitute.For<ICapacityConcurrencyLock>(),
            Substitute.For<ICheckoutTransactionExecutor>(), schedule,
            Substitute.For<IRestaurantPoolSnapshotService>(), new ConfigurationBuilder().Build(),
            Substitute.For<IAllowedSubscriptionDurationService>(), Substitute.For<ISubscriptionPaymentProcessor>(),
            Substitute.For<ISubscriptionCheckoutReadinessService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.ResumePreview(subscription.Id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var preview = Assert.IsType<CustomerSubscriptionResumePreviewDto>(ok.Value);

        Assert.False(preview.CanPayDirectly);
        Assert.Equal("capacity_hold_expired", preview.Reason);
    }

    [Fact]
    public async Task ResumePreview_WhenSubscriptionExpired_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 20));

        var startDate = new DateOnly(2026, 8, 24);
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        schedule.TryResolveCheckoutStart(startDate, false, out Arg.Any<DateOnly>(), out Arg.Any<string>())
            .Returns(x => { x[2] = startDate; x[3] = null; return true; });
        schedule.TryResolveCheckoutStart(Arg.Any<DateOnly>(), false, out Arg.Any<DateOnly>(), out Arg.Any<string>())
            .Returns(x => { x[2] = startDate; x[3] = null; return true; });

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "P_EXP", ShortCode = "P_EXP", NameAr = "برنامج", NameEn = "Program",
            DescriptionAr = "وصف", DescriptionEn = "Desc", KeywordsAr = ["كلمات"], KeywordsEn = ["Keys"],
            IsActive = true, Status = ProgramStatus.Active
        };
        var bundle = new MealMate.Domain.ProgramsSubscriptions.Bundle
        {
            ProgramId = program.Id, Code = "B_EXP", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 2, IsActive = true, Status = ProgramStatus.Active
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();

        var subscription = new MealMate.Domain.ProgramsSubscriptions.Subscription
        {
            CustomerUserId = userId, ProgramId = program.Id, BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic, DurationDays = 12, IncludeFriday = false,
            StartDate = startDate, EndDate = startDate.AddDays(12), Status = SubscriptionStatus.Expired,
            PaidAmount = new MealMate.Domain.ValueObjects.Money(50m, "KWD")
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var users = Substitute.For<UserManager<ApplicationUser>>(
            store, Substitute.For<IOptions<IdentityOptions>>(), Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(), new IdentityErrorDescriber(), Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        var controller = new CustomerCheckoutController(
            db, currentUser, Substitute.For<IMyFatoorahPaymentService>(), clock,
            users, Substitute.For<IRestaurantEligibilityService>(), Substitute.For<ICapacityConcurrencyLock>(),
            Substitute.For<ICheckoutTransactionExecutor>(), schedule,
            Substitute.For<IRestaurantPoolSnapshotService>(), new ConfigurationBuilder().Build(),
            Substitute.For<IAllowedSubscriptionDurationService>(), Substitute.For<ISubscriptionPaymentProcessor>(),
            Substitute.For<ISubscriptionCheckoutReadinessService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.ResumePreview(subscription.Id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var preview = Assert.IsType<CustomerSubscriptionResumePreviewDto>(ok.Value);

        Assert.Equal("Expired", preview.Status);
        Assert.False(preview.CanPayDirectly);
    }

    [Fact]
    public async Task ResumeCheckout_WhenSubscriptionExpired_ReplacesHoldsAndReturnsOk()
    {
        var userId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 20));

        var startDate = new DateOnly(2026, 8, 24);
        var schedule = Substitute.For<ISubscriptionScheduleService>();
        schedule.TryResolveCheckoutStart(startDate, false, out Arg.Any<DateOnly>(), out Arg.Any<string>())
            .Returns(x => { x[2] = startDate; x[3] = null; return true; });
        schedule.GetServiceDates(startDate, 12, false)
            .Returns([startDate, startDate.AddDays(1)]);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "P_RC", ShortCode = "P_RC", NameAr = "برنامج", NameEn = "Program",
            DescriptionAr = "وصف", DescriptionEn = "Desc", KeywordsAr = ["كلمات"], KeywordsEn = ["Keys"],
            IsActive = true, Status = ProgramStatus.Active
        };
        var bundle = new MealMate.Domain.ProgramsSubscriptions.Bundle
        {
            ProgramId = program.Id, Code = "B_RC", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 2, IsActive = true, Status = ProgramStatus.Active
        };
        var region = new Region { Code = "R1", NameAr = "منطقة", NameEn = "Region" };
        db.AddRange(program, bundle, region);
        await db.SaveChangesAsync();

        var subscription = new MealMate.Domain.ProgramsSubscriptions.Subscription
        {
            CustomerUserId = userId, ProgramId = program.Id, BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic, DurationDays = 12, IncludeFriday = false,
            StartDate = startDate, EndDate = startDate.AddDays(12), Status = SubscriptionStatus.Expired,
            RegionId = region.Id, DeliveryAddress = "Street 1",
            PaidAmount = new MealMate.Domain.ValueObjects.Money(50m, "KWD")
        };
        db.Subscriptions.Add(subscription);

        // Add previous expired hold
        db.CapacityHolds.Add(new MealMate.Domain.RestaurantOperations.CapacityHold
        {
            RestaurantId = Guid.NewGuid(), SubscriptionId = subscription.Id,
            MenuPlanVersionId = Guid.NewGuid(), ServiceDate = startDate, Quantity = 1,
            Status = CapacityHoldStatus.Expired, ExpiresAtUtc = now.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var users = Substitute.For<UserManager<ApplicationUser>>(
            store, Substitute.For<IOptions<IdentityOptions>>(), Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(), new IdentityErrorDescriber(), Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());
        users.FindByIdAsync(userId.ToString()).Returns(new ApplicationUser { Id = userId, FullName = "Customer", Email = "c@test.com" });

        var allowedDurations = Substitute.For<IAllowedSubscriptionDurationService>();
        allowedDurations.EvaluateAsync(12, false, Arg.Any<CancellationToken>())
            .Returns(new AllowedDurationDecision(true, null, null, false, false, null, null));

        var sender = Substitute.For<ISender>();
        var pricingResult = new PricingEngineResult(0m, 12, 1m, 50m, 0m, 0m, 0m, 50m, 0m, 50m, 30m, 20m, 40m, false, "KWD");
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(pricingResult));

        var tx = Substitute.For<ICheckoutTransactionExecutor>();
        tx.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<CheckoutReservationOutcome>>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var op = call.ArgAt<Func<CancellationToken, Task<CheckoutReservationOutcome>>>(0);
                return op(CancellationToken.None);
            });

        var restaurantId = Guid.NewGuid();
        var menuPlanVersionId = Guid.NewGuid();
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new RestaurantEligibilityDto(
                    restaurantId, "مطعم", "Rest", true, [],
                    [new EligibleMenuDateDto(startDate, menuPlanVersionId)])
            ]);

        var pool = Substitute.For<IRestaurantPoolSnapshotService>();
        pool.CaptureAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RestaurantEligibilityDto>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new RestaurantPoolCaptureResult(true, Guid.NewGuid(), 1));

        var payments = Substitute.For<IMyFatoorahPaymentService>();
        payments.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("https://portal.myfatoorah.com/pay?invoiceKey=test");

        var httpContext = new DefaultHttpContext();
        var services = new ServiceCollection();
        services.AddSingleton(sender);
        httpContext.RequestServices = services.BuildServiceProvider();

        var controller = new CustomerCheckoutController(
            db, currentUser, payments, clock,
            users, eligibility, Substitute.For<ICapacityConcurrencyLock>(),
            tx, schedule, pool, new ConfigurationBuilder().Build(),
            allowedDurations, Substitute.For<ISubscriptionPaymentProcessor>(),
            Substitute.For<ISubscriptionCheckoutReadinessService>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        var result = await controller.ResumeCheckout(subscription.Id, new ResumeCheckoutRequest(startDate), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var started = Assert.IsType<CheckoutStartedDto>(ok.Value);

        Assert.Equal(subscription.Id, started.SubscriptionId);
        Assert.Equal("https://portal.myfatoorah.com/pay?invoiceKey=test", started.PaymentUrl);

        // Verify that the old hold was replaced with the new hold
        var holds = await db.CapacityHolds.Where(x => x.SubscriptionId == subscription.Id).ToListAsync();
        Assert.Single(holds);
        Assert.Equal(CapacityHoldStatus.PendingPayment, holds[0].Status);
        Assert.Equal(restaurantId, holds[0].RestaurantId);
    }

    [Fact]
    public async Task ResumePreview_WhenActiveSubscription_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());

        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "P3", ShortCode = "P3", NameAr = "برنامج", NameEn = "Program",
            DescriptionAr = "وصف", DescriptionEn = "Desc", KeywordsAr = ["كلمات"], KeywordsEn = ["Keys"],
            IsActive = true, Status = ProgramStatus.Active
        };
        var bundle = new MealMate.Domain.ProgramsSubscriptions.Bundle
        {
            ProgramId = program.Id, Code = "B3", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 2, IsActive = true, Status = ProgramStatus.Active
        };
        db.AddRange(program, bundle);
        await db.SaveChangesAsync();

        var subscription = new MealMate.Domain.ProgramsSubscriptions.Subscription
        {
            CustomerUserId = userId, ProgramId = program.Id, BundleId = bundle.Id,
            SelectedTier = RestaurantTier.Basic, DurationDays = 12, IncludeFriday = false,
            StartDate = new DateOnly(2026, 9, 10), EndDate = new DateOnly(2026, 9, 22), Status = SubscriptionStatus.Active,
            PaidAmount = new MealMate.Domain.ValueObjects.Money(50m, "KWD")
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var users = Substitute.For<UserManager<ApplicationUser>>(
            store, Substitute.For<IOptions<IdentityOptions>>(), Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(), Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(), new IdentityErrorDescriber(), Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        var controller = new CustomerCheckoutController(
            db, currentUser, Substitute.For<IMyFatoorahPaymentService>(), Substitute.For<IDateTimeProvider>(),
            users, Substitute.For<IRestaurantEligibilityService>(), Substitute.For<ICapacityConcurrencyLock>(),
            Substitute.For<ICheckoutTransactionExecutor>(), Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IRestaurantPoolSnapshotService>(), new ConfigurationBuilder().Build(),
            Substitute.For<IAllowedSubscriptionDurationService>(), Substitute.For<ISubscriptionPaymentProcessor>(),
            Substitute.For<ISubscriptionCheckoutReadinessService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.ResumePreview(subscription.Id, CancellationToken.None);
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Contains("cannot_resume", conflict.Value?.ToString());
    }

    private static async Task<IActionResult> CheckoutWithExistingSubscriptionAsync()
    {
        var userId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var blockingId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        var items = new[]
        {
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Address, true, "عنوان", "Address", "ADDRESS", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.Allergies, true, "حساسية", "Allergies", "ALLERGIES", "e"),
            new SubscriptionProfileGateItemDto(SubscriptionCheckoutGateCodes.DietaryPreferences, true, "تفضيلات", "Dietary", "DIETARY_PREFERENCES", "e")
        };

        var readiness = Substitute.For<ISubscriptionCheckoutReadinessService>();
        readiness.EvaluateAsync(userId, programId, bundleId, RestaurantTier.Basic, 26, false, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionCheckoutReadinessDto(
                true, false, false,
                SubscriptionCheckoutGateCodes.Summary,
                "w", "w",
                SubscriptionCheckoutGateCodes.ExistingSubscription,
                new SubscriptionReadinessProgramDto(programId, "كيتو", "Keto"),
                new SubscriptionSummaryAddressDto(Guid.NewGuid(), "المنزل", "شارع", null, null, null, Guid.NewGuid()),
                0, items, [],
                blockingId,
                nameof(SubscriptionStatus.Active)));

        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var users = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());
        users.FindByIdAsync(userId.ToString()).Returns(new ApplicationUser { Id = userId });

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var controller = new CustomerCheckoutController(
            db, currentUser, Substitute.For<IMyFatoorahPaymentService>(), Substitute.For<IDateTimeProvider>(),
            users, Substitute.For<IRestaurantEligibilityService>(), Substitute.For<ICapacityConcurrencyLock>(),
            Substitute.For<ICheckoutTransactionExecutor>(), Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IRestaurantPoolSnapshotService>(), new ConfigurationBuilder().Build(),
            Substitute.For<IAllowedSubscriptionDurationService>(), Substitute.For<ISubscriptionPaymentProcessor>(),
            readiness)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return await controller.Checkout(
            new StartCheckoutRequest(programId, bundleId, RestaurantTier.Basic, 26, "mealmate://ok", "mealmate://err", DateOnly.FromDateTime(DateTime.UtcNow)),
            CancellationToken.None);
    }

    private static async Task<IActionResult> CheckoutWithItemsAsync(params SubscriptionProfileGateItemDto[] items)
    {
        var userId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);

        var readiness = Substitute.For<ISubscriptionCheckoutReadinessService>();
        readiness.EvaluateAsync(userId, programId, bundleId, RestaurantTier.Basic, 26, false, Arg.Any<CancellationToken>())
            .Returns(new SubscriptionCheckoutReadinessDto(
                false, false, false, items.First(x => !x.IsComplete).Code, "w", "w",
                SubscriptionCheckoutGateCodes.ProfileCheckoutBlockCode(new SubscriptionCheckoutReadinessDto(
                    false, false, false, "ADDRESS", "w", "w", null,
                    new SubscriptionReadinessProgramDto(programId, "كيتو", "Keto"), null, 0, items, [])) ?? "checkout.address_required",
                new SubscriptionReadinessProgramDto(programId, "كيتو", "Keto"),
                items.First(x => x.Code == SubscriptionCheckoutGateCodes.Address).IsComplete
                    ? new SubscriptionSummaryAddressDto(Guid.NewGuid(), "المنزل", "شارع", null, null, null, Guid.NewGuid())
                    : null,
                0, items, []));

        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var users = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());
        users.FindByIdAsync(userId.ToString()).Returns(new ApplicationUser { Id = userId });

        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var controller = new CustomerCheckoutController(
            db, currentUser, Substitute.For<IMyFatoorahPaymentService>(), Substitute.For<IDateTimeProvider>(),
            users, Substitute.For<IRestaurantEligibilityService>(), Substitute.For<ICapacityConcurrencyLock>(),
            Substitute.For<ICheckoutTransactionExecutor>(), Substitute.For<ISubscriptionScheduleService>(),
            Substitute.For<IRestaurantPoolSnapshotService>(), new ConfigurationBuilder().Build(),
            Substitute.For<IAllowedSubscriptionDurationService>(), Substitute.For<ISubscriptionPaymentProcessor>(),
            readiness)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        return await controller.Checkout(
            new StartCheckoutRequest(programId, bundleId, RestaurantTier.Basic, 26, "mealmate://ok", "mealmate://err", DateOnly.FromDateTime(DateTime.UtcNow)),
            CancellationToken.None);
    }

    private static void AssertConflict(IActionResult result, string expected)
    {
        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var error = conflict.Value!.GetType().GetProperty("error")?.GetValue(conflict.Value)?.ToString();
        Assert.Equal(expected, error);
    }
}
