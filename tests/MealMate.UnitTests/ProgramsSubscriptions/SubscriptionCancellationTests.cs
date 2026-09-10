using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public class SubscriptionCancellationTests
{
    private static DbContextOptions<AppDbContext> CreateInMemoryOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task GetQuote_ForActiveSubscription_CalculatesDeliveredLockedAndRefundableDaysCorrectly()
    {
        var options = CreateInMemoryOptions();
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        using var db = new AppDbContext(options, domainDispatcher);

        var customerId = Guid.NewGuid();
        var today = new DateOnly(2026, 8, 10);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 10,
            StartDate = today.AddDays(-1),
            EndDate = today.AddDays(8),
            PaidAmount = new Money(50.000m, "KWD"),
        };
        db.Subscriptions.Add(sub);

        // Day 1: Delivered
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(-1), Status = SubscriptionDayStatus.Delivered });
        // Day 2 (today): Locked (in 72h lead window)
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today, Status = SubscriptionDayStatus.Locked72h });
        // Day 3 (today + 1): Locked
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(1), Status = SubscriptionDayStatus.Locked72h });
        // Day 4 (today + 2): Locked (3rd day of 72h lead window)
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(2), Status = SubscriptionDayStatus.Locked72h });
        // Day 5-10: Open (6 refundable days)
        for (var i = 3; i < 9; i++)
        {
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.Open });
        }
        await db.SaveChangesAsync();

        var service = new SubscriptionCancellationService(db, clock);
        var result = await service.GetQuoteAsync(sub.Id, customerId, UserType.Customer);

        result.IsSuccess.Should().BeTrue();
        var quote = result.Value;
        quote.TotalServiceDays.Should().Be(10);
        quote.DeliveredDays.Should().Be(1);
        quote.LockedKitchenDays.Should().Be(3);
        quote.RefundableDays.Should().Be(6);
        quote.DailyBoxPrice.Should().Be(5.000m);
        quote.RefundAmount.Should().Be(30.000m);
        quote.CurrencyCode.Should().Be("KWD");
        quote.EffectiveCancellationDate.Should().Be(today.AddDays(3)); // Day after last locked day
    }

    [Fact]
    public async Task GetQuote_ForScheduledSubscription_Calculates100PercentRefundWithZeroLockedDays()
    {
        var options = CreateInMemoryOptions();
        using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var customerId = Guid.NewGuid();
        var today = new DateOnly(2026, 8, 10);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        // Starts in 10 days
        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Scheduled,
            DurationDays = 5,
            StartDate = today.AddDays(10),
            EndDate = today.AddDays(14),
            PaidAmount = new Money(25.000m, "KWD"),
        };
        db.Subscriptions.Add(sub);

        for (var i = 10; i <= 14; i++)
        {
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.Open });
        }
        await db.SaveChangesAsync();

        var service = new SubscriptionCancellationService(db, clock);
        var result = await service.GetQuoteAsync(sub.Id, customerId, UserType.Customer);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalServiceDays.Should().Be(5);
        result.Value.DeliveredDays.Should().Be(0);
        result.Value.LockedKitchenDays.Should().Be(0);
        result.Value.RefundableDays.Should().Be(5);
        result.Value.RefundAmount.Should().Be(25.000m);
        result.Value.EffectiveCancellationDate.Should().Be(today);
    }

    [Fact]
    public async Task RequestCancellation_ForScheduledSubscription_CancelsImmediatelyAndSetsStatusCancelled()
    {
        var options = CreateInMemoryOptions();
        using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var customerId = Guid.NewGuid();
        var today = new DateOnly(2026, 8, 10);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var audit = Substitute.For<IAuditLogger>();

        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Scheduled,
            DurationDays = 3,
            StartDate = today.AddDays(10),
            EndDate = today.AddDays(12),
            PaidAmount = new Money(15.000m, "KWD"),
        };
        db.Subscriptions.Add(sub);

        for (var i = 10; i <= 12; i++)
        {
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.Open });
        }
        await db.SaveChangesAsync();

        var service = new SubscriptionCancellationService(db, clock, audit);
        var result = await service.RequestCancellationAsync(sub.Id, customerId, UserType.Customer, "Change of travel plans");

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SubscriptionStatus.Cancelled.ToString());
        result.Value.RefundAmount.Should().Be(15.000m);
        result.Value.RefundableDays.Should().Be(3);
        result.Value.LockedKitchenDays.Should().Be(0);

        var updatedSub = await db.Subscriptions.FirstAsync(x => x.Id == sub.Id);
        updatedSub.Status.Should().Be(SubscriptionStatus.Cancelled);

        var days = await db.SubscriptionDays.Where(x => x.SubscriptionId == sub.Id).ToListAsync();
        days.Should().OnlyContain(x => x.Status == SubscriptionDayStatus.Cancelled);

        var operation = await db.SubscriptionOperations.FirstOrDefaultAsync(x => x.SubscriptionId == sub.Id && x.OperationType == "CANCELLATION_PROCESSED");
        operation.Should().NotBeNull();
        operation!.NewStatus.Should().Be(SubscriptionStatus.Cancelled);
        operation.Reason.Should().Be("Change of travel plans");

        await audit.Received(1).LogAsync(Arg.Is<AuditEntry>(e => e.Action == "SubscriptionCancelled"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestCancellation_ForActiveSubscriptionWithLockedDays_SetsStatusCancellationPending()
    {
        var options = CreateInMemoryOptions();
        using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var customerId = Guid.NewGuid();
        var today = new DateOnly(2026, 8, 10);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 5,
            StartDate = today,
            EndDate = today.AddDays(4),
            PaidAmount = new Money(25.000m, "KWD"),
        };
        db.Subscriptions.Add(sub);

        // Days 1-3 (today, today+1, today+2): Within 72h lead window
        var lockedDay1 = new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today, Status = SubscriptionDayStatus.Locked72h };
        var lockedDay2 = new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(1), Status = SubscriptionDayStatus.Locked72h };
        var lockedDay3 = new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(2), Status = SubscriptionDayStatus.Locked72h };
        db.SubscriptionDays.AddRange(lockedDay1, lockedDay2, lockedDay3);

        // Days 4-5: Open beyond lead window (2 refundable days)
        for (var i = 3; i <= 4; i++)
        {
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.Open });
        }
        await db.SaveChangesAsync();

        var service = new SubscriptionCancellationService(db, clock);
        var result = await service.RequestCancellationAsync(sub.Id, customerId, UserType.Customer, "Moving away");

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SubscriptionStatus.CancellationPending.ToString());
        result.Value.LockedKitchenDays.Should().Be(3);
        result.Value.RefundableDays.Should().Be(2);
        result.Value.RefundAmount.Should().Be(10.000m);

        var updatedSub = await db.Subscriptions.FirstAsync(x => x.Id == sub.Id);
        updatedSub.Status.Should().Be(SubscriptionStatus.CancellationPending);

        var checkLocked1 = await db.SubscriptionDays.FindAsync(lockedDay1.Id);
        checkLocked1!.Status.Should().Be(SubscriptionDayStatus.Locked72h); // Remains locked for delivery

        var cancelledCount = await db.SubscriptionDays.CountAsync(x => x.SubscriptionId == sub.Id && x.Status == SubscriptionDayStatus.Cancelled);
        cancelledCount.Should().Be(2);
    }

    [Fact]
    public async Task RequestCancellation_WhenUnauthorizedCustomerAttempts_ReturnsForbidden()
    {
        var options = CreateInMemoryOptions();
        using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var today = new DateOnly(2026, 8, 10);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var sub = new Subscription
        {
            CustomerUserId = ownerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 5,
            StartDate = today,
            EndDate = today.AddDays(4),
            PaidAmount = new Money(25.000m, "KWD"),
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        var service = new SubscriptionCancellationService(db, clock);
        var result = await service.RequestCancellationAsync(sub.Id, strangerId, UserType.Customer, "Hacking attempt");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task RequestCancellation_ForFamilySubscription_WhenNonManagerAttempts_ReturnsConflict()
    {
        var options = CreateInMemoryOptions();
        using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var managerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var familyGroupId = Guid.NewGuid();
        var today = new DateOnly(2026, 8, 10);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Al-Ahmad Family",
            Status = FamilyGroupStatus.Active,
        };
        db.FamilyGroups.Add(group);

        var sub = new Subscription
        {
            CustomerUserId = memberId,
            FamilyGroupId = group.Id,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 5,
            StartDate = today,
            EndDate = today.AddDays(4),
            PaidAmount = new Money(25.000m, "KWD"),
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        var service = new SubscriptionCancellationService(db, clock);
        var result = await service.RequestCancellationAsync(sub.Id, memberId, UserType.Customer, "Member cancel");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("family.manager_only_action");
    }

    [Fact]
    public async Task AdminCancel_WithForceImmediate_CancelsAllFutureDaysAndSetsStatusCancelled()
    {
        var options = CreateInMemoryOptions();
        using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var adminId = Guid.NewGuid();
        var today = new DateOnly(2026, 8, 10);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var sub = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 4,
            StartDate = today,
            EndDate = today.AddDays(3),
            PaidAmount = new Money(40.000m, "KWD"),
        };
        db.Subscriptions.Add(sub);

        // Day 1 (today): Locked
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today, Status = SubscriptionDayStatus.Locked72h });
        // Days 2-4: Open
        for (var i = 1; i <= 3; i++)
        {
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.Open });
        }
        await db.SaveChangesAsync();

        var service = new SubscriptionCancellationService(db, clock);
        var result = await service.AdminCancelAsync(sub.Id, adminId, "Emergency immediate cancellation", forceImmediate: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(SubscriptionStatus.Cancelled.ToString());
        result.Value.LockedKitchenDays.Should().Be(0);
        result.Value.RefundableDays.Should().Be(4); // All 4 un-delivered days cancelled
        result.Value.RefundAmount.Should().Be(40.000m);

        var updatedSub = await db.Subscriptions.FirstAsync(x => x.Id == sub.Id);
        updatedSub.Status.Should().Be(SubscriptionStatus.Cancelled);

        var cancelledDays = await db.SubscriptionDays.CountAsync(x => x.SubscriptionId == sub.Id && x.Status == SubscriptionDayStatus.Cancelled);
        cancelledDays.Should().Be(4);

        var op = await db.SubscriptionOperations.FirstAsync(x => x.SubscriptionId == sub.Id && x.OperationType == "ADMIN_CANCELLED_IMMEDIATE");
        op.Should().NotBeNull();
        op.PerformedByUserType.Should().Be(UserType.Admin);
    }

    [Fact]
    public async Task MediatR_CQRS_Handlers_ExecuteSuccessfully()
    {
        var options = CreateInMemoryOptions();
        using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var customerId = Guid.NewGuid();
        var today = new DateOnly(2026, 8, 10);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Scheduled,
            DurationDays = 5,
            StartDate = today.AddDays(5),
            EndDate = today.AddDays(9),
            PaidAmount = new Money(25.000m, "KWD"),
        };
        db.Subscriptions.Add(sub);
        for (var i = 5; i <= 9; i++)
        {
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.Open });
        }
        await db.SaveChangesAsync();

        var service = new SubscriptionCancellationService(db, clock);
        var handlers = new SubscriptionCancellationHandlers(service);

        // 1. Query quote
        var quoteRes = await handlers.Handle(new GetSubscriptionCancellationQuoteQuery(sub.Id, customerId, UserType.Customer), CancellationToken.None);
        quoteRes.IsSuccess.Should().BeTrue();
        quoteRes.Value.RefundAmount.Should().Be(25.000m);

        // 2. Command cancel
        var cancelRes = await handlers.Handle(new RequestSubscriptionCancellationCommand(sub.Id, customerId, UserType.Customer, "Cancel via MediatR"), CancellationToken.None);
        cancelRes.IsSuccess.Should().BeTrue();
        cancelRes.Value.Status.Should().Be(SubscriptionStatus.Cancelled.ToString());
    }

    [Fact]
    public async Task CancelUncompletedSubscription_CancelsAndReleasesHolds()
    {
        var options = CreateInMemoryOptions();
        using var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());

        var customerId = Guid.NewGuid();
        var today = new DateOnly(2026, 8, 10);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.PendingPayment,
            DurationDays = 5,
            StartDate = today.AddDays(5),
            EndDate = today.AddDays(9),
            PaidAmount = new Money(25.000m, "KWD"),
        };
        db.Subscriptions.Add(sub);

        var hold = new CapacityHold
        {
            SubscriptionId = sub.Id,
            RestaurantId = Guid.NewGuid(),
            ServiceDate = today.AddDays(5),
            Status = CapacityHoldStatus.PendingPayment,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
        };
        db.CapacityHolds.Add(hold);
        await db.SaveChangesAsync();

        var service = new SubscriptionCancellationService(db, clock);
        var result = await service.CancelUncompletedSubscriptionAsync(sub.Id, customerId);

        result.IsSuccess.Should().BeTrue();

        var updatedSub = await db.Subscriptions.FirstAsync(x => x.Id == sub.Id);
        updatedSub.Status.Should().Be(SubscriptionStatus.Cancelled);

        var updatedHold = await db.CapacityHolds.FirstAsync(x => x.SubscriptionId == sub.Id);
        updatedHold.Status.Should().Be(CapacityHoldStatus.Released);

        var op = await db.SubscriptionOperations.FirstAsync(x => x.SubscriptionId == sub.Id && x.OperationType == "CUSTOMER_CANCELLED_UNCOMPLETED");
        op.Should().NotBeNull();
    }
}
