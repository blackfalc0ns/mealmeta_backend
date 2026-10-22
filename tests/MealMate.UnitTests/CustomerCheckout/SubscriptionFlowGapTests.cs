using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.CustomerCheckout;

public sealed class SubscriptionFlowGapTests
{
    [Fact]
    public void New_family_member_card_defaults_to_subscribe_now()
    {
        var card = new FamilyMemberCardDto(
            Guid.NewGuid(), FamilyMemberRole.Member, "Member", "child", FamilyMemberGender.Male,
            12, null, FamilyMemberSetupStatus.NotStarted, null, null, null, null, false, false);

        Assert.True(card.SubscribeNow);
    }

    [Fact]
    public void Schedule_uses_Kuwait_cutoff_and_bundle_slot_json_supports_counts()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 13, 14, 59, 59, DateTimeKind.Utc));
        var schedule = new SubscriptionScheduleService(clock);

        Assert.Equal(new DateTime(2026, 8, 10, 21, 0, 0, DateTimeKind.Utc), schedule.GetSelectionDeadlineUtc(new DateOnly(2026, 8, 14)));
        clock.UtcNow.Returns(new DateTime(2026, 8, 10, 20, 59, 59, DateTimeKind.Utc));
        Assert.True(schedule.IsStartAllowed(new DateOnly(2026, 8, 14)));
        var requirements = SlotRequirementJson.Parse("[{\"slot\":\"lunch\",\"count\":2},{\"slot\":\"snack\",\"count\":1}]");
        Assert.Equal(2, requirements.Single(x => x.Slot == "lunch").Count);
        Assert.Equal(1, requirements.Single(x => x.Slot == "snack").Count);
    }

    [Fact]
    public void FrozenSnapshotIntegrity_accepts_parseable_payload_even_when_hash_would_drift()
    {
        var slots = "[{\"Slot\":\"lunch\",\"Count\":1}]";
        var meals = "[{\"MealId\":\"2c4f893a-0179-4f11-9c0a-111111111111\",\"Slot\":\"lunch\"}]";
        var hash = FrozenSnapshotIntegrity.ComputeContentHash(slots, meals);
        Assert.True(FrozenSnapshotIntegrity.HasUsablePayload(slots, meals, hash));

        // jsonb-like whitespace drift must not invalidate a usable frozen payload.
        var driftedSlots = "[{\"Slot\": \"lunch\", \"Count\": 1}]";
        Assert.True(FrozenSnapshotIntegrity.HasUsablePayload(driftedSlots, meals, hash));
        Assert.False(FrozenSnapshotIntegrity.HasUsablePayload(driftedSlots, meals, null));
        Assert.False(FrozenSnapshotIntegrity.HasUsablePayload("{}", meals, hash));
    }

    [Fact]
    public void Schedule_adds_fixed_Friday_quota_without_replacing_regular_duration_days()
    {
        var schedule = new SubscriptionScheduleService(FixedClock());

        var withoutFriday = schedule.GetServiceDates(new DateOnly(2026, 8, 20), 26, false);
        var withFriday = schedule.GetServiceDates(new DateOnly(2026, 8, 20), 26, true);

        Assert.Equal(26, withoutFriday.Count);
        Assert.DoesNotContain(withoutFriday, x => x.DayOfWeek == DayOfWeek.Friday);
        Assert.Equal(4, schedule.GetFridayAddonDays(26));
        Assert.Equal(30, withFriday.Count);
        Assert.Equal(4, schedule.CountFridays(withFriday));
        Assert.Equal(26, withFriday.Count(x => x.DayOfWeek != DayOfWeek.Friday));
    }

    [Fact]
    public void ResolveFirstDeliveryDate_keeps_four_days_unless_baseline_is_excluded_Friday()
    {
        var schedule = new SubscriptionScheduleService(FixedClock());

        // 1. No Friday in 3-day window [chosen+1..chosen+3] -> adds 4 days (72h + 24h) for both
        // Sun 23 Aug 2026 -> window Mon 24, Tue 25, Wed 26 (no Friday) -> first box Thu 27 Aug (+4 days).
        Assert.Equal(new DateOnly(2026, 8, 27), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 23), includeFriday: false));
        Assert.Equal(new DateOnly(2026, 8, 27), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 23), includeFriday: true));

        // Sat 22 Aug 2026 -> window Sun 23, Mon 24, Tue 25 (no Friday) -> first box Wed 26 Aug (+4 days).
        Assert.Equal(new DateOnly(2026, 8, 26), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 22), includeFriday: false));
        Assert.Equal(new DateOnly(2026, 8, 26), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 22), includeFriday: true));

        // An intermediate Friday does not shorten the four-day baseline.
        Assert.Equal(new DateOnly(2026, 8, 31), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 27), includeFriday: false));
        Assert.Equal(new DateOnly(2026, 8, 30), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 26), includeFriday: false));
        // Tue Aug 25 +4 is Saturday and needs no exception.
        Assert.Equal(new DateOnly(2026, 8, 29), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 25), includeFriday: false));
        // Monday Aug 24 +4 is an excluded Friday: omit the choice day, keeping +3.
        Assert.Equal(new DateOnly(2026, 8, 27), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 24), includeFriday: false));

        // 3. Friday in 3-day window BUT includeFriday == true -> adds 4 days (72h + 24h)
        // Thu 27 Aug 2026 + 4 days -> Mon 31 Aug.
        Assert.Equal(new DateOnly(2026, 8, 31), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 27), includeFriday: true));
        // Wed 26 Aug 2026 + 4 days -> Sun 30 Aug.
        Assert.Equal(new DateOnly(2026, 8, 30), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 26), includeFriday: true));
        // Tue 25 Aug 2026 + 4 days -> Sat 29 Aug.
        Assert.Equal(new DateOnly(2026, 8, 29), schedule.ResolveFirstDeliveryDate(new DateOnly(2026, 8, 25), includeFriday: true));
    }

    [Fact]
    public void TryResolveCheckoutStart_rejects_past_chosen_day_and_returns_first_delivery()
    {
        // FixedClock = 2026-08-13 09:00 UTC → Kuwait 2026-08-13 12:00.
        var schedule = new SubscriptionScheduleService(FixedClock());

        Assert.False(schedule.TryResolveCheckoutStart(new DateOnly(2026, 8, 12), false, out _, out var pastError));
        Assert.Equal("checkout.start_date_invalid", pastError);

        Assert.False(schedule.TryResolveCheckoutStart(null, false, out _, out var missingError));
        Assert.Equal("checkout.start_date_required", missingError);

        Assert.False(schedule.TryResolveCheckoutStart(new DateOnly(2026, 8, 15), false, out _, out var tooSoonError));
        Assert.Equal("checkout.start_date_invalid", tooSoonError);

        Assert.False(schedule.TryResolveCheckoutStart(new DateOnly(2026, 8, 16), false, out _, out _));
        Assert.True(schedule.TryResolveCheckoutStart(new DateOnly(2026, 8, 17), false, out var firstDelivery, out var okError));
        Assert.Null(okError);
        Assert.Equal(new DateOnly(2026, 8, 17), firstDelivery);
    }

    [Fact]
    public async Task Fulfillment_requires_every_payment_confirmed_hold_and_frozen_day_snapshot()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedSubscription(db, days: 2);
        var restaurantId = Guid.NewGuid();
        SeedPool(db, subscription, restaurantId, snapshotDays: 1, holdDays: 2);
        await db.SaveChangesAsync();
        var service = CreateFulfillment(db, clock);

        var result = await service.TryFulfillAsync(subscription.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("fulfillment.hold_or_snapshot_mismatch", result.ErrorCode);
        Assert.Equal(SubscriptionStatus.Paid, subscription.Status);
        Assert.Equal(1, subscription.FulfillmentAttemptCount);
        Assert.Empty(await db.SubscriptionDays.Where(x => x.SubscriptionId == subscription.Id).ToListAsync());
        Assert.All(await db.CapacityHolds.Where(x => x.SubscriptionId == subscription.Id).ToListAsync(), x => Assert.Equal(CapacityHoldStatus.PaymentConfirmed, x.Status));
    }

    [Fact]
    public async Task Fulfillment_atomically_copies_pool_snapshots_and_confirms_capacity()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedSubscription(db, days: 2);
        var restaurantId = Guid.NewGuid();
        SeedPool(db, subscription, restaurantId, snapshotDays: 2, holdDays: 2);
        await db.SaveChangesAsync();
        var service = CreateFulfillment(db, clock);

        var result = await service.TryFulfillAsync(subscription.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Scheduled, subscription.Status);
        Assert.NotNull(subscription.FulfillmentCompletedAtUtc);
        Assert.Equal(2, await db.SubscriptionDays.CountAsync(x => x.SubscriptionId == subscription.Id));
        Assert.Equal(2, await db.SubscriptionDayMealSnapshots.CountAsync(x => x.SubscriptionId == subscription.Id));
        Assert.All(
            await db.SubscriptionDays.Where(x => x.SubscriptionId == subscription.Id).ToListAsync(),
            x => Assert.Null(x.RestaurantId));
        Assert.All(
            await db.SubscriptionDays.Where(x => x.SubscriptionId == subscription.Id).ToListAsync(),
            x => Assert.Null(x.ConfirmedAtUtc));
        Assert.All(await db.CapacityHolds.Where(x => x.SubscriptionId == subscription.Id).ToListAsync(), x => Assert.Equal(CapacityHoldStatus.Confirmed, x.Status));
    }

    [Fact]
    public async Task Payment_verification_rejects_amount_mismatch_without_confirming_holds()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedSubscription(db, days: 1);
        subscription.Status = SubscriptionStatus.PendingPayment;
        subscription.PaymentId = "PAY-WRONG";
        db.PaymentWebhookReceipts.RemoveRange(db.PaymentWebhookReceipts.Local);
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        var verifiedAt = clock.UtcNow;
        gateway.GetPaymentStatusAsync("PAY-WRONG", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("INV-1", "Paid", 24m, "Customer", "c@example.com", "50000000", "TX-1", "KNET", verifiedAt,
                subscription.Id.ToString(), "KWD", "PAY-WRONG"));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        var processor = new SubscriptionPaymentProcessor(db, gateway, new InlineTransactionExecutor(), new NoOpCapacityLock(), new SubscriptionScheduleService(clock), fulfillment, clock);

        var result = await processor.VerifyAndProcessAsync(subscription.Id, "PAY-WRONG");

        Assert.False(result.IsAccepted);
        Assert.Equal("payment.amount_mismatch", result.ErrorCode);
        Assert.Equal(SubscriptionStatus.PendingPayment, subscription.Status);
        await fulfillment.DidNotReceiveWithAnyArgs().TryFulfillAsync(default);
    }

    [Fact]
    public async Task Payment_verification_sends_welcome_push_even_when_fulfillment_fails()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var subscription = SeedSubscription(db, days: 1);
        subscription.Status = SubscriptionStatus.PendingPayment;
        subscription.PaymentId = "PAY-WELCOME";
        db.PaymentWebhookReceipts.RemoveRange(db.PaymentWebhookReceipts.Local);
        var restaurantId = Guid.NewGuid();
        var expiresAt = clock.UtcNow.AddMinutes(15);
        SeedPoolPendingPayment(db, subscription, restaurantId, expiresAt);
        await db.SaveChangesAsync();

        var gateway = Substitute.For<IPaymentGatewayService>();
        var verifiedAt = clock.UtcNow;
        gateway.GetPaymentStatusAsync("PAY-WELCOME", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("INV-W", "Paid", 25m, "Customer", "c@example.com", "50000000", "TX-W", "KNET",
                verifiedAt, subscription.Id.ToString(), "KWD", "PAY-WELCOME"));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        fulfillment.TryFulfillAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FulfillmentResult(false, false, "fulfillment.skipped_in_test"));
        var push = Substitute.For<ICustomerPushNotifier>();
        var processor = new SubscriptionPaymentProcessor(
            db, gateway, new InlineTransactionExecutor(), new NoOpCapacityLock(),
            new SubscriptionScheduleService(clock), fulfillment, clock, push);

        var result = await processor.VerifyAndProcessAsync(subscription.Id, "PAY-WELCOME");

        Assert.True(result.IsAccepted);
        Assert.Equal(SubscriptionStatus.Paid, subscription.Status);
        await push.Received(1).NotifySubscriptionPaymentConfirmedAsync(
            subscription.CustomerUserId, subscription.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Payment_verification_for_family_validates_quoted_final_price_not_member_line()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = Guid.NewGuid(),
            Name = "Family",
            Status = FamilyGroupStatus.CheckoutPending,
            QuotedFinalPrice = 270m,
            CurrencyCode = "KWD",
            DiscountPercentSnapshot = 0.10m,
        };
        var familyGroupId = group.Id;
        db.FamilyGroups.Add(group);

        var subscription = SeedSubscription(db, days: 1);
        subscription.Status = SubscriptionStatus.PendingPayment;
        subscription.PaymentId = null;
        subscription.PaidAmount = new Money(200m, "KWD");
        subscription.FamilyGroupId = familyGroupId;
        group.PaymentAnchorSubscriptionId = subscription.Id;
        db.PaymentWebhookReceipts.RemoveRange(db.PaymentWebhookReceipts.Local);
        await db.SaveChangesAsync();

        group.PaymentId = "PAY-FAMILY-WRONG";
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        var verifiedAt = clock.UtcNow;
        gateway.GetPaymentStatusAsync("PAY-FAMILY-WRONG", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("INV-F", "Paid", 200m, "Customer", "c@example.com", "50000000", "TX-F", "KNET", verifiedAt,
                familyGroupId.ToString(), "KWD", "PAY-FAMILY-WRONG"));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        var processor = new SubscriptionPaymentProcessor(db, gateway, new InlineTransactionExecutor(), new NoOpCapacityLock(), new SubscriptionScheduleService(clock), fulfillment, clock);

        var result = await processor.VerifyAndProcessAsync(familyGroupId, "PAY-FAMILY-WRONG");

        Assert.False(result.IsAccepted);
        Assert.Equal("payment.amount_mismatch", result.ErrorCode);
        Assert.Equal(SubscriptionStatus.PendingPayment, subscription.Status);
        await fulfillment.DidNotReceiveWithAnyArgs().TryFulfillAsync(default);
    }

    [Fact]
    public async Task Payment_verification_for_family_writes_one_receipt_when_later_sibling_hold_fails()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.CheckoutPending,
            QuotedFinalPrice = 270m,
            CurrencyCode = "KWD",
            DiscountPercentSnapshot = 0.10m,
        };
        db.FamilyGroups.Add(group);

        var anchor = SeedSubscription(db, days: 1);
        anchor.Status = SubscriptionStatus.PendingPayment;
        anchor.PaymentId = null;
        anchor.PaidAmount = new Money(200m, "KWD");
        anchor.FamilyGroupId = group.Id;
        anchor.CustomerUserId = managerId;
        group.PaymentAnchorSubscriptionId = anchor.Id;

        var sibling = SeedSubscription(db, days: 1);
        sibling.Status = SubscriptionStatus.PendingPayment;
        sibling.PaymentId = null;
        sibling.PaidAmount = new Money(100m, "KWD");
        sibling.FamilyGroupId = group.Id;
        sibling.CustomerUserId = managerId;
        sibling.StartDate = anchor.StartDate;
        sibling.EndDate = anchor.EndDate;

        db.PaymentWebhookReceipts.RemoveRange(db.PaymentWebhookReceipts.Local);

        var restaurantId = Guid.NewGuid();
        var expiresAtUtc = new DateTime(2026, 8, 13, 9, 15, 0, DateTimeKind.Utc);
        SeedPoolPendingPayment(db, anchor, restaurantId, expiresAtUtc);
        // Sibling intentionally has no restaurant pool / holds → MarkSubscriptionPaid fails after anchor receipt.
        await db.SaveChangesAsync();

        group.PaymentId = "PAY-FAMILY-SPLIT";
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("PAY-FAMILY-SPLIT", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("INV-F2", "Paid", 270m, "Customer", "c@example.com", "50000000", "TX-F2", "KNET",
                new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc),
                group.Id.ToString(), "KWD", "PAY-FAMILY-SPLIT"));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        fulfillment.TryFulfillAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FulfillmentResult(false, false, "fulfillment.skipped_in_test"));
        var processor = new SubscriptionPaymentProcessor(db, gateway, new InlineTransactionExecutor(), new NoOpCapacityLock(), new SubscriptionScheduleService(clock), fulfillment, clock);

        var result = await processor.VerifyAndProcessAsync(group.Id, "PAY-FAMILY-SPLIT");

        Assert.True(result.IsAccepted);
        Assert.Equal(SubscriptionStatus.Paid, anchor.Status);
        Assert.Equal(SubscriptionStatus.SuspendedForReview, sibling.Status);
        Assert.Equal(1, await db.PaymentWebhookReceipts.CountAsync(x => x.PaymentId == "PAY-FAMILY-SPLIT"));
        Assert.Equal(1, await db.PaymentWebhookReceipts.CountAsync(x => x.Gateway == "Payzah" && x.EventKey == "PAY-FAMILY-SPLIT"));
    }

    [Fact]
    public async Task Payment_verification_with_active_converted_manager_anchor_pays_pending_siblings()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Converted Family",
            Status = FamilyGroupStatus.CheckoutPending,
            QuotedFinalPrice = 90m,
            CurrencyCode = "KWD",
            DiscountPercentSnapshot = 0.10m,
        };
        db.FamilyGroups.Add(group);

        var manager = SeedSubscription(db, days: 26);
        manager.Status = SubscriptionStatus.Active;
        manager.PaymentId = "PAY-OLD";
        manager.PaidAmount = new Money(200m, "KWD");
        manager.FamilyGroupId = group.Id;
        manager.CustomerUserId = managerId;
        group.PaymentAnchorSubscriptionId = manager.Id;
        group.ConvertedFromSubscriptionId = manager.Id;

        var sibling = SeedSubscription(db, days: 1);
        sibling.Status = SubscriptionStatus.PendingPayment;
        sibling.PaymentId = null;
        sibling.PaidAmount = new Money(90m, "KWD");
        sibling.FamilyGroupId = group.Id;
        sibling.CustomerUserId = managerId;
        sibling.StartDate = manager.StartDate;
        sibling.EndDate = manager.StartDate;

        db.PaymentWebhookReceipts.RemoveRange(db.PaymentWebhookReceipts.Local);

        var restaurantId = Guid.NewGuid();
        var expiresAtUtc = new DateTime(2026, 8, 13, 9, 15, 0, DateTimeKind.Utc);
        SeedPoolPendingPayment(db, sibling, restaurantId, expiresAtUtc);
        await db.SaveChangesAsync();

        group.PaymentId = "PAY-CONV-ANCHOR";
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("PAY-CONV-ANCHOR", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("INV-C1", "Paid", 90m, "Customer", "c@example.com", "50000000", "TX-C1", "KNET",
                new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc),
                group.Id.ToString(), "KWD", "PAY-CONV-ANCHOR"));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        fulfillment.TryFulfillAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FulfillmentResult(false, false, "fulfillment.skipped_in_test"));
        var processor = new SubscriptionPaymentProcessor(
            db, gateway, new InlineTransactionExecutor(), new NoOpCapacityLock(),
            new SubscriptionScheduleService(clock), fulfillment, clock);

        var result = await processor.VerifyAndProcessAsync(group.Id, "PAY-CONV-ANCHOR");

        Assert.True(result.IsAccepted);
        Assert.Equal(SubscriptionStatus.Active, manager.Status);
        Assert.Equal(SubscriptionStatus.Paid, sibling.Status);
        Assert.Equal(FamilyGroupStatus.Active, (await db.FamilyGroups.SingleAsync(x => x.Id == group.Id)).Status);
        Assert.Equal("PAY-OLD", manager.PaymentId);
    }

    [Fact]
    public async Task Payment_verification_for_family_assigns_gateway_payment_id_to_anchor_only()
    {
        await using var db = CreateDb();
        var clock = FixedClock();
        var managerId = Guid.NewGuid();
        var memberUserId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.CheckoutPending,
            QuotedFinalPrice = 133.875m,
            CurrencyCode = "KWD",
            DiscountPercentSnapshot = 0.10m,
        };
        var managerMember = new FamilyMember
        {
            FamilyGroupId = group.Id,
            DisplayName = "Manager",
            RelationshipCode = "self",
            NutritionalGoalCode = "maintain",
            Role = FamilyMemberRole.Manager,
        };
        var otherMember = new FamilyMember
        {
            FamilyGroupId = group.Id,
            DisplayName = "Member",
            RelationshipCode = "child",
            NutritionalGoalCode = "maintain",
        };
        group.Members.Add(managerMember);
        group.Members.Add(otherMember);
        db.FamilyGroups.Add(group);

        var anchor = SeedSubscription(db, days: 1);
        anchor.Status = SubscriptionStatus.PendingPayment;
        anchor.PaymentId = null;
        anchor.PaidAmount = new Money(30m, "KWD");
        anchor.FamilyGroupId = group.Id;
        anchor.FamilyMemberId = managerMember.Id;
        anchor.CustomerUserId = managerId;
        group.PaymentAnchorSubscriptionId = anchor.Id;

        var sibling = SeedSubscription(db, days: 1);
        sibling.Status = SubscriptionStatus.PendingPayment;
        sibling.PaymentId = null;
        sibling.PaidAmount = new Money(118.750m, "KWD");
        sibling.FamilyGroupId = group.Id;
        sibling.FamilyMemberId = otherMember.Id;
        sibling.CustomerUserId = memberUserId;
        sibling.StartDate = anchor.StartDate;
        sibling.EndDate = anchor.EndDate;

        db.PaymentWebhookReceipts.RemoveRange(db.PaymentWebhookReceipts.Local);

        var restaurantId = Guid.NewGuid();
        var expiresAtUtc = new DateTime(2026, 8, 13, 9, 15, 0, DateTimeKind.Utc);
        SeedPoolPendingPayment(db, anchor, restaurantId, expiresAtUtc);
        SeedPoolPendingPayment(db, sibling, restaurantId, expiresAtUtc);
        await db.SaveChangesAsync();

        group.PaymentId = "PAY-FAMILY-UNIQ";
        await db.SaveChangesAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync("PAY-FAMILY-UNIQ", "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse("INV-UNIQ", "Paid", 133.875m, "Customer", "c@example.com", "50000000", "TX-U", "VISA/MASTER",
                new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc),
                group.Id.ToString(), "KWD", "PAY-FAMILY-UNIQ"));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        fulfillment.TryFulfillAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FulfillmentResult(false, false, "fulfillment.skipped_in_test"));
        var processor = new SubscriptionPaymentProcessor(db, gateway, new InlineTransactionExecutor(), new NoOpCapacityLock(), new SubscriptionScheduleService(clock), fulfillment, clock);

        var result = await processor.VerifyAndProcessAsync(anchor.Id, "PAY-FAMILY-UNIQ");

        Assert.True(result.IsAccepted);
        Assert.Equal(SubscriptionStatus.Paid, anchor.Status);
        Assert.Equal(SubscriptionStatus.Paid, sibling.Status);
        Assert.Equal(FamilyGroupStatus.Active, (await db.FamilyGroups.SingleAsync(x => x.Id == group.Id)).Status);
        Assert.Equal("PAY-FAMILY-UNIQ", (await db.FamilyGroups.SingleAsync(x => x.Id == group.Id)).PaymentId);
        Assert.Equal(1, await db.Subscriptions.CountAsync(x => x.FamilyGroupId == group.Id && x.PaymentId == "PAY-FAMILY-UNIQ"));
        Assert.Equal("PAY-FAMILY-UNIQ", anchor.PaymentId);
        Assert.Null(sibling.PaymentId);
        Assert.Equal(1, await db.PaymentWebhookReceipts.CountAsync(x => x.PaymentId == "PAY-FAMILY-UNIQ"));
    }

    private static SubscriptionFulfillmentService CreateFulfillment(AppDbContext db, IDateTimeProvider clock)
        => new(db, new InlineTransactionExecutor(), new NoOpCapacityLock(), new SubscriptionScheduleService(clock), clock);

    private static Subscription SeedSubscription(AppDbContext db, int days)
    {
        var subscription = new Subscription
        {
            CustomerUserId = Guid.NewGuid(),
            ProgramId = Guid.NewGuid(),
            BundleId = Guid.NewGuid(),
            SelectedTier = RestaurantTier.Basic,
            DurationDays = days,
            IncludeFriday = false,
            StartDate = new DateOnly(2026, 8, 22),
            EndDate = new DateOnly(2026, 8, 22).AddDays(days - 1),
            Status = SubscriptionStatus.Paid,
            PaidAmount = new Money(25m, "KWD"),
            PaymentId = "PAY-TEST"
        };
        db.Subscriptions.Add(subscription);
        db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
        {
            Gateway = "Payzah", EventKey = "PAY-TEST", PaymentId = "PAY-TEST", SubscriptionId = subscription.Id,
            Status = "PaymentVerified", ProcessedAtUtc = DateTime.UtcNow
        });
        return subscription;
    }

    private static void SeedPool(AppDbContext db, Subscription subscription, Guid restaurantId, int snapshotDays, int holdDays)
    {
        var pool = new SubscriptionRestaurantPoolSnapshot { SubscriptionId = subscription.Id, RestaurantCount = 1, EligibilityCheckedAtUtc = DateTime.UtcNow };
        var restaurant = new SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = pool.Id, SubscriptionId = subscription.Id, RestaurantId = restaurantId,
            ProgramId = subscription.ProgramId, BundleId = subscription.BundleId, RegionId = Guid.NewGuid(), Tier = subscription.SelectedTier,
            NameAr = "مطعم", NameEn = "Restaurant", IsInitiallySelected = true, OfferingSnapshotJson = "{}", CapturedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionRestaurantPoolSnapshots.Add(pool);
        db.SubscriptionRestaurantSnapshots.Add(restaurant);
        const string slots = "[{\"slot\":\"lunch\",\"count\":1}]";
        const string meals = "[{\"mealId\":\"00000000-0000-0000-0000-000000000001\",\"nameAr\":\"وجبة\",\"nameEn\":\"Meal\",\"slot\":\"lunch\",\"sortOrder\":0,\"presentAllergenCodes\":[],\"mayContainAllergenCodes\":[],\"dietaryPreferenceCodes\":[],\"ingredients\":[]}]";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slots + "\n" + meals))).ToLowerInvariant();
        for (var index = 0; index < snapshotDays; index++)
        {
            var date = subscription.StartDate.AddDays(index);
            db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = restaurant.Id, SubscriptionId = subscription.Id, RestaurantId = restaurantId,
                ServiceDate = date, MenuPlanVersionId = Guid.NewGuid(), SlotRequirementsJson = slots, MealsJson = meals,
                ContentHash = hash, CapturedAtUtc = DateTime.UtcNow
            });
        }
        for (var index = 0; index < holdDays; index++)
        {
            var date = subscription.StartDate.AddDays(index);
            var version = db.SubscriptionRestaurantDaySnapshots.Local.SingleOrDefault(x => x.SubscriptionId == subscription.Id && x.ServiceDate == date)?.MenuPlanVersionId ?? Guid.NewGuid();
            db.CapacityHolds.Add(new CapacityHold
            {
                SubscriptionId = subscription.Id, RestaurantId = restaurantId, ServiceDate = date, MenuPlanVersionId = version,
                Quantity = 1, Status = CapacityHoldStatus.PaymentConfirmed, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
            });
        }
    }

    private static void SeedPoolPendingPayment(AppDbContext db, Subscription subscription, Guid restaurantId, DateTime expiresAtUtc)
    {
        var pool = new SubscriptionRestaurantPoolSnapshot { SubscriptionId = subscription.Id, RestaurantCount = 1, EligibilityCheckedAtUtc = DateTime.UtcNow };
        var restaurant = new SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = pool.Id, SubscriptionId = subscription.Id, RestaurantId = restaurantId,
            ProgramId = subscription.ProgramId, BundleId = subscription.BundleId, RegionId = Guid.NewGuid(), Tier = subscription.SelectedTier,
            NameAr = "مطعم", NameEn = "Restaurant", IsInitiallySelected = true, OfferingSnapshotJson = "{}", CapturedAtUtc = DateTime.UtcNow
        };
        db.SubscriptionRestaurantPoolSnapshots.Add(pool);
        db.SubscriptionRestaurantSnapshots.Add(restaurant);
        const string slots = "[{\"slot\":\"lunch\",\"count\":1}]";
        const string meals = "[{\"mealId\":\"00000000-0000-0000-0000-000000000001\",\"nameAr\":\"وجبة\",\"nameEn\":\"Meal\",\"slot\":\"lunch\",\"sortOrder\":0,\"presentAllergenCodes\":[],\"mayContainAllergenCodes\":[],\"dietaryPreferenceCodes\":[],\"ingredients\":[]}]";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slots + "\n" + meals))).ToLowerInvariant();
        for (var index = 0; index < subscription.DurationDays; index++)
        {
            var date = subscription.StartDate.AddDays(index);
            var versionId = Guid.NewGuid();
            db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot
            {
                RestaurantSnapshotId = restaurant.Id, SubscriptionId = subscription.Id, RestaurantId = restaurantId,
                ServiceDate = date, MenuPlanVersionId = versionId, SlotRequirementsJson = slots, MealsJson = meals,
                ContentHash = hash, CapturedAtUtc = DateTime.UtcNow
            });
            db.CapacityHolds.Add(new CapacityHold
            {
                SubscriptionId = subscription.Id, RestaurantId = restaurantId, ServiceDate = date, MenuPlanVersionId = versionId,
                Quantity = 1, Status = CapacityHoldStatus.PendingPayment, ExpiresAtUtc = expiresAtUtc
            });
        }
    }

    private static IDateTimeProvider FixedClock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc));
        return clock;
    }

    private static AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());

    private sealed class InlineTransactionExecutor : ICheckoutTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private sealed class NoOpCapacityLock : ICapacityConcurrencyLock
    {
        public Task AcquirePaymentAsync(string paymentId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AcquireAsync(Guid restaurantId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
