using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Flow;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

/// <summary>
/// Catalog availability contract: active-but-unavailable programs, bundles and tiers stay
/// visible with a precise semantic status and CanSelect=false, while inactive/cancelled data
/// never reappears. Counts are distinct, pre-pagination, and computed as the union of a
/// program's bundles' restaurants; the general address coverage is reported separately from
/// any program/bundle condition.
/// </summary>
public sealed class SubscriptionCatalogAvailabilityTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 10, 12);

    [Fact]
    public async Task Programs_show_active_unavailable_options_with_union_counts()
    {
        var fx = await SeedBasicAsync(withAddress: true, coverRegion: true);
        // Second bundle of the same program re-offers the SAME restaurant (duplicate offer row):
        // the program count must stay the union (1), never the summed per-bundle counts (2+).
        await AddBundleAsync(fx.Db, fx.ProgramId, "BND2", withOffers: true, offerRestaurantId: fx.RestaurantId);
        await AddBundleAsync(fx.Db, fx.ProgramId, "BND3", withOffers: false);
        // A separate active program with a valid bundle but no offers anywhere.
        var ghostProgram = await AddProgramWithBundlesAsync(fx.Db, "GHOST", offers: false);
        // A cancelled program never comes back as an option.
        var deadProgram = await AddProgramWithBundlesAsync(fx.Db, "DEAD", offers: true, active: false);

        var envelope = await fx.Service.GetProgramsAsync(fx.CustomerId, page: null);

        envelope.IsSuccess.Should().BeTrue();
        var items = envelope.Value.Items;
        items.Select(x => x.Option.ProgramId).Should().NotContain(deadProgram).And.HaveCount(2);

        var covered = items.Single(x => x.Option.ProgramId == fx.ProgramId);
        covered.Availability.Status.Should().Be(SubscriptionCatalogAvailabilityStatus.SelectionRequired);
        covered.Availability.Scope.Should().Be("coverage");
        covered.Availability.CanSelect.Should().BeTrue();
        covered.Availability.CoveredRestaurantCount.Should().Be(1, "union of the bundles' offered restaurants");
        covered.Availability.EligibleRestaurantCount.Should().BeNull("period eligibility was not evaluated in coverage scope");
        covered.Option.BundleCount.Should().Be(3, "BND1, BND2 and the offerless BND3 are all slot-valid active bundles");

        var ghost = items.Single(x => x.Option.ProgramId == ghostProgram);
        ghost.Availability.Status.Should().Be(SubscriptionCatalogAvailabilityStatus.NoRestaurantsForAddress);
        ghost.Availability.CanSelect.Should().BeFalse();
        ghost.Availability.CoveredRestaurantCount.Should().Be(0);
        ghost.Availability.MessageCode.Should().Be(SubscriptionCheckoutGateCodes.NoRestaurantsForAddress);

        envelope.Value.HasDeliveryAddress.Should().BeTrue();
        envelope.Value.AddressCoverage.Status.Should().Be(SubscriptionCatalogAvailabilityStatus.Available);
        envelope.Value.AddressCoverage.CoveredRestaurantCount.Should().Be(1);
    }

    [Fact]
    public async Task Programs_report_zero_general_coverage_separately_from_program_coverage()
    {
        var fx = await SeedBasicAsync(withAddress: true, coverRegion: false);
        // A different restaurant covers the address but offers nothing: the address itself is
        // deliverable while this program has no coverage.
        await AddApprovedRestaurantAsync(fx.Db, fx.RegionId);

        var envelope = await fx.Service.GetProgramsAsync(fx.CustomerId, page: null);

        envelope.Value.AddressCoverage.Status.Should().Be(SubscriptionCatalogAvailabilityStatus.Available);
        envelope.Value.AddressCoverage.CoveredRestaurantCount.Should().Be(1);

        var program = envelope.Value.Items.Single();
        program.Availability.Status.Should().Be(SubscriptionCatalogAvailabilityStatus.NoRestaurantsForAddress);
        program.Availability.CanSelect.Should().BeFalse();
    }

    [Fact]
    public async Task Programs_without_address_report_address_required_and_stay_navigable()
    {
        var fx = await SeedBasicAsync(withAddress: false, coverRegion: true);

        var envelope = await fx.Service.GetProgramsAsync(fx.CustomerId, page: null);

        envelope.Value.HasDeliveryAddress.Should().BeFalse();
        envelope.Value.AddressCoverage.Status.Should().Be(SubscriptionCatalogAvailabilityStatus.AddressRequired);
        // No "no delivery" message may be shown without an address.
        envelope.Value.AddressCoverage.MessageCode.Should().Be("checkout.address_required");

        var program = envelope.Value.Items.Single();
        program.Availability.Status.Should().Be(SubscriptionCatalogAvailabilityStatus.AddressRequired);
        program.Availability.CanSelect.Should().BeTrue();
        program.Availability.CoveredRestaurantCount.Should().Be(1, "unfiltered offering count without an address");
    }

    [Fact]
    public async Task Bundles_show_zero_coverage_bundle_disabled_and_count_before_pagination()
    {
        var fx = await SeedBasicAsync(withAddress: true, coverRegion: true);
        await AddBundleAsync(fx.Db, fx.ProgramId, "BND2", withOffers: false);
        // Slot-invalid bundle is a data error, not a customer option: never displayed.
        await AddBundleAsync(fx.Db, fx.ProgramId, "BND_BAD", withOffers: true, offerRestaurantId: fx.RestaurantId, validSlots: false);

        var envelope = await fx.Service.GetBundlesAsync(fx.ProgramId, fx.CustomerId, page: null);

        envelope.IsSuccess.Should().BeTrue();
        var items = envelope.Value.Items;
        items.Should().HaveCount(2, "the slot-invalid bundle is not an option");
        items.Select(x => x.Option.Code).Should().NotContain("BND_BAD");

        var covered = items.Single(x => x.Option.Code == "BND1");
        covered.Availability.Status.Should().Be(SubscriptionCatalogAvailabilityStatus.SelectionRequired);
        covered.Availability.CanSelect.Should().BeTrue();
        covered.Option.RestaurantCount.Should().Be(1);
        covered.Option.Restaurants.Should().ContainSingle(x => x.RestaurantId == fx.RestaurantId);

        var empty = items.Single(x => x.Option.Code == "BND2");
        empty.Availability.Status.Should().Be(SubscriptionCatalogAvailabilityStatus.NoRestaurantsForAddress);
        empty.Availability.CanSelect.Should().BeFalse();
        empty.Availability.CoveredRestaurantCount.Should().Be(0);

        var paged = await fx.Service.GetBundlesAsync(fx.ProgramId, fx.CustomerId, new PageRequest { Page = 1, PageSize = 1 });
        paged.Value.Pagination.Should().NotBeNull();
        paged.Value.Pagination!.TotalCount.Should().Be(2, "totalCount is the full set before pagination");
        paged.Value.Pagination.HasNextPage.Should().BeTrue();
        paged.Value.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Bundles_exclude_restaurants_that_do_not_cover_the_active_address()
    {
        var fx = await SeedBasicAsync(withAddress: true, coverRegion: false);
        var response = await fx.Service.GetBundlesAsync(fx.ProgramId, fx.CustomerId, page: null);

        response.IsSuccess.Should().BeTrue();
        var bundle = response.Value.Items.Single();
        bundle.Availability.CanSelect.Should().BeFalse();
        bundle.Availability.CoveredRestaurantCount.Should().Be(0);
        bundle.Option.RestaurantCount.Should().Be(0);
        bundle.Option.Restaurants.Should().BeEmpty();
    }

    [Fact]
    public async Task Bundles_preview_only_covered_restaurants_when_other_restaurants_also_offer_it()
    {
        var fx = await SeedBasicAsync(withAddress: true, coverRegion: true);
        var uncoveredId = await AddApprovedRestaurantAsync(fx.Db, Guid.NewGuid());
        fx.Db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
        {
            ProgramId = fx.ProgramId, BundleId = fx.BundleId, RestaurantId = uncoveredId, IsActive = true
        });
        await fx.Db.SaveChangesAsync();

        var result = await fx.Service.GetBundlesAsync(fx.ProgramId, fx.CustomerId, null);
        var card = result.Value.Items.Single();
        card.Availability.CanSelect.Should().BeTrue();
        card.Availability.CoveredRestaurantCount.Should().Be(1);
        card.Option.RestaurantCount.Should().Be(1);
        card.Option.Restaurants.Should().ContainSingle(x => x.RestaurantId == fx.RestaurantId);
    }

    [Fact]
    public async Task Tiers_coverage_scope_shows_all_sale_tiers_with_the_current_access_rule()
    {
        var fx = await SeedBasicAsync(withAddress: true, coverRegion: true, classifyRestaurant: true);
        var service = CreateService(fx, catalogTiers: new[] { RestaurantTier.Basic, RestaurantTier.Elite });

        var envelope = await service.GetTiersAsync(fx.ProgramId, fx.BundleId, fx.CustomerId,
            durationDays: null, includeFriday: false, startDate: null, page: null);

        envelope.IsSuccess.Should().BeTrue();
        envelope.Value.PeriodContextComplete.Should().BeFalse();
        var items = envelope.Value.Items;
        items.Select(x => x.Option.Tier).Should().BeEquivalentTo(new[] { RestaurantTier.Basic, RestaurantTier.Elite });

        // The current rule lets a higher tier use lower-tier restaurants, so Elite sees the
        // Basic-classified restaurant too; matching literally would be a separate product call.
        var basic = items.Single(x => x.Option.Tier == RestaurantTier.Basic);
        basic.Availability.Status.Should().Be(SubscriptionCatalogAvailabilityStatus.SelectionRequired);
        basic.Availability.Scope.Should().Be("coverage");
        basic.Availability.CanSelect.Should().BeTrue();
        basic.Availability.CoveredRestaurantCount.Should().Be(1);
        basic.Availability.EligibleRestaurantCount.Should().BeNull();

        var elite = items.Single(x => x.Option.Tier == RestaurantTier.Elite);
        elite.Availability.CanSelect.Should().BeTrue();
        elite.Availability.CoveredRestaurantCount.Should().Be(1);
        // No duration chosen yet: prices stay unset, never zero.
        elite.Option.TotalPrice.Should().BeNull();
        elite.Option.CurrencyCode.Should().BeNull();
    }

    [Fact]
    public async Task Tiers_without_any_allowed_restaurant_stay_visible_and_disabled()
    {
        var fx = await SeedBasicAsync(withAddress: true, coverRegion: true, classifyRestaurant: false);
        var service = CreateService(fx, catalogTiers: new[] { RestaurantTier.Basic, RestaurantTier.Elite });

        var envelope = await service.GetTiersAsync(fx.ProgramId, fx.BundleId, fx.CustomerId,
            durationDays: null, includeFriday: false, startDate: null, page: null);

        envelope.Value.Items.Should().HaveCount(2);
        envelope.Value.Items.Should().OnlyContain(x =>
            x.Availability.Status == SubscriptionCatalogAvailabilityStatus.NoRestaurantsForAddress
            && !x.Availability.CanSelect
            && x.Availability.CoveredRestaurantCount == 0);
    }

    [Fact]
    public async Task Tiers_period_scope_attaches_evaluator_verdicts_once_per_tier()
    {
        var fx = await SeedBasicAsync(withAddress: true, coverRegion: true, classifyRestaurant: true);
        var availability = Substitute.For<ISubscriptionRestaurantAvailabilityService>();
        availability.EvaluateDetailedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), RestaurantTier.Basic,
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(MakeReport(IndividualRestaurantAvailabilityStatus.Available, covered: 1, eligible: 3, messageCode: null));
        availability.EvaluateDetailedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), RestaurantTier.Elite,
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(MakeReport(IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection, covered: 1, eligible: 0,
                messageCode: SubscriptionCheckoutGateCodes.NoRestaurantsForSelection));
        var service = CreateService(fx, catalogTiers: new[] { RestaurantTier.Basic, RestaurantTier.Elite }, availability: availability);

        var envelope = await service.GetTiersAsync(fx.ProgramId, fx.BundleId, fx.CustomerId,
            durationDays: 6, includeFriday: false, startDate: StartDate, page: null);

        envelope.Value.PeriodContextComplete.Should().BeTrue();
        var basic = envelope.Value.Items.Single(x => x.Option.Tier == RestaurantTier.Basic);
        basic.Availability.Scope.Should().Be("period");
        basic.Availability.Status.Should().Be(IndividualRestaurantAvailabilityStatus.Available);
        basic.Availability.EligibleRestaurantCount.Should().Be(3);
        basic.Availability.CanSelect.Should().BeTrue();

        var elite = envelope.Value.Items.Single(x => x.Option.Tier == RestaurantTier.Elite);
        elite.Availability.Status.Should().Be(IndividualRestaurantAvailabilityStatus.NoRestaurantsForSelection);
        elite.Availability.EligibleRestaurantCount.Should().Be(0);
        elite.Availability.CanSelect.Should().BeFalse();

        // The chosen start date is passed through as itself, once per displayed tier.
        await availability.Received(1).EvaluateDetailedAsync(
            fx.CustomerId, fx.ProgramId, fx.BundleId, RestaurantTier.Basic, 6, false, StartDate, Arg.Any<CancellationToken>());
        await availability.Received(1).EvaluateDetailedAsync(
            fx.CustomerId, fx.ProgramId, fx.BundleId, RestaurantTier.Elite, 6, false, StartDate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tiers_missing_sale_price_report_pricing_unavailable_without_zero_price()
    {
        var fx = await SeedBasicAsync(withAddress: true, coverRegion: true, classifyRestaurant: true);
        var sender = Substitute.For<ISender>();
        IReadOnlyList<CustomerSubscriptionCatalogItemDto> catalog =
        [
            MakeCatalogItem(fx.ProgramId, fx.BundleId, RestaurantTier.Basic, priced: true),
            MakeCatalogItem(fx.ProgramId, fx.BundleId, RestaurantTier.Elite, priced: false),
        ];
        sender.Send(Arg.Any<GetCustomerSubscriptionPricingCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<CustomerSubscriptionCatalogItemDto>>.Success(catalog));
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PricingEngineResult>(new Error("pricing_unavailable", null, ErrorType.Conflict)));
        var service = CreateService(fx, catalogTiers: null, sender: sender);

        var envelope = await service.GetTiersAsync(fx.ProgramId, fx.BundleId, fx.CustomerId,
            durationDays: 6, includeFriday: false, startDate: StartDate, page: null);

        var basic = envelope.Value.Items.Single(x => x.Option.Tier == RestaurantTier.Basic);
        basic.Availability.CanSelect.Should().BeTrue();
        basic.Option.TotalPrice.Should().BeGreaterThan(0m);

        var elite = envelope.Value.Items.Single(x => x.Option.Tier == RestaurantTier.Elite);
        elite.Availability.Status.Should().Be(SubscriptionCatalogAvailabilityStatus.PricingUnavailable);
        elite.Availability.CanSelect.Should().BeFalse();
        elite.Option.TotalPrice.Should().BeNull("a missing price is never fabricated as zero");
        elite.Option.CurrencyCode.Should().BeNull();
    }

    // ---- helpers ----

    private sealed record CatalogFixture(
        AppDbContext Db,
        IDateTimeProvider Clock,
        Guid CustomerId,
        Guid RegionId,
        Guid RestaurantId,
        Guid ProgramId,
        Guid BundleId,
        ISubscriptionCatalogAvailabilityService Service);

    private static IndividualRestaurantAvailabilityReport MakeReport(
        string status, int covered, int eligible, string? messageCode)
        => new(
            new IndividualRestaurantAvailabilityDto(status, covered, eligible, Now, StartDate, messageCode),
            [],
            new Dictionary<Guid, RestaurantTier>(),
            Guid.NewGuid());

    private static CustomerSubscriptionCatalogItemDto MakeCatalogItem(
        Guid programId, Guid bundleId, RestaurantTier tier, bool priced)
        => new(
            programId, "برنامج", "Program", bundleId, "باقة", "Bundle",
            tier, "اسم الفئة", "Tier name",
            priced ? [new CustomerSubscriptionDurationPriceDto(6, 24m, 4m)] : [],
            CurrencyCode: "KWD");

    private static ISubscriptionCatalogAvailabilityService CreateService(
        CatalogFixture fx,
        IReadOnlyList<RestaurantTier>? catalogTiers = null,
        ISender? sender = null,
        ISubscriptionRestaurantAvailabilityService? availability = null)
    {
        var senderCreatedInternally = false;
        if (sender is null)
        {
            sender = Substitute.For<ISender>();
            senderCreatedInternally = true;
        }
        if (catalogTiers is not null)
        {
            IReadOnlyList<CustomerSubscriptionCatalogItemDto> catalog = catalogTiers
                .Select(t => MakeCatalogItem(fx.ProgramId, fx.BundleId, t, priced: true))
                .ToList();
            sender.Send(Arg.Any<GetCustomerSubscriptionPricingCatalogQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result<IReadOnlyList<CustomerSubscriptionCatalogItemDto>>.Success(catalog));
        }

        if (senderCreatedInternally)
        {
            sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result<PricingEngineResult>.Success(
                    new PricingEngineResult(4m, 6, 1m, 24m, 0m, 0m, 0m, 24m, 0m, 25m, 18m, 4m, 0.15m, false)));
        }

        // Defaults only apply to dependencies this helper created; caller-supplied mocks keep
        // their own specific setups.
        if (availability is null)
        {
            availability = Substitute.For<ISubscriptionRestaurantAvailabilityService>();
            availability.EvaluateDetailedAsync(
                    Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<RestaurantTier>(),
                    Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
                .Returns(MakeReport(IndividualRestaurantAvailabilityStatus.Available, 1, 1, null));
        }

        return new SubscriptionCatalogAvailabilityService(
            fx.Db,
            fx.Clock,
            new CustomerDeliveryAddressService(fx.Db, fx.Clock),
            new RestaurantTierClassificationService(fx.Db, fx.Clock),
            new AllowedSubscriptionDurationService(fx.Db),
            availability,
            sender);
    }

    // Convenience overload used by most tests: same defaults as CreateService above.
    private static ISubscriptionCatalogAvailabilityService DefaultService(CatalogFixture fx)
        => CreateService(fx, catalogTiers: new[] { RestaurantTier.Basic });

    private static async Task<CatalogFixture> SeedBasicAsync(bool withAddress, bool coverRegion, bool classifyRestaurant = true)
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var customerId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var restaurant = await AddApprovedRestaurantAsync(db, coverRegion ? regionId : null);
        var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "PROG_CAT", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = "BND1", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        db.AddRange(program, bundle);

        var profile = new CustomerProfile
        {
            UserId = customerId,
            AllergiesCompletedAtUtc = Now,
            DietaryPreferencesCompletedAtUtc = Now,
        };
        if (withAddress)
        {
            var address = new CustomerAddress
            {
                UserId = customerId, Label = "المنزل", AddressLine = "قطعة 1", RegionId = regionId, IsDefault = true,
            };
            db.CustomerAddresses.Add(address);
            profile.ActiveDeliveryAddressId = address.Id;
            profile.ActiveDeliveryRegionId = regionId;
            profile.ActiveDeliveryAddressLine = address.AddressLine;
        }
        db.CustomerProfiles.Add(profile);

        db.AddRange(
            new RestaurantProgramBundleOffer { RestaurantId = restaurant, ProgramId = program.Id, BundleId = bundle.Id, IsActive = true },
            new ProgramMealSlotRequirement { ProgramId = program.Id, Slot = "lunch", IsRequired = true },
            new PricingRuleVersion
            {
                IsActive = true,
                RMax = Percentage.FromFraction(.3m),
                RMin = Percentage.FromFraction(.05m),
                DShort = 6, DLong = 26, Gamma = 1, RoundingStepKwd = .1m,
            });

        // Tier classification is derived from sale pricing rows, so a tier set with no allowed
        // restaurants is modeled by seeding no pricing row at all.
        if (classifyRestaurant)
        {
            db.Add(new RestaurantBundlePricing
            {
                ProgramId = program.Id, BundleId = bundle.Id, RestaurantId = restaurant,
                ReferencePrice26 = new Money(104m, "KWD"), ContractedSupplyPrice26 = new Money(80m, "KWD"),
                LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = Now.AddDays(-1),
            });
        }

        if (classifyRestaurant)
        {
            var run = new ClassificationRun { ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle, RestaurantCount = 1, ConfidenceLevel = "95", RunAtUtc = Now };
            db.Add(run);
            db.RestaurantClassifications.Add(new RestaurantClassification
            {
                ClassificationRunId = run.Id, ClassificationRun = run, RestaurantId = restaurant,
                ProgramId = program.Id, Program = program, BundleId = bundle.Id, Bundle = bundle,
                Tier = RestaurantTier.Basic,
            });
        }

        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(Now));

        // Most tests only need the single seeded tier; multi-tier tests pass their own catalog.
        var sender = Substitute.For<ISender>();
        IReadOnlyList<CustomerSubscriptionCatalogItemDto> seededCatalog =
        [
            MakeCatalogItem(program.Id, bundle.Id, RestaurantTier.Basic, priced: true),
        ];
        sender.Send(Arg.Any<GetCustomerSubscriptionPricingCatalogQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<CustomerSubscriptionCatalogItemDto>>.Success(seededCatalog));
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PricingEngineResult>.Success(
                new PricingEngineResult(4m, 6, 1m, 24m, 0m, 0m, 0m, 24m, 0m, 25m, 18m, 4m, 0.15m, false)));
        var availability = Substitute.For<ISubscriptionRestaurantAvailabilityService>();
        availability.EvaluateDetailedAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<RestaurantTier>(),
                Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>())
            .Returns(MakeReport(IndividualRestaurantAvailabilityStatus.Available, 1, 1, null));

        var service = new SubscriptionCatalogAvailabilityService(
            db, clock,
            new CustomerDeliveryAddressService(db, clock),
            new RestaurantTierClassificationService(db, clock),
            new AllowedSubscriptionDurationService(db),
            availability,
            sender);

        return new CatalogFixture(db, clock, customerId, regionId, restaurant, program.Id, bundle.Id, service);
    }

    private static async Task<Guid> AddApprovedRestaurantAsync(AppDbContext db, Guid? regionId)
    {
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(),
            LegalNameAr = "مطعم كتالوج", LegalNameEn = "Catalog Restaurant",
            TradeNameAr = "مطعم كتالوج", TradeNameEn = "Catalog Restaurant",
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
        };
        db.RestaurantProfiles.Add(restaurant);
        if (regionId is { } region)
            db.RestaurantServiceRegions.Add(new RestaurantServiceRegion { RestaurantId = restaurant.Id, RegionId = region, IsActive = true });
        await db.SaveChangesAsync();
        return restaurant.Id;
    }

    private static async Task AddBundleAsync(
        AppDbContext db, Guid programId, string code, bool withOffers, Guid? offerRestaurantId = null, bool validSlots = true)
    {
        var bundle = new Bundle
        {
            ProgramId = programId, Code = code, NameAr = "باقة", NameEn = "Bundle",
            MealsPerDay = validSlots ? 1 : 0,
            LunchCount = validSlots ? 1 : 0,
            Status = ProgramStatus.Active, IsActive = true,
        };
        db.Bundles.Add(bundle);
        if (withOffers)
        {
            db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                RestaurantId = offerRestaurantId!.Value,
                ProgramId = programId, BundleId = bundle.Id, IsActive = true,
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> AddProgramWithBundlesAsync(AppDbContext db, string code, bool offers, bool active = true)
    {
        var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = code, NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = active };
        var bundle = new Bundle { ProgramId = program.Id, Program = program, Code = $"{code}-B", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1, Status = ProgramStatus.Active, IsActive = true };
        db.AddRange(program, bundle);
        if (offers)
        {
            var restaurant = new RestaurantProfile
            {
                UserId = Guid.NewGuid(), LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
                TradeNameAr = "مطعم", TradeNameEn = "Restaurant",
                VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true,
            };
            db.Add(restaurant);
            db.Add(new RestaurantProgramBundleOffer { RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, IsActive = true });
        }
        await db.SaveChangesAsync();
        return program.Id;
    }
}
