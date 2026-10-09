using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
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
    public async Task Complete_profile_with_period_eligible_restaurants_can_checkout()
    {
        var fixture = await SeedAsync(address: true, allergies: true, dietary: true, eligibleRestaurant: true, fullEligibility: true);
        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false,
            startDate: new DateOnly(2026, 8, 19));

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
        Assert.NotNull(result.Value.Availability);
        Assert.Equal(IndividualRestaurantAvailabilityStatus.Available, result.Value.Availability!.Status);
        Assert.Equal(new DateOnly(2026, 8, 19), result.Value.Availability.EffectiveStartDate);
    }

    [Fact]
    public async Task Coverage_without_full_period_eligibility_blocks_checkout_with_selection_reason()
    {
        // The restaurant covers the address (offer + pricing + region) but the period check
        // fails: no catalog meal/menu for the bundle slots, so the summary must not approve
        // payment and must explain "covered but not for this period" — not the generic
        // no-coverage message.
        var fixture = await SeedAsync(address: true, allergies: true, dietary: true, eligibleRestaurant: true);
        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false,
            startDate: new DateOnly(2026, 8, 19));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CanOpenSummary);
        Assert.False(result.Value.CanCheckout);
        Assert.Equal(SubscriptionCheckoutGateCodes.NoRestaurantsForSelection, result.Value.CheckoutBlockReason);
        Assert.NotNull(result.Value.Availability);
        Assert.Equal(IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection, result.Value.Availability!.Status);
        Assert.Equal(1, result.Value.Availability.CoveredRestaurantCount);
        Assert.Equal(0, result.Value.Availability.EligibleRestaurantCount);
        Assert.Equal(0, result.Value.DeliveringRestaurantCount);
        Assert.Empty(result.Value.Restaurants);
    }

    [Fact]
    public async Task Missing_date_context_reports_selection_required_and_blocks_payment()
    {
        var fixture = await SeedAsync(address: true, allergies: true, dietary: true, eligibleRestaurant: true, fullEligibility: true);
        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CanOpenSummary);
        Assert.False(result.Value.CanCheckout);
        Assert.Equal("checkout.start_date_required", result.Value.CheckoutBlockReason);
        Assert.NotNull(result.Value.Availability);
        Assert.Equal(IndividualRestaurantAvailabilityStatus.SelectionRequired, result.Value.Availability!.Status);
        // Coverage stays visible as a preview count, but no restaurants are offered for payment.
        Assert.Equal(1, result.Value.Availability.CoveredRestaurantCount);
        Assert.Equal(1, result.Value.DeliveringRestaurantCount);
        Assert.Empty(result.Value.Restaurants);
    }

    [Fact]
    public async Task Complete_profile_with_no_delivering_restaurants_blocks_checkout()
    {
        var fixture = await SeedAsync(address: true, allergies: true, dietary: true);
        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false,
            startDate: new DateOnly(2026, 8, 19));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CanOpenSummary);
        Assert.False(result.Value.CanCheckout);
        Assert.Equal(SubscriptionCheckoutGateCodes.NoRestaurantsForAddress, result.Value.CheckoutBlockReason);
        Assert.NotNull(result.Value.Availability);
        Assert.Equal(IndividualRestaurantAvailabilityStatus.NoRestaurantsForAddress, result.Value.Availability!.Status);
        Assert.Equal(0, result.Value.Availability.CoveredRestaurantCount);
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
        var fixture = await SeedAsync(address: true, allergies: true, dietary: true, eligibleRestaurant: true, fullEligibility: true);
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
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false,
            startDate: new DateOnly(2026, 8, 19));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CanCheckout);
        Assert.False(result.Value.CanOpenSummary);
        Assert.Equal("checkout.existing_subscription", result.Value.CheckoutBlockReason);
        Assert.NotNull(result.Value.BlockingSubscriptionId);
    }

    [Fact]
    public async Task Active_delivery_address_overrides_default()
    {
        var fixture = await SeedAsync(allergies: true, dietary: true, eligibleRestaurant: true, fullEligibility: true);
        var defaultRegionId = Guid.NewGuid();
        var activeRegionId = fixture.RegionId;
        var defaultAddress = new CustomerAddress
        {
            UserId = fixture.CustomerId, Label = "افتراضي", AddressLine = "عنوان افتراضي",
            RegionId = defaultRegionId, IsDefault = true,
        };
        var activeAddress = new CustomerAddress
        {
            UserId = fixture.CustomerId, Label = "العمل", AddressLine = "عنوان التوصيل النشط",
            RegionId = activeRegionId, IsDefault = false,
        };
        fixture.Db.CustomerAddresses.AddRange(defaultAddress, activeAddress);
        fixture.Db.RestaurantServiceRegions.Add(new MealMate.Domain.RestaurantOperations.RestaurantServiceRegion
        {
            RestaurantId = fixture.RestaurantId, RegionId = activeRegionId, IsActive = true,
        });
        var profile = await fixture.Db.CustomerProfiles.SingleAsync(x => x.UserId == fixture.CustomerId);
        profile.ActiveDeliveryAddressId = activeAddress.Id;
        profile.ActiveDeliveryRegionId = activeRegionId;
        profile.ActiveDeliveryAddressLine = activeAddress.AddressLine;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.EvaluateAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 26, false,
            startDate: new DateOnly(2026, 8, 19));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.CanCheckout);
        Assert.Equal(activeAddress.Id, result.Value.Address!.Id);
        Assert.Equal(activeRegionId, result.Value.Address.RegionId);
        Assert.Equal("العمل", result.Value.Address.Label);
        Assert.Equal(1, result.Value.DeliveringRestaurantCount);
    }

    private static async Task<Fixture> SeedAsync(
        bool address = false, bool allergies = false, bool dietary = false,
        bool eligibleRestaurant = false, bool fullEligibility = false)
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
            var seededAddress = new CustomerAddress
            {
                UserId = customerId, Label = "المنزل", AddressLine = "قطعة 4، شارع 12",
                Governorate = "العاصمة", Area = "الشرق", Block = "4", RegionId = regionId, IsDefault = true,
            };
            db.CustomerAddresses.Add(seededAddress);
            await db.SaveChangesAsync();
            var profile = await db.CustomerProfiles.SingleAsync(x => x.UserId == customerId);
            profile.ActiveDeliveryAddressId = seededAddress.Id;
            profile.ActiveDeliveryRegionId = regionId;
            profile.ActiveDeliveryAddressLine = seededAddress.AddressLine;
        }

        if (eligibleRestaurant)
        {
            db.RestaurantServiceRegions.Add(new MealMate.Domain.RestaurantOperations.RestaurantServiceRegion
            {
                RestaurantId = restaurant.Id, RegionId = regionId, IsActive = true,
            });
        }

        if (fullEligibility)
        {
            // Everything the full-period evaluator demands: tier classification, active pricing
            // rule, bundle slot requirements, a safe catalog meal with image and nutrition
            // meeting the program standard, and base capacity for the whole window.
            var menu = new Menu { RestaurantId = restaurant.Id, ProgramId = program.Id, NameAr = "وجبات", NameEn = "Meals", IsActive = true };
            var ingredient = new Ingredient { RestaurantId = restaurant.Id, Code = "RICE", NameAr = "أرز", NameEn = "Rice", OriginCountryCode = "KW", IsActive = true };
            var meal = new Meal
            {
                RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, MenuId = menu.Id, Menu = menu,
                Code = "M_GATE", NameAr = "وجبة", NameEn = "Meal", DescriptionAr = "وصف", DescriptionEn = "Description",
                Slot = "lunch", ApprovalStatus = VerificationStatus.Approved,
                Calories = 500, ProteinG = 40, CarbsG = 50, NetCarbsG = 50, FatG = 20, FiberG = 8,
                IsAvailableForNewOrders = true, AllergensReviewed = true, DataResponsibilityConfirmed = true,
            };
            db.AddRange(
                menu, ingredient,
                new MealIngredient { MealId = meal.Id, Meal = meal, IngredientId = ingredient.Id, Ingredient = ingredient, PortionGrams = 100 },
                new MealImage { MealId = meal.Id, Meal = meal, ImageUrl = "/gate_meal.png", IsPrimary = true },
                new ClassificationRun { ProgramId = program.Id, BundleId = bundle.Id, ConfidenceLevel = "high", RunAtUtc = now },
                new PricingRuleVersion
                {
                    IsActive = true,
                    RMax = Percentage.FromFraction(.3m),
                    RMin = Percentage.FromFraction(.05m),
                    DShort = 6, DLong = 26, Gamma = 1, RoundingStepKwd = .1m,
                },
                new ProgramMealSlotRequirement { ProgramId = program.Id, Slot = "lunch", IsRequired = true },
                new ProgramNutritionStandard { ProgramId = program.Id, MealCategory = "MAIN", KcalMin = 400, KcalMax = 600, ProteinGMin = 30, ProteinGMax = 50, CarbsGMin = 40, CarbsGMax = 60, FatGMin = 15, FatGMax = 25, FiberGMin = 5 },
                new DailyCapacity { RestaurantId = restaurant.Id, ServiceDate = new DateOnly(2026, 1, 1), CapacityType = CapacityType.Base, MaxBoxes = 10 });
            await db.SaveChangesAsync();
            db.RestaurantClassifications.Add(new RestaurantClassification
            {
                ClassificationRunId = db.ClassificationRuns.AsNoTracking().First(x => x.ProgramId == program.Id).Id,
                RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, Tier = RestaurantTier.Basic,
            });
        }

        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        var deliveryAddresses = new CustomerDeliveryAddressService(db, clock);
        var schedule = new SubscriptionScheduleService(clock);
        var availability = new SubscriptionRestaurantAvailabilityService(
            db, clock, deliveryAddresses,
            new RestaurantEligibilityService(db, clock, schedule,
                new BundleSlotRequirementService(db), new MenuPlanResolutionService(db),
                new RestaurantTierClassificationService(db, clock), new RestaurantCapacityReader(db, clock)),
            new RestaurantTierClassificationService(db, clock));
        var service = new SubscriptionCheckoutReadinessService(
            db, new AllowedSubscriptionDurationService(db),
            new RestaurantTierClassificationService(db, clock), deliveryAddresses,
            new CustomerIndividualSubscriptionGate(db), availability);
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
