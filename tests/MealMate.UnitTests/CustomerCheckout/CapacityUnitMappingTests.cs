using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.CollaborativeCampaigns;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.CustomerCheckout;

/// <summary>
/// Plan task 1 fixtures (restaurant-service-availability plan): pins the confirmed
/// capacity unit rule — one unit per member/subscription/service-date at the chosen
/// restaurant, family or individual — documents the exact hold/day state mapping
/// used by the booking gate, and demonstrates the campaign-ledger split and the
/// ghost-hold divergence that the unified capacity reader (task 2) must resolve.
/// The concurrency guarantees themselves are PostgreSQL tests owned by task 2.
/// </summary>
public sealed class CapacityUnitMappingTests
{
    private static readonly DateOnly ServiceDate = new(2026, 8, 17); // Monday
    private static readonly DateTime Now = new(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, true)]
    public async Task Five_family_members_at_one_restaurant_consume_exactly_five_units(int limitBoxes, bool expectedEligible)
    {
        await using var db = CreateDb();
        var (program, bundle, regionId) = await SeedCatalogAsync(db, mealsPerDay: 1);
        var (restaurant, versionId, _) = await SeedEligibleRestaurantAsync(db, program, bundle, regionId, "R1", limitBoxes);

        var familyGroupId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            CommitUnit(db, restaurant.Id, program.Id, bundle.Id, versionId, familyGroupId, Guid.NewGuid());
        }
        await db.SaveChangesAsync();

        var candidate = Assert.Single(await EvaluateCandidateAsync(db, program, bundle, regionId));
        Assert.Equal(expectedEligible, candidate.IsEligible);
        if (!expectedEligible)
        {
            Assert.Contains(EligibilityReasonCodes.CapacityUnavailable, candidate.ReasonCodes);
        }
    }

    [Fact]
    public async Task Split_three_and_two_across_two_restaurants_consumes_three_and_two()
    {
        await using var db = CreateDb();
        var (program, bundle, regionId) = await SeedCatalogAsync(db, mealsPerDay: 1);
        var (first, firstVersionId, _) = await SeedEligibleRestaurantAsync(db, program, bundle, regionId, "R1", limitBoxes: 4);
        var (second, secondVersionId, _) = await SeedEligibleRestaurantAsync(db, program, bundle, regionId, "R2", limitBoxes: 2);

        for (var i = 0; i < 3; i++)
        {
            CommitUnit(db, first.Id, program.Id, bundle.Id, firstVersionId, familyGroupId: null, Guid.NewGuid());
        }
        for (var i = 0; i < 2; i++)
        {
            CommitUnit(db, second.Id, program.Id, bundle.Id, secondVersionId, familyGroupId: null, Guid.NewGuid());
        }
        await db.SaveChangesAsync();

        var rows = await EvaluateCandidateAsync(db, program, bundle, regionId);
        var atFirst = Assert.Single(rows, x => x.RestaurantId == first.Id);
        var atSecond = Assert.Single(rows, x => x.RestaurantId == second.Id);

        // Three units are consumed at the first restaurant and two at the second;
        // neither restaurant sees the other's units.
        Assert.True(atFirst.IsEligible);
        Assert.False(atSecond.IsEligible);
        Assert.Contains(EligibilityReasonCodes.CapacityUnavailable, atSecond.ReasonCodes);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task Multi_meal_bundle_day_still_consumes_one_unit(int limitBoxes, bool expectedEligible)
    {
        await using var db = CreateDb();
        var (program, bundle, regionId) = await SeedCatalogAsync(db, mealsPerDay: 3);
        var (restaurant, versionId, mealId) = await SeedEligibleRestaurantAsync(db, program, bundle, regionId, "R1", limitBoxes, lunchMealCount: 3);

        var (subscription, subscriptionDay) = CommitUnit(db, restaurant.Id, program.Id, bundle.Id, versionId, familyGroupId: null, Guid.NewGuid());
        db.AddRange(Enumerable.Range(1, 3).Select(sortOrder => new DailyMealSelection
        {
            SubscriptionDayId = subscriptionDay.Id,
            MealId = mealId,
            ResolvedMenuPlanVersionId = versionId,
            ResolvedProgramId = program.Id,
            ScheduleMode = ScheduleMode.Cycle,
            SortOrder = sortOrder,
            SelectedAtUtc = Now
        }));
        await db.SaveChangesAsync();

        var candidate = Assert.Single(await EvaluateCandidateAsync(db, program, bundle, regionId));
        Assert.Equal(expectedEligible, candidate.IsEligible);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public async Task Hold_states_map_to_units_exactly_as_documented(int limitBoxes, bool expectedEligible)
    {
        await using var db = CreateDb();
        var (program, bundle, regionId) = await SeedCatalogAsync(db, mealsPerDay: 1);
        var (restaurant, versionId, _) = await SeedEligibleRestaurantAsync(db, program, bundle, regionId, "R1", limitBoxes);

        // Counts one: a fulfilled identity (day + Confirmed hold) — the Confirmed hold itself is not re-counted.
        CommitUnit(db, restaurant.Id, program.Id, bundle.Id, versionId, familyGroupId: null, Guid.NewGuid());
        // Counts one: live PendingPayment hold without a day.
        CommitHold(db, restaurant.Id, program.Id, bundle.Id, versionId, CapacityHoldStatus.PendingPayment, Now.AddMinutes(10));
        // Counts one: PaymentConfirmed hold, even past its own expiry timestamp.
        CommitHold(db, restaurant.Id, program.Id, bundle.Id, versionId, CapacityHoldStatus.PaymentConfirmed, Now.AddMinutes(-10));
        // Counts zero: Released, expired PendingPayment, and terminal Expired holds.
        CommitHold(db, restaurant.Id, program.Id, bundle.Id, versionId, CapacityHoldStatus.Released, Now.AddMinutes(-10));
        CommitHold(db, restaurant.Id, program.Id, bundle.Id, versionId, CapacityHoldStatus.PendingPayment, Now.AddMinutes(-10));
        CommitHold(db, restaurant.Id, program.Id, bundle.Id, versionId, CapacityHoldStatus.Expired, Now.AddMinutes(-10));
        await db.SaveChangesAsync();

        var candidate = Assert.Single(await EvaluateCandidateAsync(db, program, bundle, regionId));
        Assert.Equal(expectedEligible, candidate.IsEligible);
    }

    [Fact]
    public async Task Campaign_ledger_is_invisible_to_general_gate_until_purchase_completes()
    {
        await using var db = CreateDb();
        var (program, bundle, regionId) = await SeedCatalogAsync(db, mealsPerDay: 1);
        var (restaurant, versionId, _) = await SeedEligibleRestaurantAsync(db, program, bundle, regionId, "R1", limitBoxes: 1);

        var campaignId = Guid.NewGuid();
        db.AddRange(
            new CampaignRestaurantCapacity { CampaignId = campaignId, RestaurantId = restaurant.Id, ServiceDate = ServiceDate, MaxBoxes = 1, ReservedBoxes = 1 },
            new CampaignReservationAllocation { CheckoutSessionId = Guid.NewGuid(), CampaignId = campaignId, BundleId = bundle.Id, ServiceDate = ServiceDate, RestaurantId = restaurant.Id, Units = 1 });
        await db.SaveChangesAsync();

        var beforePurchase = Assert.Single(await EvaluateCandidateAsync(db, program, bundle, regionId));
        Assert.True(beforePurchase.IsEligible);

        // Completed campaign purchases create SubscriptionDay rows without holds;
        // from that point the general gate sees the consumed unit.
        var campaignSubscription = new Subscription { CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, BundleId = bundle.Id, SelectedTier = RestaurantTier.Basic, DurationDays = 1, StartDate = ServiceDate, EndDate = ServiceDate, Status = SubscriptionStatus.Active, PaidAmount = new Money(10, "KWD") };
        db.AddRange(campaignSubscription,
            new SubscriptionDay { SubscriptionId = campaignSubscription.Id, RestaurantId = restaurant.Id, ServiceDate = ServiceDate, ResolvedMenuPlanVersionId = versionId, Status = SubscriptionDayStatus.AutoSelected });
        await db.SaveChangesAsync();

        var afterPurchase = Assert.Single(await EvaluateCandidateAsync(db, program, bundle, regionId));
        Assert.False(afterPurchase.IsEligible);
        Assert.Contains(EligibilityReasonCodes.CapacityUnavailable, afterPurchase.ReasonCodes);
    }

    [Fact]
    public async Task Ghost_confirmed_hold_no_longer_blocks_transfer_or_eligibility()
    {
        await using var db = CreateDb();
        var (program, bundle, regionId) = await SeedCatalogAsync(db, mealsPerDay: 1);
        var (restaurant, versionId, _) = await SeedEligibleRestaurantAsync(db, program, bundle, regionId, "R1", limitBoxes: 2);

        CommitUnit(db, restaurant.Id, program.Id, bundle.Id, versionId, familyGroupId: null, Guid.NewGuid());
        // Cancelled subscription whose Confirmed hold was never released.
        var cancelled = new Subscription { CustomerUserId = Guid.NewGuid(), ProgramId = program.Id, BundleId = bundle.Id, SelectedTier = RestaurantTier.Basic, DurationDays = 1, StartDate = ServiceDate, EndDate = ServiceDate, Status = SubscriptionStatus.Cancelled, PaidAmount = new Money(10, "KWD") };
        db.AddRange(cancelled,
            new CapacityHold { SubscriptionId = cancelled.Id, RestaurantId = restaurant.Id, ServiceDate = ServiceDate, MenuPlanVersionId = versionId, Quantity = 1, Status = CapacityHoldStatus.Confirmed, ExpiresAtUtc = Now.AddMinutes(10) });
        await db.SaveChangesAsync();

        var candidate = Assert.Single(await EvaluateCandidateAsync(db, program, bundle, regionId));
        Assert.True(candidate.IsEligible);

        var executor = Substitute.For<IApplicationTransactionExecutor>();
        executor.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var operation = callInfo.Arg<Func<CancellationToken, Task<Result>>>();
                return operation(CancellationToken.None);
            });
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        var transferSubscriptionId = Guid.NewGuid();
        db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot { SubscriptionId = transferSubscriptionId, RestaurantId = restaurant.Id, ServiceDate = ServiceDate, MenuPlanVersionId = versionId, ContentHash = "capacity-transfer-fixture" });
        await db.SaveChangesAsync();
        var transfer = await new CapacityTransferService(
                db, Substitute.For<IApplicationLockManager>(), executor, clock,
                Substitute.For<ICapacityConcurrencyLock>(), new RestaurantCapacityReader(db, clock))
            .TransferCapacityAsync(transferSubscriptionId, Guid.NewGuid(), ServiceDate, oldRestaurantId: null, restaurant.Id, quantity: 1);

        // The ghost hold is residue, not a commitment: with the unified reader both the
        // booking gate and the transfer path agree that one live unit leaves one free box.
        Assert.True(transfer.IsSuccess);
    }

    [Fact]
    public async Task Transfer_still_fails_when_target_full_with_live_commitments()
    {
        await using var db = CreateDb();
        var (program, bundle, regionId) = await SeedCatalogAsync(db, mealsPerDay: 1);
        var (restaurant, versionId, _) = await SeedEligibleRestaurantAsync(db, program, bundle, regionId, "R1", limitBoxes: 1);

        CommitUnit(db, restaurant.Id, program.Id, bundle.Id, versionId, familyGroupId: null, Guid.NewGuid());
        await db.SaveChangesAsync();

        var executor = Substitute.For<IApplicationTransactionExecutor>();
        executor.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var operation = callInfo.Arg<Func<CancellationToken, Task<Result>>>();
                return operation(CancellationToken.None);
            });
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        var transfer = await new CapacityTransferService(
                db, Substitute.For<IApplicationLockManager>(), executor, clock,
                Substitute.For<ICapacityConcurrencyLock>(), new RestaurantCapacityReader(db, clock))
            .TransferCapacityAsync(Guid.NewGuid(), Guid.NewGuid(), ServiceDate, oldRestaurantId: null, restaurant.Id, quantity: 1);

        Assert.True(transfer.IsFailure);
        Assert.Equal("RESTAURANT_CAPACITY_FULL", transfer.Error.Code);
    }

    [Fact]
    public async Task Transfer_of_identity_already_at_target_does_not_double_count()
    {
        await using var db = CreateDb();
        var (program, bundle, regionId) = await SeedCatalogAsync(db, mealsPerDay: 1);
        var (restaurant, versionId, _) = await SeedEligibleRestaurantAsync(db, program, bundle, regionId, "R1", limitBoxes: 1);

        var (subscription, day) = CommitUnit(db, restaurant.Id, program.Id, bundle.Id, versionId, familyGroupId: null, Guid.NewGuid());
        db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot { SubscriptionId = subscription.Id, RestaurantId = restaurant.Id, ServiceDate = ServiceDate, MenuPlanVersionId = versionId, ContentHash = "capacity-transfer-fixture" });
        await db.SaveChangesAsync();

        var executor = Substitute.For<IApplicationTransactionExecutor>();
        executor.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var operation = callInfo.Arg<Func<CancellationToken, Task<Result>>>();
                return operation(CancellationToken.None);
            });
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        var transfer = await new CapacityTransferService(
                db, Substitute.For<IApplicationLockManager>(), executor, clock,
                Substitute.For<ICapacityConcurrencyLock>(), new RestaurantCapacityReader(db, clock))
            .TransferCapacityAsync(subscription.Id, day.Id, ServiceDate, oldRestaurantId: restaurant.Id, restaurant.Id, quantity: 1);

        // Same-identity move onto a day already at the target consumes no extra unit.
        Assert.True(transfer.IsSuccess);
    }

    [Fact]
    public async Task Family_checkout_rechecks_capacity_after_the_lock()
    {
        await using var db = CreateDb();
        var (program, bundle, regionId) = await SeedCatalogAsync(db, mealsPerDay: 1);
        var restaurantId = Guid.NewGuid();
        var serviceDates = new List<DateOnly> { ServiceDate, ServiceDate.AddDays(1) };
        foreach (var date in serviceDates)
        {
            db.DailyCapacities.Add(new DailyCapacity { RestaurantId = restaurantId, ServiceDate = date, CapacityType = CapacityType.DateOverride, MaxBoxes = 2 });
        }

        var managerId = Guid.NewGuid();
        var spouseUserId = Guid.NewGuid();
        var address = new CustomerAddress { UserId = managerId, Label = "Home", AddressLine = "Kuwait City", RegionId = regionId, IsDefault = true };
        db.CustomerAddresses.Add(address);
        db.CustomerProfiles.Add(new CustomerProfile { UserId = managerId, AllergiesCompletedAtUtc = Now });
        db.CustomerProfiles.Add(new CustomerProfile { UserId = spouseUserId, AllergiesCompletedAtUtc = Now });

        var group = new FamilyGroup { ManagerCustomerUserId = managerId, Name = "Family", Status = FamilyGroupStatus.Draft, DiscountPercentSnapshot = 0.10m };
        var manager = ReadyMember(group.Id, FamilyMemberRole.Manager, "Manager", address.Id, program.Id, bundle.Id, 26);
        var spouse = ReadyMember(group.Id, FamilyMemberRole.Member, "Spouse", address.Id, program.Id, bundle.Id, 26);
        group.Members.Add(manager);
        group.Members.Add(spouse);
        db.FamilyGroups.Add(group);
        db.FamilyMemberAccesses.Add(new FamilyMemberAccess { FamilyMemberId = spouse.Id, HasSeparateAccount = true, LinkedCustomerUserId = spouseUserId });
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);

        var schedule = Substitute.For<ISubscriptionScheduleService>();
        schedule.GetServiceDates(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<bool>()).Returns(serviceDates);

        var menuVersionId = Guid.NewGuid();
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns([new RestaurantEligibilityDto(restaurantId, "مطعم", "Restaurant", true, [],
                serviceDates.Select(date => new EligibleMenuDateDto(date, menuVersionId)).ToList())]);

        // The per-member eligibility substitute above says both members are fine; a
        // reservation that wins the race commits two competing units on the first date
        // right when the capacity lock is granted, before any hold is written.
        var competingHolds = new[] { Guid.NewGuid(), Guid.NewGuid() }.Select(subscriptionId => new CapacityHold
        {
            RestaurantId = restaurantId, SubscriptionId = subscriptionId, MenuPlanVersionId = menuVersionId,
            ServiceDate = serviceDates[0], Quantity = 1, Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = Now.AddMinutes(10)
        }).ToList();
        var competingReservationArrived = false;
        var capacityLock = Substitute.For<ICapacityConcurrencyLock>();
        capacityLock.AcquireAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<DateOnly>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (!competingReservationArrived)
                {
                    competingReservationArrived = true;
                    db.CapacityHolds.AddRange(competingHolds);
                    db.SaveChanges();
                }
                return Task.CompletedTask;
            });

        var quote = new FamilyQuoteResult(
            [
                new FamilyQuoteLine(manager.Id, "Manager", 200m, 0, 0, 0),
                new FamilyQuoteLine(spouse.Id, "Spouse", 100m, 0, 0, 0),
            ],
            Subtotal: 300m,
            FamilyDiscountPercent: 0.10m,
            FamilyDiscountAmount: 30m,
            FinalPrice: 270m,
            CurrencyCode: "KWD",
            DiscountApplied: true,
            EffectiveFamilyPackageId: Guid.NewGuid(),
            EffectiveFamilyPackageMinMembers: 2,
            EffectiveFamilyPackageMaxMembers: 4,
            ParticipantCount: 2,
            PackageSelectionMode: "selected");
        var quoteService = Substitute.For<IFamilyQuoteService>();
        quoteService.QuoteAsync(Arg.Any<FamilyGroup>(), Arg.Any<CancellationToken>(), Arg.Any<FamilyQuoteScope>()).Returns(quote);
        var settings = Substitute.For<IFamilyPricingSettingsReader>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new FamilyPricingSettings { DiscountPercent = 0.10m, MaxMembers = 4, MinMembersForDiscount = 2 });
        var pool = Substitute.For<IRestaurantPoolSnapshotService>();
        pool.CaptureAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RestaurantEligibilityDto>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new RestaurantPoolCaptureResult(true, Guid.NewGuid(), 1));
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(managerId, "m@example.com", "50000000", "Manager", UserType.Customer, AccountStatus.Active, null, true, true, DateTime.UtcNow, null));
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);

        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = group.ManagerCustomerUserId,
            IdempotencyKey = "capacity-selection", CreatedAtUtc = Now };
        db.FamilyCheckoutBatches.Add(batch);
        foreach (var member in group.Members)
        {
            member.StartDate ??= ServiceDate;
            db.FamilyCheckoutBatchLines.Add(new FamilyCheckoutBatchLine { BatchId = batch.Id, FamilyMemberId = member.Id, CheckoutSelected = true });
        }
        await db.SaveChangesAsync();
        var payments = Substitute.For<IPaymentGatewayService>();
        payments.GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns([new PaymentGatewayMethod(2, "test-card", "بطاقة اختبار", "Test card", null, null, "KWD", false)]);
        var sut = new FamilyCheckoutService(
            db, currentUser, users, quoteService, schedule, eligibility, capacityLock,
            new InlineCheckoutTransactionExecutor(), pool, payments,
            Substitute.For<ISender>(), clock, new RestaurantCapacityReader(db, clock), Substitute.For<IApplicationLockManager>());

        var result = await sut.StartAsync(
            new FamilyStartCheckoutRequest("https://localhost/callback", "https://localhost/error", CorrelationId: "corr-capacity", PaymentMethodId: 2,
                ClientContractVersion: FamilyInvoiceGate.SupportedContractVersion),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("family.capacity_unavailable", result.Error!.Code);
        Assert.Equal(0, await db.Subscriptions.CountAsync());
        Assert.Equal(2, await db.CapacityHolds.CountAsync());
        Assert.All(competingHolds, hold => Assert.Equal(CapacityHoldStatus.PendingPayment, hold.Status));
    }

    private sealed class InlineCheckoutTransactionExecutor : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }

    private static FamilyMember ReadyMember(
        Guid groupId,
        FamilyMemberRole role,
        string name,
        Guid addressId,
        Guid programId,
        Guid bundleId,
        int days)
        => new()
        {
            FamilyGroupId = groupId,
            Role = role,
            DisplayName = name,
            RelationshipCode = role == FamilyMemberRole.Manager ? "self" : "spouse",
            Gender = FamilyMemberGender.Male,
            AgeYears = 30,
            NutritionalGoalCode = "balanced",
            ActivityLevel = FamilyActivityLevel.Moderate,
            DeliveryAddressId = addressId,
            SetupStatus = FamilyMemberSetupStatus.Ready,
            ProgramId = programId,
            BundleId = bundleId,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = days,
            IncludeFriday = false,
        };

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static async Task<(Program Program, Bundle Bundle, Guid RegionId)> SeedCatalogAsync(AppDbContext db, int mealsPerDay)
    {
        var program = new Program { Code = "P", NameAr = "برنامج", NameEn = "Program", Status = ProgramStatus.Active, IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Code = "B", NameAr = "باقة", NameEn = "Bundle", Status = ProgramStatus.Active, IsActive = true, MealsPerDay = mealsPerDay, LunchCount = mealsPerDay };
        db.AddRange(program, bundle,
            new PricingRuleVersion { IsActive = true, RMax = Percentage.FromFraction(.3m), RMin = Percentage.FromFraction(.05m), DShort = 6, DLong = 26, Gamma = 1, RoundingStepKwd = .1m },
            new ProgramMealSlotRequirement { ProgramId = program.Id, Slot = "lunch", IsRequired = true },
            new ProgramNutritionStandard { ProgramId = program.Id, MealCategory = "MAIN", KcalMin = 400, KcalMax = 600, ProteinGMin = 30, ProteinGMax = 50, CarbsGMin = 40, CarbsGMax = 60, FatGMin = 15, FatGMax = 25, FiberGMin = 5 });
        await db.SaveChangesAsync();
        return (program, bundle, Guid.NewGuid());
    }

    private static async Task<(RestaurantProfile Restaurant, Guid MenuPlanVersionId, Guid FirstMealId)> SeedEligibleRestaurantAsync(
        AppDbContext db, Program program, Bundle bundle, Guid regionId, string code, int limitBoxes, int lunchMealCount = 1)
    {
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(), LegalNameAr = "مطعم " + code, LegalNameEn = code,
            TradeNameAr = "مطعم " + code, TradeNameEn = code,
            VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true
        };
        var menu = new Menu { RestaurantId = restaurant.Id, ProgramId = program.Id, NameAr = "وجبات", NameEn = "Meals", IsActive = true };
        var plan = new MenuPlan { RestaurantId = restaurant.Id, NameAr = "خطة", NameEn = "Plan", Source = MenuPlanSource.RestaurantCreated, Mode = ScheduleMode.Cycle };
        var version = new MenuPlanVersion { MenuPlanId = plan.Id, MenuPlan = plan, VersionNumber = 1, EffectiveFrom = ServiceDate, Status = VerificationStatus.Approved };
        var menuDay = new MenuDay { MenuPlanVersionId = version.Id, MenuPlanVersion = version, DayNumber = 1, NameAr = "اليوم", NameEn = "Day" };
        var ingredient = new Ingredient { RestaurantId = restaurant.Id, Code = "ING-" + code, NameAr = "أرز", NameEn = "Rice", OriginCountryCode = "KW", IsActive = true };

        Guid firstMealId = Guid.Empty;
        for (var i = 1; i <= lunchMealCount; i++)
        {
            var meal = new Meal
            {
                RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, MenuId = menu.Id, Menu = menu,
                Code = $"MEAL-{code}-{i}", NameAr = "وجبة", NameEn = "Meal", DescriptionAr = "وصف", DescriptionEn = "Description", Slot = "lunch",
                ApprovalStatus = VerificationStatus.Approved,
                Calories = 500, ProteinG = 40, CarbsG = 50, NetCarbsG = 50, FatG = 20, FiberG = 8,
                IsAvailableForNewOrders = true, AllergensReviewed = true, DataResponsibilityConfirmed = true
            };
            var ingredientLink = new MealIngredient { MealId = meal.Id, Meal = meal, IngredientId = ingredient.Id, Ingredient = ingredient, PortionGrams = 100 };
            var image = new MealImage { MealId = meal.Id, Meal = meal, ImageUrl = $"/{code}-{i}.png", IsPrimary = true };
            var menuDayMeal = new MenuDayMeal { MenuDayId = menuDay.Id, MenuDay = menuDay, MealId = meal.Id, Meal = meal, ProgramId = program.Id, Slot = "lunch", IsPrimary = i == 1 };
            if (i == 1) firstMealId = meal.Id;
            db.AddRange(meal, ingredientLink, image, menuDayMeal);
        }
        var planProgram = new MenuPlanProgram { MenuPlanId = plan.Id, MenuPlan = plan, ProgramId = program.Id, EffectiveFrom = ServiceDate, IsPrimaryOwner = true };
        var run = new ClassificationRun { ProgramId = program.Id, BundleId = bundle.Id, ConfidenceLevel = "high", RunAtUtc = Now };

        db.AddRange(restaurant, menu, ingredient, plan, version, menuDay, planProgram, run,
            new RestaurantClassification { ClassificationRunId = run.Id, ClassificationRun = run, RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, Tier = RestaurantTier.Basic },
            new RestaurantProgramBundleOffer { RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, IsActive = true },
            new RestaurantServiceRegion { RestaurantId = restaurant.Id, RegionId = regionId, IsActive = true },
            new RestaurantBundlePricing { RestaurantId = restaurant.Id, ProgramId = program.Id, BundleId = bundle.Id, ReferencePrice26 = new Money(110, "KWD"), ContractedSupplyPrice26 = new Money(100, "KWD"), LockStatus = PriceLockStatus.Locked, EffectiveFromUtc = Now.AddDays(-1) },
            new DailyCapacity { RestaurantId = restaurant.Id, ServiceDate = ServiceDate, CapacityType = CapacityType.DateOverride, MaxBoxes = limitBoxes });
        await db.SaveChangesAsync();
        return (restaurant, version.Id, firstMealId);
    }

    private static (Subscription Subscription, SubscriptionDay Day) CommitUnit(AppDbContext db, Guid restaurantId, Guid programId, Guid bundleId, Guid menuPlanVersionId, Guid? familyGroupId, Guid familyMemberId)
    {
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(), ProgramId = programId, BundleId = bundleId,
            SelectedTier = RestaurantTier.Basic, DurationDays = 1, StartDate = ServiceDate, EndDate = ServiceDate,
            Status = SubscriptionStatus.Active, PaidAmount = new Money(10, "KWD"),
            FamilyGroupId = familyGroupId, FamilyMemberId = familyMemberId
        };
        var day = new SubscriptionDay { SubscriptionId = subscription.Id, RestaurantId = restaurantId, ServiceDate = ServiceDate, ResolvedMenuPlanVersionId = menuPlanVersionId, Status = SubscriptionDayStatus.Open };
        db.AddRange(subscription, day,
            new CapacityHold { SubscriptionId = subscription.Id, RestaurantId = restaurantId, ServiceDate = ServiceDate, MenuPlanVersionId = menuPlanVersionId, Quantity = 1, Status = CapacityHoldStatus.Confirmed, ExpiresAtUtc = Now.AddMinutes(10) });
        return (subscription, day);
    }

    private static void CommitHold(AppDbContext db, Guid restaurantId, Guid programId, Guid bundleId, Guid menuPlanVersionId, CapacityHoldStatus status, DateTime expiresAtUtc)
    {
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(), ProgramId = programId, BundleId = bundleId,
            SelectedTier = RestaurantTier.Basic, DurationDays = 1, StartDate = ServiceDate, EndDate = ServiceDate,
            Status = SubscriptionStatus.Active, PaidAmount = new Money(10, "KWD")
        };
        db.AddRange(subscription,
            new CapacityHold { SubscriptionId = subscription.Id, RestaurantId = restaurantId, ServiceDate = ServiceDate, MenuPlanVersionId = menuPlanVersionId, Quantity = 1, Status = status, ExpiresAtUtc = expiresAtUtc });
    }

    private static async Task<IReadOnlyList<RestaurantEligibilityDto>> EvaluateCandidateAsync(AppDbContext db, Program program, Bundle bundle, Guid regionId)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        var service = new RestaurantEligibilityService(
            db, clock, new SubscriptionScheduleService(clock), new BundleSlotRequirementService(db),
            new MenuPlanResolutionService(db), new RestaurantTierClassificationService(db, clock),
            new RestaurantCapacityReader(db, clock));
        return await service.EvaluateAsync(new RestaurantEligibilityRequest(program.Id, bundle.Id, RestaurantTier.Basic, regionId, ServiceDate, 1, IncludeFriday: false, Guid.NewGuid()));
    }
}
