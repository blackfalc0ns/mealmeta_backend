using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

/// <summary>
/// The availability verdict must explain why covered restaurants fail the period: FailureReasons
/// maps eligibility reason codes to distinct-restaurant counts so the client can render the
/// cause, and stays null when nothing fails. Codes are identifiers, never product text.
/// </summary>
public sealed class SubscriptionRestaurantAvailabilityServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 10, 13);

    [Fact]
    public async Task Zero_eligible_periods_report_failure_reasons_with_distinct_restaurant_counts()
    {
        var fx = await SeedAsync();
        var menuBroken = new RestaurantEligibilityDto(fx.RestaurantId, "مطعم", "Restaurant", false,
            ["menu_slot_incomplete", "capacity_unavailable"], []);
        var higherTier = new RestaurantEligibilityDto(Guid.NewGuid(), "مطعم ٢", "Restaurant 2", false,
            ["tier_mismatch"], []);
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[] { menuBroken, higherTier });

        var report = await CreateService(fx, eligibility).EvaluateDetailedAsync(
            fx.CustomerId, fx.ProgramId, fx.BundleId, RestaurantTier.Basic, 26, false, StartDate);

        report.Availability.Status.Should().Be(IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection);
        report.Availability.FailureReasons.Should().NotBeNull("a covered-but-unservable period must explain itself");
        report.Availability.FailureReasons!["menu_slot_incomplete"].Should().Be(1);
        report.Availability.FailureReasons!["capacity_unavailable"].Should().Be(1);
        report.Availability.FailureReasons!["tier_mismatch"].Should().Be(1);
    }

    [Fact]
    public async Task Same_reason_across_restaurants_counts_restaurants_not_occurrences()
    {
        var fx = await SeedAsync();
        var first = new RestaurantEligibilityDto(fx.RestaurantId, "مطعم", "Restaurant", false,
            ["menu_plan_missing"], []);
        var second = new RestaurantEligibilityDto(Guid.NewGuid(), "مطعم ٢", "Restaurant 2", false,
            ["menu_plan_missing"], []);
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[] { first, second });

        var report = await CreateService(fx, eligibility).EvaluateDetailedAsync(
            fx.CustomerId, fx.ProgramId, fx.BundleId, RestaurantTier.Basic, 26, false, StartDate);

        report.Availability.FailureReasons.Should().NotBeNull();
        report.Availability.FailureReasons!.Should().HaveCount(1);
        report.Availability.FailureReasons["menu_plan_missing"].Should().Be(2);
    }

    [Fact]
    public async Task Partial_eligibility_still_reports_the_ineligible_restaurants_reasons()
    {
        var fx = await SeedAsync();
        var eligible = new RestaurantEligibilityDto(fx.RestaurantId, "مطعم", "Restaurant", true, [],
            [new EligibleMenuDateDto(StartDate, Guid.NewGuid())]);
        var ineligible = new RestaurantEligibilityDto(Guid.NewGuid(), "مطعم ٢", "Restaurant 2", false,
            ["restaurant_closed"], []);
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[] { eligible, ineligible });

        var report = await CreateService(fx, eligibility).EvaluateDetailedAsync(
            fx.CustomerId, fx.ProgramId, fx.BundleId, RestaurantTier.Basic, 26, false, StartDate);

        report.Availability.Status.Should().Be(IndividualRestaurantAvailabilityStatus.Available);
        report.Availability.EligibleRestaurantCount.Should().Be(1);
        report.Availability.FailureReasons.Should().NotBeNull();
        report.Availability.FailureReasons!["restaurant_closed"].Should().Be(1);
    }

    [Fact]
    public async Task Fully_eligible_periods_report_no_failure_reasons()
    {
        var fx = await SeedAsync();
        var eligible = new RestaurantEligibilityDto(fx.RestaurantId, "مطعم", "Restaurant", true, [],
            [new EligibleMenuDateDto(StartDate, Guid.NewGuid())]);
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[] { eligible });

        var report = await CreateService(fx, eligibility).EvaluateDetailedAsync(
            fx.CustomerId, fx.ProgramId, fx.BundleId, RestaurantTier.Basic, 26, false, StartDate);

        report.Availability.Status.Should().Be(IndividualRestaurantAvailabilityStatus.Available);
        report.Availability.FailureReasons.Should().BeNull("nothing failed, so no reason dictionary is emitted");
    }

    [Fact]
    public async Task Missing_start_date_stays_in_selection_required_without_failure_reasons()
    {
        var fx = await SeedAsync();

        var report = await CreateService(fx).EvaluateDetailedAsync(
            fx.CustomerId, fx.ProgramId, fx.BundleId, RestaurantTier.Basic, 26, false, startDate: null);

        report.Availability.Status.Should().Be(IndividualRestaurantAvailabilityStatus.SelectionRequired);
        report.Availability.FailureReasons.Should().BeNull("eligibility was never evaluated without a start date");
    }

    // ---- helpers ----

    private sealed record AvailabilityFixture(
        AppDbContext Db, Guid CustomerId, Guid RegionId, Guid RestaurantId, Guid ProgramId, Guid BundleId);

    private static SubscriptionRestaurantAvailabilityService CreateService(
        AvailabilityFixture fx, IRestaurantEligibilityService? eligibility = null)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(Now));
        var tiers = Substitute.For<IRestaurantTierClassificationService>();
        tiers.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new RestaurantTierClassificationResult(
                fx.ProgramId, fx.BundleId, 4m, 0.5m, 3m, 5m,
                [new RestaurantTierAssignment(fx.RestaurantId, RestaurantTier.Basic, 104m, 4m, false, null)]));
        return new SubscriptionRestaurantAvailabilityService(
            fx.Db, clock, new CustomerDeliveryAddressService(fx.Db, clock),
            eligibility ?? Substitute.For<IRestaurantEligibilityService>(), tiers);
    }

    private static async Task<AvailabilityFixture> SeedAsync()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var restaurantId = await AddApprovedRestaurantAsync(db, regionId);
        var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "PROG_AVL", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "BND_AVL", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        var profile = new CustomerProfile
        {
            UserId = customerId,
            AllergiesCompletedAtUtc = Now,
            DietaryPreferencesCompletedAtUtc = Now,
        };
        var address = new CustomerAddress
        {
            UserId = customerId, Label = "المنزل", AddressLine = "قطعة 1", RegionId = regionId, IsDefault = true,
        };
        db.CustomerAddresses.Add(address);
        profile.ActiveDeliveryAddressId = address.Id;
        profile.ActiveDeliveryRegionId = regionId;
        profile.ActiveDeliveryAddressLine = address.AddressLine;
        db.CustomerProfiles.Add(profile);
        db.AddRange(
            program,
            bundle,
            new RestaurantProgramBundleOffer { RestaurantId = restaurantId, ProgramId = program.Id, BundleId = bundle.Id, IsActive = true });
        await db.SaveChangesAsync();
        return new AvailabilityFixture(db, customerId, regionId, restaurantId, program.Id, bundle.Id);
    }

    private static async Task<Guid> AddApprovedRestaurantAsync(AppDbContext db, Guid regionId)
    {
        var restaurant = new RestaurantProfile
        {
            TradeNameAr = "مطعم", TradeNameEn = "Restaurant", LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.Add(restaurant);
        db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
        {
            RestaurantId = restaurant.Id, RegionId = regionId, IsActive = true,
        });
        await db.SaveChangesAsync();
        return restaurant.Id;
    }
}
