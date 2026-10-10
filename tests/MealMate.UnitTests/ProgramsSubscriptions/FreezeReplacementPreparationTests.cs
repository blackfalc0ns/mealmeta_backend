using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Pricing;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class FreezeReplacementPreparationTests
{
    [Fact]
    public async Task Second_unavailable_replacement_prevents_every_calendar_and_capacity_mutation()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var now = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        var date = new DateOnly(2026, 10, 20);
        var subscription = await FreezeTestFixture.SeedMenuPoolAsync(db, now, date);
        subscription.EndDate = date.AddDays(1);
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = date.AddDays(1), SelectionDeadlineUtc = now.AddDays(5) });
        var capacity = await db.DailyCapacities.SingleAsync();
        capacity.ServiceDate = date.AddDays(2); // first replacement available, second unavailable
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(now); clock.TodayUtc.Returns(DateOnly.FromDateTime(now));
        var result = await FreezeTestFixture.CreateHandler(db, clock).Handle(new RequestSubscriptionFreezeCommand(subscription.Id, date, date.AddDays(1), null, true), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("freeze.replacement_capacity_unavailable");
        (await db.SubscriptionDays.CountAsync()).Should().Be(2);
        (await db.SubscriptionDays.AnyAsync(x => x.Status == SubscriptionDayStatus.Frozen)).Should().BeFalse();
        (await db.CapacityHolds.SingleAsync()).Status.Should().Be(CapacityHoldStatus.Confirmed);
        (await db.SubscriptionRestaurantDaySnapshots.CountAsync()).Should().Be(1);
        (await db.SubscriptionFreezeRequests.CountAsync()).Should().Be(0);
        subscription.EndDate.Should().Be(date.AddDays(1));
        db.ChangeTracker.Entries().Should().NotContain(x => x.State == EntityState.Added);
    }

    [Fact]
    public async Task Changed_delivery_region_revalidates_coverage_for_replacement()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var now = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        var date = new DateOnly(2026, 10, 20);
        var subscription = await FreezeTestFixture.SeedMenuPoolAsync(db, now, date);
        (await db.SubscriptionDays.SingleAsync()).DeliveryRegionId = Guid.NewGuid();
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(now); clock.TodayUtc.Returns(DateOnly.FromDateTime(now));
        var result = await FreezeTestFixture.CreateHandler(db, clock).Handle(new RequestSubscriptionFreezeCommand(subscription.Id, date, date, null, true), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("freeze.replacement_restaurants_unavailable");
        (await db.CapacityHolds.SingleAsync()).Status.Should().Be(CapacityHoldStatus.Confirmed);
        (await db.SubscriptionDays.CountAsync()).Should().Be(1);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Unavailable_replacement_or_due_source_leaves_request_calendar_selections_and_hold_unchanged(bool adminApproval, bool dueSource)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var now = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        var date = dueSource ? new DateOnly(2026, 10, 13) : new DateOnly(2026, 10, 20);
        var subscription = await FreezeTestFixture.SeedMenuPoolAsync(db, now, date, capacity: dueSource);
        var source = await db.SubscriptionDays.SingleAsync();

        var selection = new DailyMealSelection { SubscriptionDayId = source.Id, MealId = (await db.Meals.SingleAsync()).Id, SelectedAtUtc = now };
        db.DailyMealSelections.Add(selection);
        var freeze = new SubscriptionFreezeRequest { SubscriptionId = subscription.Id, Subscription = subscription, FreezeStartDate = date, FreezeEndDate = date, FreezeDays = 1, Status = VerificationStatus.Submitted };
        if (adminApproval) db.SubscriptionFreezeRequests.Add(freeze);
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(now); clock.TodayUtc.Returns(DateOnly.FromDateTime(now));
        var handler = FreezeTestFixture.CreateHandler(db, clock);
        var result = adminApproval
            ? await handler.Handle(new ApproveSubscriptionFreezeCommand(freeze.Id, true), CancellationToken.None)
            : await handler.Handle(new RequestSubscriptionFreezeCommand(subscription.Id, date, date, null, true), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(dueSource ? "freeze.locked_days_cannot_be_frozen" : "freeze.replacement_capacity_unavailable");
        source.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
        subscription.EndDate.Should().Be(date);
        (await db.CapacityHolds.SingleAsync()).Status.Should().Be(CapacityHoldStatus.Confirmed);
        (await db.DailyMealSelections.CountAsync()).Should().Be(1);
        (await db.SubscriptionDays.CountAsync()).Should().Be(1);
        (await db.SubscriptionRestaurantDaySnapshots.CountAsync()).Should().Be(1);
        (await db.SubscriptionOperations.CountAsync()).Should().Be(0);
        if (adminApproval) freeze.Status.Should().Be(VerificationStatus.Submitted);
        else (await db.SubscriptionFreezeRequests.CountAsync()).Should().Be(0);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Freeze_prepares_fresh_date_menu_unconfirmed_day_and_moves_capacity(bool adminApproval)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var now = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        var date = new DateOnly(2026, 10, 22); // replacement is Friday; evaluate exactly this date
        var subscription = await FreezeTestFixture.SeedMenuPoolAsync(db, now, date);
        var freeze = new SubscriptionFreezeRequest { SubscriptionId = subscription.Id, Subscription = subscription, FreezeStartDate = date, FreezeEndDate = date, FreezeDays = 1, Status = VerificationStatus.Submitted };
        if (adminApproval) { db.SubscriptionFreezeRequests.Add(freeze); await db.SaveChangesAsync(); }
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(now); clock.TodayUtc.Returns(DateOnly.FromDateTime(now));
        var handler = FreezeTestFixture.CreateHandler(db, clock);
        var result = adminApproval
            ? await handler.Handle(new ApproveSubscriptionFreezeCommand(freeze.Id, true), CancellationToken.None)
            : await handler.Handle(new RequestSubscriptionFreezeCommand(subscription.Id, date, date, null, true), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        var replacement = await db.SubscriptionDays.SingleAsync(x => x.ServiceDate == date.AddDays(1));
        replacement.Status.Should().Be(SubscriptionDayStatus.Open);
        replacement.ConfirmedAtUtc.Should().BeNull("capacity reservation is not restaurant confirmation");
        replacement.LockAtUtc.Should().BeNull();
        replacement.SelectionDeadlineUtc.Should().Be(new DateTime(2026, 10, 19, 21, 0, 0, DateTimeKind.Utc));
        var freshVersion = await db.MenuPlanVersions.SingleAsync();
        replacement.ResolvedMenuPlanVersionId.Should().Be(freshVersion.Id);
        var snapshot = await db.SubscriptionRestaurantDaySnapshots.SingleAsync(x => x.ServiceDate == date.AddDays(1));
        snapshot.MenuPlanVersionId.Should().Be(freshVersion.Id);
        snapshot.MealsJson.Should().Contain("Fresh meal");
        (await db.CapacityHolds.SingleAsync(x => x.ServiceDate == date)).Status.Should().Be(CapacityHoldStatus.Released);
        (await db.CapacityHolds.SingleAsync(x => x.ServiceDate == date.AddDays(1))).Status.Should().Be(CapacityHoldStatus.Confirmed);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Freeze_without_verified_replacement_preparation_rejects_without_mutation(bool adminApproval)
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var now = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        var date = new DateOnly(2026, 10, 20);
        var subscription = new Subscription { CustomerUserId = Guid.NewGuid(), ProgramId = Guid.NewGuid(), BundleId = Guid.NewGuid(), StartDate = date, EndDate = date, Status = SubscriptionStatus.Active, DurationDays = 1, IncludeFriday = true, PaidAmount = new Money(50m, "KWD") };
        var day = new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = date, SelectionDeadlineUtc = now.AddDays(5), Status = SubscriptionDayStatus.Open };
        db.Subscriptions.Add(subscription);
        db.SubscriptionDays.Add(day);
        var freeze = new SubscriptionFreezeRequest { SubscriptionId = subscription.Id, Subscription = subscription, FreezeStartDate = date, FreezeEndDate = date, FreezeDays = 1, Status = VerificationStatus.Submitted };
        if (adminApproval) db.SubscriptionFreezeRequests.Add(freeze);
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(now));
        var handler = new SubscriptionOperationsHandlers(db, Substitute.For<IAuditLogger>(), clock, Substitute.For<ISubscriptionTierUpgradeQuoteBuilder>());
        var result = adminApproval
            ? await handler.Handle(new ApproveSubscriptionFreezeCommand(freeze.Id, true), CancellationToken.None)
            : await handler.Handle(new RequestSubscriptionFreezeCommand(subscription.Id, date, date, null), CancellationToken.None);
        result.IsFailure.Should().BeTrue("replacement menus and capacity must be verified before freezing");
        day.Status.Should().Be(SubscriptionDayStatus.Open);
        subscription.EndDate.Should().Be(date);
        (await db.SubscriptionDays.CountAsync()).Should().Be(1);
        if (adminApproval) freeze.Status.Should().Be(VerificationStatus.Submitted);
    }
}
