using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace MealMate.IntegrationTests.IdentityAccess;

/// <summary>
/// Real-PostgreSQL checks for the customer notification inbox: the migration chain
/// applies on an isolated database, and the (UserId, EventKey, SubjectId) unique key
/// keeps concurrent inbox writers idempotent.
/// </summary>
public sealed class CustomerNotificationInboxPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private AppDbContext Db() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    [Fact]
    public async Task ConcurrentDuplicateInserts_KeepExactlyOneInboxRow()
    {
        var userId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        await using var first = Db();
        await using var second = Db();
        first.CustomerNotifications.Add(New(userId, subjectId));
        second.CustomerNotifications.Add(New(userId, subjectId));

        var errors = new List<Exception>();
        async Task Save(AppDbContext db)
        {
            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        await Task.WhenAll(Save(first), Save(second));

        errors.Should().ContainSingle().Which.Should().BeOfType<DbUpdateException>();
        await using var verify = Db();
        (await verify.CustomerNotifications.CountAsync(x => x.UserId == userId && x.SubjectId == subjectId)).Should().Be(1);
    }

    [Fact]
    public async Task UnreadCount_AfterRead_MatchesReadAtUtcState()
    {
        var userId = Guid.NewGuid();
        await using var db = Db();
        var row = New(userId, Guid.NewGuid());
        db.CustomerNotifications.Add(row);
        await db.SaveChangesAsync();

        (await db.CustomerNotifications.CountAsync(x => x.UserId == userId && x.ReadAtUtc == null)).Should().Be(1);

        row.ReadAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        (await db.CustomerNotifications.CountAsync(x => x.UserId == userId && x.ReadAtUtc == null)).Should().Be(0);
    }

    [Fact]
    public async Task SoftDeletedRow_IsExcludedByGlobalQueryFilter()
    {
        var userId = Guid.NewGuid();
        await using var db = Db();
        var row = New(userId, Guid.NewGuid());
        db.CustomerNotifications.Add(row);
        await db.SaveChangesAsync();

        row.IsDeleted = true;
        await db.SaveChangesAsync();

        (await db.CustomerNotifications.CountAsync(x => x.UserId == userId)).Should().Be(0);
    }

    [Fact]
    public async Task RolledBackTransaction_LeavesNoInboxRowBehind()
    {
        var userId = Guid.NewGuid();
        await using var db = Db();
        var day = new MealMate.Domain.CalendarMeals.SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            ServiceDate = new DateOnly(2026, 10, 10),
            Status = MealMate.Domain.Enums.SubscriptionDayStatus.SentToRestaurant,
        };
        db.SubscriptionDays.Add(day);
        await db.SaveChangesAsync();
        var dayId = day.Id;

        var clock = Substitute.For<MealMate.Application.Common.Abstractions.IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var writer = new MealMate.Application.Features.CustomerNotifications.CustomerNotificationWriter(
            db, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<MealMate.Application.Features.CustomerNotifications.CustomerNotificationWriter>.Instance);

        await using var transaction = await db.Database.BeginTransactionAsync();
        await writer.EnsureCreatedAsync(userId, MealMate.Application.Features.CustomerPush.CustomerPushEventKeys.OrderLocked72h, dayId);
        (await db.CustomerNotifications.IgnoreQueryFilters().CountAsync(x => x.UserId == userId)).Should().Be(1);
        await transaction.RollbackAsync();

        await using var verify = Db();
        (await verify.CustomerNotifications.IgnoreQueryFilters().CountAsync(x => x.UserId == userId)).Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentEnsureCreatedAsync_StaysIdempotent()
    {
        var userId = Guid.NewGuid();
        await using var setup = Db();
        var day = new MealMate.Domain.CalendarMeals.SubscriptionDay
        {
            SubscriptionId = Guid.NewGuid(),
            ServiceDate = new DateOnly(2026, 10, 10),
            Status = MealMate.Domain.Enums.SubscriptionDayStatus.SentToRestaurant,
        };
        setup.SubscriptionDays.Add(day);
        await setup.SaveChangesAsync();
        var dayId = day.Id;

        var clock = Substitute.For<MealMate.Application.Common.Abstractions.IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        await using var dbA = Db();
        await using var dbB = Db();
        var writerA = new MealMate.Application.Features.CustomerNotifications.CustomerNotificationWriter(
            dbA, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<MealMate.Application.Features.CustomerNotifications.CustomerNotificationWriter>.Instance);
        var writerB = new MealMate.Application.Features.CustomerNotifications.CustomerNotificationWriter(
            dbB, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<MealMate.Application.Features.CustomerNotifications.CustomerNotificationWriter>.Instance);

        var act = () => Task.WhenAll(
            writerA.EnsureCreatedAsync(userId, MealMate.Application.Features.CustomerPush.CustomerPushEventKeys.OrderConfirmedByRestaurant, dayId),
            writerB.EnsureCreatedAsync(userId, MealMate.Application.Features.CustomerPush.CustomerPushEventKeys.OrderConfirmedByRestaurant, dayId));

        await act.Should().NotThrowAsync();

        await using var verify = Db();
        (await verify.CustomerNotifications.CountAsync(x => x.UserId == userId && x.EventKey == MealMate.Application.Features.CustomerPush.CustomerPushEventKeys.OrderConfirmedByRestaurant))
            .Should().Be(1);
    }

    private static CustomerNotification New(Guid userId, Guid subjectId) => new()
    {
        UserId = userId,
        EventKey = "order.confirmed_by_restaurant",
        SubjectId = subjectId,
        SubscriptionId = null,
        TitleAr = "تم تأكيد طلبك",
        TitleEn = "Order Confirmed",
        BodyAr = "المطعم أكد وجبتك.",
        BodyEn = "The restaurant confirmed your meal.",
        DeepLink = "mealmate://orders",
    };
}
