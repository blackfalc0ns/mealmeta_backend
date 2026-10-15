using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class SubscriptionLifecycleOperationsTests
{
    private static DbContextOptions<AppDbContext> CreateInMemoryOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task Freeze_with_no_service_calendar_rejects_without_fabricated_extension()
    {
        using var db = new AppDbContext(CreateInMemoryOptions(), Substitute.For<IDomainEventDispatcher>());
        var now = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        var start = new DateOnly(2026, 10, 20);
        var subscription = new Subscription { CustomerUserId = Guid.NewGuid(), ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(), StartDate = start, EndDate = start.AddDays(5), Status = SubscriptionStatus.Active, DurationDays = 6, PaidAmount = new Money(50m, "KWD") };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(now); clock.TodayUtc.Returns(DateOnly.FromDateTime(now));
        var handler = FreezeTestFixture.CreateHandler(db, clock);
        var result = await handler.Handle(new RequestSubscriptionFreezeCommand(subscription.Id, start, start, "Vacation"), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("freeze.no_service_days");
        subscription.EndDate.Should().Be(start.AddDays(5));
        (await db.SubscriptionFreezeRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RequestSubscriptionFreeze_WhenDaysHaveSelections_RequiresAcknowledge_AndExtendsDays()
    {
        using var db = new AppDbContext(CreateInMemoryOptions(), Substitute.For<IDomainEventDispatcher>());
        var now = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        var date = new DateOnly(2026, 10, 20);
        var subscription = await FreezeTestFixture.SeedMenuPoolAsync(db, now, date);
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(now); clock.TodayUtc.Returns(DateOnly.FromDateTime(now));
        var handler = FreezeTestFixture.CreateHandler(db, clock);
        var unacknowledged = await handler.Handle(new RequestSubscriptionFreezeCommand(subscription.Id, date, date, "Vacation", false), CancellationToken.None);
        unacknowledged.IsFailure.Should().BeTrue();
        unacknowledged.Error.Code.Should().Be("freeze.confirm_overwrite_selections");
        var accepted = await handler.Handle(new RequestSubscriptionFreezeCommand(subscription.Id, date, date, "Vacation", true), CancellationToken.None);
        accepted.IsSuccess.Should().BeTrue();
        var approvedAgain = await handler.Handle(new ApproveSubscriptionFreezeCommand(accepted.Value.Id, true), CancellationToken.None);
        approvedAgain.IsSuccess.Should().BeTrue();
        subscription.EndDate.Should().Be(date.AddDays(1));
        (await db.SubscriptionDays.CountAsync()).Should().Be(2);
        (await db.SubscriptionDays.SingleAsync(x => x.ServiceDate == date)).Status.Should().Be(SubscriptionDayStatus.Frozen);
        (await db.SubscriptionDays.SingleAsync(x => x.ServiceDate == date.AddDays(1))).ConfirmedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Customer_freeze_command_rechecks_family_permission_before_mutation()
    {
        using var db = new AppDbContext(CreateInMemoryOptions(), Substitute.For<IDomainEventDispatcher>());
        var actor = Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = Guid.NewGuid(), Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, FamilyGroup = group, DisplayName = "Member",
            Role = FamilyMemberRole.Member, RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        var date = new DateOnly(2026, 10, 20);
        var subscription = new Subscription { CustomerUserId = group.ManagerCustomerUserId, FamilyGroupId = group.Id,
            FamilyMemberId = member.Id, ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(), StartDate = date,
            EndDate = date, Status = SubscriptionStatus.Active, DurationDays = 1, PaidAmount = new Money(50m, "KWD") };
        member.MemberSubscriptionId = subscription.Id;
        db.AddRange(group, member, subscription,
            new FamilyMemberAccess { FamilyMemberId = member.Id, LinkedCustomerUserId = actor, HasSeparateAccount = true },
            new FamilyMemberPermissions { FamilyMemberId = member.Id, RequestFreeze = false });
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc));
        clock.TodayUtc.Returns(new DateOnly(2026, 10, 10));
        var transactions = Substitute.For<IApplicationTransactionExecutor>();
        transactions.ExecuteResultAsync<SubscriptionFreezeRequestDto>(Arg.Any<Func<CancellationToken, Task<MealMate.Application.Common.Models.Result<SubscriptionFreezeRequestDto>>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<MealMate.Application.Common.Models.Result<SubscriptionFreezeRequestDto>>>>()(call.Arg<CancellationToken>()));
        var locks = Substitute.For<IApplicationLockManager>();
        locks.LockFamilyGroupAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        locks.LockSubscriptionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        locks.LockSubscriptionDaysAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await FreezeTestFixture.CreateHandler(db, clock, transactions, locks).Handle(
            new RequestSubscriptionFreezeCommand(subscription.Id, date, date, "Vacation", true, actor), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(MealMate.Application.Common.Models.ErrorType.Forbidden);
        result.Error.Code.Should().Be("family.permission_required");
        (await db.SubscriptionFreezeRequests.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task Freeze_replacement_uses_latest_accepted_address_when_last_original_day_is_protected()
    {
        using var db = new AppDbContext(CreateInMemoryOptions(), Substitute.For<IDomainEventDispatcher>());
        var manager = Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = manager, Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, FamilyGroup = group, DisplayName = "Member",
            RelationshipCode = "child", NutritionalGoalCode = "balance" };
        var start = new DateOnly(2026, 10, 14);
        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var subscription = await FreezeTestFixture.SeedMenuPoolAsync(db, now, start);
        subscription.CustomerUserId = manager; subscription.FamilyGroupId = group.Id; subscription.FamilyMemberId = member.Id;
        subscription.DurationDays = 2; subscription.EndDate = start.AddDays(1); subscription.IncludeFriday = false;
        member.MemberSubscriptionId = subscription.Id;
        var mutableDay = await db.SubscriptionDays.SingleAsync(x => x.SubscriptionId == subscription.Id);
        mutableDay.Status = SubscriptionDayStatus.Open; mutableDay.DeliveryAddressId = Guid.NewGuid();
        mutableDay.DeliveryAddressLine = "Accepted B";
        db.DailyMealSelections.RemoveRange(await db.DailyMealSelections.Where(x => x.SubscriptionDayId == mutableDay.Id).ToListAsync());
        var poolRestaurant = await db.SubscriptionRestaurantSnapshots.SingleAsync(x => x.SubscriptionId == subscription.Id);
        db.DailyCapacities.Add(new MealMate.Domain.RestaurantOperations.DailyCapacity { RestaurantId = poolRestaurant.RestaurantId, ServiceDate = start.AddDays(3), CapacityType = CapacityType.DateOverride, MaxBoxes = 1 });
        var protectedDay = new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = start.AddDays(1),
            Status = SubscriptionDayStatus.ConfirmedByRestaurant, DeliveryAddressId = Guid.NewGuid(),
            DeliveryAddressLine = "Original A", DeliveryRegionId = Guid.NewGuid() };
        var amendment = new FamilySubscriptionDeliveryAmendment { SubscriptionId = subscription.Id, Version = 1,
            ActorUserId = manager, EffectiveFromDate = start, AddressId = mutableDay.DeliveryAddressId!.Value,
            AddressLabel = "B", AddressLine = "Accepted B", RegionId = mutableDay.DeliveryRegionId!.Value,
            AffectedDayIdsJson = JsonSerializer.Serialize(new[] { mutableDay.Id }), CreatedAtUtc = DateTime.UtcNow };
        mutableDay.SourceFamilyDeliveryAmendmentId = amendment.Id;
        var paidBatch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = manager,
            Status = FamilyCheckoutBatchStatus.Paid, IdempotencyKey = "paid-family", CurrencyCode = "KWD", FinalPrice = 90m };
        paidBatch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = paidBatch.Id, Batch = paidBatch,
            SubscriptionId = subscription.Id, FamilyMemberId = member.Id, CheckoutSelected = true,
            DeliveryAddressId = protectedDay.DeliveryAddressId, AddressLabel = "A", AddressLine = "Original A",
            RegionId = protectedDay.DeliveryRegionId, MenuPlanVersionsJson = "[]" });
        db.AddRange(group, member, protectedDay, amendment, paidBatch);
        db.SystemSettings.Add(new SystemSetting { Key = SubscriptionFreezePolicyDefaults.Key,
            ValueJson = JsonSerializer.Serialize(new SubscriptionFreezePolicyDocument(7, 3, true)), IsActive = true });
        await db.SaveChangesAsync();

        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(now); clock.TodayUtc.Returns(new DateOnly(2026, 10, 9));
        var handler = FreezeTestFixture.CreateHandler(db, clock);

        var result = await handler.Handle(new RequestSubscriptionFreezeCommand(subscription.Id, start, start, "Vacation"), default);

        Assert.True(result.IsSuccess, result.IsFailure ? $"{result.Error.Code}:{result.Error.Description}" : null);
        var resultDays = await db.SubscriptionDays.Where(x => x.SubscriptionId == subscription.Id).OrderBy(x => x.ServiceDate).ToListAsync();
        Assert.Equal(3, resultDays.Count);
        var replacement = resultDays.Single(x => x.ServiceDate == start.AddDays(3));
        Assert.Equal("Accepted B", replacement.DeliveryAddressLine);
        Assert.Equal(amendment.Id, replacement.SourceFamilyDeliveryAmendmentId);
        Assert.Null(replacement.ConfirmedAtUtc);
        Assert.Equal("Original A", protectedDay.DeliveryAddressLine);
    }

    [Fact]
    public async Task Family_freeze_rejects_replacement_that_overlaps_funded_upcoming_renewal_before_mutation()
    {
        using var db = new AppDbContext(CreateInMemoryOptions(), Substitute.For<IDomainEventDispatcher>());
        var manager = Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = manager, Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, FamilyGroup = group, DisplayName = "Member",
            RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        var oldStart = new DateOnly(2026, 10, 12);
        var oldLast = new DateOnly(2026, 10, 13);
        var renewalStart = new DateOnly(2026, 10, 14);
        var paidAt = new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);
        var previous = new Subscription { CustomerUserId = manager, FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(), Status = SubscriptionStatus.Active,
            DurationDays = 2, StartDate = oldStart, EndDate = oldLast, PaidAmount = new Money(90m, "KWD"),
            PaymentVerifiedAtUtc = paidAt };
        var next = new Subscription { CustomerUserId = manager, FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            ProgramId = previous.ProgramId, BundleId = previous.BundleId, Status = SubscriptionStatus.Scheduled,
            DurationDays = 1, StartDate = renewalStart, EndDate = renewalStart, PaidAmount = new Money(90m, "KWD"),
            PaymentVerifiedAtUtc = paidAt };
        member.MemberSubscriptionId = previous.Id;
        var firstDay = new SubscriptionDay { SubscriptionId = previous.Id, ServiceDate = oldStart, Status = SubscriptionDayStatus.Open };
        var lastDay = new SubscriptionDay { SubscriptionId = previous.Id, ServiceDate = oldLast, Status = SubscriptionDayStatus.Open };
        var fundedUpcomingDay = new SubscriptionDay { SubscriptionId = next.Id, ServiceDate = renewalStart, Status = SubscriptionDayStatus.Open };
        var paidBatch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = manager,
            Status = FamilyCheckoutBatchStatus.Paid, IdempotencyKey = "freeze-overlap-paid", CurrencyCode = "KWD", FinalPrice = 90m,
            PaidAtUtc = paidAt };
        paidBatch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = paidBatch.Id, Batch = paidBatch,
            FamilyMemberId = member.Id, SubscriptionId = next.Id, CheckoutSelected = true,
            StartDate = renewalStart, NetPaidAmount = 90m, PriceBeforeFamilyDiscount = 90m, CurrencyCode = "KWD" });
        db.AddRange(group, member, previous, next, firstDay, lastDay, fundedUpcomingDay, paidBatch);
        db.FamilyCheckoutBatchLines.AddRange(paidBatch.Lines);
        db.SystemSettings.Add(new SystemSetting { Key = "operational_policies.settings",
            ValueJson = JsonSerializer.Serialize(new MealMate.Application.Features.AdminDashboard.OperationalPolicies.OperationalPolicySettings() with
            { Freeze = new MealMate.Application.Features.AdminDashboard.OperationalPolicies.FreezePolicySettings(
                LeadDaysNotice: 0, MinDays: 1, MaxDays: 7, ExtendSubscriptionEnd: true) }),
            IsActive = true });
        await db.SaveChangesAsync();

        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        clock.TodayUtc.Returns(new DateOnly(2026, 10, 9));
        var handler = CreateHandler(db, clock, Substitute.For<IAuditLogger>());

        var result = await handler.Handle(new RequestSubscriptionFreezeCommand(previous.Id, oldStart, oldStart, "Vacation"), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("family.renewal_dates_overlap");
        (await db.SubscriptionDays.CountAsync(x => x.SubscriptionId == previous.Id)).Should().Be(2);
        (await db.SubscriptionDays.SingleAsync(x => x.Id == firstDay.Id)).Status.Should().Be(SubscriptionDayStatus.Open);
        (await db.Subscriptions.SingleAsync(x => x.Id == previous.Id)).EndDate.Should().Be(oldLast);
        (await db.Subscriptions.SingleAsync(x => x.Id == next.Id)).Status.Should().Be(SubscriptionStatus.Scheduled);
        (await db.FamilyCheckoutBatches.SingleAsync(x => x.Id == paidBatch.Id)).Status.Should().Be(FamilyCheckoutBatchStatus.Paid);
        (await db.FamilyCheckoutBatchLines.SingleAsync(x => x.SubscriptionId == next.Id)).NetPaidAmount.Should().Be(90m);
        (await db.SubscriptionFreezeRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RequestUpgrade_CalculatesProRataDelta_AndApprovesTierChange()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
        var endDate = startDate.AddDays(26);
        var programId = Guid.NewGuid();
        var bundleId = Guid.NewGuid();

        var sub = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = programId,
            BundleId = bundleId,
            SelectedTier = RestaurantTier.Basic,
            Status = SubscriptionStatus.Active,
            DurationDays = 26,
            StartDate = startDate,
            EndDate = endDate,
            PaidAmount = new Money(100.000m, "KWD"),
        };
        db.Subscriptions.Add(sub);
        SeedUpgradePricing(db, programId, bundleId);
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = CreateHandler(db, clock, audit);

        var reqCmd = new RequestSubscriptionUpgradeCommand(sub.Id, RestaurantTier.Elite, "Upgrade to Elite");
        var reqRes = await handler.Handle(reqCmd, CancellationToken.None);

        reqRes.IsSuccess.Should().BeTrue();
        reqRes.Value.PriceDifferenceKwd.Should().BeGreaterThan(0m);

        var appCmd = new ApproveSubscriptionUpgradeCommand(reqRes.Value.Id, Approved: true);
        var appRes = await handler.Handle(appCmd, CancellationToken.None);

        appRes.IsSuccess.Should().BeTrue();

        var updatedSub = await db.Subscriptions.FirstAsync(x => x.Id == sub.Id);
        updatedSub.SelectedTier.Should().Be(RestaurantTier.Elite);
    }

    [Fact]
    public async Task ApproveUpgrade_RejectsCustomerPaidUpgradeWithPaymentId()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var sub = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            Status = SubscriptionStatus.Active,
            DurationDays = 26,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(21)),
            PaidAmount = new Money(100m, "KWD"),
        };
        var upgrade = new SubscriptionTierUpgrade
        {
            SubscriptionId = sub.Id,
            FromTier = RestaurantTier.Basic,
            ToTier = RestaurantTier.Platinum,
            PriceDifference = new Money(15m, "KWD"),
            QuotedRemainingDays = 20,
            QuotedTotalDue = new Money(15m, "KWD"),
            PaymentId = "UPG-BLOCK",
            Status = VerificationStatus.Submitted,
        };
        db.Subscriptions.Add(sub);
        db.SubscriptionTierUpgrades.Add(upgrade);
        await db.SaveChangesAsync();

        var handler = CreateHandler(db, Substitute.For<IDateTimeProvider>(), Substitute.For<IAuditLogger>());
        var result = await handler.Handle(new ApproveSubscriptionUpgradeCommand(upgrade.Id, Approved: true), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("upgrade.payment_required");
        (await db.Subscriptions.FindAsync(sub.Id))!.SelectedTier.Should().Be(RestaurantTier.Basic);
    }

    [Fact]
    public async Task ProcessExpirationsWorker_MarksActiveDueSubscriptionsAsExpired()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var pastDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        var subExpired = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            StartDate = pastDate.AddDays(-26),
            EndDate = pastDate,
            PaidAmount = new Money(100.000m, "KWD"),
        };

        db.Subscriptions.Add(subExpired);
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = CreateHandler(db, clock, audit);
        var command = new ProcessSubscriptionRenewalsAndExpirationsCommand();

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExpiredCount.Should().Be(1);

        var checkSub = await db.Subscriptions.FirstAsync(x => x.Id == subExpired.Id);
        checkSub.Status.Should().Be(SubscriptionStatus.Expired);
    }

    private static SubscriptionOperationsHandlers CreateHandler(AppDbContext db, IDateTimeProvider clock, IAuditLogger audit)
    {
        var quotes = new SubscriptionTierUpgradeQuoteBuilder(
            db,
            clock,
            new RestaurantTierClassificationService(db, clock),
            new SubscriptionScheduleService(clock));
        return new SubscriptionOperationsHandlers(db, audit, clock, quotes);
    }

    private static void SeedUpgradePricing(AppDbContext db, Guid programId, Guid bundleId)
    {
        db.SystemSettings.Add(new SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = JsonSerializer.Serialize(SubscriptionPricingSettingsDefaults.Create(), SubscriptionPricingSettingsDefaults.JsonOptions),
            IsActive = true,
        });
        db.PricingRuleVersions.Add(new PricingRuleVersion
        {
            RMax = Percentage.FromFraction(.3m),
            RMin = Percentage.FromFraction(.15m),
            DShort = 6,
            DLong = 26,
            Gamma = 1m,
            RoundingStepKwd = .1m,
            IsActive = true,
        });

        foreach (var reference26 in new[] { 104m, 143m, 169m })
        {
            var restaurant = new RestaurantProfile
            {
                UserId = Guid.NewGuid(),
                LegalNameAr = "مطعم",
                LegalNameEn = "Restaurant",
                TradeNameAr = "مطعم",
                TradeNameEn = "Restaurant",
                VerificationStatus = VerificationStatus.Approved,
                IsOperationallyActive = true,
            };
            db.RestaurantProfiles.Add(restaurant);
            db.RestaurantProgramBundleOffers.Add(new RestaurantProgramBundleOffer
            {
                RestaurantId = restaurant.Id,
                ProgramId = programId,
                BundleId = bundleId,
                IsActive = true,
            });
            db.RestaurantBundlePricings.Add(new RestaurantBundlePricing
            {
                RestaurantId = restaurant.Id,
                ProgramId = programId,
                BundleId = bundleId,
                ReferencePrice26 = new Money(reference26, "KWD"),
                ContractedSupplyPrice26 = new Money(reference26 * 0.8m, "KWD"),
                LockStatus = PriceLockStatus.Locked,
                EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
        }
    }
}
