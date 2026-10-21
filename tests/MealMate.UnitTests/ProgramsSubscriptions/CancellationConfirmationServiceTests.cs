using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.ProgramsSubscriptions;

public sealed class CancellationConfirmationServiceTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    [Fact]
    public async Task ConfirmCancellation_CreatesDecisionAndReservedSourceAllocations()
    {
        // Arrange
        using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 10);
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
            StartDate = today.AddDays(-2),
            EndDate = today.AddDays(7),
            PaidAmount = new Money(100.000m, "KWD")
        };
        db.Subscriptions.Add(sub);

        // Day 1: Delivered
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(-2), Status = SubscriptionDayStatus.Delivered });
        // Day 2: Delivered
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(-1), Status = SubscriptionDayStatus.Delivered });
        // Day 3 (today): Locked
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today, Status = SubscriptionDayStatus.CustomerSelected });
        // Day 4 (tomorrow): Locked (within 24h)
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(1), Status = SubscriptionDayStatus.CustomerSelected });

        // Days 5-10: Refundable / Cancellable
        for (int i = 2; i <= 7; i++)
        {
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.CustomerSelected });
        }

        await db.SaveChangesAsync();

        var service = new CancellationConfirmationService(db, clock);

        var request = new CancellationConfirmationRequestDto(
            sub.Id,
            Reason: "CustomerMovingAway",
            IdempotencyKey: "op-test-12345");

        // Act
        var result = await service.ConfirmCancellationAsync(request, customerId, UserType.Customer);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.CancellationState.Should().Be("Confirmed");
        result.Value.OperationId.Should().Be("op-test-12345");
        result.Value.RefundDue.Should().Be(54.000m); // 6 unused days * 10.000m minus 10% cancel fee (6.000m)
        result.Value.IsZeroRefund.Should().BeFalse();

        // Verify database writes
        var savedDecision = await db.CancellationSettlementDecisions.FirstOrDefaultAsync(x => x.OperationId == "op-test-12345");
        savedDecision.Should().NotBeNull();
        savedDecision!.RefundDue.Should().Be(54.000m);
        savedDecision.FinancialState.Should().Be("PendingPosting");

        var sourceAllocs = await db.RefundSourceAllocations.Where(x => x.RefundOperationId == savedDecision.Id).ToListAsync();
        sourceAllocs.Should().HaveCount(1);
        sourceAllocs[0].Amount.Should().Be(54.000m);
        sourceAllocs[0].State.Should().Be(RefundSourceAllocationState.Reserved);

        // Subscription status: locked days remain, so CancellationPending
        sub.Status.Should().Be(SubscriptionStatus.CancellationPending);
    }

    [Fact]
    public async Task RepeatedConfirmWithSameIdempotencyKey_IsIdempotentAndDoesNotDuplicate()
    {
        using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 10);
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
            StartDate = today.AddDays(2),
            EndDate = today.AddDays(6),
            PaidAmount = new Money(50.000m, "KWD")
        };
        db.Subscriptions.Add(sub);

        for (int i = 2; i <= 6; i++)
        {
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.CustomerSelected });
        }
        await db.SaveChangesAsync();

        var service = new CancellationConfirmationService(db, clock);

        var request = new CancellationConfirmationRequestDto(
            sub.Id,
            Reason: "TestDuplicate",
            IdempotencyKey: "stable-op-999");

        // First call
        var result1 = await service.ConfirmCancellationAsync(request, customerId, UserType.Customer);
        result1.IsSuccess.Should().BeTrue();

        // Second call with same idempotency key
        var result2 = await service.ConfirmCancellationAsync(request, customerId, UserType.Customer);
        result2.IsSuccess.Should().BeTrue();
        result2.Value.DecisionId.Should().Be(result1.Value.DecisionId);
        result2.Value.OperationId.Should().Be("stable-op-999");

        // Ensure only one decision row exists in DB
        var count = await db.CancellationSettlementDecisions.CountAsync(x => x.OperationId == "stable-op-999");
        count.Should().Be(1);
    }

    [Fact]
    public async Task ZeroRefundCancellation_WithoutExplicitConfirmation_ReturnsConflict()
    {
        using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 10);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.TodayUtc.Returns(today);
        clock.UtcNow.Returns(today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var sub = new Subscription
        {
            CustomerUserId = customerId,
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            DurationDays = 2,
            StartDate = today.AddDays(-1),
            EndDate = today,
            PaidAmount = new Money(20.000m, "KWD")
        };
        db.Subscriptions.Add(sub);

        // Both days are either delivered or locked -> 0 unused refundable days
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(-1), Status = SubscriptionDayStatus.Delivered });
        db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today, Status = SubscriptionDayStatus.CustomerSelected }); // locked

        await db.SaveChangesAsync();

        var service = new CancellationConfirmationService(db, clock);

        var request = new CancellationConfirmationRequestDto(
            sub.Id,
            Reason: "ZeroRefundTest",
            IdempotencyKey: "op-zero-refund",
            ConfirmZeroRefund: false);

        var result = await service.ConfirmCancellationAsync(request, customerId, UserType.Customer);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("cancellation.zero_refund_confirmation_required");
    }

    [Fact]
    public async Task TwoDistinctRefundsCannotOverReserveSameSource()
    {
        using var db = CreateDb();
        var customerId = Guid.NewGuid();
        var today = new DateOnly(2026, 10, 10);
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
            StartDate = today.AddDays(2),
            EndDate = today.AddDays(11),
            PaidAmount = new Money(100.000m, "KWD")
        };
        db.Subscriptions.Add(sub);

        for (int i = 2; i <= 11; i++)
        {
            db.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = sub.Id, ServiceDate = today.AddDays(i), Status = SubscriptionDayStatus.CustomerSelected });
        }

        // Simulate an existing prior reserved allocation on the same subscription source of 60 KWD
        db.RefundSourceAllocations.Add(new RefundSourceAllocation
        {
            RefundOperationId = Guid.NewGuid(),
            OriginalSourceId = sub.Id,
            SourceType = FundingSourceType.OriginalCash,
            Amount = 60.000m,
            Currency = "KWD",
            State = RefundSourceAllocationState.Reserved
        });

        await db.SaveChangesAsync();

        var service = new CancellationConfirmationService(db, clock);

        // This cancellation attempts to refund 10 unused days = 90 KWD net refund (100 - 10% fee).
        // But 60 KWD is already reserved, so only 40 KWD is available. 90 > 40 -> Over-reserve rejection!
        var request = new CancellationConfirmationRequestDto(
            sub.Id,
            Reason: "ConcurrentOverReserveAttempt",
            IdempotencyKey: "op-concurrent-overreserve");

        var result = await service.ConfirmCancellationAsync(request, customerId, UserType.Customer);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("refund.source_already_reserved");
    }

    [Fact]
    public void RefundSourceAllocation_StateTransitions_SettleAndRelease_UpdateStateAndVersion()
    {
        var alloc = new RefundSourceAllocation
        {
            RefundOperationId = Guid.NewGuid(),
            OriginalSourceId = Guid.NewGuid(),
            SourceType = FundingSourceType.OriginalCash,
            Amount = 25.000m,
            Currency = "KWD",
            State = RefundSourceAllocationState.Reserved,
            ConcurrencyVersion = 1
        };

        // 1. Settle
        var settleTime = DateTime.UtcNow;
        alloc.Settle(journalRef: "GL-REF-101", providerRef: "PAY-GATE-999", settledAtUtc: settleTime);

        alloc.State.Should().Be(RefundSourceAllocationState.Settled);
        alloc.JournalRef.Should().Be("GL-REF-101");
        alloc.ProviderRef.Should().Be("PAY-GATE-999");
        alloc.SettledAtUtc.Should().Be(settleTime);
        alloc.ConcurrencyVersion.Should().Be(2);

        // 2. Attempting to settle again throws
        Action settleAgain = () => alloc.Settle();
        settleAgain.Should().Throw<InvalidOperationException>();

        // 3. New allocation release
        var alloc2 = new RefundSourceAllocation
        {
            RefundOperationId = Guid.NewGuid(),
            OriginalSourceId = Guid.NewGuid(),
            SourceType = FundingSourceType.OriginalCash,
            Amount = 15.000m,
            Currency = "KWD",
            State = RefundSourceAllocationState.Reserved,
            ConcurrencyVersion = 1
        };

        alloc2.Release();
        alloc2.State.Should().Be(RefundSourceAllocationState.Released);
        alloc2.ConcurrencyVersion.Should().Be(2);

        // 4. Attempting to release again throws
        Action releaseAgain = () => alloc2.Release();
        releaseAgain.Should().Throw<InvalidOperationException>();
    }
}

