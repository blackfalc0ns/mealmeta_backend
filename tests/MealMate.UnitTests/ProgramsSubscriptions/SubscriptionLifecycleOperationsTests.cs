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
