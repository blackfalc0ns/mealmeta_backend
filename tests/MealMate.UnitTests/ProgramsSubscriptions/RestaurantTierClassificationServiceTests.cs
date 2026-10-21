using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class RestaurantTierClassificationServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(104.000, RestaurantTier.Platinum)] // one-item cohort is all Plus
    [InlineData(143.000, RestaurantTier.Platinum)]
    [InlineData(169.000, RestaurantTier.Platinum)]
    public async Task Single_restaurant_cohort_is_plus_without_invented_thresholds(decimal reference26, RestaurantTier expected)
    {
        await using var db = CreateDb();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        AddPricing(db, restaurantId, programId, bundleId, reference26);
        await db.SaveChangesAsync();

        var result = await CreateService(db).ResolveAsync(programId, bundleId, eligibleOnly: false, CancellationToken.None);

        result.Assignments.Should().ContainSingle().Which.Tier.Should().Be(expected);
        result.Assignments[0].RestaurantId.Should().Be(restaurantId);
    }

    [Fact]
    public async Task Multiple_restaurants_use_mean_and_half_standard_deviation()
    {
        await using var db = CreateDb();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        AddPricing(db, Guid.NewGuid(), programId, bundleId, 52.000m);  // 2.0 / day
        AddPricing(db, Guid.NewGuid(), programId, bundleId, 78.000m);  // 3.0 / day
        AddPricing(db, Guid.NewGuid(), programId, bundleId, 104.000m); // 4.0 / day
        await db.SaveChangesAsync();

        var result = await CreateService(db).ResolveAsync(programId, bundleId, eligibleOnly: false, CancellationToken.None);

        result.MeanReferenceDaily.Should().Be(3.000m);
        result.Assignments.Single(x => x.ReferenceDailyBox == 2.000m).Tier.Should().Be(RestaurantTier.Basic);
        result.Assignments.Single(x => x.ReferenceDailyBox == 3.000m).Tier.Should().Be(RestaurantTier.Platinum);
        result.Assignments.Single(x => x.ReferenceDailyBox == 4.000m).Tier.Should().Be(RestaurantTier.Elite);
    }

    [Fact]
    public async Task Eligible_only_excludes_restaurants_without_active_offer_or_approved_profile()
    {
        await using var db = CreateDb();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var eligible = AddRestaurant(db, VerificationStatus.Approved, isOperationallyActive: true);
        var notApproved = AddRestaurant(db, VerificationStatus.UnderReview, isOperationallyActive: true);
        var notOperational = AddRestaurant(db, VerificationStatus.Approved, isOperationallyActive: false);
        var withoutOffer = AddRestaurant(db, VerificationStatus.Approved, isOperationallyActive: true);
        AddPricing(db, eligible, programId, bundleId, 104.000m);
        AddPricing(db, notApproved, programId, bundleId, 52.000m);
        AddPricing(db, notOperational, programId, bundleId, 78.000m);
        AddPricing(db, withoutOffer, programId, bundleId, 169.000m);
        AddOffer(db, eligible, programId, bundleId);
        AddOffer(db, notApproved, programId, bundleId);
        AddOffer(db, notOperational, programId, bundleId);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var eligibleResult = await service.ResolveAsync(programId, bundleId, eligibleOnly: true, CancellationToken.None);
        var allResult = await service.ResolveAsync(programId, bundleId, eligibleOnly: false, CancellationToken.None);

        eligibleResult.Assignments.Should().ContainSingle().Which.RestaurantId.Should().Be(eligible);
        eligibleResult.Assignments[0].Tier.Should().Be(RestaurantTier.Platinum);
        allResult.Assignments.Should().HaveCount(4);
    }

    [Fact]
    public async Task Draft_and_expired_prices_are_ignored()
    {
        await using var db = CreateDb();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        AddPricing(db, Guid.NewGuid(), programId, bundleId, 52.000m, lockStatus: PriceLockStatus.Draft);
        AddPricing(db, Guid.NewGuid(), programId, bundleId, 78.000m, effectiveToUtc: Now.AddDays(-1));
        await db.SaveChangesAsync();

        var result = await CreateService(db).ResolveAsync(programId, bundleId, eligibleOnly: false, CancellationToken.None);

        result.Assignments.Should().BeEmpty();
        result.MeanReferenceDaily.Should().Be(0m);
    }

    [Fact]
    public async Task Only_the_newest_open_price_per_restaurant_is_used()
    {
        await using var db = CreateDb();
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        AddPricing(db, restaurantId, programId, bundleId, 169.000m, effectiveFromUtc: Now.AddDays(-30));
        AddPricing(db, restaurantId, programId, bundleId, 104.000m, effectiveFromUtc: Now.AddDays(-1));
        await db.SaveChangesAsync();

        var result = await CreateService(db).ResolveAsync(programId, bundleId, eligibleOnly: false, CancellationToken.None);

        var assignment = result.Assignments.Should().ContainSingle().Subject;
        assignment.ReferencePrice26.Should().Be(104.000m);
        assignment.Tier.Should().Be(RestaurantTier.Platinum);
    }

    private static RestaurantTierClassificationService CreateService(AppDbContext db)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        return new RestaurantTierClassificationService(db, clock);
    }

    private static Guid AddRestaurant(AppDbContext db, VerificationStatus status, bool isOperationallyActive)
    {
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "مطعم",
            LegalNameEn = "Restaurant",
            TradeNameAr = "مطعم",
            TradeNameEn = "Restaurant",
            VerificationStatus = status,
            IsOperationallyActive = isOperationallyActive,
        };
        db.RestaurantProfiles.Add(restaurant);
        return restaurant.Id;
    }

    private static void AddOffer(AppDbContext db, Guid restaurantId, Guid programId, Guid bundleId)
        => db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            RestaurantId = restaurantId,
            ProgramId = programId,
            BundleId = bundleId,
            IsActive = true,
        });

    private static void AddPricing(AppDbContext db, Guid restaurantId, Guid programId, Guid bundleId, decimal reference26,
        PriceLockStatus lockStatus = PriceLockStatus.Locked, DateTime? effectiveFromUtc = null, DateTime? effectiveToUtc = null)
        => db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            RestaurantId = restaurantId,
            ProgramId = programId,
            BundleId = bundleId,
            ReferencePrice26 = new Money(reference26, "KWD"),
            ContractedSupplyPrice26 = new Money(reference26 * 0.8m, "KWD"),
            LockStatus = lockStatus,
            EffectiveFromUtc = effectiveFromUtc ?? Now.AddDays(-7),
            EffectiveToUtc = effectiveToUtc,
        });

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
