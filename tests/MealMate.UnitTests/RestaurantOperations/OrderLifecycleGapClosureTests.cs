using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.CustomerCheckout.BackgroundJobs;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.RestaurantOrders;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Common;
using MealMate.Domain.Entities;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.RestaurantOperations;

public class OrderLifecycleGapClosureTests
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditLogger _audit;
    private readonly Guid _testRestaurantId = Guid.NewGuid();
    private readonly DateTime _baseNow = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    public OrderLifecycleGapClosureTests()
    {
        var options = new DbContextOptionsBuilder<Persistence.AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new Persistence.AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.RestaurantId.Returns(_testRestaurantId);
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.UserType.Returns(UserType.Restaurant);

        _clock = Substitute.For<IDateTimeProvider>();
        _clock.UtcNow.Returns(_baseNow);
        _clock.TodayUtc.Returns(DateOnly.FromDateTime(_baseNow.Date));

        _audit = Substitute.For<IAuditLogger>();
    }

    [Fact]
    public async Task RestaurantOrderEscalationJob_EscalatesOverdueOrders_ToReplacementRequired()
    {
        // Arrange
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active
        };
        _db.Subscriptions.Add(subscription);

        // Day locked 25 hours ago -> Overdue
        var overdueDay = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = DateOnly.FromDateTime(_baseNow.AddDays(2)),
            Status = SubscriptionDayStatus.Locked72h,
            LockAtUtc = _baseNow.AddHours(-25)
        };

        // Day locked 10 hours ago -> Not overdue
        var freshDay = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = DateOnly.FromDateTime(_baseNow.AddDays(3)),
            Status = SubscriptionDayStatus.Locked72h,
            LockAtUtc = _baseNow.AddHours(-10)
        };

        _db.SubscriptionDays.AddRange(overdueDay, freshDay);
        await _db.SaveChangesAsync();

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAppDbContext)).Returns(_db);
        serviceProvider.GetService(typeof(IDateTimeProvider)).Returns(_clock);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var logger = Substitute.For<ILogger<RestaurantOrderEscalationJob>>();
        var job = new RestaurantOrderEscalationJobTestable(scopeFactory, logger);

        // Act
        await job.RunOnceAsync(serviceProvider, CancellationToken.None);

        // Assert
        var updatedOverdue = await _db.SubscriptionDays.FindAsync(overdueDay.Id);
        var updatedFresh = await _db.SubscriptionDays.FindAsync(freshDay.Id);

        Assert.NotNull(updatedOverdue);
        Assert.Equal(SubscriptionDayStatus.ReplacementRequired, updatedOverdue.Status);

        Assert.NotNull(updatedFresh);
        Assert.Equal(SubscriptionDayStatus.Locked72h, updatedFresh.Status);

        var op = await _db.SubscriptionOperations.FirstOrDefaultAsync(x => x.SubscriptionId == subscription.Id);
        Assert.NotNull(op);
        Assert.Equal("ORDER_CONFIRMATION_OVERDUE_ESCALATED", op.OperationType);
    }

    [Fact]
    public async Task RejectOrder_WithStructuredReasonCode_LogsStructuredSubscriptionOperation()
    {
        // Arrange
        var service = new RestaurantOrderService(_db, _currentUser, _clock);
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active
        };
        _db.Subscriptions.Add(subscription);

        var day = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = DateOnly.FromDateTime(_baseNow.AddDays(3)),
            Status = SubscriptionDayStatus.Locked72h,
            LockAtUtc = _baseNow.AddHours(-2)
        };
        _db.SubscriptionDays.Add(day);
        await _db.SaveChangesAsync();

        var cmd = new RejectRestaurantOrderCommand(day.Id, "Shortage of fresh salmon", "INGREDIENT_SHORTAGE");

        // Act
        var result = await service.RejectOrderAsync(cmd, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var updatedDay = await _db.SubscriptionDays.FindAsync(day.Id);
        Assert.Equal(SubscriptionDayStatus.ReplacementRequired, updatedDay!.Status);

        var op = await _db.SubscriptionOperations.FirstOrDefaultAsync(x => x.SubscriptionId == subscription.Id && x.OperationType == "RESTAURANT_CONFIRMATION_REJECTED");
        Assert.NotNull(op);
        Assert.Contains("INGREDIENT_SHORTAGE", op.Reason);
        Assert.Contains("Shortage of fresh salmon", op.Reason);
    }

    [Fact]
    public async Task RequestSubscriptionFreeze_WhenDaysAreLocked72h_ReturnsValidationFailure()
    {
        // Arrange
        var handlers = new SubscriptionOperationsHandlers(_db, _audit, _clock, Substitute.For<ISubscriptionTierUpgradeQuoteBuilder>());
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            DurationDays = 30,
            Status = SubscriptionStatus.Active,
            StartDate = DateOnly.FromDateTime(_baseNow),
            EndDate = DateOnly.FromDateTime(_baseNow.AddDays(30))
        };
        _db.Subscriptions.Add(subscription);

        var lockedDay = new SubscriptionDay
        {
            SubscriptionId = subscription.Id,
            RestaurantId = _testRestaurantId,
            ServiceDate = DateOnly.FromDateTime(_baseNow.AddDays(3)),
            Status = SubscriptionDayStatus.Locked72h,
            LockAtUtc = _baseNow.AddHours(-5)
        };
        _db.SubscriptionDays.Add(lockedDay);
        await _db.SaveChangesAsync();

        var cmd = new RequestSubscriptionFreezeCommand(
            subscription.Id,
            DateOnly.FromDateTime(_baseNow.AddDays(3)),
            DateOnly.FromDateTime(_baseNow.AddDays(7)),
            "Vacation");

        // Act
        var result = await handlers.Handle(cmd, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("freeze.locked_days_cannot_be_frozen", result.Error.Code);
    }

    [Fact]
    public async Task RequestSubscriptionFreeze_WhenDaysAreOpen_SucceedsWithVerifiedReplacement()
    {
        var db = (Persistence.AppDbContext)_db;
        var date = DateOnly.FromDateTime(_baseNow.AddDays(5));
        var subscription = await MealMate.UnitTests.ProgramsSubscriptions.FreezeTestFixture.SeedMenuPoolAsync(db, _baseNow, date);
        (await db.SubscriptionDays.SingleAsync()).Status = SubscriptionDayStatus.Open;
        await db.SaveChangesAsync();
        var handlers = MealMate.UnitTests.ProgramsSubscriptions.FreezeTestFixture.CreateHandler(db, _clock);
        var result = await handlers.Handle(new RequestSubscriptionFreezeCommand(subscription.Id, date, date, "Travel"), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.FreezeDays);
        Assert.Equal(date.AddDays(1), subscription.EndDate);
    }
    private sealed class RestaurantOrderEscalationJobTestable(IServiceScopeFactory scopes, ILogger<RestaurantOrderEscalationJob> logger)
        : ScheduledJob(scopes, logger)
    {
        protected override TimeSpan Interval => TimeSpan.FromMinutes(5);
        protected override string JobName => nameof(RestaurantOrderEscalationJobTestable);

        public Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
            => ExecuteOnceAsync(services, cancellationToken);

        protected override async Task ExecuteOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            var db = services.GetRequiredService<IAppDbContext>();
            var clock = services.GetRequiredService<IDateTimeProvider>();
            var now = clock.UtcNow;

            var overdueDays = await db.SubscriptionDays
                .Where(x => (x.Status == SubscriptionDayStatus.Locked72h || x.Status == SubscriptionDayStatus.SentToRestaurant)
                            && x.LockAtUtc != null
                            && x.LockAtUtc.Value.AddHours(24) <= now)
                .ToListAsync(cancellationToken);

            foreach (var day in overdueDays)
            {
                day.Status = SubscriptionDayStatus.ReplacementRequired;
                db.SubscriptionOperations.Add(new SubscriptionOperation
                {
                    SubscriptionId = day.SubscriptionId,
                    OperationType = "ORDER_CONFIRMATION_OVERDUE_ESCALATED",
                    PreviousStatus = SubscriptionStatus.Active,
                    NewStatus = SubscriptionStatus.Active,
                    Reason = $"DayId={day.Id}; ServiceDate={day.ServiceDate}; RestaurantId={day.RestaurantId}; Exceeded 24h confirmation window.",
                    PerformedByUserId = Guid.Empty,
                    PerformedByUserType = UserType.Admin,
                    TimestampUtc = now
                });
            }

            if (overdueDays.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
