using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.Capacity;

public sealed class FreezeCapacityPostgresTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await _container.StartAsync();
            await using var db = CreateDb();
            await db.Database.MigrateAsync();
        }
        catch (Exception ex) { throw new XunitException("Docker is required for FreezeCapacityPostgresTests.", ex); }
    }
    public async Task DisposeAsync() => await _container.DisposeAsync();
    private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options, Substitute.For<IDomainEventDispatcher>());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_freezes_competing_for_last_replacement_box_commit_only_one_complete_freeze(bool adminApproval)
    {
        var now = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        var date = new DateOnly(2026, 10, 20);
        Guid firstId, secondId, firstRequestId = Guid.Empty, secondRequestId = Guid.Empty;
        await using (var seed = CreateDb())
        {
            var first = await FreezePostgresFixture.SeedMenuPoolAsync(seed, now, date);
            firstId = first.Id;
            var originalCaptured = await seed.SubscriptionRestaurantSnapshots.SingleAsync(x => x.SubscriptionId == first.Id);
            var second = new Subscription { CustomerUserId = Guid.NewGuid(), ProgramId = first.ProgramId, BundleId = first.BundleId, SelectedTier = first.SelectedTier, RegionId = first.RegionId, StartDate = date, EndDate = date, IncludeFriday = true, Status = SubscriptionStatus.Active, DurationDays = 1, PaidAmount = new MealMate.Domain.ValueObjects.Money(first.PaidAmount.Amount, first.PaidAmount.CurrencyCode) };
            secondId = second.Id;
            var pool = new SubscriptionRestaurantPoolSnapshot { SubscriptionId = second.Id, RestaurantCount = 1, EligibilityCheckedAtUtc = now };
            seed.AddRange(second, pool,
                new SubscriptionRestaurantSnapshot { SubscriptionId = second.Id, PoolSnapshotId = pool.Id, RestaurantId = originalCaptured.RestaurantId, ProgramId = first.ProgramId, BundleId = first.BundleId, RegionId = originalCaptured.RegionId, Tier = first.SelectedTier, NameAr = originalCaptured.NameAr, NameEn = originalCaptured.NameEn, IsInitiallySelected = true },
                new SubscriptionDay { SubscriptionId = second.Id, ServiceDate = date, RestaurantId = originalCaptured.RestaurantId, Status = SubscriptionDayStatus.CustomerSelected, SelectionDeadlineUtc = now.AddDays(5), DeliveryRegionId = first.RegionId },
                new CapacityHold { SubscriptionId = second.Id, RestaurantId = originalCaptured.RestaurantId, ServiceDate = date, MenuPlanVersionId = Guid.NewGuid(), Status = CapacityHoldStatus.Confirmed, ConfirmedAtUtc = now, ExpiresAtUtc = now });
            if (adminApproval)
            {
                var firstRequest = new SubscriptionFreezeRequest { SubscriptionId = first.Id, FreezeStartDate = date, FreezeEndDate = date, FreezeDays = 1, Status = VerificationStatus.Submitted };
                var secondRequest = new SubscriptionFreezeRequest { SubscriptionId = second.Id, FreezeStartDate = date, FreezeEndDate = date, FreezeDays = 1, Status = VerificationStatus.Submitted };
                firstRequestId = firstRequest.Id; secondRequestId = secondRequest.Id;
                seed.SubscriptionFreezeRequests.AddRange(firstRequest, secondRequest);
            }
            await seed.SaveChangesAsync();
        }
        await using var db1 = CreateDb(); await using var db2 = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(now); clock.TodayUtc.Returns(DateOnly.FromDateTime(now));
        var handler1 = FreezePostgresFixture.CreateHandler(db1, clock);
        var handler2 = FreezePostgresFixture.CreateHandler(db2, clock);
        var results = adminApproval
            ? await Task.WhenAll(handler1.Handle(new ApproveSubscriptionFreezeCommand(firstRequestId, true), default), handler2.Handle(new ApproveSubscriptionFreezeCommand(secondRequestId, true), default))
            : await Task.WhenAll(handler1.Handle(new RequestSubscriptionFreezeCommand(firstId, date, date, null, true), default), handler2.Handle(new RequestSubscriptionFreezeCommand(secondId, date, date, null, true), default));
        results.Count(x => x.IsSuccess).Should().Be(1);
        results.Count(x => x.IsFailure && x.Error.Code == "freeze.replacement_capacity_unavailable").Should().Be(1);
        await using var verify = CreateDb();
        var winner = results[0].IsSuccess ? firstId : secondId;
        var loser = winner == firstId ? secondId : firstId;
        (await verify.SubscriptionDays.SingleAsync(x => x.SubscriptionId == winner && x.ServiceDate == date)).Status.Should().Be(SubscriptionDayStatus.Frozen);
        var replacement = await verify.SubscriptionDays.SingleAsync(x => x.SubscriptionId == winner && x.ServiceDate == date.AddDays(1));
        replacement.Status.Should().Be(SubscriptionDayStatus.Open);
        replacement.ConfirmedAtUtc.Should().BeNull();
        (await verify.CapacityHolds.SingleAsync(x => x.SubscriptionId == winner && x.ServiceDate == date)).Status.Should().Be(CapacityHoldStatus.Released);
        (await verify.CapacityHolds.SingleAsync(x => x.SubscriptionId == winner && x.ServiceDate == date.AddDays(1))).Status.Should().Be(CapacityHoldStatus.Confirmed);
        (await verify.SubscriptionDays.SingleAsync(x => x.SubscriptionId == loser)).Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
        (await verify.CapacityHolds.SingleAsync(x => x.SubscriptionId == loser)).Status.Should().Be(CapacityHoldStatus.Confirmed);
        (await verify.Subscriptions.FindAsync(loser))!.EndDate.Should().Be(date);
        (await verify.SubscriptionRestaurantDaySnapshots.CountAsync(x => x.SubscriptionId == loser && x.ServiceDate > date)).Should().Be(0);
        if (adminApproval) (await verify.SubscriptionFreezeRequests.SingleAsync(x => x.SubscriptionId == loser)).Status.Should().Be(VerificationStatus.Submitted);
        else (await verify.SubscriptionFreezeRequests.CountAsync(x => x.SubscriptionId == loser)).Should().Be(0);
    }
}
