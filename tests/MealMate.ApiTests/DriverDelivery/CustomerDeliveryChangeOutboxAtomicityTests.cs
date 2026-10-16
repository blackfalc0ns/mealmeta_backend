using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.ApiTests.DriverDelivery;

public sealed class CustomerDeliveryChangeOutboxAtomicityTests : IAsyncLifetime
{
    private readonly MealMate.Testing.PostgresTestDatabase _database = new();
    private static readonly DateTime Now = new(2026, 10, 16, 12, 30, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Subscription_day_update_and_delivery_invalidation_roll_back_together()
    {
        var day = NewDay();
        await using (var seed = CreateDb())
        {
            seed.SubscriptionDays.Add(day);
            await seed.SaveChangesAsync();
        }

        await using (var db = CreateDb())
        {
            var transaction = await db.Database.BeginTransactionAsync();
            var tracked = await db.SubscriptionDays.SingleAsync(item => item.Id == day.Id);
            tracked.Status = SubscriptionDayStatus.Preparing;
            await db.SaveChangesAsync();
            Assert.Single(await db.OutboxMessages.ToListAsync());
            await transaction.RollbackAsync();
        }

        await using var verification = CreateDb();
        Assert.Equal(SubscriptionDayStatus.Open, (await verification.SubscriptionDays.SingleAsync(item => item.Id == day.Id)).Status);
        Assert.Empty(await verification.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_same_day_transitions_commit_exactly_one_invalidation()
    {
        var day = NewDay();
        await using (var seed = CreateDb())
        {
            seed.SubscriptionDays.Add(day);
            await seed.SaveChangesAsync();
        }

        await using var first = CreateDb();
        await using var second = CreateDb();
        var firstDay = await first.SubscriptionDays.SingleAsync(item => item.Id == day.Id);
        var secondDay = await second.SubscriptionDays.SingleAsync(item => item.Id == day.Id);
        firstDay.Status = SubscriptionDayStatus.Preparing;
        secondDay.Status = SubscriptionDayStatus.Preparing;

        var outcomes = await Task.WhenAll(
            SaveAndCaptureAsync(first),
            SaveAndCaptureAsync(second));

        Assert.Equal(1, outcomes.Count(succeeded => succeeded));
        await using var verification = CreateDb();
        Assert.Equal(SubscriptionDayStatus.Preparing, (await verification.SubscriptionDays.SingleAsync(item => item.Id == day.Id)).Status);
        var invalidation = await verification.OutboxMessages.SingleAsync();
        Assert.Equal(day.Id.ToString("D"), invalidation.AggregateId);
    }

    private async Task<bool> SaveAndCaptureAsync(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_database.GetConnectionString())
            .Options;
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(Now);
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>(), clock);
    }

    private static SubscriptionDay NewDay() => new()
    {
        SubscriptionId = Guid.NewGuid(),
        ServiceDate = new DateOnly(2026, 10, 16),
        Status = SubscriptionDayStatus.Open
    };
}
