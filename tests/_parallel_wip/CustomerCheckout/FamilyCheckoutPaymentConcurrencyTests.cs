using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Application.Features.RestaurantOperations.Availability;
using MealMate.Persistence;
using MealMate.Persistence.CustomerCheckout;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit.Sdk;

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
        var gateway = Substitute.For<IMyFatoorahPaymentService>();
        gateway.GetPaymentStatusAsync(paymentId, "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new MyFatoorahPaymentStatusResponse(invoiceId, "Paid", amount, "Customer", "c@example.com", "50000000",
                "TX-" + paymentId, "KNET", now, familyGroupId.ToString(), "KWD", paymentId));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        fulfillment.TryFulfillAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FulfillmentResult(false, false, "fulfillment.skipped_in_test"));
        var processor = new SubscriptionPaymentProcessor(db, gateway, new PostgresCheckoutTransactionExecutor(db),
            new PostgresCapacityConcurrencyLock(db), new SubscriptionScheduleService(clock), fulfillment, clock);
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
            db.FamilyCheckoutPaymentAttempts.Add(new FamilyCheckoutPaymentAttempt
            {
                BatchId = batchId,
                AttemptKey = "retry-key",
                RequestedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            });
            await db.SaveChangesAsync();
        }

        var (first, second) = await RunResumeTwiceAsync(group.GroupId, managerId, anchor, "retry-key");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.PaymentUrl, second.Value.PaymentUrl);
        Assert.Equal(1, _gatewayCalls);

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
            db.FamilyCheckoutPaymentAttempts.Add(new FamilyCheckoutPaymentAttempt
            {
                BatchId = batchId,
                AttemptKey = "race-key",
                RequestedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            });
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
            var payments = Substitute.For<IMyFatoorahPaymentService>();
            payments.ExecutePaymentAsync(
                    Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns($"https://pay.example/invoice?paymentId=PAY-RACE-{callIndex}");
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

        await using var verify = CreateDb();
        var attempts = await verify.FamilyCheckoutPaymentAttempts.AsNoTracking()
            .Where(x => x.BatchId == batchId && x.AttemptKey == "race-key").ToListAsync();
        Assert.Single(attempts);
        Assert.NotNull(attempts.Single().GatewayPaymentId);
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
            var payments = Substitute.For<IMyFatoorahPaymentService>();
            payments.ExecutePaymentAsync(
                    Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    Interlocked.Increment(ref _gatewayCalls);
                    return $"https://pay.example/invoice?paymentId=PAY-REUSE-{_gatewayCalls}";
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
        var gateway = Substitute.For<IMyFatoorahPaymentService>();
        gateway.GetPaymentStatusAsync(paymentId, "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new MyFatoorahPaymentStatusResponse(invoiceId, invoiceStatus, amount, "Customer", "c@example.com", "50000000",
                "TX-" + paymentId, "KNET", now, familyGroupId.ToString(), "KWD", responsePaymentId));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        fulfillment.TryFulfillAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FulfillmentResult(false, false, "fulfillment.skipped_in_test"));
        var processor = new SubscriptionPaymentProcessor(db, gateway, new PostgresCheckoutTransactionExecutor(db),
            new PostgresCapacityConcurrencyLock(db), new SubscriptionScheduleService(clock), fulfillment, clock);
        return await processor.VerifyAndProcessAsync(subscriptionId, paymentId);
    }

    private async Task<PaymentProcessingResult> ProcessFailureAsync(Guid subscriptionId, Guid familyGroupId, string paymentId)
    {
        await using var db = CreateDb();
        var clock = Substitute.For<IDateTimeProvider>();
        var now = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);
        clock.UtcNow.Returns(now);
        var invoiceId = "INV-" + paymentId;
        var gateway = Substitute.For<IMyFatoorahPaymentService>();
        gateway.GetPaymentStatusAsync(paymentId, "PaymentId", Arg.Any<CancellationToken>()).Returns(
            new MyFatoorahPaymentStatusResponse(invoiceId, "Failed", 180m, "Customer", "c@example.com", "50000000",
                "TX-" + paymentId, "KNET", now, familyGroupId.ToString(), "KWD", paymentId));
        var fulfillment = Substitute.For<ISubscriptionFulfillmentService>();
        var processor = new SubscriptionPaymentProcessor(db, gateway, new PostgresCheckoutTransactionExecutor(db),
            new PostgresCapacityConcurrencyLock(db), new SubscriptionScheduleService(clock), fulfillment, clock);
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

    private (Guid ProgramId, Guid BundleId) _catalog;

    private async Task<Guid> SeedPendingSubscriptionAsync(Guid familyGroupId, Guid managerId)
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
                PriceBeforeFamilyDiscount = 90m,
                NetPaidAmount = status == FamilyCheckoutBatchStatus.Paid ? 90m : 0m,
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
