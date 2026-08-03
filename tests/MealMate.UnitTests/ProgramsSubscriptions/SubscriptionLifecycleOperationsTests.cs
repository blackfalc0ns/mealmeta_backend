using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.Enums;
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

        var handler = new SubscriptionOperationsHandlers(db, audit, clock);

        var freezeStart = startDate.AddDays(10);
        var freezeEnd = freezeStart.AddDays(4); // 5 days freeze

        var reqCmd = new RequestSubscriptionFreezeCommand(sub.Id, freezeStart, freezeEnd, "Vacation");
        var reqRes = await handler.Handle(reqCmd, CancellationToken.None);

        reqRes.IsSuccess.Should().BeTrue();
        reqRes.Value.FreezeDays.Should().Be(5);
        reqRes.Value.Status.Should().Be(VerificationStatus.Submitted);

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
    public async Task RequestUpgrade_CalculatesProRataDelta_AndApprovesTierChange()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var startDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
        var endDate = startDate.AddDays(26);

        var sub = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
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

        var handler = new SubscriptionOperationsHandlers(db, audit, clock);

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

        var handler = new SubscriptionOperationsHandlers(db, audit, clock);
        var command = new ProcessSubscriptionRenewalsAndExpirationsCommand();

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExpiredCount.Should().Be(1);

        var checkSub = await db.Subscriptions.FirstAsync(x => x.Id == subExpired.Id);
        checkSub.Status.Should().Be(SubscriptionStatus.Expired);
    }
}
