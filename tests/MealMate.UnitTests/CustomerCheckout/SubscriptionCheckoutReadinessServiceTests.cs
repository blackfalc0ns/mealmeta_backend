using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class SubscriptionCheckoutReadinessServiceTests
{
    [Fact]
    public async Task Missing_all_items_returns_address_as_next_action()
    {
        var fixture = await SeedAsync();
        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsComplete);
        Assert.False(result.Value.CanOpenSummary);
        Assert.False(result.Value.CanCheckout);
        Assert.Equal(SubscriptionCheckoutGateCodes.Address, result.Value.NextAction);
        Assert.Equal(3, result.Value.Items.Count);
        Assert.All(result.Value.Items, x => Assert.False(x.IsComplete));
        Assert.Equal("checkout.address_required", SubscriptionCheckoutGateCodes.ProfileCheckoutBlockCode(result.Value));
        Assert.Equal(fixture.ProgramId, result.Value.Program.ProgramId);
        Assert.Contains("عنوان", result.Value.WarningAr);
    }

    [Fact]
    public async Task Address_only_returns_dietary_preferences_next()
    {
        var fixture = await SeedAsync(address: true);
        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionCheckoutGateCodes.DietaryPreferences, result.Value.NextAction);
        Assert.True(result.Value.Items.Single(x => x.Code == SubscriptionCheckoutGateCodes.Address).IsComplete);
        Assert.False(result.Value.Items.Single(x => x.Code == SubscriptionCheckoutGateCodes.DietaryPreferences).IsComplete);
        Assert.Equal(fixture.RegionId, result.Value.Address!.RegionId);
        Assert.Equal("المنزل", result.Value.Address.Label);
    }

    [Fact]
    public async Task Address_and_dietary_returns_allergies_next()
    {
        var fixture = await SeedAsync(address: true, dietary: true);
        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionCheckoutGateCodes.Allergies, result.Value.NextAction);
        Assert.Equal("checkout.allergies_required", SubscriptionCheckoutGateCodes.ProfileCheckoutBlockCode(result.Value));
    }

    [Fact]
    public async Task Address_and_allergies_returns_dietary_block_code()
    {
        var fixture = await SeedAsync(address: true, allergies: true);
        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionCheckoutGateCodes.DietaryPreferences, result.Value.NextAction);
        Assert.Equal("checkout.dietary_preferences_required", SubscriptionCheckoutGateCodes.ProfileCheckoutBlockCode(result.Value));
    }

    [Fact]
    public async Task Complete_profile_with_delivering_restaurants_can_open_summary_and_checkout()
    {
        var fixture = await SeedAsync(address: true, allergies: true, dietary: true, eligibleRestaurant: true);
        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsComplete);
        Assert.True(result.Value.CanOpenSummary);
        Assert.True(result.Value.CanCheckout);
        Assert.Equal(SubscriptionCheckoutGateCodes.Summary, result.Value.NextAction);
        Assert.Null(result.Value.CheckoutBlockReason);
        Assert.Equal(1, result.Value.DeliveringRestaurantCount);
        var restaurant = Assert.Single(result.Value.Restaurants);
        Assert.Equal("مطعم المنطقة", restaurant.NameAr);
        Assert.Equal(RestaurantTier.Basic, restaurant.Tier);
        Assert.Null(SubscriptionCheckoutGateCodes.ProfileCheckoutBlockCode(result.Value));
    }

    [Fact]
    public async Task Complete_profile_with_no_delivering_restaurants_blocks_checkout()
    {
        var fixture = await SeedAsync(address: true, allergies: true, dietary: true);
        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CanOpenSummary);
        Assert.False(result.Value.CanCheckout);
        Assert.Equal(SubscriptionCheckoutGateCodes.NoRestaurantsForAddress, result.Value.CheckoutBlockReason);
        Assert.Equal(0, result.Value.DeliveringRestaurantCount);
    }

    [Fact]
    public async Task Address_without_region_is_incomplete()
    {
        var fixture = await SeedAsync();
        fixture.Db.CustomerAddresses.Add(new CustomerAddress
        {
            UserId = fixture.CustomerId, Label = "بدون منطقة", AddressLine = "شارع", RegionId = null, IsDefault = true,
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Items.Single(x => x.Code == SubscriptionCheckoutGateCodes.Address).IsComplete);
        Assert.Equal(SubscriptionCheckoutGateCodes.Address, result.Value.NextAction);
    }

    [Fact]
    public async Task Existing_active_individual_subscription_blocks_checkout()
    {
        var fixture = await SeedAsync(address: true, allergies: true, dietary: true, eligibleRestaurant: true);
        fixture.Db.Subscriptions.Add(new Subscription
        {
            CustomerUserId = fixture.CustomerId,
            ProgramId = fixture.ProgramId,
            BundleId = fixture.BundleId,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 26,
            Status = SubscriptionStatus.Active,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 26),
            PaidAmount = new Money(100m, "KWD"),
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanCheckout);
        Assert.False(result.Value.CanOpenSummary);
        Assert.Equal("checkout.existing_subscription", result.Value.CheckoutBlockReason);
        Assert.NotNull(result.Value.BlockingSubscriptionId);
    }

    private static async Task<Fixture> SeedAsync(
        bool address = false, bool allergies = false, bool dietary = false, bool eligibleRestaurant = false)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var program = new Program
        {
            Code = "PROG_GATE", NameAr = "كيتو", NameEn = "Keto",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "BND_GATE", NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true,
        };
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم المنطقة", TradeNameEn = "Area restaurant",
            LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        var now = new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc);
        db.AddRange(new CustomerProfile
        {
            UserId = customerId,
            AllergiesCompletedAtUtc = allergies ? now : null,
            DietaryPreferencesCompletedAtUtc = dietary ? now : null,
        }, program, bundle, restaurant);
        db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id, IsActive = true,
        });
        db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
        {
            ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
            ReferencePrice26 = new Money(104m, "KWD"),
            ContractedSupplyPrice26 = new Money(80m, "KWD"),
            LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = now.AddDays(-1),
        });
        if (address)
        {
            db.CustomerAddresses.Add(new CustomerAddress
            {
                UserId = customerId, Label = "المنزل", AddressLine = "قطعة 4، شارع 12",
                Governorate = "العاصمة", Area = "الشرق", Block = "4", RegionId = regionId, IsDefault = true,
            });
        }

        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns(eligibleRestaurant
                ? [new RestaurantEligibilityDto(restaurant.Id, "مطعم المنطقة", "Area restaurant", true, [], [])]
                : []);
        var service = new SubscriptionCheckoutReadinessService(
            db, clock, eligibility, new AllowedSubscriptionDurationService(db),
            new RestaurantTierClassificationService(db, clock), new CustomerIndividualSubscriptionGate(db));
        return new Fixture(service, db, customerId, program.Id, bundle.Id, regionId, restaurant.Id);
    }

    private sealed record Fixture(
        SubscriptionCheckoutReadinessService Service,
        AppDbContext Db,
        Guid CustomerId,
        Guid ProgramId,
        Guid BundleId,
        Guid RegionId,
        Guid RestaurantId);
}
