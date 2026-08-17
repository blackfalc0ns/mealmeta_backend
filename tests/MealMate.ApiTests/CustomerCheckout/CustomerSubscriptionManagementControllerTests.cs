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

    [Fact]
    public async Task Current_ReturnsActiveSubscriptionWithoutInvoices()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).Current(CancellationToken.None));
        var screen = Assert.IsType<CustomerCurrentSubscriptionScreenDto>(result.Value);

        Assert.NotNull(screen.Subscription);
        Assert.Equal(subscription.Id, screen.Subscription.SubscriptionId);
        Assert.Equal(nameof(SubscriptionStatus.Active), screen.Subscription.Status);
        Assert.Null(screen.EmptyStateCode);
    }

    [Fact]
    public async Task Current_PrefersActiveOverPendingPayment()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var active = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        var pending = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.PendingPayment);
        db.AddRange(program, bundle, active, pending);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).Current(CancellationToken.None));
        var screen = Assert.IsType<CustomerCurrentSubscriptionScreenDto>(result.Value);
        Assert.Equal(active.Id, screen.Subscription!.SubscriptionId);
    }

    [Fact]
    public async Task Current_MapsSuspendedForReviewToPaidForCustomer()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.SuspendedForReview);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).Current(CancellationToken.None));
        var screen = Assert.IsType<CustomerCurrentSubscriptionScreenDto>(result.Value);

        Assert.NotNull(screen.Subscription);
        Assert.Equal(nameof(SubscriptionStatus.Paid), screen.Subscription.Status);
        Assert.Equal("تم الدفع", screen.Subscription.StatusNameAr);
        Assert.False(screen.Subscription.Actions.CanUpgrade);
        Assert.False(screen.Subscription.Actions.CanFreeze);
        Assert.Equal(SubscriptionStatus.SuspendedForReview, (await db.Subscriptions.FindAsync(subscription.Id))!.Status);
    }

    [Fact]
    public async Task Current_DoesNotExposeAnotherCustomersSubscription()
    {
        await using var db = CreateDb();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(Guid.NewGuid(), program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, Guid.NewGuid()).Current(CancellationToken.None));
        var screen = Assert.IsType<CustomerCurrentSubscriptionScreenDto>(result.Value);
        Assert.Null(screen.Subscription);
        Assert.Equal("no_current_subscription", screen.EmptyStateCode);
    }

    [Fact]
    public async Task List_FiltersToCurrentCustomer()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var first = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        var second = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Completed);
        var other = NewSubscription(otherCustomerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, first, second, other);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).List(true, CancellationToken.None));
        var list = Assert.IsAssignableFrom<IReadOnlyList<CustomerSubscriptionSlimDto>>(result.Value);
        Assert.Equal(2, list.Count);
        Assert.All(list, x => Assert.Equal(customerId, db.Subscriptions.Find(x.SubscriptionId)!.CustomerUserId));
    }

    [Fact]
    public async Task Invoices_ReturnsOnlyOwnedReceiptsNewestFirst()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var ownedSubscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        var otherSubscription = NewSubscription(otherCustomerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, ownedSubscription, otherSubscription);
        db.PaymentWebhookReceipts.AddRange(
            NewReceipt(ownedSubscription.Id, "MyFatoorah", new DateTime(2026, 8, 8, 10, 0, 0, DateTimeKind.Utc), "inv-owned-1"),
            NewReceipt(ownedSubscription.Id, "MyFatoorah", new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc), "inv-owned-2"),
            NewReceipt(otherSubscription.Id, "MyFatoorah", new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc), "inv-other"));
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).Invoices(CancellationToken.None));
        var invoices = Assert.IsAssignableFrom<IReadOnlyList<CustomerSubscriptionInvoiceListDto>>(result.Value);
        Assert.Equal(2, invoices.Count);
        Assert.Equal("inv-owned-2", invoices[0].InvoiceNumber);
        Assert.Equal("inv-owned-1", invoices[1].InvoiceNumber);
    }

    [Fact]
    public async Task InvoiceDetail_ReturnsNotFoundForOtherCustomer()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var ownedSubscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        var otherSubscription = NewSubscription(otherCustomerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, ownedSubscription, otherSubscription);
        var receipt = NewReceipt(otherSubscription.Id, "MyFatoorah", new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc), "inv-other");
        db.PaymentWebhookReceipts.Add(receipt);
        await db.SaveChangesAsync();

        var result = await CreateController(db, customerId).InvoiceDetail(receipt.Id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DefaultPaymentMethod_DoesNotInventCardDigits()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active);
        db.AddRange(program, bundle, subscription);
        db.PaymentWebhookReceipts.Add(NewReceipt(subscription.Id, "Tap", new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc), "inv-1"));
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).DefaultPaymentMethod(CancellationToken.None));
        var method = Assert.IsType<CustomerDefaultPaymentMethodDto>(result.Value);

        Assert.False(method.HasSavedMethod);
        Assert.Null(method.Last4);
        Assert.Null(method.Expiry);
        Assert.Null(method.Brand);
        Assert.Equal("saved_card_not_available", method.EmptyStateCode);
        Assert.Equal("Tap", method.Gateway);
    }

    [Fact]
    public async Task FreezePolicy_ReturnsDefaultMessages()
    {
        await using var db = CreateDb();
        var controller = CreateController(db, Guid.NewGuid());

        var result = Assert.IsType<OkObjectResult>(await controller.FreezePolicy(CancellationToken.None));
        var policy = Assert.IsType<CustomerSubscriptionFreezePolicyDto>(result.Value);

        Assert.Equal(7, policy.DefaultDurationDays);
        Assert.Equal(3, policy.StartDelayDays);
        Assert.True(policy.ExtendSubscriptionEnd);
        Assert.Contains("7", policy.MessageAr);
        Assert.Contains("3", policy.MessageAr);
        Assert.Contains("7", policy.MessageEn);
        Assert.Contains("3", policy.MessageEn);
    }

    [Fact]
    public async Task UpgradeOptions_OnlyHigherTiers()
    {
        await using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var program = NewProgram();
        var bundle = NewBundle(program.Id);
        var subscription = NewSubscription(customerId, program.Id, bundle.Id, SubscriptionStatus.Active, RestaurantTier.Basic);
        db.AddRange(program, bundle, subscription);
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await CreateController(db, customerId).UpgradeOptions(subscription.Id, CancellationToken.None));
        var options = Assert.IsType<CustomerUpgradeOptionsDto>(result.Value);

        Assert.True(options.CanUpgrade);
        Assert.Null(options.UnavailableReason);
        Assert.Equal(RestaurantTier.Basic, options.CurrentTier);
        Assert.Equal(3, options.Options.Count);
        Assert.Equal(RestaurantTier.Basic, options.Options[0].TargetTier);
        Assert.Equal(RestaurantTier.Platinum, options.Options[1].TargetTier);
        Assert.Equal(RestaurantTier.Elite, options.Options[2].TargetTier);
        Assert.Equal(0m, options.Options[0].PriceDifference);
        Assert.True(options.Options[1].PriceDifference > 0);
        Assert.True(options.Options[2].PriceDifference > options.Options[1].PriceDifference);
        Assert.Equal($"/api/v1/customer/subscriptions/{subscription.Id}/upgrade-requests", options.UpgradeEndpoint);
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

    private static Subscription NewSubscription(Guid customerId, Guid programId, Guid bundleId, SubscriptionStatus status, RestaurantTier tier = RestaurantTier.Platinum) => new()
    {
        CustomerUserId = customerId, ProgramId = programId, BundleId = bundleId, SelectedTier = tier,
        DurationDays = 2, StartDate = new DateOnly(2026, 8, 10), EndDate = new DateOnly(2026, 8, 15), Status = status,
        PaidAmount = new Money(49.99m, "KWD")
    };

    private static PaymentWebhookReceipt NewReceipt(Guid subscriptionId, string gateway, DateTime processedAtUtc, string invoiceId, string status = "PaymentVerified") => new()
    {
        Gateway = gateway,
        EventKey = $"evt-{Guid.NewGuid()}",
        SubscriptionId = subscriptionId,
        Status = status,
        PaymentId = $"pay-{Guid.NewGuid()}",
        InvoiceId = invoiceId,
        Amount = 49.99m,
        CurrencyCode = "KWD",
        ProcessedAtUtc = processedAtUtc,
        VerifiedAtUtc = processedAtUtc
    };

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());
}
