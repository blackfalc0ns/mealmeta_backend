using MealMate.Api.Controllers.V1;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.ApiTests.CustomerCheckout;

public sealed class CustomerSubscriptionManagementControllerTests
{
    [Fact]
    public async Task Management_ReturnsOnlyCurrentCustomersSubscriptionsWithProgressAndReceipts()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription, NewSubscription(otherCustomerId, program.Id, bundle.Id, SubscriptionStatus.Active));
        db.SubscriptionDays.AddRange(
            new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = new DateOnly(2026, 8, 10), Status = SubscriptionDayStatus.Delivered },
            new MealMate.Domain.CalendarMeals.SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = new DateOnly(2026, 8, 15), Status = SubscriptionDayStatus.Open });
        db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
        {
            Gateway = "MyFatoorah", EventKey = "evt-1", SubscriptionId = subscription.Id, Status = "Paid", PaymentId = "pay-1",
            InvoiceId = "inv-1", Amount = 49.99m, CurrencyCode = "KWD", ProcessedAtUtc = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc),
            VerifiedAtUtc = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, customerId);
        var result = Assert.IsType<OkObjectResult>(await controller.Management(true, CancellationToken.None));
        var screen = Assert.IsType<CustomerSubscriptionManagementScreenDto>(result.Value);

        Assert.Single(screen.Subscriptions);
        Assert.NotNull(screen.CurrentSubscription);
        Assert.Equal(1, screen.CurrentSubscription.CompletedServiceDays);
        Assert.Equal(1, screen.CurrentSubscription.RemainingServiceDays);
        Assert.Equal(50m, screen.CurrentSubscription.ProgressPercentage);
        Assert.Single(screen.Invoices);
        Assert.Null(screen.Invoices[0].MaskedCardNumber);
        Assert.False(screen.Invoices[0].CanDownload);
    }

    [Fact]
    public async Task CancellationRequest_IsIdempotentAndDoesNotChangeSubscriptionStatus()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();
        var controller = CreateController(db, customerId);

        var first = Assert.IsType<AcceptedResult>(await controller.RequestCancellation(subscription.Id, new CustomerCancellationRequest("travel", null), CancellationToken.None));
        var second = Assert.IsType<AcceptedResult>(await controller.RequestCancellation(subscription.Id, new CustomerCancellationRequest("duplicate", null), CancellationToken.None));

        Assert.Equal(((CustomerCancellationRequestDto)first.Value!).RequestId, ((CustomerCancellationRequestDto)second.Value!).RequestId);
        Assert.Single(db.SubscriptionOperations.Where(x => x.SubscriptionId == subscription.Id));
        Assert.Equal(SubscriptionStatus.Active, (await db.Subscriptions.FindAsync(subscription.Id))!.Status);
    }

    [Fact]
    public async Task ManagementDetails_DoesNotExposeAnotherCustomersSubscription()
    {
        await using var db = CreateDb();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(Guid.NewGuid(), program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var result = await CreateController(db, Guid.NewGuid()).ManagementDetails(subscription.Id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Management_PrefersActiveSubscriptionOverNewPendingCheckout()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var active = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        var pending = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.PendingPayment);
        db.AddRange(program, bundle, active, pending);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).Management(true, CancellationToken.None));
        var screen = Assert.IsType<CustomerSubscriptionManagementScreenDto>(result.Value);
        Assert.Equal(active.Id, screen.CurrentSubscription!.SubscriptionId);
    }

    private static CustomerSubscriptionManagementController CreateController(AppDbContext db, Guid customerId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(customerId);
        user.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(new DateOnly(2026, 8, 13));
        return new CustomerSubscriptionManagementController(db, user, clock)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static MealMate.Domain.ProgramsSubscriptions.Program NewProgram() => new()
    {
        Code = "WEIGHT", NameAr = "خسارة الوزن", NameEn = "Weight loss", Status = ProgramStatus.Active, IsActive = true
    };

    private static Bundle NewBundle(Guid programId) => new()
    {
        ProgramId = programId, Code = "FOUR", NameAr = "باقة 4 وجبات", NameEn = "4 meals", MealsPerDay = 4,
        BreakfastCount = 1, LunchCount = 1, DinnerCount = 1, SnackCount = 1, Status = ProgramStatus.Active, IsActive = true
    };

    private static Subscription NewSubscription(Guid customerId, Guid programId, Guid bundleId, SubscriptionStatus status) => new()
    {
        CustomerUserId = customerId, ProgramId = programId, BundleId = bundleId, SelectedTier = RestaurantTier.Platinum,
        DurationDays = 2, StartDate = new DateOnly(2026, 8, 10), EndDate = new DateOnly(2026, 8, 15), Status = status,
        PaidAmount = new Money(49.99m, "KWD")
    };

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());
}
