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
    public async Task RequestFreeze_ApproveFreeze_ShiftsEndDate_AndSetsStatusFrozen()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));
        var endDate = startDate.AddDays(26);

        var sub = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 26,
            StartDate = startDate,
            EndDate = endDate,
            PaidAmount = new Money(100.000m, "KWD"),
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);

        var handler = CreateHandler(db, clock, audit);

        var freezeStart = startDate.AddDays(10);
        var freezeEnd = freezeStart.AddDays(4); // 5 days freeze

        var reqCmd = new RequestSubscriptionFreezeCommand(sub.Id, freezeStart, freezeEnd, "Vacation");
        var reqRes = await handler.Handle(reqCmd, CancellationToken.None);

        reqRes.IsSuccess.Should().BeTrue();
        reqRes.Value.FreezeDays.Should().Be(5);
        reqRes.Value.Status.Should().Be(VerificationStatus.Approved);

        // Approve freeze
        var appCmd = new ApproveSubscriptionFreezeCommand(reqRes.Value.Id, Approved: true);
        var appRes = await handler.Handle(appCmd, CancellationToken.None);

        appRes.IsSuccess.Should().BeTrue();
        appRes.Value.Status.Should().Be(VerificationStatus.Approved);

        var updatedSub = await db.Subscriptions.FirstAsync(x => x.Id == sub.Id);
        updatedSub.Status.Should().Be(SubscriptionStatus.Frozen);
        updatedSub.EndDate.Should().Be(endDate.AddDays(5));
    }

    [Fact]
    public async Task RequestSubscriptionFreeze_WhenDaysHaveSelections_RequiresAcknowledge_AndExtendsDays()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var endDate = startDate.AddDays(10);
        var sub = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            Status = SubscriptionStatus.Active,
            DurationDays = 3,
            StartDate = startDate,
            EndDate = endDate,
            PaidAmount = new Money(50.000m, "KWD"),
            IncludeFriday = true,
        };
        db.Subscriptions.Add(sub);

        var day1 = new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = startDate.AddDays(4), Status = SubscriptionDayStatus.Open };
        var day2 = new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = startDate.AddDays(5), Status = SubscriptionDayStatus.CustomerSelected, RestaurantId = Guid.NewGuid() };
        var day3 = new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = startDate.AddDays(6), Status = SubscriptionDayStatus.Open };
        db.SubscriptionDays.AddRange(day1, day2, day3);

        db.DailyMealSelections.Add(new DailyMealSelection
        {
            SubscriptionDayId = day2.Id,
            MealId = Guid.NewGuid(),
            SortOrder = 0,
            SelectedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var audit = Substitute.For<IAuditLogger>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(DateTime.UtcNow));

        var handler = CreateHandler(db, clock, audit);

        var freezeStart = startDate.AddDays(5);
        var freezeEnd = startDate.AddDays(5);

        // 1. Without acknowledge -> should return validation error asking for confirmation
        var unackCmd = new RequestSubscriptionFreezeCommand(sub.Id, freezeStart, freezeEnd, "Vacation", AcknowledgeLostSelections: false);
        var unackRes = await handler.Handle(unackCmd, CancellationToken.None);
        unackRes.IsFailure.Should().BeTrue();
        unackRes.Error.Code.Should().Be("freeze.confirm_overwrite_selections");

        // 2. With acknowledge -> succeeds, freezes day2, clears selections, and adds 1 compensation day
        var ackCmd = new RequestSubscriptionFreezeCommand(sub.Id, freezeStart, freezeEnd, "Vacation", AcknowledgeLostSelections: true);
        var ackRes = await handler.Handle(ackCmd, CancellationToken.None);
        ackRes.IsSuccess.Should().BeTrue();

        var reloadedDay2 = await db.SubscriptionDays.FirstAsync(x => x.Id == day2.Id);
        reloadedDay2.Status.Should().Be(SubscriptionDayStatus.Frozen);
        reloadedDay2.RestaurantId.Should().BeNull();

        var remainingSelections = await db.DailyMealSelections.Where(x => x.SubscriptionDayId == day2.Id).ToListAsync();
        remainingSelections.Should().BeEmpty();

        var allDays = await db.SubscriptionDays.Where(x => x.SubscriptionId == sub.Id).OrderBy(x => x.ServiceDate).ToListAsync();
        allDays.Count.Should().Be(4); // 3 original + 1 compensation
        var compensationDay = allDays.Last();
        compensationDay.ServiceDate.Should().Be(startDate.AddDays(7));
        compensationDay.Status.Should().Be(SubscriptionDayStatus.Open);

        var updatedSub = await db.Subscriptions.FirstAsync(x => x.Id == sub.Id);
        updatedSub.EndDate.Should().Be(compensationDay.ServiceDate);
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
