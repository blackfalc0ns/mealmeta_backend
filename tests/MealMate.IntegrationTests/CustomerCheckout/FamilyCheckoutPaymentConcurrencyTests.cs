using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Application.Features.ProgramsSubscriptions.CustomerSubscriptions;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Persistence;
using MealMate.Persistence.CustomerCheckout;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit.Sdk;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace MealMate.IntegrationTests.CustomerCheckout;

/// <summary>
/// Real-PostgreSQL proofs that family payment verification is batch-scoped:
/// a late paid callback for a dead batch never rolls onto a newer pending
/// addon, concurrent callbacks of one batch payment stay idempotent, and the
/// paid transition stamps exactly one batch. In-memory tests cannot prove this.
/// </summary>
public sealed class FamilyCheckoutPaymentConcurrencyTests : IAsyncLifetime
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
        catch (Exception ex)
        {
            throw new XunitException("Docker is required for family checkout payment concurrency tests.", ex);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Late_paid_callback_for_expired_batch_goes_to_review_not_to_newer_batch()
    {
        var group = await SeedGroupAsync();
        var oldSubscription = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        var oldBatch = await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.Expired, "PAY-OLD", oldSubscription);
        var newSubscription = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-NEW", newSubscription);

        var result = await ProcessAsync(oldSubscription, group.GroupId, "PAY-OLD", 180m);

        // Accepted-for-review: the gateway may hold the money, so it is never
        // auto-rejected — and it is never rolled onto the newer pending batch.
        Assert.True(result.IsAccepted);
        Assert.Equal("ReviewRequired", result.FulfillmentStatus);
        Assert.Equal("family.batch_not_payable", result.ErrorCode);
        await using var verify = CreateDb();
        Assert.Equal(SubscriptionStatus.PendingPayment,
            await verify.Subscriptions.Where(x => x.Id == newSubscription).Select(x => x.Status).SingleAsync());
        Assert.Equal(FamilyCheckoutBatchStatus.PaymentPending,
            await verify.FamilyCheckoutBatches.Where(x => x.FamilyGroupId == group.GroupId
                    && x.Status == FamilyCheckoutBatchStatus.PaymentPending)
                .Select(x => x.Status).SingleAsync());
        var receipt = await verify.PaymentWebhookReceipts.SingleAsync(x => x.PaymentId == "PAY-OLD");
        Assert.Equal("FinancialReviewRequired", receipt.Status);
    }

    [Fact]
    public async Task Concurrent_callbacks_for_one_batch_payment_confirm_exactly_once()
    {
        var group = await SeedGroupAsync();
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        var sibling = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-BATCH-1", anchor, sibling);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<PaymentProcessingResult> Run()
        {
            await start.Task;
            return await ProcessAsync(anchor, group.GroupId, "PAY-BATCH-1", 180m);
        }

        var first = Run();
        var second = Run();
        start.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.All(results, x => Assert.True(x.IsAccepted));
        Assert.Single(results, x => x.IsDuplicate);
        await using var verify = CreateDb();
        Assert.Equal(1, await verify.PaymentWebhookReceipts.CountAsync(x => x.PaymentId == "PAY-BATCH-1"));
        Assert.All(
            await verify.Subscriptions.Where(x => x.Id == anchor || x.Id == sibling).ToListAsync(),
            x => Assert.Equal(SubscriptionStatus.Paid, x.Status));
        var batch = await verify.FamilyCheckoutBatches.SingleAsync(x => x.FamilyGroupId == group.GroupId);
        Assert.Equal(FamilyCheckoutBatchStatus.Paid, batch.Status);
        Assert.NotNull(batch.PaidAtUtc);
    }

    [Fact]
    public async Task Paid_renewal_callback_completes_exact_request_and_keeps_old_service_funded()
    {
        var seed = await SeedRenewalBatchAsync(previousEndDate: new DateOnly(2026, 8, 21));
        await using (var diagnostic = CreateDb())
        {
            var resolved = await FamilyBatchPaymentResolver.ResolveAsync(diagnostic, seed.GroupId, seed.PaymentId, default);
            Assert.NotNull(resolved);
            Assert.Equal("Renewal", resolved.Purpose);
            Assert.Contains(seed.NewSubscriptionId, resolved.SubscriptionIds);
        }
        var result = await ProcessAsync(seed.NewSubscriptionId, seed.GroupId, seed.PaymentId, 90m);

        Assert.True(result.IsAccepted, result.ErrorCode);
        Assert.Equal("PendingRetry", result.FulfillmentStatus);
        await using var verify = CreateDb();
        var renewal = await verify.SubscriptionRenewals.SingleAsync(x => x.Id == seed.RenewalId);
        Assert.Equal("Completed", renewal.Status);
        Assert.Equal(seed.NewSubscriptionId, renewal.NewSubscriptionId);
        Assert.NotNull(renewal.CompletedAtUtc);
        Assert.Equal(SubscriptionStatus.Active,
            await verify.Subscriptions.Where(x => x.Id == seed.PreviousSubscriptionId).Select(x => x.Status).SingleAsync());
        Assert.Equal(SubscriptionStatus.Paid,
            await verify.Subscriptions.Where(x => x.Id == seed.NewSubscriptionId).Select(x => x.Status).SingleAsync());
        Assert.Equal(seed.NewSubscriptionId,
            await verify.FamilyMembers.Where(x => x.Id == seed.MemberId).Select(x => x.MemberSubscriptionId).SingleAsync());
    }

    [Fact]
    public async Task Paid_renewal_callback_goes_to_review_if_old_freeze_extension_overlaps_new_start()
    {
        var seed = await SeedRenewalBatchAsync(previousEndDate: new DateOnly(2026, 8, 22));
        var result = await ProcessAsync(seed.NewSubscriptionId, seed.GroupId, seed.PaymentId, 90m);

        Assert.True(result.IsAccepted);
        Assert.Equal("ReviewRequired", result.FulfillmentStatus);
        await using var verify = CreateDb();
        var renewal = await verify.SubscriptionRenewals.SingleAsync(x => x.Id == seed.RenewalId);
        Assert.Equal("ReviewRequired", renewal.Status);
        Assert.Null(renewal.NewSubscriptionId);
        Assert.Equal(SubscriptionStatus.PendingPayment,
            await verify.Subscriptions.Where(x => x.Id == seed.NewSubscriptionId).Select(x => x.Status).SingleAsync());
        Assert.Equal(SubscriptionStatus.Active,
            await verify.Subscriptions.Where(x => x.Id == seed.PreviousSubscriptionId).Select(x => x.Status).SingleAsync());
        Assert.Equal(seed.PreviousSubscriptionId,
            await verify.FamilyMembers.Where(x => x.Id == seed.MemberId).Select(x => x.MemberSubscriptionId).SingleAsync());
    }

    [Fact]
    public async Task Paid_renewal_callback_accepts_paid_completed_previous_service()
    {
        var seed = await SeedRenewalBatchAsync(new DateOnly(2026, 8, 21), SubscriptionStatus.Completed);
        var result = await ProcessAsync(seed.NewSubscriptionId, seed.GroupId, seed.PaymentId, 90m);
        Assert.True(result.IsAccepted, result.ErrorCode);
        await using var verify = CreateDb();
        Assert.Equal("Completed", await verify.SubscriptionRenewals.Where(x => x.Id == seed.RenewalId)
            .Select(x => x.Status).SingleAsync());
        Assert.Equal(SubscriptionStatus.Completed,
            await verify.Subscriptions.Where(x => x.Id == seed.PreviousSubscriptionId).Select(x => x.Status).SingleAsync());
        Assert.Equal(SubscriptionStatus.Paid,
            await verify.Subscriptions.Where(x => x.Id == seed.NewSubscriptionId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Same_key_paid_renewal_replay_returns_recorded_invoice_after_member_pointer_advances()
    {
        var seed = await SeedRenewalBatchAsync(new DateOnly(2026, 8, 21));
        var callbackUrl = "https://localhost/callback";
        var errorUrl = "https://localhost/error";
        await ProcessAsync(seed.NewSubscriptionId, seed.GroupId, seed.PaymentId, 90m);

        DateOnly startDate;
        Guid addressId;
        Guid batchId;
        long batchVersion;
        string paidUrl;
        Guid managerId;
        await using (var arrange = CreateDb())
        {
            var line = await arrange.FamilyCheckoutBatchLines.SingleAsync(x => x.RenewalRequestId == seed.RenewalId);
            startDate = line.StartDate!.Value;
            addressId = line.DeliveryAddressId!.Value;
            var renewal = await arrange.SubscriptionRenewals.SingleAsync(x => x.Id == seed.RenewalId);
            var batch = await arrange.FamilyCheckoutBatches.SingleAsync(x => x.Id == renewal.BatchId);
            var attempt = await arrange.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.BatchId == batch.Id);
            paidUrl = attempt.GatewayPaymentUrl!;
            batchId = batch.Id;
            batchVersion = batch.Version;
            managerId = await arrange.FamilyGroups.Where(x => x.Id == seed.GroupId).Select(x => x.ManagerCustomerUserId).SingleAsync();
            var fingerprintPayload = string.Join("\n", managerId.ToString("D", CultureInfo.InvariantCulture),
                startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), addressId.ToString("D", CultureInfo.InvariantCulture),
                callbackUrl, errorUrl, 3.ToString(CultureInfo.InvariantCulture),
                string.Empty, string.Empty);
            renewal.CheckoutFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintPayload)));
            await arrange.SaveChangesAsync();
        }

        await using var db = CreateDb();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc));
        var payments = Substitute.For<IPaymentGatewayService>();
        var service = new MealMate.Application.Features.FamilyCheckout.FamilyCheckoutService(
            db, currentUser, Substitute.For<IUserAccountService>(),
            Substitute.For<MealMate.Application.Features.FamilyCheckout.IFamilyQuoteService>(),
            new SubscriptionScheduleService(clock), Substitute.For<IRestaurantEligibilityService>(),
            new PostgresCapacityConcurrencyLock(db), new PostgresCheckoutTransactionExecutor(db),
            Substitute.For<IRestaurantPoolSnapshotService>(), payments, Substitute.For<ISender>(), clock,
            Substitute.For<IRestaurantCapacityReader>(), new MealMate.Persistence.Common.PostgresApplicationLockManager(db));

        var replay = await service.StartRenewalAsync(seed.RenewalId,
            new FamilyRenewalCheckoutRequest(startDate, addressId, callbackUrl, errorUrl));

        Assert.True(replay.IsSuccess);
        Assert.Equal(paidUrl, replay.Value.PaymentUrl);
        Assert.Equal(batchId, replay.Value.BatchId);
        Assert.Equal(batchVersion, replay.Value.BatchVersion);
        Assert.Empty(payments.ReceivedCalls());
    }

    [Fact]
    public async Task Paid_renewal_callback_after_old_service_cancelled_goes_to_review()
    {
        var seed = await SeedRenewalBatchAsync(new DateOnly(2026, 8, 21));
        await using (var db = CreateDb())
        {
            var previous = await db.Subscriptions.SingleAsync(x => x.Id == seed.PreviousSubscriptionId);
            previous.Status = SubscriptionStatus.Cancelled;
            await db.SaveChangesAsync();
        }
        var result = await ProcessAsync(seed.NewSubscriptionId, seed.GroupId, seed.PaymentId, 90m);
        Assert.True(result.IsAccepted);
        Assert.Equal("ReviewRequired", result.FulfillmentStatus);
        await using var verify = CreateDb();
        Assert.Equal("ReviewRequired", await verify.SubscriptionRenewals.Where(x => x.Id == seed.RenewalId)
            .Select(x => x.Status).SingleAsync());
        Assert.Equal(FamilyCheckoutBatchStatus.ReviewRequired,
            await verify.FamilyCheckoutBatches.Where(x => x.RenewalRequestId == seed.RenewalId).Select(x => x.Status).SingleAsync());
        Assert.Equal(SubscriptionStatus.PendingPayment,
            await verify.Subscriptions.Where(x => x.Id == seed.NewSubscriptionId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Unknown_payment_for_strict_batch_group_goes_to_review_and_touches_nothing()
    {
        var group = await SeedGroupAsync();
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-KNOWN", anchor);

        var result = await ProcessAsync(anchor, group.GroupId, "PAY-FOREIGN", 180m);

        // Accepted-for-review: an unresolvable family payment gets an independent
        // reconciliation receipt; the anchor keeps its state and metadata and the
        // live batch is untouched.
        Assert.True(result.IsAccepted);
        Assert.Equal("ReviewRequired", result.FulfillmentStatus);
        Assert.Equal("family.batch_unresolved", result.ErrorCode);
        await using var verify = CreateDb();
        var anchorRow = await verify.Subscriptions.SingleAsync(x => x.Id == anchor);
        Assert.Equal(SubscriptionStatus.PendingPayment, anchorRow.Status);
        Assert.NotEqual("PAY-FOREIGN", anchorRow.PaymentId);
        Assert.Equal(1, await verify.PaymentWebhookReceipts.CountAsync(
            x => x.PaymentId == "PAY-FOREIGN" && x.Status == "FinancialReviewRequired"));
        Assert.Equal(FamilyCheckoutBatchStatus.PaymentPending,
            await verify.FamilyCheckoutBatches.Where(x => x.FamilyGroupId == group.GroupId)
                .Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Failure_callback_for_old_batch_never_fails_newer_batch()
    {
        // B04: a stale failure webhook ends only its own batch's subscriptions.
        var group = await SeedGroupAsync();
        var oldSub = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        // The old batch is already Expired; only its attempt ledger still maps the link.
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.Expired, "PAY-OLD-FAIL", oldSub);
        // Newer pending batch with its own subscription.
        var newSub = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-NEW-LINK", newSub);

        var result = await ProcessFailureAsync(oldSub, group.GroupId, "PAY-OLD-FAIL");

        Assert.True(result.IsAccepted);
        await using var verify = CreateDb();
        Assert.Equal(SubscriptionStatus.PaymentFailed,
            await verify.Subscriptions.Where(x => x.Id == oldSub).Select(x => x.Status).SingleAsync());
        // The newer batch's subscription was untouched by the old failure.
        Assert.Equal(SubscriptionStatus.PendingPayment,
            await verify.Subscriptions.Where(x => x.Id == newSub).Select(x => x.Status).SingleAsync());
        Assert.Equal(FamilyCheckoutBatchStatus.PaymentPending,
            await verify.FamilyCheckoutBatches.Where(x => x.FamilyGroupId == group.GroupId
                    && x.GatewayPaymentId == "PAY-NEW-LINK")
                .Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Paid_callback_with_batch_amount_mismatch_goes_to_review()
    {
        // B07: the batch is the financial authority — a wrong amount is a human
        // review case, never a silent acceptance against a newer quote.
        var group = await SeedGroupAsync();
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-BATCH-AMT", anchor);

        var result = await ProcessAsync(anchor, group.GroupId, "PAY-BATCH-AMT", 999m);

        Assert.True(result.IsAccepted);
        Assert.Equal("ReviewRequired", result.FulfillmentStatus);
        Assert.Equal("family.batch_amount_mismatch", result.ErrorCode);
        await using var verify = CreateDb();
        // R02: the review lives on the batch; subscriptions are not suspended.
        Assert.Equal(SubscriptionStatus.PendingPayment,
            await verify.Subscriptions.Where(x => x.Id == anchor).Select(x => x.Status).SingleAsync());
        Assert.Equal(FamilyCheckoutBatchStatus.ReviewRequired,
            await verify.FamilyCheckoutBatches.Where(x => x.FamilyGroupId == group.GroupId)
                .Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Gateway_status_other_than_paid_never_accepts_batch_payment()
    {
        // R01: the batch path must not bypass the general gateway verification —
        // a Pending/Failed gateway result with a perfect amount/currency/reference
        // is NOT money received.
        foreach (var status in new[] { "Pending", "Failed", "Cancelled" })
        {
            var group = await SeedGroupAsync();
            var anchor = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
            await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-" + status, anchor);

            var result = await ProcessWithGatewayStatusAsync(anchor, group.GroupId, "PAY-" + status, 180m, status, "PAY-" + status);

            Assert.False(result.IsAccepted, $"status {status} must not be accepted");
            await using var verify = CreateDb();
            Assert.Equal(SubscriptionStatus.PendingPayment,
                await verify.Subscriptions.Where(x => x.Id == anchor).Select(x => x.Status).SingleAsync());
            Assert.Equal(FamilyCheckoutBatchStatus.PaymentPending,
                await verify.FamilyCheckoutBatches.Where(x => x.FamilyGroupId == group.GroupId)
                    .Select(x => x.Status).SingleAsync());
            Assert.Equal(0, await verify.PaymentWebhookReceipts
                .CountAsync(x => x.PaymentId == "PAY-" + status && x.Status == "PaymentVerified"));
        }
    }

    [Fact]
    public async Task Verified_payment_id_mismatch_is_rejected_on_batch_path()
    {
        // R01: the response's own payment id must match the callback's payment id.
        var group = await SeedGroupAsync();
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-MISMATCH-ID", anchor);

        var result = await ProcessWithGatewayStatusAsync(anchor, group.GroupId, "PAY-MISMATCH-ID", 180m, "Paid", "PAY-DIFFERENT");

        Assert.False(result.IsAccepted);
        Assert.Equal("payment.payment_id_mismatch", result.ErrorCode);
        await using var verify = CreateDb();
        Assert.Equal(SubscriptionStatus.PendingPayment,
            await verify.Subscriptions.Where(x => x.Id == anchor).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Late_group_callback_for_expired_batch_never_suspends_new_anchor()
    {
        // R02: family payment links carry the groupId. A late callback for a dead
        // batch arrives with the groupId, resolves to the NEWEST anchor — the fix
        // must record the review on the historical batch, not suspend that anchor.
        var group = await SeedGroupAsync();
        var oldSub = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.Expired, "PAY-OLD-GROUP", oldSub);
        var newSub = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        var newBatchId = await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-NEW-GROUP", newSub);
        // The newest anchor is what the group points at (what groupId resolves to).
        await using (var db = CreateDb())
        {
            var g = await db.FamilyGroups.SingleAsync(x => x.Id == group.GroupId);
            g.PaymentAnchorSubscriptionId = newSub;
            await db.SaveChangesAsync();
        }

        // The callback passes the GROUP id (as real family payment links do).
        var result = await ProcessWithGatewayStatusAsync(group.GroupId, group.GroupId, "PAY-OLD-GROUP", 180m, "Paid", "PAY-OLD-GROUP");

        Assert.True(result.IsAccepted);
        Assert.Equal("ReviewRequired", result.FulfillmentStatus);
        await using var verify = CreateDb();
        // Neither the new anchor nor the old subscription may be suspended.
        Assert.Equal(SubscriptionStatus.PendingPayment,
            await verify.Subscriptions.Where(x => x.Id == newSub).Select(x => x.Status).SingleAsync());
        Assert.Equal(SubscriptionStatus.PendingPayment,
            await verify.Subscriptions.Where(x => x.Id == oldSub).Select(x => x.Status).SingleAsync());
        // The historical batch carries the review.
        Assert.Equal(1, await verify.FamilyCheckoutBatches.CountAsync(
            x => x.FamilyGroupId == group.GroupId && x.Status == FamilyCheckoutBatchStatus.ReviewRequired));
        // The live batch is untouched.
        Assert.Equal(FamilyCheckoutBatchStatus.PaymentPending,
            await verify.FamilyCheckoutBatches.Where(x => x.Id == newBatchId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Unresolved_group_callback_records_review_without_touching_anchor()
    {
        // R02: an unmappable payment with our group reference gets a review record;
        // the newest anchor subscription keeps its state and metadata.
        var group = await SeedGroupAsync();
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-KNOWN-LINK", anchor);

        var result = await ProcessWithGatewayStatusAsync(anchor, group.GroupId, "PAY-FOREIGN-GROUP", 180m, "Paid", "PAY-FOREIGN-GROUP");

        Assert.True(result.IsAccepted);
        Assert.Equal("ReviewRequired", result.FulfillmentStatus);
        await using var verify = CreateDb();
        var anchorRow = await verify.Subscriptions.SingleAsync(x => x.Id == anchor);
        Assert.Equal(SubscriptionStatus.PendingPayment, anchorRow.Status);
        Assert.NotEqual("PAY-FOREIGN-GROUP", anchorRow.PaymentId);
        Assert.Equal(1, await verify.PaymentWebhookReceipts.CountAsync(
            x => x.PaymentId == "PAY-FOREIGN-GROUP" && x.Status == "FinancialReviewRequired"));
    }

    [Fact]
    public async Task First_payment_failure_releases_group_for_reselection()
    {
        // R03: after the FIRST payment fails, the group must return to a state
        // where the manager can select and buy again (not stuck CheckoutPending).
        var group = await SeedGroupAsync();
        var managerId = group.ManagerId;
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, managerId);
        await SeedBatchAsync(group.GroupId, managerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-FIRST-FAIL", anchor);
        await using (var db = CreateDb())
        {
            var g = await db.FamilyGroups.SingleAsync(x => x.Id == group.GroupId);
            g.PaymentAnchorSubscriptionId = anchor;
            var member = new FamilyMember
            {
                FamilyGroupId = group.GroupId,
                Role = FamilyMemberRole.Member,
                DisplayName = "Member",
                RelationshipCode = "child",
                NutritionalGoalCode = "maintain",
            };
            db.FamilyMembers.Add(member);
            await db.SaveChangesAsync();
            db.FamilyMemberAccesses.Add(new FamilyMemberAccess
            {
                FamilyMemberId = member.Id,
                HasSeparateAccount = true,
                LinkedCustomerUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var result = await ProcessFailureAsync(anchor, group.GroupId, "PAY-FIRST-FAIL");

        Assert.True(result.IsAccepted);
        await using (var verify = CreateDb())
        {
            Assert.Equal(FamilyGroupStatus.Draft,
                await verify.FamilyGroups.Where(x => x.Id == group.GroupId).Select(x => x.Status).SingleAsync());
            Assert.Equal(FamilyCheckoutBatchStatus.Failed,
                await verify.FamilyCheckoutBatches.Where(x => x.FamilyGroupId == group.GroupId).Select(x => x.Status).SingleAsync());
        }

        // The manager can select again for a fresh purchase cycle.
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var selectionDb = CreateDb();
        var selection = new MealMate.Application.Features.FamilyCheckout.FamilyCheckoutSelectionService(
            selectionDb, currentUser, clock,
            new PostgresCheckoutTransactionExecutor(selectionDb),
            new MealMate.Persistence.Common.PostgresApplicationLockManager(selectionDb));
        var memberToSelect = (await CreateDb().FamilyMembers.AsNoTracking()
            .FirstAsync(x => x.FamilyGroupId == group.GroupId)).Id;
        var reselect = await selection.SetAsync(
            new MealMate.Application.Features.FamilyCheckout.SetFamilyCheckoutSelectionCommand(memberToSelect, SubscribeNow: true),
            CancellationToken.None);
        Assert.True(reselect.IsSuccess, reselect.IsFailure ? reselect.Error!.Code : "ok");
    }

    private async Task<PaymentProcessingResult> ProcessAsync(Guid subscriptionId, Guid familyGroupId, string paymentId, decimal amount)
    {
        await using var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        var now = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);
        clock.UtcNow.Returns(now);
        var invoiceId = "INV-" + paymentId;
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync(paymentId, "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse(invoiceId, "Paid", amount, "Customer", "c@example.com", "50000000",
                "TX-" + paymentId, "KNET", now, familyGroupId.ToString(), "KWD", paymentId));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        fulfillment.TryFulfillAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FulfillmentResult(false, false, "fulfillment.skipped_in_test"));
        var processor = new SubscriptionPaymentProcessor(db, gateway, new PostgresCheckoutTransactionExecutor(db),
            new PostgresCapacityConcurrencyLock(db), new SubscriptionScheduleService(clock), fulfillment, clock, locks: new MealMate.Persistence.Common.PostgresApplicationLockManager(db));
        return await processor.VerifyAndProcessAsync(subscriptionId, paymentId);
    }

    private async Task<(Guid ProgramId, Guid BundleId)> SeedCatalogAsync()
    {
        await using var db = CreateDb();
        var program = new MealMate.Domain.ProgramsSubscriptions.Program
        {
            Code = "P-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "برنامج",
            NameEn = "Program",
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        var bundle = new MealMate.Domain.ProgramsSubscriptions.Bundle
        {
            ProgramId = program.Id,
            Program = program,
            Code = "B-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "باقة",
            NameEn = "Bundle",
            MealsPerDay = 1,
            LunchCount = 1,
            Status = ProgramStatus.Active,
            IsActive = true,
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);
        await db.SaveChangesAsync();
        return (program.Id, bundle.Id);
    }

    private sealed record RenewalStartFixture(Guid GroupId, Guid ManagerId, Guid MemberId, Guid PreviousSubscriptionId,
        Guid RenewalId, Guid AddressId, Guid RegionId, Guid ProgramId, Guid BundleId, DateOnly NewStartDate,
        DateOnly PreviousEndDate, DateOnly[] PreviousServiceDates, Guid RestaurantId, DateTime Now);

    private async Task<RenewalStartFixture> SeedRenewalStartAsync()
    {
        var groupSeed = await SeedGroupAsync();
        _catalog = await SeedCatalogAsync();
        await using var db = CreateDb();
        var group = await db.FamilyGroups.SingleAsync(x => x.Id == groupSeed.GroupId);
        var country = await db.Countries.SingleAsync(x => x.Code == "KW");
        var now = new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
        var newStart = new DateOnly(2026, 10, 19);
        var oldStart = new DateOnly(2026, 9, 28);
        var oldClock = Substitute.For<IDateTimeProvider>();
        oldClock.UtcNow.Returns(now);
        var oldSchedule = new SubscriptionScheduleService(oldClock);
        var oldServiceDates = oldSchedule.GetServiceDates(oldStart, 6, false).ToArray();
        var oldEnd = oldServiceDates[^1];
        var region = new MealMate.Domain.Foundation.Region
        {
            CountryId = country.Id, Country = country, Code = "B2-REGION", NameAr = "منطقة الاختبار", NameEn = "Proof Region", IsActive = true
        };
        var address = new CustomerAddress
        {
            UserId = groupSeed.ManagerId, Label = "Paid address A", AddressLine = "Paid address A",
            RegionId = region.Id, Governorate = "Capital", Area = "City", Block = "1", Street = "Main", Building = "1"
        };
        var restaurantId = Guid.NewGuid();
        var managerMember = new FamilyMember
        {
            FamilyGroupId = groupSeed.GroupId, Role = FamilyMemberRole.Manager, DisplayName = "Manager",
            RelationshipCode = "self", NutritionalGoalCode = "maintain", SetupStatus = FamilyMemberSetupStatus.Ready,
            ProgramId = _catalog.ProgramId, BundleId = _catalog.BundleId, SelectedTier = RestaurantTier.Basic,
            DurationDays = 6, StartDate = oldStart, DeliveryAddressId = address.Id
        };
        var previous = new Subscription
        {
            CustomerUserId = groupSeed.ManagerId, FamilyGroupId = groupSeed.GroupId, FamilyMemberId = managerMember.Id,
            ProgramId = _catalog.ProgramId, BundleId = _catalog.BundleId, SelectedTier = RestaurantTier.Basic,
            DurationDays = 6, StartDate = oldStart, EndDate = oldEnd, IncludeFriday = false,
            Status = SubscriptionStatus.Active, PaidAmount = new Money(90m, "KWD"),
            PaymentVerifiedAtUtc = now.AddDays(-10), RegionId = region.Id, DeliveryAddress = address.AddressLine
        };
        group.Status = FamilyGroupStatus.Active;
        db.Regions.Add(region);
        db.CustomerAddresses.Add(address);
        db.CustomerProfiles.Add(new CustomerProfile { UserId = groupSeed.ManagerId, AllergiesCompletedAtUtc = now.AddDays(-20) });
        var pricingSettings = SubscriptionPricingSettingsDefaults.Create();
        pricingSettings.Family.Packages[0].MinMembers = 1;
        db.SystemSettings.Add(new MealMate.Domain.AdminDashboard.SystemSetting
        {
            Key = SubscriptionPricingSettingsDefaults.Key,
            ValueJson = System.Text.Json.JsonSerializer.Serialize(pricingSettings, SubscriptionPricingSettingsDefaults.JsonOptions),
            IsActive = true
        });
        db.FamilyMembers.Add(managerMember);
        db.FamilyMemberAccesses.Add(new FamilyMemberAccess
        {
            FamilyMemberId = managerMember.Id, LinkedCustomerUserId = groupSeed.ManagerId, HasSeparateAccount = true
        });
        db.FamilyMemberPermissions.Add(new FamilyMemberPermissions { FamilyMemberId = managerMember.Id, RequestRenewal = true, EditAddress = true });
        var otherMember = new FamilyMember
        {
            FamilyGroupId = groupSeed.GroupId, Role = FamilyMemberRole.Member, DisplayName = "Other paid member",
            RelationshipCode = "child", NutritionalGoalCode = "maintain", SetupStatus = FamilyMemberSetupStatus.Ready,
            ProgramId = _catalog.ProgramId, BundleId = _catalog.BundleId, SelectedTier = RestaurantTier.Basic,
            DurationDays = 6, StartDate = newStart, DeliveryAddressId = address.Id
        };
        db.FamilyMembers.Add(otherMember);
        db.FamilyMemberAccesses.Add(new FamilyMemberAccess { FamilyMemberId = otherMember.Id,
            LinkedCustomerUserId = Guid.NewGuid(), HasSeparateAccount = true });
        db.FamilyMemberPermissions.Add(new FamilyMemberPermissions { FamilyMemberId = otherMember.Id });
        var restaurant = new RestaurantProfile
        {
            UserId = Guid.NewGuid(), LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
            TradeNameAr = "مطعم", TradeNameEn = "Restaurant", PrimaryRegionId = region.Id, IsOperationallyActive = true
        };
        restaurantId = restaurant.Id;
        db.RestaurantProfiles.Add(restaurant);
        await db.SaveChangesAsync();
        var paidBatch = new FamilyCheckoutBatch
        {
            FamilyGroupId = group.Id, PayerCustomerUserId = groupSeed.ManagerId,
            IdempotencyKey = "paid-renewal-proof-" + Guid.NewGuid().ToString("N"), Status = FamilyCheckoutBatchStatus.Paid,
            CurrencyCode = "KWD", Subtotal = 90m, FinalPrice = 90m, PaidAtUtc = now.AddDays(-10),
            CreatedAtUtc = now.AddDays(-10)
        };
        paidBatch.Lines.Add(new FamilyCheckoutBatchLine
        {
            Batch = paidBatch, FamilyMemberId = managerMember.Id, SubscriptionId = previous.Id,
            CheckoutSelected = true, ProgramId = _catalog.ProgramId, BundleId = _catalog.BundleId,
            Tier = RestaurantTier.Basic, DurationDays = 6, StartDate = oldStart,
            DeliveryAddressId = address.Id, AddressLabel = address.Label, AddressLine = address.AddressLine,
            RegionId = region.Id, ResolvedRestaurantId = restaurantId, MenuPlanVersionsJson = "[]",
            PriceBeforeFamilyDiscount = 90m, NetPaidAmount = 90m, CurrencyCode = "KWD", CreatedAtUtc = now.AddDays(-10)
        });
        var nextServiceDates = oldSchedule.GetServiceDates(newStart, 6, false);
        var otherSubscription = new Subscription
        {
            CustomerUserId = groupSeed.ManagerId, FamilyGroupId = groupSeed.GroupId, FamilyMemberId = otherMember.Id,
            ProgramId = _catalog.ProgramId, BundleId = _catalog.BundleId, SelectedTier = RestaurantTier.Basic,
            DurationDays = 6, StartDate = newStart, EndDate = nextServiceDates[^1],
            Status = SubscriptionStatus.Active, PaidAmount = new Money(90m, "KWD"),
            PaymentVerifiedAtUtc = now.AddDays(-10), RegionId = region.Id, DeliveryAddress = address.AddressLine
        };
        var otherPaidBatch = new FamilyCheckoutBatch
        {
            FamilyGroupId = group.Id, PayerCustomerUserId = groupSeed.ManagerId,
            IdempotencyKey = "paid-other-proof-" + Guid.NewGuid().ToString("N"), Status = FamilyCheckoutBatchStatus.Paid,
            CurrencyCode = "KWD", Subtotal = 90m, FinalPrice = 90m, PaidAtUtc = now.AddDays(-10),
            CreatedAtUtc = now.AddDays(-10)
        };
        otherPaidBatch.Lines.Add(new FamilyCheckoutBatchLine
        {
            Batch = otherPaidBatch, FamilyMemberId = otherMember.Id, SubscriptionId = otherSubscription.Id,
            CheckoutSelected = true, ProgramId = _catalog.ProgramId, BundleId = _catalog.BundleId,
            Tier = RestaurantTier.Basic, DurationDays = 6, StartDate = newStart,
            DeliveryAddressId = address.Id, AddressLabel = address.Label, AddressLine = address.AddressLine,
            RegionId = region.Id, ResolvedRestaurantId = restaurantId, MenuPlanVersionsJson = "[]",
            PriceBeforeFamilyDiscount = 90m, NetPaidAmount = 90m, CurrencyCode = "KWD", CreatedAtUtc = now.AddDays(-10)
        });
        var renewal = new SubscriptionRenewal
        {
            PreviousSubscriptionId = previous.Id, FamilyGroupId = group.Id, FamilyMemberId = managerMember.Id,
            Status = "Requested", RequestedAtUtc = now.AddMinutes(-1)
        };
        db.Subscriptions.Add(previous);
        db.Subscriptions.Add(otherSubscription);
        db.FamilyCheckoutBatches.Add(paidBatch);
        db.FamilyCheckoutBatches.Add(otherPaidBatch);
        db.FamilyCheckoutBatchLines.AddRange(paidBatch.Lines);
        db.FamilyCheckoutBatchLines.AddRange(otherPaidBatch.Lines);
        foreach (var dayDate in oldServiceDates)
            db.SubscriptionDays.Add(new SubscriptionDay
            {
                SubscriptionId = previous.Id, ServiceDate = dayDate, Status = SubscriptionDayStatus.Open,
                DeliveryAddressId = address.Id, DeliveryAddressLine = address.AddressLine, DeliveryRegionId = region.Id,
                RestaurantId = restaurantId
            });
        foreach (var dayDate in nextServiceDates)
            db.SubscriptionDays.Add(new SubscriptionDay
            {
                SubscriptionId = otherSubscription.Id, ServiceDate = dayDate, Status = SubscriptionDayStatus.Open,
                DeliveryAddressId = address.Id, DeliveryAddressLine = address.AddressLine, DeliveryRegionId = region.Id,
                RestaurantId = restaurantId
            });
        await db.SaveChangesAsync();
        managerMember.MemberSubscriptionId = previous.Id;
        otherMember.MemberSubscriptionId = otherSubscription.Id;
        group.PaymentAnchorSubscriptionId = previous.Id;
        await db.SaveChangesAsync();
        db.SubscriptionRenewals.Add(renewal);
        await db.SaveChangesAsync();
        return new(group.Id, groupSeed.ManagerId, managerMember.Id, previous.Id, renewal.Id, address.Id, region.Id,
            _catalog.ProgramId, _catalog.BundleId, newStart, oldEnd, oldServiceDates, restaurantId, now);
    }

    private MealMate.Application.Features.FamilyCheckout.FamilyCheckoutService RenewalCheckoutFor(AppDbContext db,
        RenewalStartFixture fixture, Func<Task<PaymentCreationReceipt>> createInvoice,
        FamilyInvoiceOptions? invoiceOptions = null, string methodCurrency = "KWD")
    {
        var current = Substitute.For<ICurrentUser>();
        current.UserId.Returns(fixture.ManagerId);
        current.UserType.Returns(UserType.Customer);
        var managerId = fixture.ManagerId;
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(fixture.Now);
        clock.TodayUtc.Returns(DateOnly.FromDateTime(fixture.Now));
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(managerId,
            "manager@example.test", "50000000", "Manager", UserType.Customer, AccountStatus.Active,
            null, true, true, fixture.Now, null));
        var schedule = new SubscriptionScheduleService(clock);
        var candidates = Substitute.For<IRestaurantEligibilityService>();
        candidates.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>()).Returns(
            [new RestaurantEligibilityDto(fixture.RestaurantId, "مطعم", "Restaurant", true, [], [])]);
        var capacity = Substitute.For<IRestaurantCapacityReader>();
        capacity.ReadAsync(Arg.Any<IReadOnlyCollection<(Guid RestaurantId, DateOnly ServiceDate)>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyDictionary<(Guid RestaurantId, DateOnly ServiceDate), RestaurantCapacityUnits>)
                call.Arg<IReadOnlyCollection<(Guid RestaurantId, DateOnly ServiceDate)>>().ToDictionary(x => x,
                    x => new RestaurantCapacityUnits(x.RestaurantId, x.ServiceDate, 20, 0, 0, 0, 0, 0, 0, 0, 0)));
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<PreviewCustomerPricingQuery>(), Arg.Any<CancellationToken>()).Returns(
            Result.Success(new MealMate.Application.Features.ProgramsSubscriptions.Pricing.PricingEngineResult(
                90m, 6, 1m, 90m, 0m, 0m, 0m, 90m, 0m, 90m, 0m, 0m, 0m, false, "KWD")));
        sender.Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var command = call.Arg<CreateSubscriptionCommand>();
            var dates = schedule.GetServiceDates(command.StartDate, command.DurationDays, command.IncludeFriday);
            var subscription = new Subscription
            {
                CustomerUserId = command.CustomerUserId, ProgramId = command.ProgramId, BundleId = command.BundleId,
                SelectedTier = command.SelectedTier, DurationDays = command.DurationDays, IncludeFriday = command.IncludeFriday,
                StartDate = command.StartDate, EndDate = dates[^1], RegionId = command.RegionId,
                DeliveryAddress = command.DeliveryAddress, Status = SubscriptionStatus.PendingPayment,
                PaidAmount = new Money(90m, "KWD")
            };
            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync();
            return Result.Success(new SubscriptionDto(subscription.Id, subscription.CustomerUserId, subscription.ProgramId,
                "Program", "Program", subscription.BundleId, "Bundle", "Bundle", subscription.SelectedTier,
                subscription.DurationDays, subscription.Status, subscription.StartDate, subscription.EndDate,
                90m, "KWD", subscription.DeliveryAddress, null, fixture.Now, [], []));
        });
        var payments = Substitute.For<IPaymentGatewayService>();
        payments.GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>()).Returns(
            [new PaymentGatewayMethod(3, "PAYZAH", "دفع", "Payzah", null, null, methodCurrency, false)]);
        payments.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => createInvoice());
        var restaurantPool = Substitute.For<IRestaurantPoolSnapshotService>();
        restaurantPool.CaptureAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RestaurantEligibilityDto>>(),
                Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => new RestaurantPoolCaptureResult(true, Guid.NewGuid(), 1));
        return new MealMate.Application.Features.FamilyCheckout.FamilyCheckoutService(db, current, users,
            Substitute.For<IFamilyQuoteService>(), schedule, candidates, new PostgresCapacityConcurrencyLock(db),
            new PostgresCheckoutTransactionExecutor(db), restaurantPool, payments,
            sender, clock, capacity, new MealMate.Persistence.Common.PostgresApplicationLockManager(db),
            invoiceOptions: invoiceOptions is null ? null : Options.Create(invoiceOptions));
    }

    [Fact]
    public async Task Selection_edit_rejected_after_concurrent_freeze_under_group_lock()
    {
        // R04: a selection edit that passed its pre-check while the batch was Draft
        // must still be rejected when checkout froze the batch before it acquired
        // the shared group lock.
        var group = await SeedGroupAsync();
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        var batchId = await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.Draft, null, anchor);
        Guid memberId;
        await using (var db = CreateDb())
        {
            var member = new FamilyMember
            {
                FamilyGroupId = group.GroupId,
                Role = FamilyMemberRole.Member,
                DisplayName = "Second",
                RelationshipCode = "child",
                NutritionalGoalCode = "maintain",
            };
            db.FamilyMembers.Add(member);
            await db.SaveChangesAsync();
            db.FamilyMemberAccesses.Add(new FamilyMemberAccess
            {
                FamilyMemberId = member.Id,
                HasSeparateAccount = true,
                LinkedCustomerUserId = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
            memberId = member.Id;
        }

        var freezeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFreeze = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var freezeTask = Task.Run(async () =>
        {
            await using var db = CreateDb();
            var transactions = new PostgresCheckoutTransactionExecutor(db);
            var locks = new MealMate.Persistence.Common.PostgresApplicationLockManager(db);
            return await transactions.ExecuteAsync<bool>(async ct =>
            {
                await locks.LockFamilyGroupAsync(group.GroupId, ct);
                var batch = await db.FamilyCheckoutBatches.SingleAsync(x => x.Id == batchId, ct);
                batch.Status = FamilyCheckoutBatchStatus.PaymentPending;
                batch.Version++;
                await db.SaveChangesAsync(ct);
                freezeEntered.SetResult();
                await releaseFreeze.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                return true;
            });
        });

        await freezeEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(group.ManagerId);
        currentUser.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var selectionDb = CreateDb();
        var selection = new MealMate.Application.Features.FamilyCheckout.FamilyCheckoutSelectionService(
            selectionDb, currentUser, clock,
            new PostgresCheckoutTransactionExecutor(selectionDb),
            new MealMate.Persistence.Common.PostgresApplicationLockManager(selectionDb));

        // Blocks on the group lock until the freeze commits, then re-checks INSIDE
        // the protected scope and refuses to edit a PaymentPending batch.
        var editTask = selection.SetAsync(
            new MealMate.Application.Features.FamilyCheckout.SetFamilyCheckoutSelectionCommand(memberId, SubscribeNow: true),
            CancellationToken.None);

        releaseFreeze.SetResult();
        var editResult = await editTask.WaitAsync(TimeSpan.FromSeconds(20));
        await freezeTask.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(editResult.IsFailure, "selection edit must not land on a frozen batch");
        Assert.Equal("family.checkout_pending", editResult.Error!.Code);
        await using var verify = CreateDb();
        var line = await verify.FamilyCheckoutBatchLines.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BatchId == batchId && x.FamilyMemberId == memberId);
        Assert.Null(line);
    }

    [Fact]
    public async Task Same_key_retry_reuses_its_reserved_link_and_attempt()
    {
        // R05: a retry carrying the same idempotency key returns the SAME reserved
        // link (no second collectible URL) and keeps a single attempt row.
        var group = await SeedGroupAsync();
        var managerId = group.ManagerId;
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, managerId);
        var batchId = await SeedBatchAsync(group.GroupId, managerId, FamilyCheckoutBatchStatus.PaymentPending, null, anchor);
        await using (var db = CreateDb())
        {
            var batch = await db.FamilyCheckoutBatches.SingleAsync(x => x.Id == batchId);
            batch.IdempotencyKey = "retry-key";
            var attempt = await db.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.BatchId == batchId);
            attempt.AttemptKey = "retry-key"; attempt.ResultCode = "Initialized";
            attempt.GatewayPaymentUrl = "https://pay.example/recorded-retry-key"; attempt.GatewayPaymentId = "pz-test-track";
            await db.SaveChangesAsync();
        }

        var (first, second) = await RunResumeTwiceAsync(group.GroupId, managerId, anchor, "retry-key");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.PaymentUrl, second.Value.PaymentUrl);
        Assert.Equal(0, _gatewayCalls);

        await using var verify = CreateDb();
        var attempts = await verify.FamilyCheckoutPaymentAttempts.AsNoTracking()
            .Where(x => x.BatchId == batchId && x.AttemptKey == "retry-key").ToListAsync();
        Assert.Single(attempts);
        Assert.NotNull(attempts.Single().GatewayPaymentId);
        Assert.NotNull(attempts.Single().GatewayPaymentUrl);
    }

    [Fact]
    public async Task Concurrent_same_key_resumes_converge_on_one_attempt()
    {
        // R05: two concurrent resumes with the same key cannot each bind a
        // different link to the same reserved attempt — the Version token makes
        // the stamp single-writer and the loser reports the winner's link.
        var group = await SeedGroupAsync();
        var managerId = group.ManagerId;
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, managerId);
        var batchId = await SeedBatchAsync(group.GroupId, managerId, FamilyCheckoutBatchStatus.PaymentPending, null, anchor);
        await using (var db = CreateDb())
        {
            var batch = await db.FamilyCheckoutBatches.SingleAsync(x => x.Id == batchId);
            batch.IdempotencyKey = "race-key";
            var attempt = await db.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.BatchId == batchId);
            attempt.AttemptKey = "race-key"; attempt.ResultCode = "Initialized";
            attempt.GatewayPaymentUrl = "https://pay.example/recorded-race-key"; attempt.GatewayPaymentId = "pz-test-track";
            await db.SaveChangesAsync();
        }

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(managerId);
        currentUser.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc));
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                managerId, "m@example.com", "50000000", "Manager", UserType.Customer, AccountStatus.Active,
                null, true, true, DateTime.UtcNow, null));
        var users2 = Substitute.For<IUserAccountService>();
        users2.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                managerId, "m@example.com", "50000000", "Manager", UserType.Customer, AccountStatus.Active,
                null, true, true, DateTime.UtcNow, null));

        async Task<MealMate.Application.Features.FamilyCheckout.FamilyCheckoutService> MakeServiceAsync(int callIndex)
        {
            var db = CreateDb();
            var payments = Substitute.For<IPaymentGatewayService>();
            payments.ExecutePaymentAsync(
                    Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new PaymentCreationReceipt("provider-id", $"https://pay.example/invoice?paymentId=PAY-RACE-{callIndex}", "test-track"));
        return new MealMate.Application.Features.FamilyCheckout.FamilyCheckoutService(
                db, currentUser, callIndex == 0 ? users : users2,
                Substitute.For<MealMate.Application.Features.FamilyCheckout.IFamilyQuoteService>(),
                new SubscriptionScheduleService(clock),
                Substitute.For<IRestaurantEligibilityService>(),
                new PostgresCapacityConcurrencyLock(db),
                new PostgresCheckoutTransactionExecutor(db),
                Substitute.For<IRestaurantPoolSnapshotService>(),
                payments,
                Substitute.For<ISender>(),
                clock,
                Substitute.For<IRestaurantCapacityReader>(),
                new MealMate.Persistence.Common.PostgresApplicationLockManager(db));
        }

        var serviceA = await MakeServiceAsync(0);
        var serviceB = await MakeServiceAsync(1);
        var request = new MealMate.Application.Features.FamilyCheckout.FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error", IdempotencyKey: "race-key");

        var taskA = serviceA.StartAsync(request, CancellationToken.None);
        var taskB = serviceB.StartAsync(request, CancellationToken.None);
        await Task.WhenAll(taskA, taskB);

        Assert.True(taskA.Result.IsSuccess);
        Assert.True(taskB.Result.IsSuccess);
        Assert.Equal(taskA.Result.Value.PaymentUrl, taskB.Result.Value.PaymentUrl);

        await using var verify = CreateDb();
        var attempts = await verify.FamilyCheckoutPaymentAttempts.AsNoTracking()
            .Where(x => x.BatchId == batchId && x.AttemptKey == "race-key").ToListAsync();
        Assert.Single(attempts);
        Assert.NotNull(attempts.Single().GatewayPaymentId);
    }

    [Fact]
    public async Task Concurrent_fresh_starts_return_one_durable_link_and_one_gateway_call()
    {
        var fixture = await SeedSelectedDraftAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<PaymentCreationReceipt> Gateway()
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task;
            return new("provider-race", "https://pay.example/one-recorded-link", "race-track");
        }
        await using var dbA = CreateDb();
        await using var dbB = CreateDb();
        var firstService = CheckoutForDraft(dbA, fixture, Gateway);
        var secondService = CheckoutForDraft(dbB, fixture, Gateway);
        var request = new FamilyStartCheckoutRequest("https://localhost/paid", "https://localhost/error", IdempotencyKey: "fresh-race", ClientContractVersion: 1);
        var first = firstService.StartAsync(request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var second = secondService.StartAsync(request);
        await Task.Delay(150);
        Assert.Equal(1, Volatile.Read(ref calls));
        release.SetResult();
        var responses = await Task.WhenAll(first, second);
        Assert.All(responses, x => Assert.True(x.IsSuccess, x.IsFailure ? x.Error.Code : ""));
        Assert.Equal(responses[0].Value.PaymentUrl, responses[1].Value.PaymentUrl);
        await using var verify = CreateDb();
        var attempt = await verify.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.BatchId == fixture.BatchId);
        Assert.Equal(responses[0].Value.PaymentUrl, attempt.GatewayPaymentUrl);
        Assert.Equal("pz-race-track", attempt.GatewayPaymentId);
        Assert.Equal(1, calls);
        Assert.Equal(1, await verify.Subscriptions.CountAsync(x => x.FamilyGroupId == fixture.GroupId));
    }

    [Theory]
    [InlineData(true, 1, "family.invoice_creation_stopped")]
    [InlineData(false, null, "family.client_update_required")]
    [InlineData(false, 0, "family.client_update_required")]
    public async Task New_initial_invoice_policy_or_contract_gate_leaves_no_reservation(
        bool stopNew, int? clientVersion, string expectedCode)
    {
        var fixture = await SeedSelectedDraftAsync();
        var providerCalls = 0;
        await using var db = CreateDb();
        var service = CheckoutForDraft(db, fixture, () =>
        {
            Interlocked.Increment(ref providerCalls);
            return Task.FromResult(new PaymentCreationReceipt("unexpected", "https://pay.example/unexpected", "unexpected"));
        }, invoiceOptions: new FamilyInvoiceOptions { StopNewFamilyInvoices = stopNew, MinimumContractVersion = 1 });

        var result = await service.StartAsync(new FamilyStartCheckoutRequest("https://localhost/paid", "https://localhost/error",
            IdempotencyKey: $"blocked-initial-{Guid.NewGuid():N}", ClientContractVersion: clientVersion));

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal(0, providerCalls);
        await using var verify = CreateDb();
        Assert.Equal(FamilyCheckoutBatchStatus.Draft, await verify.FamilyCheckoutBatches.Where(x => x.Id == fixture.BatchId)
            .Select(x => x.Status).SingleAsync());
        Assert.Empty(await verify.FamilyCheckoutPaymentAttempts.Where(x => x.BatchId == fixture.BatchId).ToListAsync());
        Assert.Empty(await verify.Subscriptions.Where(x => x.FamilyGroupId == fixture.GroupId).ToListAsync());
        Assert.Empty(await verify.CapacityHolds.ToListAsync());
        Assert.Null(await verify.FamilyCheckoutBatchLines.Where(x => x.BatchId == fixture.BatchId)
            .Select(x => x.SubscriptionId).SingleAsync());
    }

    [Fact]
    public async Task Initial_currency_mismatch_fails_before_subscription_batch_attempt_or_hold_write()
    {
        var fixture = await SeedSelectedDraftAsync();
        var providerCalls = 0;
        await using var db = CreateDb();
        var service = CheckoutForDraft(db, fixture, () =>
        {
            Interlocked.Increment(ref providerCalls);
            return Task.FromResult(new PaymentCreationReceipt("unexpected", "https://pay.example/unexpected", "unexpected"));
        }, methodCurrency: "USD");

        var result = await service.StartAsync(new FamilyStartCheckoutRequest("https://localhost/paid", "https://localhost/error",
            IdempotencyKey: "currency-mismatch-initial", ClientContractVersion: 1));

        Assert.True(result.IsFailure);
        Assert.Equal("family.payment_currency_unsupported", result.Error.Code);
        Assert.Equal(0, providerCalls);
        await using var verify = CreateDb();
        Assert.Equal(FamilyCheckoutBatchStatus.Draft, await verify.FamilyCheckoutBatches.Where(x => x.Id == fixture.BatchId)
            .Select(x => x.Status).SingleAsync());
        Assert.Empty(await verify.FamilyCheckoutPaymentAttempts.Where(x => x.BatchId == fixture.BatchId).ToListAsync());
        Assert.Empty(await verify.Subscriptions.Where(x => x.FamilyGroupId == fixture.GroupId).ToListAsync());
        Assert.Empty(await verify.CapacityHolds.ToListAsync());
    }

    [Fact]
    public async Task Explicit_zero_initial_method_uses_the_same_default_method_preflighted_before_reservation()
    {
        var fixture = await SeedSelectedDraftAsync();
        var providerCalls = 0;
        var executedMethodId = -1;
        await using var db = CreateDb();
        var service = CheckoutForDraft(db, fixture, () =>
        {
            Interlocked.Increment(ref providerCalls);
            return Task.FromResult(new PaymentCreationReceipt("provider-default", "https://pay.example/default", "default-track"));
        }, paymentMethodSelected: id => executedMethodId = id);

        var result = await service.StartAsync(new FamilyStartCheckoutRequest("https://localhost/paid", "https://localhost/error",
            PaymentMethodId: 0, IdempotencyKey: "initial-default-method", ClientContractVersion: 1));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : "");
        Assert.Equal(1, providerCalls);
        Assert.Equal(3, executedMethodId);
        await using var verify = CreateDb();
        Assert.Equal(FamilyCheckoutBatchStatus.PaymentPending, await verify.FamilyCheckoutBatches.Where(x => x.Id == fixture.BatchId)
            .Select(x => x.Status).SingleAsync());
        Assert.Equal("pz-default-track", await verify.FamilyCheckoutPaymentAttempts.Where(x => x.BatchId == fixture.BatchId)
            .Select(x => x.GatewayPaymentId).SingleAsync());
        Assert.Single(await verify.Subscriptions.Where(x => x.FamilyGroupId == fixture.GroupId).ToListAsync());
    }

    [Fact]
    public async Task Stop_new_blocks_fresh_invoice_while_existing_family_callback_still_completes()
    {
        var existingGroup = await SeedGroupAsync();
        var existingSubscription = await SeedPendingSubscriptionAsync(existingGroup.GroupId, existingGroup.ManagerId);
        await SeedBatchAsync(existingGroup.GroupId, existingGroup.ManagerId, FamilyCheckoutBatchStatus.PaymentPending,
            "PAY-BEFORE-STOP", existingSubscription);

        var draft = await SeedSelectedDraftAsync();
        var providerCalls = 0;
        await using (var db = CreateDb())
        {
            var service = CheckoutForDraft(db, draft, () =>
            {
                Interlocked.Increment(ref providerCalls);
                return Task.FromResult(new PaymentCreationReceipt("unexpected", "https://pay.example/unexpected", "unexpected"));
            }, invoiceOptions: new FamilyInvoiceOptions { StopNewFamilyInvoices = true, MinimumContractVersion = 1 });
            var blocked = await service.StartAsync(new FamilyStartCheckoutRequest("https://localhost/paid",
                "https://localhost/error", IdempotencyKey: "after-stop", ClientContractVersion: 1));
            Assert.True(blocked.IsFailure);
            Assert.Equal("family.invoice_creation_stopped", blocked.Error.Code);
        }
        Assert.Equal(0, providerCalls);
        await using (var verifyNew = CreateDb())
        {
            Assert.Equal(FamilyCheckoutBatchStatus.Draft, await verifyNew.FamilyCheckoutBatches
                .Where(x => x.Id == draft.BatchId).Select(x => x.Status).SingleAsync());
            Assert.Empty(await verifyNew.FamilyCheckoutPaymentAttempts.Where(x => x.BatchId == draft.BatchId).ToListAsync());
            Assert.Empty(await verifyNew.Subscriptions.Where(x => x.FamilyGroupId == draft.GroupId).ToListAsync());
            var draftSubscriptionIds = verifyNew.Subscriptions.Where(x => x.FamilyGroupId == draft.GroupId).Select(x => x.Id);
            Assert.Empty(await verifyNew.CapacityHolds.Where(x => draftSubscriptionIds.Contains(x.SubscriptionId)).ToListAsync());
        }

        var callback = await ProcessAsync(existingSubscription, existingGroup.GroupId, "PAY-BEFORE-STOP", 180m);
        Assert.True(callback.IsAccepted, callback.ErrorCode);
        Assert.Equal("PendingRetry", callback.FulfillmentStatus);
        await using var verifyExisting = CreateDb();
        Assert.Equal(SubscriptionStatus.Paid, await verifyExisting.Subscriptions.Where(x => x.Id == existingSubscription)
            .Select(x => x.Status).SingleAsync());
        Assert.Equal(FamilyCheckoutBatchStatus.Paid, await verifyExisting.FamilyCheckoutBatches
            .Where(x => x.FamilyGroupId == existingGroup.GroupId).Select(x => x.Status).SingleAsync());
    }

    private static FamilySubscriptionDeliveryAmendments DeliveryAmendmentsFor(AppDbContext db, IDateTimeProvider clock,
        IRestaurantEligibilityService eligibility, IRestaurantCapacityReader capacity, IApplicationLockManager? lockManager = null)
    {
        var policies = Substitute.For<IOperationalPolicyService>();
        policies.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(new OperationalPolicySettings());
        return new FamilySubscriptionDeliveryAmendments(db, clock,
            new MealMate.Persistence.Common.PostgresApplicationTransactionExecutor(db),
            lockManager ?? new MealMate.Persistence.Common.PostgresApplicationLockManager(db),
            new FamilySubscriptionAccessService(db, new FamilyActionPolicy(db)), policies, eligibility, capacity);
    }

    private sealed class SignalingApplicationLockManager(IApplicationLockManager inner, TaskCompletionSource familyLockIssued)
        : IApplicationLockManager
    {
        public Task LockSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
            => inner.LockSubscriptionAsync(subscriptionId, cancellationToken);
        public Task LockFamilyBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
            => inner.LockFamilyBatchAsync(batchId, cancellationToken);
        public Task LockSubscriptionDaysAsync(Guid subscriptionId, CancellationToken cancellationToken = default)
            => inner.LockSubscriptionDaysAsync(subscriptionId, cancellationToken);
        public Task LockCustomerWalletAsync(Guid payerCustomerUserId, CancellationToken cancellationToken = default)
            => inner.LockCustomerWalletAsync(payerCustomerUserId, cancellationToken);
        public Task LockCustomerIdentityAsync(Guid userId, CancellationToken cancellationToken = default)
            => inner.LockCustomerIdentityAsync(userId, cancellationToken);
        public Task LockRefundInvoiceAsync(string invoiceId, CancellationToken cancellationToken = default)
            => inner.LockRefundInvoiceAsync(invoiceId, cancellationToken);
        public Task LockCampaignPaymentKeyAsync(Guid customerId, string key, CancellationToken cancellationToken = default)
            => inner.LockCampaignPaymentKeyAsync(customerId, key, cancellationToken);
        public Task LockCampaignCheckoutSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => inner.LockCampaignCheckoutSessionAsync(sessionId, cancellationToken);
        public Task LockTripAsync(Guid tripId, CancellationToken cancellationToken = default)
            => inner.LockTripAsync(tripId, cancellationToken);
        public Task LockSupportHandoverAsync(Guid handoverId, CancellationToken cancellationToken = default)
            => inner.LockSupportHandoverAsync(handoverId, cancellationToken);
        public Task LockDriverAsync(Guid driverProfileId, CancellationToken cancellationToken = default)
            => inner.LockDriverAsync(driverProfileId, cancellationToken);
        public Task LockSupportHandoverKeyAsync(Guid userId, string idempotencyKey, CancellationToken cancellationToken = default)
            => inner.LockSupportHandoverKeyAsync(userId, idempotencyKey, cancellationToken);
        public Task LockSupportCaseAsync(Guid caseId, CancellationToken cancellationToken = default)
            => inner.LockSupportCaseAsync(caseId, cancellationToken);
        public Task LockSupportEvidenceUploadAsync(Guid uploadFileRecordId, CancellationToken cancellationToken = default)
            => inner.LockSupportEvidenceUploadAsync(uploadFileRecordId, cancellationToken);
        public Task LockSupportTimingPolicyCatalogAsync(CancellationToken cancellationToken = default)
            => inner.LockSupportTimingPolicyCatalogAsync(cancellationToken);
        public Task LockSupportComplaintAsync(Guid complaintId, CancellationToken cancellationToken = default)
            => inner.LockSupportComplaintAsync(complaintId, cancellationToken);
        public Task LockPickupBoxAsync(Guid subscriptionDayId, CancellationToken cancellationToken = default)
            => inner.LockPickupBoxAsync(subscriptionDayId, cancellationToken);
        public Task LockRestaurantCapacityAsync(Guid restaurantId, DateOnly serviceDate, CancellationToken cancellationToken = default)
            => inner.LockRestaurantCapacityAsync(restaurantId, serviceDate, cancellationToken);
        public Task LockRestaurantCapacityAsync(IEnumerable<(Guid RestaurantId, DateOnly ServiceDate)> keys,
            CancellationToken cancellationToken = default)
            => inner.LockRestaurantCapacityAsync(keys, cancellationToken);
        public Task LockFamilyGroupAsync(Guid familyGroupId, CancellationToken cancellationToken = default)
        {
            var lockTask = inner.LockFamilyGroupAsync(familyGroupId, cancellationToken);
            familyLockIssued.TrySetResult();
            return lockTask;
        }
    }

    [Fact]
    public async Task Concurrent_family_renewal_start_reserves_once_and_replays_the_saved_invoice()
    {
        var fixture = await SeedRenewalStartAsync();
        var enteredProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCalls = 0;
        async Task<PaymentCreationReceipt> CreateInvoiceAsync()
        {
            Interlocked.Increment(ref providerCalls);
            enteredProvider.TrySetResult();
            await releaseProvider.Task;
            return new PaymentCreationReceipt("renewal-provider-handle", "https://pay.example/renewal-start", "renewal-track");
        }

        await using var dbA = CreateDb();
        await using var dbB = CreateDb();
        var serviceA = RenewalCheckoutFor(dbA, fixture, CreateInvoiceAsync);
        var serviceB = RenewalCheckoutFor(dbB, fixture, CreateInvoiceAsync);
        var request = new FamilyRenewalCheckoutRequest(fixture.NewStartDate, fixture.AddressId,
            "https://localhost/paid", "https://localhost/error", IdempotencyKey: "renewal-race", ClientContractVersion: 1);

        var firstStart = serviceA.StartRenewalAsync(fixture.RenewalId, request);
        var firstGate = await Task.WhenAny(enteredProvider.Task, firstStart);
        if (firstGate == firstStart)
        {
            var early = await firstStart;
            Assert.True(early.IsSuccess, early.IsFailure ? early.Error.Code : "The start completed before gateway creation.");
        }
        await enteredProvider.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var competingStart = await serviceB.StartRenewalAsync(fixture.RenewalId, request);
        Assert.True(competingStart.IsFailure);
        Assert.Equal("family.payment_initializing", competingStart.Error.Code);
        Assert.Equal(1, Volatile.Read(ref providerCalls));

        releaseProvider.TrySetResult();
        var started = await firstStart;
        Assert.True(started.IsSuccess, started.IsFailure ? started.Error.Code : null);
        Assert.Equal("https://pay.example/renewal-start", started.Value.PaymentUrl);

        var replay = await serviceB.StartRenewalAsync(fixture.RenewalId, request);
        Assert.True(replay.IsSuccess, replay.IsFailure ? replay.Error.Code : null);
        Assert.Equal(started.Value.PaymentUrl, replay.Value.PaymentUrl);
        Assert.Equal(started.Value.BatchId, replay.Value.BatchId);
        Assert.Equal(started.Value.PaymentAnchorSubscriptionId, replay.Value.PaymentAnchorSubscriptionId);
        Assert.Equal(started.Value.BatchVersion, replay.Value.BatchVersion);
        Assert.Equal(1, Volatile.Read(ref providerCalls));

        await using var verify = CreateDb();
        var renewalBatch = await verify.FamilyCheckoutBatches.AsNoTracking()
            .Include(x => x.Lines).Include(x => x.PaymentAttempts)
            .SingleAsync(x => x.RenewalRequestId == fixture.RenewalId);
        Assert.Equal("Renewal", renewalBatch.Purpose);
        Assert.Equal(FamilyCheckoutBatchStatus.PaymentPending, renewalBatch.Status);
        var line = Assert.Single(renewalBatch.Lines);
        Assert.True(line.CheckoutSelected);
        Assert.Equal(fixture.PreviousSubscriptionId, line.PreviousSubscriptionId);
        Assert.Equal(fixture.RenewalId, line.RenewalRequestId);
        Assert.Equal(started.Value.PaymentAnchorSubscriptionId, line.SubscriptionId);
        Assert.Equal(SubscriptionStatus.PendingPayment,
            await verify.Subscriptions.Where(x => x.Id == line.SubscriptionId).Select(x => x.Status).SingleAsync());
        var attempt = Assert.Single(renewalBatch.PaymentAttempts);
        Assert.Equal("Initialized", attempt.ResultCode);
        Assert.Equal("pz-renewal-track", attempt.GatewayPaymentId);
        Assert.Equal(started.Value.PaymentUrl, attempt.GatewayPaymentUrl);
        Assert.Equal(3, await verify.Subscriptions.CountAsync(x => x.FamilyGroupId == fixture.GroupId));
        Assert.Equal(1, await verify.Subscriptions.CountAsync(x => x.FamilyGroupId == fixture.GroupId
            && x.Status == SubscriptionStatus.PendingPayment));
        Assert.Equal(fixture.PreviousSubscriptionId,
            await verify.FamilyMembers.Where(x => x.Id == fixture.MemberId).Select(x => x.MemberSubscriptionId).SingleAsync());
        var oldSubscription = await verify.Subscriptions.AsNoTracking().SingleAsync(x => x.Id == fixture.PreviousSubscriptionId);
        Assert.Equal(SubscriptionStatus.Active, oldSubscription.Status);
        Assert.Equal(fixture.PreviousEndDate, oldSubscription.EndDate);
        var oldDays = await verify.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == fixture.PreviousSubscriptionId)
            .OrderBy(x => x.ServiceDate).ToListAsync();
        Assert.Equal(fixture.PreviousServiceDates, oldDays.Select(x => x.ServiceDate).ToArray());
        Assert.All(oldDays, day => Assert.Equal("Paid address A", day.DeliveryAddressLine));
        var oldLine = await verify.FamilyCheckoutBatchLines.AsNoTracking()
            .SingleAsync(x => x.SubscriptionId == fixture.PreviousSubscriptionId);
        Assert.Equal(90m, oldLine.NetPaidAmount);
        Assert.Equal(FamilyCheckoutBatchStatus.Paid,
            await verify.FamilyCheckoutBatches.Where(x => x.Id == oldLine.BatchId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Same_family_renewal_key_replays_across_arabic_and_english_cultures()
    {
        var fixture = await SeedRenewalStartAsync();
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var providerCalls = 0;
        var request = new FamilyRenewalCheckoutRequest(fixture.NewStartDate, fixture.AddressId,
            "https://localhost/paid", "https://localhost/error", IdempotencyKey: "renewal-culture-replay",
            ClientContractVersion: 1);
        async Task<PaymentCreationReceipt> CreateInvoiceAsync()
        {
            Interlocked.Increment(ref providerCalls);
            return await Task.FromResult(new PaymentCreationReceipt("renewal-culture-handle",
                "https://pay.example/renewal-culture", "renewal-culture-track"));
        }

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            await using var englishDb = CreateDb();
            var started = await RenewalCheckoutFor(englishDb, fixture, CreateInvoiceAsync).StartRenewalAsync(fixture.RenewalId, request);
            Assert.True(started.IsSuccess, started.IsFailure ? started.Error.Code : null);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-KW");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-KW");
            await using var arabicDb = CreateDb();
            var replay = await RenewalCheckoutFor(arabicDb, fixture, CreateInvoiceAsync,
                new FamilyInvoiceOptions { StopNewFamilyInvoices = true, MinimumContractVersion = 1 })
                .StartRenewalAsync(fixture.RenewalId, request with { ClientContractVersion = null });

            Assert.True(replay.IsSuccess, replay.IsFailure ? replay.Error.Code : null);
            Assert.Equal(started.Value.PaymentUrl, replay.Value.PaymentUrl);
            Assert.Equal(started.Value.BatchId, replay.Value.BatchId);
            Assert.Equal(1, providerCalls);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData(true, 1, "family.invoice_creation_stopped")]
    [InlineData(false, null, "family.client_update_required")]
    [InlineData(false, 0, "family.client_update_required")]
    public async Task New_renewal_policy_or_contract_gate_leaves_no_reservation(
        bool stopNew, int? clientVersion, string expectedCode)
    {
        var fixture = await SeedRenewalStartAsync();
        var providerCalls = 0;
        await using var before = CreateDb();
        var subscriptionCount = await before.Subscriptions.CountAsync(x => x.FamilyGroupId == fixture.GroupId);
        var holdCount = await before.CapacityHolds.CountAsync();
        var batchCount = await before.FamilyCheckoutBatches.CountAsync(x => x.RenewalRequestId == fixture.RenewalId);
        var lineCount = await before.FamilyCheckoutBatchLines.CountAsync(x => x.RenewalRequestId == fixture.RenewalId);
        var attemptCount = await before.FamilyCheckoutPaymentAttempts.CountAsync(x => x.Batch.RenewalRequestId == fixture.RenewalId);
        var request = new FamilyRenewalCheckoutRequest(fixture.NewStartDate, fixture.AddressId,
            "https://localhost/paid", "https://localhost/error", IdempotencyKey: $"blocked-renewal-{Guid.NewGuid():N}",
            ClientContractVersion: clientVersion);

        await using var db = CreateDb();
        var result = await RenewalCheckoutFor(db, fixture, () =>
        {
            Interlocked.Increment(ref providerCalls);
            return Task.FromResult(new PaymentCreationReceipt("unexpected", "https://pay.example/unexpected", "unexpected"));
        }, new FamilyInvoiceOptions { StopNewFamilyInvoices = stopNew, MinimumContractVersion = 1 })
            .StartRenewalAsync(fixture.RenewalId, request);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal(0, providerCalls);
        await using var verify = CreateDb();
        Assert.Null(await verify.SubscriptionRenewals.Where(x => x.Id == fixture.RenewalId).Select(x => x.BatchId).SingleAsync());
        Assert.Equal(subscriptionCount, await verify.Subscriptions.CountAsync(x => x.FamilyGroupId == fixture.GroupId));
        Assert.Equal(holdCount, await verify.CapacityHolds.CountAsync());
        Assert.Equal(batchCount, await verify.FamilyCheckoutBatches.CountAsync(x => x.RenewalRequestId == fixture.RenewalId));
        Assert.Equal(lineCount, await verify.FamilyCheckoutBatchLines.CountAsync(x => x.RenewalRequestId == fixture.RenewalId));
        Assert.Equal(attemptCount, await verify.FamilyCheckoutPaymentAttempts.CountAsync(x => x.Batch.RenewalRequestId == fixture.RenewalId));
    }

    [Fact]
    public async Task Renewal_currency_mismatch_fails_before_subscription_batch_or_hold_write()
    {
        var fixture = await SeedRenewalStartAsync();
        var providerCalls = 0;
        await using var before = CreateDb();
        var subscriptionCount = await before.Subscriptions.CountAsync(x => x.FamilyGroupId == fixture.GroupId);
        var holdCount = await before.CapacityHolds.CountAsync();
        var request = new FamilyRenewalCheckoutRequest(fixture.NewStartDate, fixture.AddressId,
            "https://localhost/paid", "https://localhost/error", IdempotencyKey: "renewal-currency-mismatch",
            ClientContractVersion: 1);

        await using var db = CreateDb();
        var result = await RenewalCheckoutFor(db, fixture, () =>
        {
            Interlocked.Increment(ref providerCalls);
            return Task.FromResult(new PaymentCreationReceipt("unexpected", "https://pay.example/unexpected", "unexpected"));
        }, methodCurrency: "USD").StartRenewalAsync(fixture.RenewalId, request);

        Assert.True(result.IsFailure);
        Assert.Equal("family.payment_currency_unsupported", result.Error.Code);
        Assert.Equal(0, providerCalls);
        await using var verify = CreateDb();
        Assert.Null(await verify.SubscriptionRenewals.Where(x => x.Id == fixture.RenewalId).Select(x => x.BatchId).SingleAsync());
        Assert.Equal(subscriptionCount, await verify.Subscriptions.CountAsync(x => x.FamilyGroupId == fixture.GroupId));
        Assert.Equal(holdCount, await verify.CapacityHolds.CountAsync());
        Assert.Empty(await verify.FamilyCheckoutBatches.Where(x => x.RenewalRequestId == fixture.RenewalId).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_same_version_delivery_amendments_accept_one_snapshot_and_keep_paid_history()
    {
        var groupSeed = await SeedGroupAsync();
        var catalog = await SeedCatalogAsync();
        var now = new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
        var firstDate = new DateOnly(2026, 10, 12);
        var protectedDate = new DateOnly(2026, 10, 13);
        Guid memberId;
        Guid subscriptionId;
        Guid restaurantId;
        Guid addressAId;
        Guid addressBId;
        Guid regionId;
        await using (var seed = CreateDb())
        {
            var group = await seed.FamilyGroups.SingleAsync(x => x.Id == groupSeed.GroupId);
            var country = await seed.Countries.SingleAsync(x => x.Code == "KW");
            group.Status = FamilyGroupStatus.Active;
            var region = new MealMate.Domain.Foundation.Region { CountryId = country.Id,
                Code = "B2-DELIVERY-REGION", NameAr = "منطقة", NameEn = "Proof Region", IsActive = true };
            regionId = region.Id;
            var addressA = new CustomerAddress { UserId = groupSeed.ManagerId, Label = "Address A",
                AddressLine = "Original paid address A", RegionId = regionId, Governorate = "Capital", Area = "City", Block = "1" };
            addressAId = addressA.Id;
            var addressB = new CustomerAddress { UserId = groupSeed.ManagerId, Label = "Address B",
                AddressLine = "Accepted delivery address B", RegionId = regionId, Governorate = "Capital", Area = "City", Block = "2" };
            addressBId = addressB.Id;
            var member = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Manager,
                DisplayName = "Manager", RelationshipCode = "self", NutritionalGoalCode = "maintain",
                SetupStatus = FamilyMemberSetupStatus.Ready, ProgramId = catalog.ProgramId, BundleId = catalog.BundleId,
                SelectedTier = RestaurantTier.Basic, DurationDays = 2, StartDate = firstDate, DeliveryAddressId = addressAId };
            memberId = member.Id;
            var subscription = new Subscription { CustomerUserId = groupSeed.ManagerId,
                FamilyGroupId = group.Id, FamilyMemberId = member.Id, ProgramId = catalog.ProgramId, BundleId = catalog.BundleId,
                SelectedTier = RestaurantTier.Basic, DurationDays = 2, StartDate = firstDate, EndDate = protectedDate,
                Status = SubscriptionStatus.Active, PaidAmount = new Money(90m, "KWD"), PaymentVerifiedAtUtc = now.AddDays(-1),
                RegionId = regionId, DeliveryAddress = addressA.AddressLine };
            subscriptionId = subscription.Id;
            var restaurant = new RestaurantProfile { UserId = Guid.NewGuid(), LegalNameAr = "مطعم", LegalNameEn = "Restaurant",
                TradeNameAr = "مطعم", TradeNameEn = "Restaurant", PrimaryRegionId = regionId, IsOperationallyActive = true };
            restaurantId = restaurant.Id;
            var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = groupSeed.ManagerId,
                IdempotencyKey = "paid-address-proof-" + Guid.NewGuid().ToString("N"), Status = FamilyCheckoutBatchStatus.Paid,
                CurrencyCode = "KWD", Subtotal = 90m, FinalPrice = 90m, PaidAtUtc = now.AddDays(-1), CreatedAtUtc = now.AddDays(-1) };
            batch.Lines.Add(new FamilyCheckoutBatchLine { Batch = batch, FamilyMemberId = member.Id, SubscriptionId = subscription.Id,
                CheckoutSelected = true, ProgramId = catalog.ProgramId, BundleId = catalog.BundleId, Tier = RestaurantTier.Basic,
                DurationDays = 2, StartDate = firstDate, DeliveryAddressId = addressA.Id, AddressLabel = addressA.Label,
                AddressLine = addressA.AddressLine, RegionId = regionId, ResolvedRestaurantId = restaurantId,
                MenuPlanVersionsJson = "[]", PriceBeforeFamilyDiscount = 90m, NetPaidAmount = 90m,
                CurrencyCode = "KWD", CreatedAtUtc = now.AddDays(-1) });
            seed.Regions.Add(region);
            seed.CustomerAddresses.AddRange(addressA, addressB);
            seed.RestaurantProfiles.Add(restaurant);
            seed.FamilyMembers.Add(member);
            seed.FamilyMemberAccesses.Add(new FamilyMemberAccess { FamilyMemberId = member.Id,
                LinkedCustomerUserId = groupSeed.ManagerId, HasSeparateAccount = true });
            seed.FamilyMemberPermissions.Add(new FamilyMemberPermissions { FamilyMemberId = member.Id, EditAddress = true });
            seed.Subscriptions.Add(subscription);
            seed.FamilyCheckoutBatches.Add(batch);
            seed.FamilyCheckoutBatchLines.AddRange(batch.Lines);
            seed.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = firstDate,
                Status = SubscriptionDayStatus.Open, SelectionDeadlineUtc = now.AddDays(1), RestaurantId = restaurantId,
                DeliveryAddressId = addressA.Id, DeliveryAddressLine = addressA.AddressLine, DeliveryRegionId = regionId });
            seed.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = protectedDate,
                Status = SubscriptionDayStatus.ConfirmedByRestaurant, SelectionDeadlineUtc = now.AddDays(1), RestaurantId = restaurantId,
                DeliveryAddressId = addressA.Id, DeliveryAddressLine = addressA.AddressLine, DeliveryRegionId = regionId });
            await seed.SaveChangesAsync();
            member.MemberSubscriptionId = subscription.Id;
            group.PaymentAnchorSubscriptionId = subscription.Id;
            await seed.SaveChangesAsync();
        }

        var firstInsideEligibility = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFamilyLockIssued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eligibilityCalls = 0;
        var firstClock = Substitute.For<IDateTimeProvider>();
        firstClock.UtcNow.Returns(now);
        var secondClock = Substitute.For<IDateTimeProvider>();
        secondClock.UtcNow.Returns(now);
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            if (Interlocked.Increment(ref eligibilityCalls) == 1)
            {
                firstInsideEligibility.TrySetResult();
                await releaseFirst.Task;
            }
            return [new RestaurantEligibilityDto(restaurantId, "مطعم", "Restaurant", true, [], [])];
        });
        var capacity = Substitute.For<IRestaurantCapacityReader>();
        capacity.ReadAsync(Arg.Any<IReadOnlyCollection<(Guid RestaurantId, DateOnly ServiceDate)>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyDictionary<(Guid RestaurantId, DateOnly ServiceDate), RestaurantCapacityUnits>)call
                .Arg<IReadOnlyCollection<(Guid RestaurantId, DateOnly ServiceDate)>>().ToDictionary(x => x,
                    x => new RestaurantCapacityUnits(x.RestaurantId, x.ServiceDate, 5, 1, 1, 0, 1, 0, 0, 0, 0)));
        await using var dbA = CreateDb();
        await using var dbB = CreateDb();
        var serviceA = DeliveryAmendmentsFor(dbA, firstClock, eligibility, capacity);
        var serviceB = DeliveryAmendmentsFor(dbB, secondClock, eligibility, capacity,
            new SignalingApplicationLockManager(new MealMate.Persistence.Common.PostgresApplicationLockManager(dbB), secondFamilyLockIssued));
        var request = new FamilyDeliveryAddressRequest(addressBId, firstDate, 0);
        var first = serviceA.AmendAsync(subscriptionId, groupSeed.ManagerId, request, default);
        await firstInsideEligibility.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var second = serviceB.AmendAsync(subscriptionId, groupSeed.ManagerId, request, default);
        await secondFamilyLockIssued.Task.WaitAsync(TimeSpan.FromSeconds(20));
        releaseFirst.TrySetResult();
        var results = await Task.WhenAll(first, second);

        var accepted = Assert.Single(results, x => x.IsSuccess);
        var rejected = Assert.Single(results, x => x.IsFailure);
        Assert.Equal("family.delivery_version_changed", rejected.Error.Code);
        Assert.Equal(1, accepted.Value.Version);
        Assert.Equal(new[] { subscriptionId }, new[] { accepted.Value.SubscriptionId });
        Assert.Equal(1, Volatile.Read(ref eligibilityCalls));

        await using var verify = CreateDb();
        var amendment = await verify.FamilySubscriptionDeliveryAmendments.AsNoTracking().SingleAsync(x => x.SubscriptionId == subscriptionId);
        Assert.Equal(1, amendment.Version);
        Assert.Equal(addressBId, amendment.AddressId);
        Assert.Equal("Accepted delivery address B", amendment.AddressLine);
        var days = await verify.SubscriptionDays.AsNoTracking().Where(x => x.SubscriptionId == subscriptionId).OrderBy(x => x.ServiceDate).ToListAsync();
        var mutable = Assert.Single(days, x => x.ServiceDate == firstDate);
        var protectedDay = Assert.Single(days, x => x.ServiceDate == protectedDate);
        Assert.Equal(addressBId, mutable.DeliveryAddressId);
        Assert.Equal("Accepted delivery address B", mutable.DeliveryAddressLine);
        Assert.Equal(amendment.Id, mutable.SourceFamilyDeliveryAmendmentId);
        Assert.Equal(addressAId, protectedDay.DeliveryAddressId);
        Assert.Equal("Original paid address A", protectedDay.DeliveryAddressLine);
        Assert.Null(protectedDay.SourceFamilyDeliveryAmendmentId);
        var paidLine = await verify.FamilyCheckoutBatchLines.AsNoTracking().SingleAsync(x => x.SubscriptionId == subscriptionId);
        Assert.Equal(addressAId, paidLine.DeliveryAddressId);
        Assert.Equal("Original paid address A", paidLine.AddressLine);
        var details = await DeliveryAmendmentsFor(verify, firstClock, eligibility, capacity)
            .GetAsync(subscriptionId, groupSeed.ManagerId, default);
        Assert.True(details.IsSuccess, details.IsFailure ? details.Error.Code : null);
        Assert.Equal(1, details.Value.CurrentVersion);
        Assert.Equal(addressBId, details.Value.AcceptedAddress!.Id);
        Assert.Equal("Accepted delivery address B", details.Value.AcceptedAddress.AddressLine);
    }

    [Fact]
    public async Task Selection_cannot_change_between_quote_and_freeze()
    {
        var fixture = await SeedSelectedDraftAsync();
        var quoted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQuote = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var checkoutDb = CreateDb();
        var checkout = CheckoutForDraft(checkoutDb, fixture,
            () => Task.FromResult(new PaymentCreationReceipt("provider-selection", "https://pay.example/selection", "selection-track")),
            async () => { quoted.SetResult(); await releaseQuote.Task; });
        var started = checkout.StartAsync(new FamilyStartCheckoutRequest("https://localhost/paid", "https://localhost/error", ClientContractVersion: 1));
        await quoted.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using var selectionDb = CreateDb();
        var current = Substitute.For<ICurrentUser>(); current.UserId.Returns(fixture.ManagerId); current.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(fixture.Now);
        var selection = new FamilyCheckoutSelectionService(selectionDb, current, clock,
            new PostgresCheckoutTransactionExecutor(selectionDb), new MealMate.Persistence.Common.PostgresApplicationLockManager(selectionDb));
        var deselect = selection.SetAsync(new SetFamilyCheckoutSelectionCommand(fixture.MemberId, false), default);
        await Task.Delay(150);
        Assert.False(deselect.IsCompleted, "selection must wait for the checkout's family lock");
        releaseQuote.SetResult();
        Assert.True((await started).IsSuccess);
        var changed = await deselect;
        Assert.True(changed.IsFailure);
        Assert.Equal("family.checkout_pending", changed.Error.Code);
        await using var verify = CreateDb();
        Assert.True(await verify.FamilyCheckoutBatchLines.Where(x => x.BatchId == fixture.BatchId).Select(x => x.CheckoutSelected).SingleAsync());
    }

    [Fact]
    public async Task Uncertain_gateway_initialization_cannot_create_a_second_invoice()
    {
        var fixture = await SeedSelectedDraftAsync();
        var calls = 0;
        await using var firstDb = CreateDb();
        var service = CheckoutForDraft(firstDb, fixture, () => { calls++; throw new TimeoutException(); });
        var request = new FamilyStartCheckoutRequest("https://localhost/paid", "https://localhost/error", IdempotencyKey: "uncertain-start", ClientContractVersion: 1);
        Assert.Equal("family.batch_review_required", (await service.StartAsync(request)).Error.Code);
        await using var retryDb = CreateDb();
        var retried = await CheckoutForDraft(retryDb, fixture, () => { calls++; throw new TimeoutException(); }).StartAsync(request);
        Assert.Equal("family.batch_review_required", retried.Error.Code);
        Assert.Equal(1, calls);
        await using var verify = CreateDb();
        Assert.Equal("InitializationUncertain", (await verify.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.BatchId == fixture.BatchId)).ResultCode);
    }

    [Fact]
    public async Task Expiry_during_gateway_initialization_preserves_receipt_and_blocks_restart()
    {
        var fixture = await SeedSelectedDraftAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<PaymentCreationReceipt> Gateway()
        {
            calls++; entered.SetResult(); await release.Task;
            return new("provider-delayed", "https://pay.example/delayed-receipt", "delayed-track");
        }
        await using var startDb = CreateDb();
        var request = new FamilyStartCheckoutRequest("https://localhost/paid", "https://localhost/error", IdempotencyKey: "expiry-initializing", ClientContractVersion: 1);
        var started = CheckoutForDraft(startDb, fixture, Gateway).StartAsync(request);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using (var expiryDb = CreateDb())
        {
            var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(fixture.Now.AddMinutes(20));
            var expiry = new CapacityHoldExpiryService(expiryDb, new PostgresCheckoutTransactionExecutor(expiryDb),
                new PostgresCapacityConcurrencyLock(expiryDb), clock, familyLocks: new MealMate.Persistence.Common.PostgresApplicationLockManager(expiryDb));
            await expiry.ExpireDueAsync();
        }
        release.SetResult();
        Assert.Equal("family.batch_review_required", (await started).Error.Code);
        await using var retryDb = CreateDb();
        Assert.Equal("family.batch_review_required", (await CheckoutForDraft(retryDb, fixture, Gateway).StartAsync(request)).Error.Code);
        Assert.Equal(1, calls);
        await using var verify = CreateDb();
        var attempt = await verify.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.BatchId == fixture.BatchId);
        Assert.Equal("pz-delayed-track", attempt.GatewayPaymentId);
        Assert.Equal("https://pay.example/delayed-receipt", attempt.GatewayPaymentUrl);
        Assert.Equal(FamilyCheckoutBatchStatus.ReviewRequired, (await verify.FamilyCheckoutBatches.SingleAsync(x => x.Id == fixture.BatchId)).Status);
        Assert.Equal(1, await verify.FamilyMembers.CountAsync(x => x.FamilyGroupId == fixture.GroupId));
    }

    [Fact]
    public async Task Paid_key_retry_returns_original_receipt_without_changing_new_draft()
    {
        var fixture = await SeedSelectedDraftAsync();
        var calls = 0;
        Task<PaymentCreationReceipt> Gateway() { calls++; return Task.FromResult(new PaymentCreationReceipt("provider-history", "https://pay.example/historical-link", "historical-track")); }
        var request = new FamilyStartCheckoutRequest("https://localhost/paid", "https://localhost/error",
            IdempotencyKey: "paid-history-key", ClientContractVersion: 1);
        await using var firstDb = CreateDb();
        var first = await CheckoutForDraft(firstDb, fixture, Gateway).StartAsync(request);
        Assert.True(first.IsSuccess);
        await using var retryDb = CreateDb();
        var second = await CheckoutForDraft(retryDb, fixture, Gateway,
            invoiceOptions: new FamilyInvoiceOptions { StopNewFamilyInvoices = true, MinimumContractVersion = 1 })
            .StartAsync(request with { ClientContractVersion = null });
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.Code : "");
        Assert.Equal(first.Value.PaymentUrl, second.Value.PaymentUrl);
        Assert.Equal(first.Value.BatchId, second.Value.BatchId);
        Assert.Equal(1, calls);
        Guid newDraft;
        await using (var db = CreateDb())
        {
            var original = await db.FamilyCheckoutBatches.SingleAsync(x => x.Id == fixture.BatchId);
            original.Status = FamilyCheckoutBatchStatus.Paid;
            (await db.FamilyCheckoutPaymentAttempts.SingleAsync(x => x.BatchId == original.Id)).ResultCode = "Paid";
            (await db.FamilyGroups.SingleAsync(x => x.Id == fixture.GroupId)).Status = FamilyGroupStatus.Active;
            var next = new FamilyCheckoutBatch { FamilyGroupId = fixture.GroupId, PayerCustomerUserId = fixture.ManagerId,
                IdempotencyKey = "next-draft", CreatedAtUtc = fixture.Now.AddMinutes(1), Version = 7 };
            db.FamilyCheckoutBatches.Add(next); newDraft = next.Id;
            await db.SaveChangesAsync();
        }
        await using (var paidReplayDb = CreateDb())
        {
            var paidReplay = await CheckoutForDraft(paidReplayDb, fixture, Gateway,
                invoiceOptions: new FamilyInvoiceOptions { StopNewFamilyInvoices = true, MinimumContractVersion = 1 })
                .StartAsync(request with { ClientContractVersion = null });
            Assert.True(paidReplay.IsSuccess, paidReplay.IsFailure ? paidReplay.Error.Code : "");
            Assert.Equal(first.Value.PaymentUrl, paidReplay.Value.PaymentUrl);
            Assert.Equal(first.Value.BatchId, paidReplay.Value.BatchId);
        }
        Assert.Equal(1, calls);
        await using var verify = CreateDb();
        var untouched = await verify.FamilyCheckoutBatches.SingleAsync(x => x.Id == newDraft);
        Assert.Equal(FamilyCheckoutBatchStatus.Draft, untouched.Status);
        Assert.Equal(7, untouched.Version);
    }

    private sealed record SelectedDraft(Guid GroupId, Guid ManagerId, Guid MemberId, Guid BatchId, DateTime Now, Guid ProgramId, Guid BundleId);

    [Fact]
    public async Task Duplicate_financial_review_preserves_reason_and_does_not_fulfill()
    {
        var group = await SeedGroupAsync();
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-RECORDED", anchor);
        var first = await ProcessAsync(anchor, group.GroupId, "PAY-UNMAPPED", 180m);
        var second = await ProcessAsync(anchor, group.GroupId, "PAY-UNMAPPED", 180m);
        Assert.True(first.IsAccepted);
        Assert.True(second.IsAccepted);
        Assert.True(second.IsDuplicate);
        Assert.Equal("ReviewRequired", second.FulfillmentStatus);
        Assert.Equal(first.ErrorCode, second.ErrorCode);
        await using var verify = CreateDb();
        Assert.Equal(SubscriptionStatus.PendingPayment, (await verify.Subscriptions.SingleAsync(x => x.Id == anchor)).Status);
        Assert.Equal(1, await verify.PaymentWebhookReceipts.CountAsync(x => x.PaymentId == "PAY-UNMAPPED"));
    }

    [Fact]
    public async Task Anchor_sorting_last_keeps_one_payment_id_and_canonical_receipt_across_group_retries()
    {
        var group = await SeedGroupAsync();
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var sibling = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId, Guid.Parse("11111111-1111-1111-1111-111111111111"));
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-HIGH-ANCHOR", anchor, sibling);
        await using (var db = CreateDb())
        {
            (await db.Subscriptions.SingleAsync(x => x.Id == anchor)).PaymentId = "PAY-HIGH-ANCHOR";
            (await db.FamilyGroups.SingleAsync(x => x.Id == group.GroupId)).PaymentAnchorSubscriptionId = anchor;
            await db.SaveChangesAsync();
        }
        Assert.True((await ProcessAsync(group.GroupId, group.GroupId, "PAY-HIGH-ANCHOR", 180m)).IsAccepted);
        var duplicate = await ProcessAsync(group.GroupId, group.GroupId, "PAY-HIGH-ANCHOR", 180m);
        Assert.True(duplicate.IsAccepted);
        Assert.True(duplicate.IsDuplicate);
        await using var verify = CreateDb();
        Assert.Equal(2, await verify.Subscriptions.CountAsync(x => x.FamilyGroupId == group.GroupId && x.Status == SubscriptionStatus.Paid));
        Assert.Equal(1, await verify.Subscriptions.CountAsync(x => x.PaymentId == "PAY-HIGH-ANCHOR"));
        Assert.Equal(anchor, (await verify.PaymentWebhookReceipts.SingleAsync(x => x.PaymentId == "PAY-HIGH-ANCHOR")).SubscriptionId);
    }

    [Fact]
    public async Task Invalid_later_sibling_hold_puts_whole_batch_in_review_before_any_paid_transition()
    {
        var group = await SeedGroupAsync();
        var anchor = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        var sibling = await SeedPendingSubscriptionAsync(group.GroupId, group.ManagerId);
        await SeedBatchAsync(group.GroupId, group.ManagerId, FamilyCheckoutBatchStatus.PaymentPending, "PAY-INVALID-SIBLING", anchor, sibling);
        await using (var db = CreateDb())
        {
            (await db.CapacityHolds.SingleAsync(x => x.SubscriptionId == sibling)).Status = CapacityHoldStatus.Released;
            await db.SaveChangesAsync();
        }
        var result = await ProcessAsync(anchor, group.GroupId, "PAY-INVALID-SIBLING", 180m);
        Assert.Equal("ReviewRequired", result.FulfillmentStatus);
        await using var verify = CreateDb();
        Assert.Equal(2, await verify.Subscriptions.CountAsync(x => x.FamilyGroupId == group.GroupId && x.Status == SubscriptionStatus.PendingPayment));
        Assert.Equal(FamilyCheckoutBatchStatus.ReviewRequired, (await verify.FamilyCheckoutBatches.SingleAsync(x => x.FamilyGroupId == group.GroupId)).Status);
    }

    private async Task<SelectedDraft> SeedSelectedDraftAsync()
    {
        var group = await SeedGroupAsync();
        if (_catalog == default) _catalog = await SeedCatalogAsync();
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var persisted = await db.FamilyGroups.SingleAsync(x => x.Id == group.GroupId);
        persisted.Status = FamilyGroupStatus.Draft;
        var address = new CustomerAddress { UserId = group.ManagerId, Label = "Home", AddressLine = "Fixture delivery address", RegionId = Guid.NewGuid() };
        db.CustomerAddresses.Add(address);
        db.CustomerProfiles.Add(new CustomerProfile { UserId = group.ManagerId, AllergiesCompletedAtUtc = now });
        var member = new FamilyMember { FamilyGroupId = group.GroupId, Role = FamilyMemberRole.Manager, DisplayName = "Manager",
            RelationshipCode = "self", NutritionalGoalCode = "maintain", ProgramId = _catalog.ProgramId, BundleId = _catalog.BundleId,
            SelectedTier = RestaurantTier.Basic, DurationDays = 1, StartDate = DateOnly.FromDateTime(now.AddDays(2)), DeliveryAddressId = address.Id };
        db.FamilyMembers.Add(member);
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.GroupId, PayerCustomerUserId = group.ManagerId, IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAtUtc = now };
        db.FamilyCheckoutBatches.Add(batch);
        db.FamilyCheckoutBatchLines.Add(new FamilyCheckoutBatchLine { BatchId = batch.Id, FamilyMemberId = member.Id, CheckoutSelected = true });
        await db.SaveChangesAsync();
        return new(group.GroupId, group.ManagerId, member.Id, batch.Id, now, _catalog.ProgramId, _catalog.BundleId);
    }

    private FamilyCheckoutService CheckoutForDraft(AppDbContext db, SelectedDraft fixture,
        Func<Task<PaymentCreationReceipt>> gateway, Func<Task>? beforeQuote = null,
        FamilyInvoiceOptions? invoiceOptions = null, string methodCurrency = "KWD", Action<int>? paymentMethodSelected = null)
    {
        var current = Substitute.For<ICurrentUser>(); current.UserId.Returns(fixture.ManagerId); current.UserType.Returns(UserType.Customer);
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(fixture.Now);
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(fixture.ManagerId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(fixture.ManagerId,
            "manager@example.com", "50000000", "Manager", UserType.Customer, AccountStatus.Active, null, true, true, fixture.Now, null));
        var quote = Substitute.For<IFamilyQuoteService>();
        quote.QuoteAsync(Arg.Any<FamilyGroup>(), Arg.Any<CancellationToken>(), Arg.Any<FamilyQuoteScope>()).Returns(async _ =>
        {
            if (beforeQuote is not null) await beforeQuote();
            return Result.Success(new FamilyQuoteResult([new FamilyQuoteLine(fixture.MemberId, "Manager", 180m, 0m, 0m, 0m)],
                180m, 0m, 0m, 180m, "KWD", false, Guid.NewGuid(), 1, 4, 1));
        });
        var date = DateOnly.FromDateTime(fixture.Now.AddDays(2));
        var schedule = Substitute.For<ISubscriptionScheduleService>(); schedule.GetServiceDates(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<bool>()).Returns([date]);
        var restaurant = Guid.NewGuid(); var menu = Guid.NewGuid();
        var eligibility = Substitute.For<IRestaurantEligibilityService>();
        eligibility.EvaluateAsync(Arg.Any<RestaurantEligibilityRequest>(), Arg.Any<CancellationToken>()).Returns(
            [new RestaurantEligibilityDto(restaurant, "مطعم", "Restaurant", true, [], [new EligibleMenuDateDto(date, menu)])]);
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<CreateSubscriptionCommand>(), Arg.Any<CancellationToken>()).Returns(async ci =>
        {
            var command = ci.Arg<CreateSubscriptionCommand>();
            var sub = new Subscription { CustomerUserId = command.CustomerUserId, ProgramId = command.ProgramId, BundleId = command.BundleId,
                SelectedTier = command.SelectedTier, DurationDays = command.DurationDays, StartDate = date, EndDate = date,
                Status = SubscriptionStatus.PendingPayment, RegionId = command.RegionId, DeliveryAddress = command.DeliveryAddress, PaidAmount = new Money(180m, "KWD") };
            db.Subscriptions.Add(sub); await db.SaveChangesAsync();
            return Result.Success(new SubscriptionDto(sub.Id, sub.CustomerUserId, sub.ProgramId, "", "", sub.BundleId, "", "", sub.SelectedTier,
                sub.DurationDays, sub.Status, date, date, 180m, "KWD", sub.DeliveryAddress, null, fixture.Now, [], []));
        });
        var pool = Substitute.For<IRestaurantPoolSnapshotService>();
        pool.CaptureAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<RestaurantEligibilityDto>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new RestaurantPoolCaptureResult(true, Guid.NewGuid(), 1));
        var capacity = Substitute.For<IRestaurantCapacityReader>();
        capacity.ReadAsync(Arg.Any<IReadOnlyCollection<(Guid RestaurantId, DateOnly ServiceDate)>>(), Arg.Any<CancellationToken>()).Returns(ci =>
            (IReadOnlyDictionary<(Guid RestaurantId, DateOnly ServiceDate), RestaurantCapacityUnits>)ci.Arg<IReadOnlyCollection<(Guid RestaurantId, DateOnly ServiceDate)>>()
                .ToDictionary(x => x, x => new RestaurantCapacityUnits(x.RestaurantId, x.ServiceDate, 100, 0, 0, 0, 0, 0, 0, 0, 0)));
        var payments = Substitute.For<IPaymentGatewayService>();
        payments.GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>()).Returns(
            [new PaymentGatewayMethod(3, "PAYZAH", "دفع", "Payzah", null, null, methodCurrency, false)]);
        payments.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ci =>
            {
                paymentMethodSelected?.Invoke(ci.ArgAt<int>(7));
                return gateway();
            });
        return new(db, current, users, quote, schedule, eligibility, new PostgresCapacityConcurrencyLock(db), new PostgresCheckoutTransactionExecutor(db),
            pool, payments, sender, clock, capacity, new MealMate.Persistence.Common.PostgresApplicationLockManager(db),
            invoiceOptions: invoiceOptions is null ? null : Options.Create(invoiceOptions));
    }

    private int _gatewayCalls;

    private async Task<(
        MealMate.Application.Common.Models.Result<MealMate.Application.Features.FamilyCheckout.FamilyCheckoutStartedDto> First,
        MealMate.Application.Common.Models.Result<MealMate.Application.Features.FamilyCheckout.FamilyCheckoutStartedDto> Second)>
        RunResumeTwiceAsync(Guid groupId, Guid managerId, Guid anchorId, string idempotencyKey)
    {
        _gatewayCalls = 0;
        async Task<MealMate.Application.Features.FamilyCheckout.FamilyCheckoutService> MakeServiceAsync()
        {
            var db = CreateDb();
            var payments = Substitute.For<IPaymentGatewayService>();
            payments.ExecutePaymentAsync(
                    Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    Interlocked.Increment(ref _gatewayCalls);
                    return new PaymentCreationReceipt("provider-id", $"https://pay.example/invoice?paymentId=PAY-REUSE-{_gatewayCalls}", "test-track");
                });
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.UserId.Returns(managerId);
            currentUser.UserType.Returns(UserType.Customer);
            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc));
            var users = Substitute.For<IUserAccountService>();
            users.FindByIdAsync(managerId, Arg.Any<CancellationToken>())
                .Returns(new UserAccountDto(
                    managerId, "m@example.com", "50000000", "Manager", UserType.Customer, AccountStatus.Active,
                    null, true, true, DateTime.UtcNow, null));
            return new MealMate.Application.Features.FamilyCheckout.FamilyCheckoutService(
                db, currentUser, users,
                Substitute.For<MealMate.Application.Features.FamilyCheckout.IFamilyQuoteService>(),
                new SubscriptionScheduleService(clock),
                Substitute.For<IRestaurantEligibilityService>(),
                new PostgresCapacityConcurrencyLock(db),
                new PostgresCheckoutTransactionExecutor(db),
                Substitute.For<IRestaurantPoolSnapshotService>(),
                payments,
                Substitute.For<ISender>(),
                clock,
                Substitute.For<IRestaurantCapacityReader>(),
                new MealMate.Persistence.Common.PostgresApplicationLockManager(db));
        }

        var request = new MealMate.Application.Features.FamilyCheckout.FamilyStartCheckoutRequest(
            "https://localhost/callback", "https://localhost/error", IdempotencyKey: idempotencyKey);
        var firstService = await MakeServiceAsync();
        var first = await firstService.StartAsync(request, CancellationToken.None);
        var secondService = await MakeServiceAsync();
        var second = await secondService.StartAsync(request, CancellationToken.None);
        return (first, second);
    }

    private async Task<PaymentProcessingResult> ProcessWithGatewayStatusAsync(
        Guid subscriptionId, Guid familyGroupId, string paymentId, decimal amount, string invoiceStatus, string responsePaymentId)
    {
        await using var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        var now = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);
        clock.UtcNow.Returns(now);
        var invoiceId = "INV-" + paymentId;
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync(paymentId, "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse(invoiceId, invoiceStatus, amount, "Customer", "c@example.com", "50000000",
                "TX-" + paymentId, "KNET", now, familyGroupId.ToString(), "KWD", responsePaymentId));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        fulfillment.TryFulfillAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FulfillmentResult(false, false, "fulfillment.skipped_in_test"));
        var processor = new SubscriptionPaymentProcessor(db, gateway, new PostgresCheckoutTransactionExecutor(db),
            new PostgresCapacityConcurrencyLock(db), new SubscriptionScheduleService(clock), fulfillment, clock, locks: new MealMate.Persistence.Common.PostgresApplicationLockManager(db));
        return await processor.VerifyAndProcessAsync(subscriptionId, paymentId);
    }

    private async Task<PaymentProcessingResult> ProcessFailureAsync(Guid subscriptionId, Guid familyGroupId, string paymentId)
    {
        await using var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        var now = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);
        clock.UtcNow.Returns(now);
        var invoiceId = "INV-" + paymentId;
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.GetPaymentStatusAsync(paymentId, "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new PaymentGatewayStatusResponse(invoiceId, "Failed", 180m, "Customer", "c@example.com", "50000000",
                "TX-" + paymentId, "KNET", now, familyGroupId.ToString(), "KWD", paymentId));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        var processor = new SubscriptionPaymentProcessor(db, gateway, new PostgresCheckoutTransactionExecutor(db),
            new PostgresCapacityConcurrencyLock(db), new SubscriptionScheduleService(clock), fulfillment, clock, locks: new MealMate.Persistence.Common.PostgresApplicationLockManager(db));
        return await processor.VerifyAndReleaseFailedAsync(subscriptionId, paymentId);
    }

    private async Task<(Guid GroupId, Guid ManagerId)> SeedGroupAsync()
    {
        await using var db = CreateDb();
        var managerId = Guid.NewGuid();
        var group = new FamilyGroup
        {
            ManagerCustomerUserId = managerId,
            Name = "Family",
            Status = FamilyGroupStatus.CheckoutPending,
            QuotedFinalPrice = 180m,
            CurrencyCode = "KWD",
        };
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();
        return (group.Id, managerId);
    }

    private async Task<RenewalSeed> SeedRenewalBatchAsync(DateOnly previousEndDate,
        SubscriptionStatus previousStatus = SubscriptionStatus.Active)
    {
        var groupSeed = await SeedGroupAsync();
        var newSubscriptionId = await SeedPendingSubscriptionAsync(groupSeed.GroupId, groupSeed.ManagerId);
        await using var db = CreateDb();
        var next = await db.Subscriptions.SingleAsync(x => x.Id == newSubscriptionId);
        var group = await db.FamilyGroups.SingleAsync(x => x.Id == groupSeed.GroupId);
        group.Status = FamilyGroupStatus.Active;
        var previous = new Subscription
        {
            CustomerUserId = groupSeed.ManagerId,
            ProgramId = next.ProgramId,
            BundleId = next.BundleId,
            SelectedTier = next.SelectedTier,
            DurationDays = 1,
            IncludeFriday = false,
            Status = previousStatus,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = previousEndDate,
            PaidAmount = new Money(90m, "KWD"),
            PaymentVerifiedAtUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
            FamilyGroupId = groupSeed.GroupId,
        };
        var member = new FamilyMember
        {
            FamilyGroupId = groupSeed.GroupId,
            Role = FamilyMemberRole.Member,
            DisplayName = "Renewal member",
            RelationshipCode = "child",
            NutritionalGoalCode = "maintain",
            SetupStatus = FamilyMemberSetupStatus.Ready,
        };
        previous.FamilyMemberId = member.Id;
        db.FamilyMembers.Add(member);
        await db.SaveChangesAsync();
        previous.FamilyMemberId = member.Id;
        db.Subscriptions.Add(previous);
        await db.SaveChangesAsync();
        member.MemberSubscriptionId = previous.Id;
        next.FamilyMemberId = member.Id;
        group.PaymentAnchorSubscriptionId = previous.Id;
        var addressId = Guid.NewGuid();
        var paymentId = "PAY-RENEWAL-" + Guid.NewGuid().ToString("N");
        var renewal = new SubscriptionRenewal
        {
            PreviousSubscriptionId = previous.Id,
            FamilyGroupId = groupSeed.GroupId,
            FamilyMemberId = member.Id,
            Status = "PaymentPending",
            RequestedStartDate = next.StartDate,
            RequestedAddressId = addressId,
            RequestedAtUtc = new DateTime(2026, 8, 13, 8, 0, 0, DateTimeKind.Utc),
        };
        var batch = new FamilyCheckoutBatch
        {
            FamilyGroupId = groupSeed.GroupId,
            PayerCustomerUserId = groupSeed.ManagerId,
            IdempotencyKey = "renewal:" + Guid.NewGuid().ToString("N"),
            Purpose = "Renewal",
            RenewalRequestId = renewal.Id,
            Status = FamilyCheckoutBatchStatus.PaymentPending,
            CurrencyCode = "KWD",
            Subtotal = 90m,
            FinalPrice = 90m,
            PaymentAnchorSubscriptionId = next.Id,
            GatewayPaymentId = paymentId,
            ExpiresAtUtc = new DateTime(2026, 8, 13, 9, 15, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc),
        };
        renewal.BatchId = batch.Id;
        renewal.BatchVersion = batch.Version;
        batch.Lines.Add(new FamilyCheckoutBatchLine
        {
            Batch = batch,
            FamilyMemberId = member.Id,
            SubscriptionId = next.Id,
            PreviousSubscriptionId = previous.Id,
            RenewalRequestId = renewal.Id,
            CheckoutSelected = true,
            ProgramId = next.ProgramId,
            BundleId = next.BundleId,
            Tier = next.SelectedTier,
            DurationDays = next.DurationDays,
            IncludeFriday = next.IncludeFriday,
            StartDate = next.StartDate,
            DeliveryAddressId = addressId,
            AddressLine = "Kuwait City",
            RegionId = Guid.NewGuid(),
            ResolvedRestaurantId = Guid.NewGuid(),
            MenuPlanVersionsJson = "[]",
            PriceBeforeFamilyDiscount = 90m,
            NetPaidAmount = 90m,
            CurrencyCode = "KWD",
            CreatedAtUtc = batch.CreatedAtUtc,
        });
        var attempt = new FamilyCheckoutPaymentAttempt
        {
            Batch = batch,
            AttemptKey = paymentId,
            GatewayPaymentId = paymentId,
            GatewayPaymentUrl = "https://pay.example/renewal",
            ResultCode = "Initialized",
            RequestedAtUtc = batch.CreatedAtUtc,
            CreatedAtUtc = batch.CreatedAtUtc,
        };
        batch.PaymentAttempts.Add(attempt);
        db.SubscriptionRenewals.Add(renewal);
        db.FamilyCheckoutBatches.Add(batch);
        await db.SaveChangesAsync();
        return new RenewalSeed(groupSeed.GroupId, member.Id, previous.Id, next.Id, renewal.Id, paymentId);
    }

    private sealed record RenewalSeed(Guid GroupId, Guid MemberId, Guid PreviousSubscriptionId,
        Guid NewSubscriptionId, Guid RenewalId, string PaymentId);

    private (Guid ProgramId, Guid BundleId) _catalog;

    private async Task<Guid> SeedPendingSubscriptionAsync(Guid familyGroupId, Guid managerId, Guid? forcedId = null)
    {
        if (_catalog == default) _catalog = await SeedCatalogAsync();
        await using var db = CreateDb();
        var serviceDate = new DateOnly(2026, 8, 22);
        var menuVersionId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var subscription = new Subscription
        {
            CustomerUserId = managerId,
            ProgramId = _catalog.ProgramId,
            BundleId = _catalog.BundleId,
            SelectedTier = RestaurantTier.Basic,
            DurationDays = 1,
            StartDate = serviceDate,
            EndDate = serviceDate,
            Status = SubscriptionStatus.PendingPayment,
            PaidAmount = new Money(90m, "KWD"),
            FamilyGroupId = familyGroupId,
        };
        if (forcedId.HasValue) db.Entry(subscription).Property(x => x.Id).CurrentValue = forcedId.Value;
        var subscriptionId = subscription.Id;
        var pool = new SubscriptionRestaurantPoolSnapshot { SubscriptionId = subscriptionId, RestaurantCount = 1, EligibilityCheckedAtUtc = DateTime.UtcNow };
        var restaurant = new SubscriptionRestaurantSnapshot
        {
            PoolSnapshotId = pool.Id, SubscriptionId = subscriptionId, RestaurantId = restaurantId,
            ProgramId = _catalog.ProgramId, BundleId = _catalog.BundleId, RegionId = Guid.NewGuid(), Tier = RestaurantTier.Basic,
            NameAr = "مطعم", NameEn = "Restaurant", IsInitiallySelected = true, OfferingSnapshotJson = "{}", CapturedAtUtc = DateTime.UtcNow
        };
        db.Subscriptions.Add(subscription);
        db.SubscriptionRestaurantPoolSnapshots.Add(pool);
        db.SubscriptionRestaurantSnapshots.Add(restaurant);
        db.SubscriptionRestaurantDaySnapshots.Add(new SubscriptionRestaurantDaySnapshot
        {
            RestaurantSnapshotId = restaurant.Id, SubscriptionId = subscriptionId, RestaurantId = restaurantId,
            ServiceDate = serviceDate, MenuPlanVersionId = menuVersionId, SlotRequirementsJson = "[]", MealsJson = "[]",
            ContentHash = new string('a', 64), CapturedAtUtc = DateTime.UtcNow
        });
        db.CapacityHolds.Add(new CapacityHold
        {
            SubscriptionId = subscriptionId, RestaurantId = restaurantId, ServiceDate = serviceDate,
            MenuPlanVersionId = menuVersionId, Quantity = 1, Status = CapacityHoldStatus.PendingPayment,
            ExpiresAtUtc = new DateTime(2026, 8, 13, 9, 15, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
        return subscriptionId;
    }

    private async Task<Guid> SeedBatchAsync(
        Guid familyGroupId,
        Guid managerId,
        FamilyCheckoutBatchStatus status,
        string gatewayPaymentId,
        params Guid[] subscriptionIds)
    {
        await using var db = CreateDb();
        var memberIds = new List<Guid>();
        foreach (var _ in subscriptionIds)
        {
            var member = new FamilyMember
            {
                FamilyGroupId = familyGroupId,
                Role = FamilyMemberRole.Member,
                DisplayName = "Member",
                RelationshipCode = "child",
                NutritionalGoalCode = "maintain",
            };
            db.FamilyMembers.Add(member);
            memberIds.Add(member.Id);
        }
        await db.SaveChangesAsync();

        var batch = new FamilyCheckoutBatch
        {
            FamilyGroupId = familyGroupId,
            PayerCustomerUserId = managerId,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            Status = status,
            CurrencyCode = "KWD",
            Subtotal = 180m,
            FamilyDiscountAmount = 0m,
            FinalPrice = 180m,
            PaymentAnchorSubscriptionId = subscriptionIds.FirstOrDefault(),
            GatewayPaymentId = status == FamilyCheckoutBatchStatus.PaymentPending ? gatewayPaymentId : null,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30),
        };
        db.FamilyCheckoutBatches.Add(batch);
        for (var index = 0; index < subscriptionIds.Length; index++)
        {
            var subscriptionId = subscriptionIds[index];
            db.FamilyCheckoutBatchLines.Add(new FamilyCheckoutBatchLine
            {
                BatchId = batch.Id,
                FamilyMemberId = memberIds[index],
                SubscriptionId = subscriptionId,
                CheckoutSelected = true,
                ProgramId = Guid.NewGuid(),
                BundleId = Guid.NewGuid(),
                StartDate = new DateOnly(2026, 8, 22),
                AddressLine = "Kuwait City",
                PriceBeforeFamilyDiscount = 180m / subscriptionIds.Length,
                NetPaidAmount = 180m / subscriptionIds.Length,
                CurrencyCode = "KWD",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30),
            });
        }
        db.FamilyCheckoutPaymentAttempts.Add(new FamilyCheckoutPaymentAttempt
        {
            BatchId = batch.Id,
            AttemptKey = "checkout",
            GatewayPaymentId = gatewayPaymentId,
            RequestedAtUtc = DateTime.UtcNow.AddMinutes(-30),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30),
        });
        await db.SaveChangesAsync();
        return batch.Id;
    }

    private AppDbContext CreateDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options,
            Substitute.For<IDomainEventDispatcher>());
}
