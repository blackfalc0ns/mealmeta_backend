using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MediatR;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

/// <summary>
/// RED tests for the unified availability contract: a restaurant that covers the address but
/// fails the full-period check (no menu day, no capacity, no safe meal, no valid supply price)
/// must yield eligibleCount 0 with CanCheckout=false on a complete profile, and "no coverage"
/// must be reported separately from "coverage exists but not for this period".
/// </summary>
public sealed class IndividualSubscriptionAvailabilityTests
{
    [Fact]
    public async Task Subscription_types_use_persisted_active_family_package_limits()
    {
        var fixture = await SeedAsync(FailureMode.None);
        await using var db = fixture.Db;
        db.SystemSettings.Add(new MealMate.Domain.AdminDashboard.SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            IsActive = true,
            ValueJson = """{"family":{"packages":[{"id":"11111111-1111-1111-1111-111111111111","nameAr":"عائلة","nameEn":"Family","minMembers":2,"maxMembers":6,"isActive":true},{"id":"22222222-2222-2222-2222-222222222222","nameAr":"متوقفة","nameEn":"Inactive","minMembers":2,"maxMembers":9,"isActive":false}]}}"""
        });
        await db.SaveChangesAsync();
        var result = await CreateFlowService(fixture).GetSubscriptionTypesAsync(fixture.CustomerId);
        Assert.True(result.IsSuccess);
        Assert.Equal(6, result.Value.Single(x => x.Code == "FAMILY").MaxMembers);
        Assert.Equal(1, result.Value.Single(x => x.Code == "INDIVIDUAL").MaxMembers);
    }

    private static readonly DateTime Now = new(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 8, 19);

    [Fact]
    public async Task Covered_restaurant_without_capacity_is_not_period_eligible()
    {
        var fixture = await SeedAsync(FailureMode.NoCapacity);
        var report = await fixture.Availability.EvaluateDetailedAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 6, false, StartDate);

        Assert.Equal(IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection, report.Availability.Status);
        Assert.Equal(1, report.Availability.CoveredRestaurantCount);
        Assert.Equal(0, report.Availability.EligibleRestaurantCount);
        Assert.Equal(SubscriptionCheckoutGateCodes.NoRestaurantsForSelection, report.Availability.MessageCode);
        Assert.Equal(StartDate, report.Availability.EffectiveStartDate);
        Assert.Empty(report.EligibleRestaurants);
    }

    [Fact]
    public async Task Covered_restaurant_without_menu_for_the_period_is_not_period_eligible()
    {
        var fixture = await SeedAsync(FailureMode.NoMeal);
        var report = await fixture.Availability.EvaluateDetailedAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 6, false, StartDate);

        Assert.Equal(IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection, report.Availability.Status);
        Assert.Equal(1, report.Availability.CoveredRestaurantCount);
        Assert.Equal(0, report.Availability.EligibleRestaurantCount);
    }

    [Fact]
    public async Task Covered_restaurant_with_only_unsafe_meals_is_not_period_eligible()
    {
        var fixture = await SeedAsync(FailureMode.UnsafeMealOnly, customerAllergy: true);
        var report = await fixture.Availability.EvaluateDetailedAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 6, false, StartDate);

        Assert.Equal(IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection, report.Availability.Status);
        Assert.Equal(0, report.Availability.EligibleRestaurantCount);
        Assert.Equal(1, report.Availability.CoveredRestaurantCount);
    }

    [Fact]
    public async Task Covered_restaurant_without_valid_supply_pricing_is_not_period_eligible()
    {
        var fixture = await SeedAsync(FailureMode.NoPricing);
        var report = await fixture.Availability.EvaluateDetailedAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 6, false, StartDate);

        Assert.Equal(IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection, report.Availability.Status);
        Assert.Equal(0, report.Availability.EligibleRestaurantCount);
        Assert.Equal(1, report.Availability.CoveredRestaurantCount);
    }

    [Fact]
    public async Task Fully_eligible_restaurant_reports_available_with_eligible_list()
    {
        var fixture = await SeedAsync(FailureMode.None);
        var report = await fixture.Availability.EvaluateDetailedAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 6, false, StartDate);

        Assert.Equal(IndividualRestaurantAvailabilityStatus.Available, report.Availability.Status);
        Assert.Null(report.Availability.MessageCode);
        Assert.Equal(1, report.Availability.EligibleRestaurantCount);
        var eligible = Assert.Single(report.EligibleRestaurants);
        Assert.Equal(fixture.RestaurantId, eligible.RestaurantId);
        Assert.Equal(6, eligible.MenuPlanVersionsByDate.Count);
        Assert.Equal(RestaurantTier.Basic, report.TierByRestaurant[fixture.RestaurantId]);
    }

    [Fact]
    public async Task Restaurant_availability_route_evaluates_from_first_delivery_not_customer_payment_day()
    {
        var fixture = await SeedAsync(FailureMode.NoCapacity);
        var flow = CreateFlowService(fixture);

        var response = await flow.GetRestaurantAvailabilityAsync(
            SummaryRequest(fixture.ProgramId, fixture.BundleId), fixture.CustomerId);

        Assert.True(response.IsSuccess);
        Assert.Equal(new DateOnly(2026, 8, 22), response.Value.Availability.EffectiveStartDate);
    }

    [Fact]
    public async Task Uncovered_address_reports_no_restaurants_for_address()
    {
        var fixture = await SeedAsync(FailureMode.None, includeCoverageRegion: false);
        var report = await fixture.Availability.EvaluateDetailedAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 6, false, StartDate);

        Assert.Equal(IndividualRestaurantAvailabilityStatus.NoRestaurantsForAddress, report.Availability.Status);
        Assert.Equal(SubscriptionCheckoutGateCodes.NoRestaurantsForAddress, report.Availability.MessageCode);
        Assert.Equal(0, report.Availability.CoveredRestaurantCount);
        Assert.Equal(0, report.Availability.EligibleRestaurantCount);
    }

    [Fact]
    public async Task Missing_address_reports_address_required()
    {
        var fixture = await SeedAsync(FailureMode.None, withAddress: false);
        var report = await fixture.Availability.EvaluateDetailedAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 6, false, StartDate);

        Assert.Equal(IndividualRestaurantAvailabilityStatus.AddressRequired, report.Availability.Status);
        Assert.Equal("checkout.address_required", report.Availability.MessageCode);
        Assert.Equal(0, report.Availability.EligibleRestaurantCount);
    }

    [Fact]
    public async Task Missing_start_date_reports_selection_required_without_payment_approval()
    {
        var fixture = await SeedAsync(FailureMode.None);
        var report = await fixture.Availability.EvaluateDetailedAsync(
            fixture.CustomerId, fixture.ProgramId, fixture.BundleId, RestaurantTier.Basic, 6, false, null);

        Assert.Equal(IndividualRestaurantAvailabilityStatus.SelectionRequired, report.Availability.Status);
        Assert.Equal("checkout.start_date_required", report.Availability.MessageCode);
        Assert.Equal(1, report.Availability.CoveredRestaurantCount);
        Assert.Equal(0, report.Availability.EligibleRestaurantCount);
        Assert.Null(report.Availability.EffectiveStartDate);
    }

    /// <summary>The contextual browse route must count the eligible list before pagination and
    /// return an empty card list (never count 0) when the period is not servable.</summary>
    [Fact]
    public async Task Availability_route_returns_full_count_before_pagination_and_zero_cards_when_unservable()
    {
        var failing = await SeedAsync(FailureMode.NoCapacity);
        var failingService = CreateFlowService(failing);
        var failingResult = await failingService.GetRestaurantAvailabilityAsync(SummaryRequest(failing.ProgramId, failing.BundleId), failing.CustomerId);

        Assert.True(failingResult.IsSuccess);
        Assert.Equal(IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection, failingResult.Value.Availability.Status);
        Assert.Equal(0, failingResult.Value.TotalCount);
        Assert.Empty(failingResult.Value.Restaurants);

        var passing = await SeedAsync(FailureMode.None, extraRestaurants: 3);
        var passingService = CreateFlowService(passing);
        var pagedRequest = SummaryRequest(passing.ProgramId, passing.BundleId, restaurantsPage: 1, restaurantsPageSize: 2);
        var passingResult = await passingService.GetRestaurantAvailabilityAsync(pagedRequest, passing.CustomerId);

        Assert.True(passingResult.IsSuccess);
        Assert.Equal(IndividualRestaurantAvailabilityStatus.Available, passingResult.Value.Availability.Status);
        // Total count is the full eligible list, not the page size.
        Assert.Equal(4, passingResult.Value.TotalCount);
        Assert.Equal(2, passingResult.Value.Restaurants.Count);
        Assert.NotNull(passingResult.Value.Pagination);
        Assert.True(passingResult.Value.Pagination!.HasNextPage);
    }

    // ---- helpers ----

    private enum FailureMode { None, NoCapacity, NoMeal, UnsafeMealOnly, NoPricing }

    private static SubscriptionSummaryRequest SummaryRequest(Guid programId, Guid bundleId, int? restaurantsPage = null, int? restaurantsPageSize = null)
        => new(programId, bundleId, RestaurantTier.Basic, 6, StartDate, IncludeFriday: false, restaurantsPage, restaurantsPageSize, PromoCode: null);


    private static ISubscriptionFlowQueryService CreateFlowService(Fixture fixture)
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PricingEngineResult>.Success(
                new PricingEngineResult(4m, 6, 1m, 24m, 0m, 0m, 0m, 24m, 0m, 25m, 18m, 4m, 0.15m, false)));
        var schedule = new SubscriptionScheduleService(fixture.Clock);
        return new SubscriptionFlowQueryService(
            fixture.Db,
            fixture.Eligibility,
            new MenuPlanResolutionService(fixture.Db),
            schedule,
            new BundleSlotRequirementService(fixture.Db),
            Substitute.For<IPaymentGatewayService>(),
            new AllowedSubscriptionDurationService(fixture.Db),
            new RestaurantTierClassificationService(fixture.Db, fixture.Clock),
            Substitute.For<ISubscriptionCheckoutReadinessService>(),
            new CustomerIndividualSubscriptionGate(fixture.Db),
            new RestaurantDiversificationCalculator(),
            sender,
            configuration: Substitute.For<IConfiguration>(),
            environment: Substitute.For<IHostEnvironment>(),
            couponService: null,
            availability: fixture.Availability);
    }

    private sealed record Fixture(
        AppDbContext Db,
        IDateTimeProvider Clock,
        ISubscriptionRestaurantAvailabilityService Availability,
        IRestaurantEligibilityService Eligibility,
        Guid CustomerId,
        Guid ProgramId,
        Guid BundleId,
        Guid RestaurantId);

    private static async Task<Fixture> SeedAsync(
        FailureMode failureMode,
        bool customerAllergy = false,
        bool withAddress = true,
        bool includeCoverageRegion = true,
        int extraRestaurants = 0)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var program = new Program { Code = "PROG_AVAIL", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "BND_AVAIL", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(), LegalNameAr = "مطعم التوافر", LegalNameEn = "Availability Restaurant",
            TradeNameAr = "مطعم التوافر", TradeNameEn = "Availability Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.AddRange(new CustomerProfile
        {
            UserId = customerId,
            AllergiesCompletedAtUtc = Now,
            DietaryPreferencesCompletedAtUtc = Now,
        }, program, bundle, restaurant);

        if (withAddress)
        {
            var address = new CustomerAddress
            {
                UserId = customerId, Label = "المنزل", AddressLine = "قطعة 1", RegionId = regionId, IsDefault = true,
            };
            db.CustomerAddresses.Add(address);
            var profile = await Task.FromResult(db.CustomerProfiles.Local.Single(x => x.UserId == customerId));
            profile.ActiveDeliveryAddressId = address.Id;
            profile.ActiveDeliveryRegionId = regionId;
            profile.ActiveDeliveryAddressLine = address.AddressLine;
        }

        if (includeCoverageRegion)
        {
            db.RestaurantServiceRegions.Add(new RestaurantServiceRegion { RestaurantId = restaurant.Id, RegionId = regionId, IsActive = true });
        }

        db.AddRange(
            new RestaurantProgramBundleOffer { RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, IsActive = true },
            new ClassificationRun { ProgramId = program.Id, BundleId = bundle.Id, ConfidenceLevel = "high", RunAtUtc = Now },
            new PricingRuleVersion
            {
                IsActive = true,
                RMax = Percentage.FromFraction(.3m),
                RMin = Percentage.FromFraction(.05m),
                DShort = 6, DLong = 26, Gamma = 1, RoundingStepKwd = .1m,
            },
            new ProgramMealSlotRequirement { ProgramId = program.Id, Slot = "lunch", IsRequired = true },
            new ProgramNutritionStandard { ProgramId = program.Id, MealCategory = "MAIN", KcalMin = 400, KcalMax = 600, ProteinGMin = 30, ProteinGMax = 50, CarbsGMin = 40, CarbsGMax = 60, FatGMin = 15, FatGMax = 25, FiberGMin = 5 });

        if (failureMode != FailureMode.NoPricing)
        {
            db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant.Id,
                ReferencePrice26 = new Money(104m, "KWD"), ContractedSupplyPrice26 = new Money(80m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = Now.AddDays(-1),
            });
        }

        if (failureMode is FailureMode.None or FailureMode.UnsafeMealOnly or FailureMode.NoCapacity)
        {
            var menu = new Menu { RestaurantId = restaurant.Id, ProgramId = program.Id, NameAr = "وجبات", NameEn = "Meals", IsActive = true };
            var allergenCode = failureMode == FailureMode.UnsafeMealOnly ? "MILK" : "RICE";
            var ingredient = new Ingredient { RestaurantId = restaurant.Id, Code = allergenCode, NameAr = "مكوّن", NameEn = "Ingredient", AllergenCodes = failureMode == FailureMode.UnsafeMealOnly ? "MILK" : null, OriginCountryCode = "KW", IsActive = true };
            var meal = new Meal
            {
                RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, MenuId = menu.Id, Menu = menu,
                Code = "M_AVAIL", NameAr = "وجبة", NameEn = "Meal", DescriptionAr = "وصف", DescriptionEn = "Description",
                Slot = "lunch", ApprovalStatus = VerificationStatus.Approved,
                Calories = 500, ProteinG = 40, CarbsG = 50, NetCarbsG = 50, FatG = 20, FiberG = 8,
                IsAvailableForNewOrders = true, AllergensReviewed = true, DataResponsibilityConfirmed = true,
            };
            db.AddRange(
                menu, ingredient, meal,
                new MealIngredient { MealId = meal.Id, Meal = meal, IngredientId = ingredient.Id, Ingredient = ingredient, PortionGrams = 100 },
                new MealImage { MealId = meal.Id, Meal = meal, ImageUrl = "/avail_meal.png", IsPrimary = true });
        }

        if (failureMode != FailureMode.NoCapacity)
        {
            db.DailyCapacities.Add(new DailyCapacity { RestaurantId = restaurant.Id, ServiceDate = new DateOnly(2026, 1, 1), CapacityType = CapacityType.Base, MaxBoxes = 10 });
        }

        db.RestaurantClassifications.Add(new RestaurantClassification
        {
            ClassificationRunId = db.ClassificationRuns.Local.Single(x => x.ProgramId == program.Id).Id,
            RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, Tier = RestaurantTier.Basic,
        });

        for (var index = 0; index < extraRestaurants; index++)
        {
            var extra = new RestaurantProfile
            {
                UserId = Guid.NewGuid(),
                LegalNameAr = $"مطعم إضافي {index}", LegalNameEn = $"Extra restaurant {index}",
                TradeNameAr = $"مطعم إضافي {index}", TradeNameEn = $"Extra restaurant {index}",
                VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
            };
            db.Add(extra);
            db.AddRange(
                new RestaurantProgramBundleOffer { RestaurantId = extra.Id, ProgramId = program.Id, BundleId = bundle.Id, IsActive = true },
                new RestaurantServiceRegion { RestaurantId = extra.Id, RegionId = regionId, IsActive = true },
                new RestaurantBundlePricing
                {
                    ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = extra.Id,
                    ReferencePrice26 = new Money(104m, "KWD"), ContractedSupplyPrice26 = new Money(80m, "KWD"),
                    LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = Now.AddDays(-1),
                },
                new RestaurantClassification
                {
                    ClassificationRunId = db.ClassificationRuns.Local.Single(x => x.ProgramId == program.Id).Id,
                    RestaurantId = extra.Id, ProgramId = program.Id, BundleId = bundle.Id, Tier = RestaurantTier.Basic,
                },
                new DailyCapacity { RestaurantId = extra.Id, ServiceDate = new DateOnly(2026, 1, 1), CapacityType = CapacityType.Base, MaxBoxes = 10 });
            var extraMenu = new Menu { RestaurantId = extra.Id, ProgramId = program.Id, NameAr = "وجبات", NameEn = "Meals", IsActive = true };
            var extraIngredient = new Ingredient { RestaurantId = extra.Id, Code = "RICE", NameAr = "أرز", NameEn = "Rice", OriginCountryCode = "KW", IsActive = true };
            var extraMeal = new Meal
            {
                RestaurantId = extra.Id, ProgramId = program.Id, BundleId = bundle.Id, MenuId = extraMenu.Id, Menu = extraMenu,
                Code = $"M_EXTRA_{index}", NameAr = "وجبة", NameEn = "Meal", DescriptionAr = "وصف", DescriptionEn = "Description",
                Slot = "lunch", ApprovalStatus = VerificationStatus.Approved,
                Calories = 500, ProteinG = 40, CarbsG = 50, NetCarbsG = 50, FatG = 20, FiberG = 8,
                IsAvailableForNewOrders = true, AllergensReviewed = true, DataResponsibilityConfirmed = true,
            };
            db.AddRange(
                extraMenu, extraIngredient, extraMeal,
                new MealIngredient { MealId = extraMeal.Id, Meal = extraMeal, IngredientId = extraIngredient.Id, Ingredient = extraIngredient, PortionGrams = 100 },
                new MealImage { MealId = extraMeal.Id, Meal = extraMeal, ImageUrl = $"/extra_{index}.png", IsPrimary = true });
        }

        if (customerAllergy)
        {
            db.CustomerAllergyPreferences.Add(new CustomerAllergyPreference
            {
                CustomerUserId = customerId, AllergenCode = "MILK", IsDislike = false,
            });
        }

        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        var deliveryAddresses = new CustomerDeliveryAddressService(db, clock);
        var schedule = new SubscriptionScheduleService(clock);
        var eligibility = new RestaurantEligibilityService(
            db, clock, schedule, new BundleSlotRequirementService(db), new MenuPlanResolutionService(db),
            new RestaurantTierClassificationService(db, clock), new RestaurantCapacityReader(db, clock));
        var availability = new SubscriptionRestaurantAvailabilityService(
            db, clock, deliveryAddresses, eligibility, new RestaurantTierClassificationService(db, clock));
        return new Fixture(db, clock, availability, eligibility, customerId, program.Id, bundle.Id, restaurant.Id);
    }
}
