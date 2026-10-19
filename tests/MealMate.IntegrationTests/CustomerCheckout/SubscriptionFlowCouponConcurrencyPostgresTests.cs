using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Offers.Services;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Domain.Enums;
using MealMate.Domain.Offers;
using MealMate.Domain.Offers.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.CustomerCheckout;

/// <summary>
/// Relational-database guarantees behind the subscription coupon flow: the usage limit survives
/// concurrent checkouts (CurrentUsageCount is a concurrency token) and one offer can be redeemed
/// only once per subscription (unique index).
/// </summary>
public sealed class SubscriptionFlowCouponConcurrencyPostgresTests : IAsyncLifetime
{
    private MealMate.Testing.PostgresTestDatabase _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MealMate.Testing.PostgresTestDatabase();
            await _container.StartAsync();
            await using var db = CreateDb();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException("Docker or an explicitly configured local PostgreSQL test database is required for coupon concurrency integration tests.", ex);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Two_concurrent_checkouts_cannot_both_take_the_last_coupon_slot()
    {
        var offerId = await SeedOfferWithLimitAsync(totalLimit: 1);
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> TryRedeemAsync(Guid customerId)
        {
            await using var db = CreateDb();
            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(DateTime.UtcNow);
            await start.Task;
            var service = new SubscriptionFlowCouponService(db, new OfferEligibilityEngine(), clock);
            var evaluation = await service.EvaluateAsync("LASTSLOT", customerId, Guid.NewGuid(), Guid.NewGuid(), 26, 200m);
            if (!evaluation.IsValid) return false;
            try
            {
                await service.CreateCheckoutHoldAsync(evaluation.Offer!, Guid.NewGuid(), customerId, evaluation.DiscountAmount);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // The other checkout committed the last usage first: CurrentUsageCount no longer matches.
                return false;
            }
        }

        var first = TryRedeemAsync(customerA);
        var second = TryRedeemAsync(customerB);
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, x => x);
        await using var verificationDb = CreateDb();
        Assert.Equal(1, await verificationDb.OfferRedemptions.CountAsync(x => x.OfferId == offerId));
    }

    [Fact]
    public async Task Duplicate_redemption_for_same_offer_and_subscription_is_rejected_by_database()
    {
        var offerId = await SeedOfferWithLimitAsync(totalLimit: 10);
        var customerId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();

        await using (var db = CreateDb())
        {
            db.OfferRedemptions.Add(NewRedemption(offerId, customerId, subscriptionId));
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb())
        {
            db.OfferRedemptions.Add(NewRedemption(offerId, customerId, subscriptionId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    private static OfferRedemption NewRedemption(Guid offerId, Guid customerId, Guid subscriptionId) => new()
    {
        OfferId = offerId,
        CustomerId = customerId,
        SubscriptionId = subscriptionId,
        DiscountAppliedAmount = new Money(5m, "KWD"),
        PlatformFundedAmount = new Money(5m, "KWD"),
        RestaurantFundedAmount = new Money(0m, "KWD"),
        RedeemedAtUtc = DateTime.UtcNow,
    };

    private async Task<Guid> SeedOfferWithLimitAsync(int totalLimit)
    {
        await using var db = CreateDb();
        var offer = new Offer
        {
            NameAr = "آخر فتحة", NameEn = "Last slot",
            Status = OfferStatus.Active,
            DiscountType = DiscountType.FixedAmount,
            DiscountAmount = new Money(5m, "KWD"),
            StartsAtUtc = DateTime.UtcNow.AddDays(-1),
            EndsAtUtc = DateTime.UtcNow.AddDays(30),
            PromoCode = "LASTSLOT",
            PerCustomerLimit = 1,
            TotalUsageLimit = totalLimit,
            AllowPromoCodeStacking = true,
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        return offer.Id;
    }

    private AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
