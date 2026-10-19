using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.CustomerCheckout;

public sealed class IndividualCheckoutIdempotencyPostgresTests : IAsyncLifetime
{
    private MealMate.Testing.PostgresTestDatabase _database = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _database = new();
            await _database.StartAsync();
            await using var db = CreateDb();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker or an explicitly configured local PostgreSQL test database is required.", ex);
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is not null) await _database.DisposeAsync();
    }

    [Fact]
    public async Task Same_key_is_owned_and_payload_mismatch_conflicts()
    {
        var customer = Guid.NewGuid();
        var request = Request(quoteId: Guid.NewGuid());
        await using var db = CreateDb();
        var service = new CheckoutIdempotencyService(db, Clock());
        await service.ClaimAsync(customer, request);
        var sameAttempt = await service.InspectAsync(customer, request);
        var changedPayload = await service.InspectAsync(customer, request with { PaymentMethodId = 8 });
        var otherCustomer = await service.InspectAsync(Guid.NewGuid(), request);

        Assert.Equal(CheckoutAttemptState.InProgress, sameAttempt.State);
        Assert.Equal(CheckoutAttemptState.Conflict, changedPayload.State);
        Assert.Equal(CheckoutAttemptState.Unavailable, otherCustomer.State);
        Assert.Null(otherCustomer.SubscriptionId);
    }

    [Fact]
    public async Task Completed_attempt_replays_original_response_after_quote_expiry_and_consumption()
    {
        var customer = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        var request = Request(quoteId);
        var original = Started();
        await using var db = CreateDb();
        var service = new CheckoutIdempotencyService(db, Clock());

        db.OfferCheckoutQuotes.Add(new OfferCheckoutQuote(quoteId)
        {
            CustomerId = customer, OfferId = request.OfferId!.Value, ProgramId = request.ProgramId,
            BundleId = request.BundleId, Tier = request.Tier, DurationDays = request.DurationDays,
            StartDate = request.StartDate, EndDate = request.StartDate.AddDays(25),
            CurrencyCode = "KWD", FinalPrice = 180.250m,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1), IsUsed = true, CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await service.ClaimAsync(customer, request);
        await service.BindSubscriptionAsync(customer, request, original.SubscriptionId);
        await service.CompleteAsync(customer, request, original);
        var replay = await service.InspectAsync(customer, request);

        Assert.Equal(CheckoutAttemptState.Replay, replay.State);
        Assert.Equal(original, replay.Response);
    }

    [Fact]
    public async Task Concurrent_same_key_claims_persist_only_one_attempt()
    {
        var customer = Guid.NewGuid();
        var request = Request(quoteId: Guid.NewGuid());
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> ClaimAsync()
        {
            await barrier.Task;
            await using var db = CreateDb();
            var service = new CheckoutIdempotencyService(db, Clock());
            try
            {
                await service.ClaimAsync(customer, request);
                return true;
            }
            catch (CheckoutIdempotencyClaimException)
            {
                return false;
            }
        }

        var first = ClaimAsync();
        var second = ClaimAsync();
        barrier.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes.Where(x => x));
        await using var verify = CreateDb();
        Assert.Equal(1, await verify.IdempotencyRecords.CountAsync(x => x.Scope.Contains("CustomerCheckout:Individual:")
            && x.Key == request.IdempotencyKey));
        Assert.Equal(1, await verify.IdempotencyRecords.CountAsync(x => x.Scope == "CustomerCheckout:Quote"
            && x.Key == request.QuoteId!.Value.ToString("N")));
    }

    [Fact]
    public async Task A_quote_cannot_be_reserved_under_two_different_keys()
    {
        var customer = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        await using var db = CreateDb();
        var service = new CheckoutIdempotencyService(db, Clock());

        await service.ClaimAsync(customer, Request(quoteId, "attempt-1"));
        await Assert.ThrowsAsync<CheckoutIdempotencyClaimException>(() =>
            service.ClaimAsync(customer, Request(quoteId, "attempt-2")));
    }

    [Fact]
    public async Task Foreign_customer_cannot_learn_the_owner_or_subscription_of_a_consumed_quote()
    {
        var owner = Guid.NewGuid();
        var quoteId = Guid.NewGuid();
        await using var db = CreateDb();
        var service = new CheckoutIdempotencyService(db, Clock());
        await service.ClaimAsync(owner, Request(quoteId), CancellationToken.None);
        await service.BindSubscriptionAsync(owner, Request(quoteId), Guid.NewGuid());

        var inspection = await service.InspectAsync(Guid.NewGuid(), Request(quoteId));

        Assert.Equal(CheckoutAttemptState.Unavailable, inspection.State);
        Assert.Null(inspection.SubscriptionId);
    }

    private AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_database.GetConnectionString()).Options,
            Substitute.For<IDomainEventDispatcher>());

    private static IDateTimeProvider Clock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return clock;
    }

    private static StartCheckoutRequest Request(Guid quoteId, string key = "attempt-1")
        => new(Guid.NewGuid(), Guid.NewGuid(), RestaurantTier.Basic, 26,
            "https://maelmate.runasp.net/callback", "https://maelmate.runasp.net/error",
            new DateOnly(2026, 11, 1), PaymentMethodId: 3, IdempotencyKey: key,
            OfferId: Guid.NewGuid(), QuoteId: quoteId);

    private static CheckoutStartedDto Started()
        => new(Guid.NewGuid(), "https://gateway.example/payment", DateTime.UtcNow.AddMinutes(15),
            Guid.NewGuid(), 180.250m, PaymentStatus: "PendingPayment", FulfillmentStatus: "PendingPayment");
}
