using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.CustomerCheckout;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.CustomerCheckout;

public sealed class CapacityReservationConcurrencyPostgresTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await _container.StartAsync();
            await using var db = CreateDb();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker is required for checkout concurrency integration tests.", ex);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Two_checkouts_for_last_capacity_allow_exactly_one_hold()
    {
        var restaurantId = Guid.NewGuid();
        var serviceDate = new DateOnly(2026, 8, 20);
        var menuVersionId = Guid.NewGuid();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> ReserveAsync()
        {
            await using var db = CreateDb();
            var transaction = new PostgresCheckoutTransactionExecutor(db);
            var capacityLock = new PostgresCapacityConcurrencyLock(db);
            await start.Task;
            return await transaction.ExecuteAsync(async ct =>
            {
                await capacityLock.AcquireAsync(restaurantId, [serviceDate], ct);
                var reserved = await db.CapacityHolds.CountAsync(x => x.RestaurantId == restaurantId && x.ServiceDate == serviceDate
                    && ((x.Status == CapacityHoldStatus.PendingPayment && x.ExpiresAtUtc > DateTime.UtcNow)
                        || x.Status == CapacityHoldStatus.PaymentConfirmed), ct);
                if (reserved >= 1) return false;
                db.CapacityHolds.Add(new CapacityHold
                {
                    RestaurantId = restaurantId,
                    SubscriptionId = Guid.NewGuid(),
                    MenuPlanVersionId = menuVersionId,
                    ServiceDate = serviceDate,
                    Quantity = 1,
                    Status = CapacityHoldStatus.PendingPayment,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
                });
                await db.SaveChangesAsync(ct);
                return true;
            });
        }

        var first = ReserveAsync();
        var second = ReserveAsync();
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, x => x);
        await using var verificationDb = CreateDb();
        Assert.Equal(1, await verificationDb.CapacityHolds.CountAsync(x => x.RestaurantId == restaurantId && x.ServiceDate == serviceDate));
    }

    [Fact]
    public async Task Concurrent_payment_webhooks_are_idempotent_and_confirm_holds_once()
    {
        var restaurantId = Guid.NewGuid();
        var paymentId = "PAY-" + Guid.NewGuid().ToString("N");
        var invoiceId = "INV-" + Guid.NewGuid().ToString("N");
        var serviceDate = new DateOnly(2026, 8, 20);
        var menuVersionId = Guid.NewGuid();
        var subscriptionId = await SeedPendingSubscriptionAsync(restaurantId, serviceDate, menuVersionId);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<PaymentProcessingResult> ProcessAsync()
        {
            await using var db = CreateDb();
            var clock = Substitute.For<IDateTimeProvider>();
            var now = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);
            clock.UtcNow.Returns(now);
            var gateway = Substitute.For<IMyFatoorahPaymentService>();
            gateway.GetPaymentStatusAsync(paymentId, "PaymentId", Arg.Any<CancellationToken>()).Returns(
                new MyFatoorahPaymentStatusResponse(invoiceId, "Paid", 25m, "Customer", "customer@example.com", "50000000", "TX-1", "KNET", now,
                    subscriptionId.ToString(), "KWD", paymentId));
            var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
            fulfillment.TryFulfillAsync(subscriptionId, Arg.Any<CancellationToken>()).Returns(new FulfillmentResult(false, false, "test.deferred"));
            var processor = new SubscriptionPaymentProcessor(db, gateway, new PostgresCheckoutTransactionExecutor(db),
                new PostgresCapacityConcurrencyLock(db), new SubscriptionScheduleService(clock), fulfillment, clock);
            await start.Task;
            return await processor.VerifyAndProcessAsync(subscriptionId, paymentId, invoiceId, 25m, "KWD", subscriptionId.ToString());
        }

        var first = ProcessAsync();
        var second = ProcessAsync();
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, x => Assert.True(x.IsAccepted));
        Assert.Single(results, x => x.IsDuplicate);
        await using var verificationDb = CreateDb();
        Assert.Equal(1, await verificationDb.PaymentWebhookReceipts.CountAsync(x => x.PaymentId == paymentId));
        Assert.Equal(SubscriptionStatus.Paid, await verificationDb.Subscriptions.Where(x => x.Id == subscriptionId).Select(x => x.Status).SingleAsync());
        Assert.Equal(CapacityHoldStatus.PaymentConfirmed, await verificationDb.CapacityHolds.Where(x => x.SubscriptionId == subscriptionId).Select(x => x.Status).SingleAsync());
    }

    private async Task<Guid> SeedPendingSubscriptionAsync(Guid restaurantId, DateOnly serviceDate, Guid menuVersionId)
    {
        await using var db = CreateDb();
        var program = new Program { Code = "P-" + Guid.NewGuid().ToString("N"), NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active };
        var bundle = new Bundle { Program = program, Code = "B-" + Guid.NewGuid().ToString("N"), NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active };
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(), Program = program, Bundle = bundle,
            SelectedTier = RestaurantTier.Basic, DurationDays = 1, IncludeFriday = true,
            StartDate = serviceDate, EndDate = serviceDate, Status = SubscriptionStatus.PendingPayment,
            PaidAmount = new Money(25m, "KWD")
        };
        var subscriptionId = subscription.Id;
        var pool = new SubscriptionRestaurantPoolSnapshot { SubscriptionId = subscriptionId, RestaurantCount = 1, EligibilityCheckedAtUtc = DateTime.UtcNow };
        var restaurant = new SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = pool.Id, SubscriptionId = subscriptionId, RestaurantId = restaurantId,
            ProgramId = program.Id, BundleId = bundle.Id, RegionId = Guid.NewGuid(), Tier = RestaurantTier.Basic,
            NameAr = "مطعم", NameEn = "Restaurant", IsInitiallySelected = true, OfferingSnapshotJson = "{}", CapturedAtUtc = DateTime.UtcNow
        };
        db.Subscriptions.Add(subscription);
        db.SubscriptionRestaurantPoolSnapshots.Add(pool);
        db.SubscriptionRestaurantSnapshots.Add(restaurant);
        db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot
        {
            RestaurantSnapshotId = restaurant.Id, SubscriptionId = subscriptionId, RestaurantId = restaurantId,
            ServiceDate = serviceDate, MenuPlanVersionId = menuVersionId, SlotRequirementsJson = "[]", MealsJson = "[]",
            ContentHash = new string('a', 64), CapturedAtUtc = DateTime.UtcNow
        });
        db.CapacityHolds.Add(new CapacityHold
        {
            SubscriptionId = subscriptionId, RestaurantId = restaurantId, ServiceDate = serviceDate, MenuPlanVersionId = menuVersionId,
            Quantity = 1, Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = new DateTime(2026, 8, 13, 9, 15, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
        return subscriptionId;
    }

    private AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>());
}
