using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Infrastructure.Email;
using MealMate.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class SandboxCheckoutTests
{
    [Fact]
    public async Task ExplicitSandboxMode_ReturnsCompletionOptionWithoutCallingProvider()
    {
        await using var db = CreateDb();
        db.PayzahGatewayConfigs.Add(new PayzahGatewayConfig(true, "https://development.payzah.net/ws/paymentgateway", "", ""));
        db.SystemSettings.Add(new SystemSetting { Key = "checkout.sandbox_completion",
            ValueJson = "{\"enabled\":true,\"publicBaseUrl\":\"https://checkout.example.org\"}" });
        await db.SaveChangesAsync();
        var payments = Substitute.For<IPaymentGatewayService>();
        payments.GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<PaymentGatewayMethod>>(new HttpRequestException("Provider unavailable")));
        var queries = new SubscriptionFlowQueryService(db,
            Substitute.For<IRestaurantEligibilityService>(), Substitute.For<IMenuPlanResolutionService>(),
            Substitute.For<ISubscriptionScheduleService>(), Substitute.For<IBundleSlotRequirementService>(), payments,
            Substitute.For<IAllowedSubscriptionDurationService>(), Substitute.For<IRestaurantTierClassificationService>(),
            Substitute.For<ISubscriptionCheckoutReadinessService>(), Substitute.For<ICustomerIndividualSubscriptionGate>(),
            Substitute.For<IRestaurantDiversificationCalculator>(), Substitute.For<ISender>());

        var result = await queries.GetPaymentMethodsAsync(118.25m);

        Assert.True(result.IsSuccess);
        var method = Assert.Single(result.Value);
        Assert.Equal("SANDBOX_WITHOUT_PAYMENT", method.Code);
        Assert.Equal(118.25m, method.TotalAmount);
        Assert.Contains("تجربة", method.NameAr);
        await payments.DidNotReceive().GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>());

        var familyMethods = await queries.GetPaymentMethodsAsync(118.25m, allowSandboxCompletion: false);
        Assert.True(familyMethods.IsFailure);
        await payments.Received(1).GetPaymentMethodsAsync(118.25m, Arg.Any<CancellationToken>());
    }

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CompletionIsDisabledUnlessBothExplicitSwitchesAreEnabled(bool sandbox, bool enabled)
    {
        await using var db = CreateDb();
        Configure(db, sandbox, enabled);
        await db.SaveChangesAsync();
        Assert.Null(await SandboxCheckoutPolicy.ReadAsync(db));
    }

    [Fact]
    public async Task ConfirmedCheckout_FulfillsPersistedReservationOnceWithoutProviderReceipt()
    {
        await using var db = CreateDb();
        var (service, subscription, clock) = await CreateCheckoutAsync(db);
        var token = Token(await service.StartAsync(subscription.Id, subscription.CustomerUserId, clock.UtcNow.AddMinutes(10), "mealmate://checkout/success"));

        var preview = await service.ReadPageAsync(token);
        Assert.True(preview.IsSuccess);
        Assert.Equal(SubscriptionStatus.PendingPayment, subscription.Status);
        Assert.Empty(await db.SubscriptionDays.ToListAsync());

        Assert.True((await service.CompleteAsync(token)).IsSuccess);
        Assert.True((await service.CompleteAsync(token)).IsSuccess);
        Assert.Equal(SubscriptionStatus.Scheduled, subscription.Status);
        Assert.Single(await db.SubscriptionDays.ToListAsync());
        Assert.Single(await db.SubscriptionOperations.Where(x => x.OperationType == "SANDBOX_CHECKOUT_CONFIRMED").ToListAsync());
        Assert.Empty(await db.PaymentWebhookReceipts.ToListAsync());
        Assert.Null(subscription.PaymentId);
        Assert.Null(subscription.PaymentInvoiceId);
        Assert.Null(subscription.PaymentVerifiedAtUtc);
    }

    [Theory]
    [InlineData("expired-link")]
    [InlineData("expired-hold")]
    [InlineData("tampered-link")]
    [InlineData("live-mode")]
    [InlineData("disabled-mode")]
    public async Task InvalidOrDisabledCompletion_DoesNotActivateSubscription(string rejection)
    {
        await using var db = CreateDb();
        var (service, subscription, clock) = await CreateCheckoutAsync(db);
        var token = Token(await service.StartAsync(subscription.Id, subscription.CustomerUserId, clock.UtcNow.AddMinutes(10), "mealmate://checkout/success"));
        if (rejection == "expired-link") clock.UtcNow.Returns(clock.UtcNow.AddMinutes(11));
        if (rejection == "expired-hold") (await db.CapacityHolds.SingleAsync()).ExpiresAtUtc = clock.UtcNow.AddSeconds(-1);
        if (rejection == "tampered-link") token = "!" + token;
        if (rejection == "live-mode")
        {
            var config = await db.PayzahGatewayConfigs.SingleAsync();
            config.Update(false, "https://payzah.net/production770/ws/paymentgateway", "", "");
        }
        if (rejection == "disabled-mode") (await db.SystemSettings.SingleAsync()).IsActive = false;
        await db.SaveChangesAsync();

        Assert.True((await service.CompleteAsync(token)).IsFailure);
        Assert.Equal(SubscriptionStatus.PendingPayment, subscription.Status);
        Assert.Empty(await db.SubscriptionDays.ToListAsync());
        Assert.DoesNotContain(await db.SubscriptionOperations.ToListAsync(), x => x.OperationType == "SANDBOX_CHECKOUT_CONFIRMED");
    }

    [Fact]
    public async Task TicketCanOnlyBeIssuedToSubscriptionOwner()
    {
        await using var db = CreateDb();
        var (service, subscription, clock) = await CreateCheckoutAsync(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(subscription.Id, Guid.NewGuid(), clock.UtcNow.AddMinutes(10), "mealmate://checkout/success"));
        Assert.Empty(await db.SubscriptionOperations.ToListAsync());
    }

    [Fact]
    public async Task OldTicketCannotConfirmReplacementReservation()
    {
        await using var db = CreateDb();
        var (service, subscription, clock) = await CreateCheckoutAsync(db);
        var oldToken = Token(await service.StartAsync(subscription.Id, subscription.CustomerUserId, clock.UtcNow.AddMinutes(10), "mealmate://checkout/success"));
        var hold = await db.CapacityHolds.SingleAsync();
        db.CapacityHolds.Remove(hold);
        db.CapacityHolds.Add(new CapacityHold { SubscriptionId = subscription.Id, RestaurantId = hold.RestaurantId,
            MenuPlanVersionId = hold.MenuPlanVersionId, ServiceDate = hold.ServiceDate, Quantity = hold.Quantity,
            Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = clock.UtcNow.AddMinutes(10) });
        await db.SaveChangesAsync();
        Assert.True((await service.CompleteAsync(oldToken)).IsFailure);
        Assert.Equal(SubscriptionStatus.PendingPayment, subscription.Status);
        var newToken = Token(await service.StartAsync(subscription.Id, subscription.CustomerUserId, clock.UtcNow.AddMinutes(10), "mealmate://checkout/success"));
        Assert.True((await service.CompleteAsync(newToken)).IsSuccess);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task ProviderPollingResumesWhenSandboxModeIsDisabled(bool enabled, int expectedProviderCalls)
    {
        await using var db = CreateDb();
        var (_, subscription, clock) = await CreateCheckoutAsync(db);
        var sandbox = Substitute.For<ISandboxCheckoutService>();
        sandbox.IsEnabledAsync(Arg.Any<CancellationToken>()).Returns(enabled);
        sandbox.HasStartedAsync(subscription.Id, Arg.Any<CancellationToken>()).Returns(true);
        var provider = Substitute.For<IPaymentGatewayService>();
        var checkout = new CustomerCheckoutService(db, provider, clock, Substitute.For<IUserAccountService>(),
            Substitute.For<IRestaurantEligibilityService>(), Substitute.For<ICapacityConcurrencyLock>(), new InlineTransaction(),
            new SubscriptionScheduleService(clock), Substitute.For<IRestaurantPoolSnapshotService>(), null,
            Substitute.For<IAllowedSubscriptionDurationService>(), Substitute.For<ISubscriptionPaymentProcessor>(),
            Substitute.For<ISubscriptionCheckoutReadinessService>(), Substitute.For<ISender>(),
            Substitute.For<IRestaurantCapacityReader>(), sandbox);

        Assert.True((await checkout.GetPaymentStatusAsync(subscription.Id, subscription.CustomerUserId)).IsSuccess);
        await provider.Received(expectedProviderCalls).GetPaymentStatusAsync(subscription.Id.ToString(), "CustomerReference", Arg.Any<CancellationToken>());
    }

    private static void Configure(AppDbContext db, bool sandbox = true, bool enabled = true)
    {
        db.PayzahGatewayConfigs.Add(new PayzahGatewayConfig(sandbox, sandbox ? "https://development.payzah.net/ws/paymentgateway" : "https://payzah.net/production770/ws/paymentgateway", "", ""));
        db.SystemSettings.Add(new SystemSetting { Key = "checkout.sandbox_completion",
            ValueJson = System.Text.Json.JsonSerializer.Serialize(new { enabled, publicBaseUrl = "https://checkout.example.org" }) });
    }

    private static async Task<(SandboxCheckoutService, Subscription, IDateTimeProvider)> CreateCheckoutAsync(AppDbContext db)
    {
        Configure(db);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
        var subscription = new Subscription { CustomerUserId = Guid.NewGuid(), ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 10, 3), EndDate = new DateOnly(2026, 10, 3), DurationDays = 1,
            SelectedTier = RestaurantTier.Basic, PaidAmount = new Money(118.25m, "KWD"), Status = SubscriptionStatus.PendingPayment };
        db.Subscriptions.Add(subscription);
        var restaurantId = Guid.NewGuid();
        var pool = new SubscriptionRestaurantPoolSnapshot { SubscriptionId = subscription.Id, RestaurantCount = 1, EligibilityCheckedAtUtc = clock.UtcNow };
        var restaurant = new SubscriptionRestaurantSnapshot { PoolSnapshotId = pool.Id, SubscriptionId = subscription.Id, RestaurantId = restaurantId,
            ProgramId = subscription.ProgramId, BundleId = subscription.BundleId, RegionId = Guid.NewGuid(), Tier = subscription.SelectedTier,
            NameAr = "مطعم", NameEn = "Restaurant", IsInitiallySelected = true, OfferingSnapshotJson = "{}", CapturedAtUtc = clock.UtcNow };
        db.SubscriptionRestaurantPoolSnapshots.Add(pool); db.SubscriptionRestaurantSnapshots.Add(restaurant);
        const string slots = "[{\"slot\":\"lunch\",\"count\":1}]";
        var meals = System.Text.Json.JsonSerializer.Serialize(new[] { new { mealId = Guid.NewGuid(), nameAr = "وجبة", nameEn = "Meal", slot = "lunch", sortOrder = 0,
            presentAllergenCodes = Array.Empty<string>(), mayContainAllergenCodes = Array.Empty<string>(), dietaryPreferenceCodes = Array.Empty<string>(), ingredients = Array.Empty<object>() } });
        var versionId = Guid.NewGuid();
        db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot { RestaurantSnapshotId = restaurant.Id, SubscriptionId = subscription.Id,
            RestaurantId = restaurantId, ServiceDate = subscription.StartDate, MenuPlanVersionId = versionId, SlotRequirementsJson = slots, MealsJson = meals,
            ContentHash = FrozenSnapshotIntegrity.ComputeContentHash(slots, meals), CapturedAtUtc = clock.UtcNow });
        db.CapacityHolds.Add(new CapacityHold { SubscriptionId = subscription.Id, RestaurantId = restaurantId, MenuPlanVersionId = versionId,
            ServiceDate = subscription.StartDate, Quantity = 1, Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = clock.UtcNow.AddMinutes(10) });
        await db.SaveChangesAsync();
        var protector = new EmailSecretProtector(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Email:SecretsEncryptionKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) }).Build());
        var transaction = new InlineTransaction(); var capacityLock = Substitute.For<ICapacityConcurrencyLock>();
        var fulfillment = new SubscriptionFulfillmentService(db, transaction, capacityLock, new SubscriptionScheduleService(clock), clock);
        return (new SandboxCheckoutService(db, protector, clock, transaction, capacityLock, fulfillment), subscription, clock);
    }
    private static string Token(string url) => Uri.UnescapeDataString(url[(url.IndexOf("?token=", StringComparison.Ordinal) + 7)..]);
    private sealed class InlineTransaction : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }
}
