using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.AdminOrders;
using MealMate.Application.Features.CustomerCalendar;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCalendar;

public sealed partial class CustomerCalendarRestaurantFlowTests
{
    [Fact]
    public async Task Replacement_window_uses_opening_time_and_remains_editable_after_changing_restaurant_and_completing_box()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var day = await fx.Db.SubscriptionDays.SingleAsync();
        day.Status = SubscriptionDayStatus.ReplacementRequired;
        day.ConfirmedAtUtc = BeforeDeadline;
        var order = new RestaurantDailyOrder { SubscriptionDayId = day.Id, RestaurantId = fx.RestaurantA,
            ServiceDate = Day1, Status = OrderStatus.ReplacementPending, CreatedAtUtc = BeforeDeadline };
        fx.Db.Add(order);
        await fx.Db.SaveChangesAsync();
        fx.Clock.UtcNow.Returns(AfterDeadline);
        var handler = new OpenReplacementWindowCommandHandler(fx.Db, fx.Clock);
        var opened = await handler.Handle(new(order.Id), default);
        opened.IsSuccess.Should().BeTrue();
        var detail = await fx.Engine.GetDayAsync(fx.SubscriptionId, Day1, default);
        detail.Value.CanEdit.Should().BeTrue();
        detail.Value.SelectionDeadlineUtc.Should().Be(AfterDeadline.AddHours(24));
        detail.Value.LockRemainingSeconds.Should().Be(86400);
        var list = await new GetReplacementWindowsQueryHandler(fx.Db, fx.Clock).Handle(new(), default);
        list.Value.Items.Single().Status.Should().Be("open");
        list.Value.Items.Single().WindowOpensAt.Should().Be(AfterDeadline);
        list.Value.Items.Single().WindowClosesAt.Should().Be(opened.Value.WindowClosesAt);
        fx.Clock.UtcNow.Returns(AfterDeadline.AddHours(1));
        var repeat = await handler.Handle(new(order.Id), default);
        repeat.Value.WindowClosesAt.Should().Be(opened.Value.WindowClosesAt, "retries must not extend the window");
        await FillAndComplete(fx, Day1, fx.RestaurantB, fx.BreakfastB, fx.LunchB, fx.DinnerB);
        (await fx.Engine.GetDayAsync(fx.SubscriptionId, Day1, default)).Value.CanEdit.Should().BeTrue();
        var confirm = await fx.Engine.ConfirmAndSendToRestaurantAsync(fx.SubscriptionId, Day1, default);
        confirm.IsSuccess.Should().BeTrue();
        confirm.Value.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
        var early = await fx.Engine.AutoSelectFullBoxAsync(fx.SubscriptionId, Day1, true, false, default);
        early.IsFailure.Should().BeTrue();
        early.Error.Code.Should().Be("subscription.cutoff_not_due");
        fx.Clock.UtcNow.Returns(opened.Value.WindowClosesAt);
        var expired = await fx.Engine.GetDayAsync(fx.SubscriptionId, Day1, default);
        expired.Value.CanEdit.Should().BeFalse();
        (await fx.Engine.SelectRestaurantAsync(fx.SubscriptionId, Day1, fx.RestaurantA, default)).IsFailure.Should().BeTrue();
        (await new GetReplacementWindowsQueryHandler(fx.Db, fx.Clock).Handle(new(), default)).Value.Items.Single().Status.Should().Be("expired");
        day.SelectionDeadlineUtc.Should().Be(Deadline, "the original 72-hour cutoff must remain stored");
    }

    [Theory]
    [InlineData(SubscriptionDayStatus.ConfirmedByRestaurant)]
    [InlineData(SubscriptionDayStatus.Preparing)]
    [InlineData(SubscriptionDayStatus.Delivered)]
    [InlineData(SubscriptionDayStatus.Cancelled)]
    [InlineData(SubscriptionDayStatus.Frozen)]
    public async Task Replacement_window_cannot_reopen_committed_or_unavailable_day(SubscriptionDayStatus status)
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        var day = await fx.Db.SubscriptionDays.SingleAsync();
        day.Status = status;
        var order = new RestaurantDailyOrder { SubscriptionDayId = day.Id, RestaurantId = fx.RestaurantA,
            ServiceDate = Day1, Status = OrderStatus.ReplacementPending };
        fx.Db.Add(order);
        await fx.Db.SaveChangesAsync();
        (await new OpenReplacementWindowCommandHandler(fx.Db, fx.Clock).Handle(new(order.Id), default)).IsFailure.Should().BeTrue();
        day.Status.Should().Be(status);
        var reassignment = await ReplacementAdminService(fx).ReassignRestaurantAsync(fx.SubscriptionId, Day1,
            new ReassignRestaurantRequest(fx.RestaurantB), Guid.NewGuid(), UserType.Admin);
        reassignment.Value.ErrorCode.Should().Be("reassignment.day_unavailable");
        day.Status.Should().Be(status);
    }

    [Fact]
    public async Task Replacement_window_is_processed_by_cutoff_job_only_at_its_own_deadline()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var day = await fx.Db.SubscriptionDays.SingleAsync();
        day.Status = SubscriptionDayStatus.ReplacementRequired;
        var order = new RestaurantDailyOrder { SubscriptionDayId = day.Id, RestaurantId = fx.RestaurantA,
            ServiceDate = Day1, Status = OrderStatus.ReplacementPending };
        fx.Db.Add(order);
        await fx.Db.SaveChangesAsync();
        fx.Clock.UtcNow.Returns(AfterDeadline);
        var opened = await new OpenReplacementWindowCommandHandler(fx.Db, fx.Clock).Handle(new(order.Id), default);
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IAppDbContext)).Returns(fx.Db);
        provider.GetService(typeof(IDateTimeProvider)).Returns(fx.Clock);
        provider.GetService(typeof(CustomerCalendarEngine)).Returns(fx.Engine);
        var job = new MealMate.Application.Features.CustomerCheckout.BackgroundJobs.SubscriptionDayLockJob(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<MealMate.Application.Features.CustomerCheckout.BackgroundJobs.SubscriptionDayLockJob>>());
        (await job.ProcessDueDaysAsync(provider, default)).DueCount.Should().Be(0);
        fx.Clock.UtcNow.Returns(opened.Value.WindowClosesAt);
        var processed = await job.ProcessDueDaysAsync(provider, default);
        processed.DueCount.Should().Be(1);
        processed.SucceededCount.Should().Be(1);
        (await fx.Db.SubscriptionDays.SingleAsync()).Status.Should().Be(SubscriptionDayStatus.SentToRestaurant);
        (await fx.Db.RestaurantDailyOrders.SingleAsync()).Status.Should().Be(OrderStatus.AwaitingRestaurantConfirmation);
        (await fx.Db.RestaurantDailyOrders.SingleAsync()).CreatedAtUtc.Should().Be(opened.Value.WindowClosesAt);
    }

    [Fact]
    public async Task Admin_reassignment_moves_old_pending_order_into_replacement_lifecycle_until_window_expires()
    {
        var fx = await SeedAsync(Guid.NewGuid(), BeforeDeadline);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        var day = await fx.Db.SubscriptionDays.SingleAsync();
        day.Status = SubscriptionDayStatus.ReplacementRequired;
        var old = new RestaurantDailyOrder { SubscriptionDayId = day.Id, RestaurantId = fx.RestaurantA,
            ServiceDate = Day1, Status = OrderStatus.AwaitingRestaurantConfirmation, CreatedAtUtc = BeforeDeadline };
        fx.Db.Add(old);
        var profile = new MealMate.Domain.IdentityAccess.RestaurantProfile {
            UserId = Guid.NewGuid(), TradeNameAr = "مطعم ب", TradeNameEn = "Restaurant B", LegalNameAr = "مطعم ب",
            LegalNameEn = "Restaurant B", VerificationStatus = VerificationStatus.Approved, IsOperationallyActive = true };
        fx.Db.Entry(profile).Property(x => x.Id).CurrentValue = fx.RestaurantB;
        fx.Db.Add(profile);
        await fx.Db.SaveChangesAsync();
        await SeedCapacityAsync(fx, fx.RestaurantB, 5);
        fx.Clock.UtcNow.Returns(AfterDeadline);
        var changed = await ReplacementAdminService(fx).ReassignRestaurantAsync(fx.SubscriptionId, Day1,
            new ReassignRestaurantRequest(fx.RestaurantB), Guid.NewGuid(), UserType.Admin);
        changed.Value.ErrorCode.Should().BeNull();
        old.Status.Should().Be(OrderStatus.ReplacementPending);
        await FillAndComplete(fx, Day1, fx.RestaurantB, fx.BreakfastB, fx.LunchB, fx.DinnerB);
        var confirm = await fx.Engine.ConfirmAndSendToRestaurantAsync(fx.SubscriptionId, Day1, default);
        confirm.Value.Status.Should().Be(SubscriptionDayStatus.CustomerSelected);
        (await fx.Db.RestaurantDailyOrders.CountAsync(x => x.Status == OrderStatus.AwaitingRestaurantConfirmation)).Should().Be(0);
        fx.Clock.UtcNow.Returns(AfterDeadline.AddHours(24));
        (await fx.Engine.AutoSelectFullBoxAsync(fx.SubscriptionId, Day1, true, false, default)).IsSuccess.Should().BeTrue();
        old.Status.Should().Be(OrderStatus.Cancelled);
        var replacementOrder = await fx.Db.RestaurantDailyOrders.SingleAsync(x => x.RestaurantId == fx.RestaurantB);
        replacementOrder.Status.Should().Be(OrderStatus.AwaitingRestaurantConfirmation);

        // A second replacement may return to the first restaurant. Its superseded order
        // must become a new actionable attempt rather than remaining cancelled forever.
        day.Status = SubscriptionDayStatus.ReplacementRequired;
        await fx.Db.SaveChangesAsync();
        var secondWindow = await new OpenReplacementWindowCommandHandler(fx.Db, fx.Clock).Handle(new(replacementOrder.Id), default);
        await FillAndComplete(fx, Day1, fx.RestaurantA, fx.BreakfastA, fx.LunchA, fx.DinnerA);
        fx.Clock.UtcNow.Returns(secondWindow.Value.WindowClosesAt);
        (await fx.Engine.AutoSelectFullBoxAsync(fx.SubscriptionId, Day1, true, false, default)).IsSuccess.Should().BeTrue();
        old.Status.Should().Be(OrderStatus.AwaitingRestaurantConfirmation);
        old.CreatedAtUtc.Should().Be(secondWindow.Value.WindowClosesAt);
        replacementOrder.Status.Should().Be(OrderStatus.Cancelled);
    }

    private static AdminCheckoutOperationsService ReplacementAdminService(FlowFixture fx)
        => new(fx.Db, fx.Clock, Substitute.For<ICapacityConcurrencyLock>(), new InlineTransactionExecutor(),
            Substitute.For<ICapacityHoldExpiryService>(), Substitute.For<ISubscriptionFulfillmentService>(),
            new RestaurantCapacityReader(fx.Db, fx.Clock));
}
