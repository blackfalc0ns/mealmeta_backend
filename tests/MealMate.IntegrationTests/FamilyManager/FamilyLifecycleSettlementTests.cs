using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.AdminDashboard.OperationalPolicies;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.CustomerCheckout;
using MealMate.Application.Features.ProgramsSubscriptions.Cancellation;
using MealMate.Application.Features.ProgramsSubscriptions.Upgrade;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.AdminDashboard;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.RestaurantOperations;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;
using System.Text.Json;

namespace MealMate.IntegrationTests.FamilyManager;

public sealed class FamilyLifecycleSettlementTests : IAsyncLifetime
{
    private static readonly DateTime UtcNow = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await _container.StartAsync();
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Customer_cancellation_racing_manager_removal_creates_one_payer_credit_and_preserves_locked_day()
    {
        var seed = await SeedPaidFamilyAsync();
        await using var cancelDb = CreateDb();
        await using var removeDb = CreateDb();
        var cancelService = CreateService(cancelDb);
        var removeService = CreateService(removeDb);

        var quote = await cancelService.QuoteAsync(seed.SubscriptionId, seed.ManagerId, UserType.Customer, CancellationToken.None);
        Assert.True(quote.IsSuccess, quote.IsFailure ? quote.Error.Code : null);
        var cancelTask = cancelService.CancelAsync(seed.SubscriptionId, seed.ManagerId, UserType.Customer, "Customer cancellation", false, CancellationToken.None);
        var removeTask = removeService.RemoveAsync(seed.MemberId, seed.ManagerId, CancellationToken.None);
        await Task.WhenAll(cancelTask, removeTask);

        Assert.True(cancelTask.Result.IsSuccess, cancelTask.Result.IsFailure ? cancelTask.Result.Error.Code : null);
        Assert.True(removeTask.Result.IsSuccess, removeTask.Result.IsFailure ? removeTask.Result.Error.Code : null);
        Assert.Equal(quote.Value.NetRefundAmount, cancelTask.Result.Value.NetRefundAmount);
        Assert.Equal(seed.PayerId, Assert.Single(cancelTask.Result.Value.Beneficiaries!).PayerCustomerUserId);

        await using var read = CreateDb();
        var settlement = await read.SubscriptionCancellations.SingleAsync(x => x.SubscriptionId == seed.SubscriptionId && x.SettlementKind == "FamilyCancellation");
        Assert.Equal("Settled", settlement.Status);
        Assert.Equal(seed.ManagerId, settlement.ActorUserId);
        Assert.Equal(seed.PayerId, settlement.PayerCustomerUserId);
        Assert.Equal(seed.BatchId, settlement.BatchId);
        Assert.Single(await read.WalletTransactions.Where(x => x.ReferenceType == "FamilyCancellationRefund" && x.ReferenceId == settlement.Id).ToListAsync());
        Assert.Equal(quote.Value.NetRefundAmount, (await read.CustomerCashWallets.SingleAsync(x => x.CustomerUserId == seed.PayerId)).Balance.Amount);
        Assert.NotNull((await read.FamilyMemberAccesses.SingleAsync(x => x.FamilyMemberId == seed.MemberId)).RemovedAtUtc);
        Assert.Equal(SubscriptionDayStatus.Locked72h, (await read.SubscriptionDays.SingleAsync(x => x.SubscriptionId == seed.SubscriptionId && x.ServiceDate == seed.ServiceDates[0])).Status);
        Assert.Equal(SubscriptionDayStatus.Cancelled, (await read.SubscriptionDays.SingleAsync(x => x.SubscriptionId == seed.SubscriptionId && x.ServiceDate == seed.ServiceDates[1])).Status);
        Assert.Equal(CapacityHoldStatus.PaymentConfirmed, (await read.CapacityHolds.SingleAsync(x => x.SubscriptionId == seed.SubscriptionId && x.ServiceDate == seed.ServiceDates[0])).Status);
        Assert.Equal(CapacityHoldStatus.Released, (await read.CapacityHolds.SingleAsync(x => x.SubscriptionId == seed.SubscriptionId && x.ServiceDate == seed.ServiceDates[1])).Status);
    }

    [Fact]
    public async Task Concurrent_families_for_same_absent_wallet_sum_credits_for_the_actual_payer()
    {
        var sharedPayer = Guid.NewGuid();
        var first = await SeedPaidFamilyAsync(sharedPayer);
        var second = await SeedPaidFamilyAsync(sharedPayer);
        await using var firstDb = CreateDb();
        await using var secondDb = CreateDb();
        var firstService = CreateService(firstDb);
        var secondService = CreateService(secondDb);

        var results = await Task.WhenAll(
            firstService.CancelAsync(first.SubscriptionId, first.ManagerId, UserType.Customer, null, false, CancellationToken.None),
            secondService.CancelAsync(second.SubscriptionId, second.ManagerId, UserType.Customer, null, false, CancellationToken.None));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null));
        var expected = results.Sum(x => x.Value.NetRefundAmount);
        await using var read = CreateDb();
        Assert.Equal(expected, (await read.CustomerCashWallets.SingleAsync(x => x.CustomerUserId == sharedPayer)).Balance.Amount);
        Assert.Equal(2, await read.WalletTransactions.CountAsync(x => x.ReferenceType == "FamilyCancellationRefund" && x.CustomerUserId == sharedPayer));
        Assert.Equal(2, await read.SubscriptionCancellations.CountAsync(x => x.SettlementKind == "FamilyCancellation" && x.PayerCustomerUserId == sharedPayer));
    }

    [Fact]
    public async Task Fractional_fee_allocation_credits_exact_net_across_real_payers()
    {
        var seed = await SeedPaidFamilyAsync();
        var payers = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).Append(seed.PayerId).Distinct().ToArray();
        await using (var arrange = CreateDb())
        {
            var subscription = await arrange.Subscriptions.SingleAsync(x => x.Id == seed.SubscriptionId);
            subscription.Status = SubscriptionStatus.Paid;
            subscription.StartDate = new DateOnly(2026, 11, 1);
            subscription.EndDate = new DateOnly(2026, 11, 2);
            subscription.PaidAmount = new Money(0.001m, "KWD");
            subscription.FulfillmentAttemptCount = 1;
            subscription.LastFulfillmentAttemptUtc = UtcNow.AddHours(-1);
            subscription.LastFulfillmentErrorCode = "fulfillment.transient_calendar_error";
            arrange.SubscriptionDays.RemoveRange(await arrange.SubscriptionDays.Where(x => x.SubscriptionId == seed.SubscriptionId).ToListAsync());

            var batch = await arrange.FamilyCheckoutBatches.Include(x => x.Lines).Include(x => x.PaymentAttempts)
                .SingleAsync(x => x.Id == seed.BatchId);
            batch.Subtotal = batch.FinalPrice = 0.001m;
            batch.FamilyDiscountAmount = 0m;
            batch.Lines.Single().PriceBeforeFamilyDiscount = batch.Lines.Single().NetPaidAmount = 0.001m;
            batch.Lines.Single().FamilyDiscountAllocated = 0m;
            var receipt = await arrange.PaymentWebhookReceipts.SingleAsync(x => x.PaymentId == batch.GatewayPaymentId);
            receipt.Amount = 0.001m;
            receipt.Status = "FulfillmentRetryPending";
            receipt.FulfillmentAttemptCount = 1;
            receipt.LastFulfillmentAttemptUtc = subscription.LastFulfillmentAttemptUtc;
            receipt.LastFulfillmentErrorCode = subscription.LastFulfillmentErrorCode;
            arrange.SubscriptionOperations.Add(new SubscriptionOperation
            {
                SubscriptionId = subscription.Id,
                OperationType = "FULFILLMENT_RETRY_SCHEDULED",
                PreviousStatus = SubscriptionStatus.Paid,
                NewStatus = SubscriptionStatus.Paid,
                TimestampUtc = subscription.LastFulfillmentAttemptUtc!.Value,
            });

            var serviceDatesJson = JsonSerializer.Serialize(seed.ServiceDates);
            foreach (var payer in payers.Where(x => x != seed.PayerId))
            {
                var paymentId = $"upgrade-{Guid.NewGuid():N}";
                var invoiceId = $"invoice-{Guid.NewGuid():N}";
                arrange.SubscriptionTierUpgrades.Add(new SubscriptionTierUpgrade
                {
                    SubscriptionId = subscription.Id,
                    FromTier = RestaurantTier.Basic,
                    ToTier = RestaurantTier.Platinum,
                    PriceDifference = new Money(0.001m, "KWD"),
                    QuotedTotalDue = new Money(0.001m, "KWD"),
                    QuotedRemainingDays = seed.ServiceDates.Count,
                    EffectiveServiceDatesJson = serviceDatesJson,
                    Status = VerificationStatus.Approved,
                    PaymentCreationState = "Ready",
                    PaymentId = paymentId,
                    PayerCustomerUserId = payer,
                    VerifiedPaidAmount = 0.001m,
                    VerifiedCurrencyCode = "KWD",
                    VerifiedInvoiceId = invoiceId,
                    VerifiedPaidAtUtc = UtcNow,
                });
                arrange.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt
                {
                    Gateway = "Payzah", EventKey = paymentId, PaymentId = paymentId, SubscriptionId = subscription.Id,
                    InvoiceId = invoiceId, Amount = 0.001m, CurrencyCode = "KWD", CustomerReference = subscription.Id.ToString(),
                    Status = "PaymentVerified", VerifiedAtUtc = UtcNow,
                });
            }

            var policy = new OperationalPolicySettings(new CutoffPolicySettings(),
                new CancellationPolicySettings(CancellationFeeType.FixedAmount, 0.002m), new FreezePolicySettings());
            arrange.SystemSettings.Add(new SystemSetting
            {
                Key = OperationalPolicyService.SettingKey,
                ValueJson = JsonSerializer.Serialize(policy, OperationalPolicyRules.JsonOptions),
            });
            await arrange.SaveChangesAsync();
        }

        await using var settle = CreateDb();
        var service = CreateService(settle);
        var quote = await service.QuoteAsync(seed.SubscriptionId, seed.ManagerId, UserType.Customer, CancellationToken.None);
        Assert.True(quote.IsSuccess, quote.IsFailure ? quote.Error.Code : null);
        Assert.Equal(0.002m, quote.Value.CancellationFeeAmount);
        Assert.Equal(0.003m, quote.Value.NetRefundAmount);
        Assert.All(quote.Value.Beneficiaries!, x => Assert.InRange(x.RefundAmount, 0m, 0.001m));
        Assert.Equal(0.003m, quote.Value.Beneficiaries!.Sum(x => x.RefundAmount));

        var cancelled = await service.CancelAsync(seed.SubscriptionId, seed.ManagerId, UserType.Customer,
            "Fractional KWD settlement", false, CancellationToken.None);
        Assert.True(cancelled.IsSuccess, cancelled.IsFailure ? cancelled.Error.Code : null);
        Assert.Equal(0.003m, cancelled.Value.NetRefundAmount);

        await using var read = CreateDb();
        var preparing = await read.Subscriptions.AsNoTracking().SingleAsync(x => x.Id == seed.SubscriptionId);
        var basePaymentId = await read.FamilyCheckoutBatches.AsNoTracking().Where(x => x.Id == seed.BatchId)
            .Select(x => x.GatewayPaymentId).SingleAsync();
        var preparingReceipt = await read.PaymentWebhookReceipts.AsNoTracking().SingleAsync(x => x.PaymentId == basePaymentId);
        Assert.Equal(SubscriptionStatus.Paid, preparing.Status);
        Assert.True(preparing.StartDate > new DateOnly(2026, 10, 9));
        Assert.Equal(1, preparing.FulfillmentAttemptCount);
        Assert.Null(preparing.FulfillmentCompletedAtUtc);
        Assert.Equal("FulfillmentRetryPending", preparingReceipt.Status);
        Assert.Equal(preparing.FulfillmentAttemptCount, preparingReceipt.FulfillmentAttemptCount);
        Assert.Equal(preparing.LastFulfillmentAttemptUtc, preparingReceipt.LastFulfillmentAttemptUtc);
        Assert.Equal(preparing.LastFulfillmentErrorCode, preparingReceipt.LastFulfillmentErrorCode);
        Assert.Null(preparingReceipt.FulfillmentCompletedAtUtc);
        var walletRows = await read.WalletTransactions.Where(x => x.ReferenceType == "FamilyCancellationRefund"
            && x.ReferenceId == cancelled.Value.SettlementId).ToListAsync();
        Assert.Equal(0.003m, walletRows.Sum(x => x.Amount.Amount));
        Assert.All(walletRows, x => Assert.InRange(x.Amount.Amount, 0m, 0.001m));
        Assert.Equal(0.003m, (await read.CustomerCashWallets.Where(x => payers.Contains(x.CustomerUserId))
            .ToListAsync()).Sum(x => x.Balance.Amount));
    }

    [Fact]
    public async Task Active_subscription_without_service_days_is_reviewed_without_batch_or_wallet_mutation()
    {
        var seed = await SeedPaidFamilyAsync();
        await using (var arrange = CreateDb())
        {
            var days = await arrange.SubscriptionDays.Where(x => x.SubscriptionId == seed.SubscriptionId).ToListAsync();
            arrange.SubscriptionDays.RemoveRange(days);
            await arrange.SaveChangesAsync();
        }

        await using var db = CreateDb();
        var result = await CreateService(db).CancelAsync(seed.SubscriptionId, seed.ManagerId, UserType.Customer,
            "Contradictory no-day state", false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("family.financial_review_required", result.Error.Code);
        await using var read = CreateDb();
        var review = await read.SubscriptionCancellations.SingleAsync(x => x.SubscriptionId == seed.SubscriptionId
            && x.SettlementKind == "FamilyCancellation");
        Assert.Equal("ReviewRequired", review.Status);
        Assert.Equal(SubscriptionStatus.Active, await read.Subscriptions.Where(x => x.Id == seed.SubscriptionId)
            .Select(x => x.Status).SingleAsync());
        Assert.Equal(FamilyCheckoutBatchStatus.Paid, await read.FamilyCheckoutBatches.Where(x => x.Id == seed.BatchId)
            .Select(x => x.Status).SingleAsync());
        Assert.Empty(await read.WalletTransactions.Where(x => x.ReferenceType == "FamilyCancellationRefund").ToListAsync());
        Assert.Empty(await read.SubscriptionDays.Where(x => x.SubscriptionId == seed.SubscriptionId).ToListAsync());
    }

    [Fact]
    public async Task Foreign_actor_cannot_change_existing_no_day_review_or_paid_history()
    {
        var seed = await SeedPaidFamilyAsync();
        await using (var arrange = CreateDb())
        {
            arrange.SubscriptionDays.RemoveRange(await arrange.SubscriptionDays.Where(x => x.SubscriptionId == seed.SubscriptionId).ToListAsync());
            var subscription = await arrange.Subscriptions.SingleAsync(x => x.Id == seed.SubscriptionId);
            subscription.Status = SubscriptionStatus.Active;
            await arrange.SaveChangesAsync();
        }

        await using (var managerDb = CreateDb())
        {
            var review = await CreateService(managerDb).CancelAsync(seed.SubscriptionId, seed.ManagerId, UserType.Customer,
                "Missing service evidence", false, CancellationToken.None);
            Assert.True(review.IsFailure);
            Assert.Equal("family.financial_review_required", review.Error.Code);
        }
        await using (var outsiderDb = CreateDb())
        {
            var foreign = await CreateService(outsiderDb).CancelAsync(seed.SubscriptionId, Guid.NewGuid(), UserType.Customer,
                "Unauthorized review mutation", false, CancellationToken.None);
            Assert.True(foreign.IsFailure);
            Assert.Equal("Forbidden", foreign.Error.Type.ToString());
        }

        await using var read = CreateDb();
        var reviewRow = await read.SubscriptionCancellations.SingleAsync(x => x.SubscriptionId == seed.SubscriptionId
            && x.SettlementKind == "FamilyCancellation");
        Assert.Equal("ReviewRequired", reviewRow.Status);
        Assert.Equal(seed.ManagerId, reviewRow.ActorUserId);
        Assert.Equal(SubscriptionStatus.Active, await read.Subscriptions.Where(x => x.Id == seed.SubscriptionId)
            .Select(x => x.Status).SingleAsync());
        Assert.Equal(FamilyCheckoutBatchStatus.Paid, await read.FamilyCheckoutBatches.Where(x => x.Id == seed.BatchId)
            .Select(x => x.Status).SingleAsync());
        Assert.Empty(await read.WalletTransactions.Where(x => x.ReferenceType == "FamilyCancellationRefund").ToListAsync());
        Assert.Empty(await read.SubscriptionDays.Where(x => x.SubscriptionId == seed.SubscriptionId).ToListAsync());
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Expired")]
    [InlineData("FulfillmentReceipt")]
    [InlineData("FulfillmentTimestamp")]
    public async Task Contradictory_no_day_evidence_is_reviewed_without_mutating_paid_history(string caseName)
    {
        var seed = await SeedPaidFamilyAsync();
        SubscriptionStatus originalStatus;
        await using (var arrange = CreateDb())
        {
            var subscription = await arrange.Subscriptions.SingleAsync(x => x.Id == seed.SubscriptionId);
            arrange.SubscriptionDays.RemoveRange(await arrange.SubscriptionDays.Where(x => x.SubscriptionId == seed.SubscriptionId).ToListAsync());
            if (caseName == "Completed") subscription.Status = SubscriptionStatus.Completed;
            else if (caseName == "Expired") subscription.Status = SubscriptionStatus.Expired;
            else subscription.Status = SubscriptionStatus.Paid;
            if (caseName == "FulfillmentTimestamp")
                subscription.FulfillmentCompletedAtUtc = UtcNow.AddHours(-1);
            if (caseName == "FulfillmentReceipt")
            {
                var completedReceipt = await arrange.PaymentWebhookReceipts.SingleAsync(x => x.PaymentId ==
                    arrange.FamilyCheckoutBatches.Where(b => b.Id == seed.BatchId).Select(b => b.GatewayPaymentId).Single());
                completedReceipt.Status = "FulfillmentCompleted";
                completedReceipt.FulfillmentCompletedAtUtc = UtcNow.AddHours(-1);
            }
            originalStatus = subscription.Status;
            await arrange.SaveChangesAsync();
        }

        await using var db = CreateDb();
        var result = await CreateService(db).CancelAsync(seed.SubscriptionId, seed.ManagerId, UserType.Customer,
            $"Contradictory no-day {caseName}", false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("family.financial_review_required", result.Error.Code);
        await using var read = CreateDb();
        Assert.Equal("ReviewRequired", await read.SubscriptionCancellations.Where(x => x.SubscriptionId == seed.SubscriptionId
            && x.SettlementKind == "FamilyCancellation").Select(x => x.Status).SingleAsync());
        Assert.Equal(originalStatus, await read.Subscriptions.Where(x => x.Id == seed.SubscriptionId).Select(x => x.Status).SingleAsync());
        Assert.Equal(FamilyCheckoutBatchStatus.Paid, await read.FamilyCheckoutBatches.Where(x => x.Id == seed.BatchId)
            .Select(x => x.Status).SingleAsync());
        Assert.Equal(0, await read.SubscriptionDays.CountAsync(x => x.SubscriptionId == seed.SubscriptionId));
        Assert.Equal(2, await read.CapacityHolds.CountAsync(x => x.SubscriptionId == seed.SubscriptionId
            && x.Status == CapacityHoldStatus.PaymentConfirmed));
        Assert.Empty(await read.WalletTransactions.Where(x => x.ReferenceType == "FamilyCancellationRefund").ToListAsync());
        Assert.Empty(await read.SubscriptionOperations.Where(x => x.SubscriptionId == seed.SubscriptionId).ToListAsync());
        var receipt = await read.PaymentWebhookReceipts.SingleAsync(x => x.PaymentId ==
            read.FamilyCheckoutBatches.Where(b => b.Id == seed.BatchId).Select(b => b.GatewayPaymentId).Single());
        if (caseName == "FulfillmentReceipt")
        {
            Assert.Equal("FulfillmentCompleted", receipt.Status);
            Assert.NotNull(receipt.FulfillmentCompletedAtUtc);
        }
        else if (caseName == "FulfillmentTimestamp")
        {
            Assert.Equal("PaymentVerified", receipt.Status);
            Assert.NotNull((await read.Subscriptions.SingleAsync(x => x.Id == seed.SubscriptionId)).FulfillmentCompletedAtUtc);
        }
        else Assert.Equal("PaymentVerified", receipt.Status);
    }

    [Fact]
    public async Task Removal_with_null_current_pointer_is_blocked_by_selected_pending_batch()
    {
        var seed = await SeedPendingFamilyAsync();
        await using var db = CreateDb();

        var result = await CreateService(db).RemoveAsync(seed.MemberId, seed.ManagerId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("family.payment_in_flight", result.Error.Code);
        await using var read = CreateDb();
        Assert.Null((await read.FamilyMemberAccesses.SingleAsync(x => x.FamilyMemberId == seed.MemberId)).RemovedAtUtc);
        Assert.Equal(FamilyCheckoutBatchStatus.PaymentPending, (await read.FamilyCheckoutBatches.SingleAsync(x => x.Id == seed.BatchId)).Status);
    }

    [Fact]
    public async Task Upgrade_idempotency_index_is_filtered_and_schema_migration_is_applied()
    {
        await using var db = CreateDb();
        var indexDefinition = await db.Database.SqlQueryRaw<string>(
            "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'SubscriptionTierUpgrades' AND indexname = 'IX_SubscriptionTierUpgrades_SubscriptionId_RequestIdempotencyK~'")
            .SingleAsync();
        Assert.Contains("UNIQUE", indexDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", indexDefinition, StringComparison.OrdinalIgnoreCase);
        Assert.True(await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS \"Value\" FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261009093618_AddFamilyUpgradePaymentReservation'")
            .SingleAsync() == 1);
    }

    [Fact]
    public async Task Family_expiration_preserves_inclusive_last_service_day_then_expires_on_next_Kuwait_day()
    {
        var seed = await SeedPaidFamilyAsync();
        var currentDate = new DateOnly(2026, 10, 9);
        var upcomingDate = currentDate.AddDays(1);
        Guid upcomingId;
        await using (var arrange = CreateDb())
        {
            var previous = await arrange.Subscriptions.SingleAsync(x => x.Id == seed.SubscriptionId);
            var member = await arrange.FamilyMembers.SingleAsync(x => x.Id == seed.MemberId);
            previous.StartDate = currentDate;
            previous.EndDate = currentDate;
            previous.DurationDays = 1;
            var oldDays = await arrange.SubscriptionDays.Where(x => x.SubscriptionId == previous.Id).ToListAsync();
            var oldHolds = await arrange.CapacityHolds.Where(x => x.SubscriptionId == previous.Id).ToListAsync();
            arrange.SubscriptionDays.RemoveRange(oldDays);
            arrange.CapacityHolds.RemoveRange(oldHolds);
            arrange.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = previous.Id, ServiceDate = currentDate,
                Status = SubscriptionDayStatus.Open, SelectionDeadlineUtc = UtcNow.AddDays(1) });
            var upcoming = new Subscription { CustomerUserId = previous.CustomerUserId, FamilyGroupId = seed.GroupId,
                FamilyMemberId = seed.MemberId, ProgramId = previous.ProgramId, BundleId = previous.BundleId,
                SelectedTier = previous.SelectedTier, DurationDays = 1, StartDate = upcomingDate, EndDate = upcomingDate,
                Status = SubscriptionStatus.Scheduled, PaidAmount = new Money(90m, "KWD"), PaymentVerifiedAtUtc = UtcNow };
            upcomingId = upcoming.Id;
            arrange.Subscriptions.Add(upcoming);
            arrange.SubscriptionDays.Add(new SubscriptionDay { SubscriptionId = upcoming.Id, ServiceDate = upcomingDate,
                Status = SubscriptionDayStatus.Open, SelectionDeadlineUtc = UtcNow.AddDays(2) });
            var paidBatch = new FamilyCheckoutBatch { FamilyGroupId = seed.GroupId, PayerCustomerUserId = seed.PayerId,
                IdempotencyKey = "paid-upcoming-" + Guid.NewGuid().ToString("N"), Status = FamilyCheckoutBatchStatus.Paid,
                CurrencyCode = "KWD", Subtotal = 90m, FinalPrice = 90m, PaidAtUtc = UtcNow, CreatedAtUtc = UtcNow };
            paidBatch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = paidBatch.Id, Batch = paidBatch,
                FamilyMemberId = seed.MemberId, SubscriptionId = upcoming.Id, CheckoutSelected = true,
                StartDate = upcomingDate, PriceBeforeFamilyDiscount = 90m, NetPaidAmount = 90m, CurrencyCode = "KWD" });
            arrange.FamilyCheckoutBatches.Add(paidBatch);
            arrange.FamilyCheckoutBatchLines.AddRange(paidBatch.Lines);
            member.MemberSubscriptionId = upcoming.Id;
            await arrange.SaveChangesAsync();
        }

        var beforeBoundaryUtc = new DateTime(2026, 10, 9, 20, 59, 0, DateTimeKind.Utc);
        await using (var processDb = CreateDb())
        {
            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(beforeBoundaryUtc);
            var handler = new SubscriptionOperationsHandlers(processDb, Substitute.For<IAuditLogger>(), clock,
                Substitute.For<ISubscriptionTierUpgradeQuoteBuilder>(), transactions: new PostgresApplicationTransactionExecutor(processDb),
                locks: new PostgresApplicationLockManager(processDb));
            var result = await handler.Handle(new ProcessSubscriptionRenewalsAndExpirationsCommand(), default);
            Assert.True(result.IsSuccess);
            Assert.Equal(0, result.Value.ExpiredCount);
        }

        await using (var sameDay = CreateDb())
        {
            Assert.Equal(SubscriptionStatus.Active, await sameDay.Subscriptions.Where(x => x.Id == seed.SubscriptionId)
                .Select(x => x.Status).SingleAsync());
            var projection = await new FamilyServiceSubscriptionResolver(sameDay).ResolveAsync(
                [seed.MemberId], new Dictionary<Guid, Guid?> { [seed.MemberId] = upcomingId }, currentDate);
            Assert.Equal(seed.SubscriptionId, projection[seed.MemberId].CurrentId);
            Assert.Equal(upcomingId, projection[seed.MemberId].UpcomingId);
        }

        var nextKuwaitDayUtc = new DateTime(2026, 10, 9, 21, 1, 0, DateTimeKind.Utc);
        await using (var processDb = CreateDb())
        {
            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(nextKuwaitDayUtc);
            var handler = new SubscriptionOperationsHandlers(processDb, Substitute.For<IAuditLogger>(), clock,
                Substitute.For<ISubscriptionTierUpgradeQuoteBuilder>(), transactions: new PostgresApplicationTransactionExecutor(processDb),
                locks: new PostgresApplicationLockManager(processDb));
            var result = await handler.Handle(new ProcessSubscriptionRenewalsAndExpirationsCommand(), default);
            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value.ExpiredCount);
        }

        await using var verify = CreateDb();
        Assert.Equal(1, await verify.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS \"Value\" FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261009112945_AddAutoRenewalConsentActor'")
            .SingleAsync());
        Assert.Equal(2, await verify.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS \"Value\" FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'Subscriptions' AND column_name IN ('AutoRenewConsentActorUserId', 'AutoRenewConsentWithdrawnByUserId')")
            .SingleAsync());
        Assert.Equal(SubscriptionStatus.Expired, await verify.Subscriptions.Where(x => x.Id == seed.SubscriptionId)
            .Select(x => x.Status).SingleAsync());
        Assert.Equal(SubscriptionStatus.Scheduled, await verify.Subscriptions.Where(x => x.Id == upcomingId)
            .Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await verify.SubscriptionDays.CountAsync(x => x.SubscriptionId == upcomingId && x.ServiceDate == upcomingDate));
        Assert.Equal(90m, await verify.FamilyCheckoutBatchLines.Where(x => x.SubscriptionId == upcomingId)
            .Select(x => x.NetPaidAmount).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_same_key_upgrade_uses_one_provider_call_and_replays_typed_link()
    {
        var seed = await SeedPaidFamilyAsync();
        var enteredProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource<PaymentCreationReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = Substitute.For<IPaymentGatewayService>();
        var calls = 0;
        gateway.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => { Interlocked.Increment(ref calls); enteredProvider.TrySetResult(); return releaseProvider.Task; });
        await using var firstDb = CreateDb();
        await using var secondDb = CreateDb();
        var request = new CustomerUpgradeRequest(RestaurantTier.Platinum, "https://app/callback", "https://app/error", IdempotencyKey: "upgrade-key-1", ClientContractVersion: 1);
        var firstTask = CreateUpgradeService(firstDb, gateway).RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId, request);
        await enteredProvider.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var concurrent = await CreateUpgradeService(secondDb, gateway).RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId, request);
        Assert.True(concurrent.IsFailure);
        Assert.Equal("upgrade.payment_creation_review_required", concurrent.Error.Code);
        releaseProvider.SetResult(new PaymentCreationReceipt("provider-id", "https://pay/provider-url", "typed-track-handle"));
        var started = await firstTask;
        Assert.True(started.IsSuccess, started.IsFailure ? started.Error.Code : null);
        Assert.Equal("https://pay/provider-url", started.Value.PaymentUrl);
        Assert.Equal(1, calls);

        await using var replayDb = CreateDb();
        var replay = await CreateUpgradeService(replayDb, gateway,
            new FamilyInvoiceOptions { StopNewFamilyInvoices = true, MinimumContractVersion = 1 })
            .RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId, request with { ClientContractVersion = null });
        Assert.True(replay.IsSuccess, replay.IsFailure ? replay.Error.Code : null);
        Assert.Equal(started.Value.UpgradeId, replay.Value.UpgradeId);
        Assert.Equal(started.Value.PaymentUrl, replay.Value.PaymentUrl);
        Assert.Equal(1, calls);
        var persisted = await replayDb.SubscriptionTierUpgrades.SingleAsync(x => x.Id == started.Value.UpgradeId);
        Assert.Equal("pz-typed-track-handle", persisted.PaymentId);
        Assert.Equal("Ready", persisted.PaymentCreationState);
        Assert.Equal(new DateOnly(2026, 11, 2), persisted.EffectiveServiceDate);

        persisted.Status = VerificationStatus.Approved;
        persisted.EffectiveAtUtc = UtcNow;
        var upgradedSubscription = await replayDb.Subscriptions.SingleAsync(x => x.Id == seed.SubscriptionId);
        upgradedSubscription.SelectedTier = RestaurantTier.Platinum;
        upgradedSubscription.Status = SubscriptionStatus.Cancelled;
        await replayDb.SaveChangesAsync();
        await using var historicalReplayDb = CreateDb();
        var historicalReplay = await CreateUpgradeService(historicalReplayDb, gateway)
            .RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId, request);
        Assert.True(historicalReplay.IsSuccess, historicalReplay.IsFailure ? historicalReplay.Error.Code : null);
        Assert.True(historicalReplay.Value.IsPaid);
        Assert.Equal(started.Value.UpgradeId, historicalReplay.Value.UpgradeId);
        Assert.Equal("https://pay/provider-url", historicalReplay.Value.PaymentUrl);
        Assert.Equal(1, calls);

        await using var mismatchDb = CreateDb();
        var mismatch = await CreateUpgradeService(mismatchDb, gateway).RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId,
            request with { CallbackUrl = "https://app/changed-callback" });
        Assert.True(mismatch.IsFailure);
        Assert.Equal("upgrade.idempotency_key_mismatch", mismatch.Error.Code);
    }

    [Fact]
    public async Task Uncertain_family_upgrade_creation_keeps_reservation_and_blocks_retry_invoice()
    {
        var seed = await SeedPaidFamilyAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        var calls = 0;
        gateway.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => { Interlocked.Increment(ref calls); return Task.FromException<PaymentCreationReceipt>(new TimeoutException("provider outcome unavailable")); });
        await using var firstDb = CreateDb();
        var request = new CustomerUpgradeRequest(RestaurantTier.Platinum, "https://app/callback", "https://app/error", IdempotencyKey: "upgrade-timeout-1", ClientContractVersion: 1);
        var first = await CreateUpgradeService(firstDb, gateway).RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId, request);
        Assert.True(first.IsFailure);
        Assert.Equal("upgrade.payment_creation_review_required", first.Error.Code);
        await using var retryDb = CreateDb();
        var retry = await CreateUpgradeService(retryDb, gateway).RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId, request);
        Assert.True(retry.IsFailure);
        Assert.Equal("upgrade.payment_creation_review_required", retry.Error.Code);
        Assert.Equal(1, calls);
        var persisted = await retryDb.SubscriptionTierUpgrades.SingleAsync(x => x.SubscriptionId == seed.SubscriptionId
            && x.RequestIdempotencyKey == request.IdempotencyKey);
        Assert.Equal(VerificationStatus.Submitted, persisted.Status);
        Assert.Equal("ReviewRequired", persisted.PaymentCreationState);
        Assert.Null(persisted.PaymentId);
        Assert.Equal(RestaurantTier.Basic, (await retryDb.Subscriptions.SingleAsync(x => x.Id == seed.SubscriptionId)).SelectedTier);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_or_removal_during_provider_creation_never_returns_payable_link(bool removeMember)
    {
        var seed = await SeedPaidFamilyAsync();
        var enteredProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource<PaymentCreationReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = Substitute.For<IPaymentGatewayService>();
        gateway.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => { enteredProvider.TrySetResult(); return releaseProvider.Task; });
        await using var requestDb = CreateDb();
        var request = new CustomerUpgradeRequest(RestaurantTier.Platinum, "https://app/callback", "https://app/error", IdempotencyKey: $"upgrade-during-{removeMember}", ClientContractVersion: 1);
        var requestTask = CreateUpgradeService(requestDb, gateway).RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId, request);
        await enteredProvider.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using var lifecycleDb = CreateDb();
        var lifecycle = CreateService(lifecycleDb);
        if (removeMember)
        {
            var removed = await lifecycle.RemoveAsync(seed.MemberId, seed.ManagerId, CancellationToken.None);
            Assert.True(removed.IsSuccess, removed.IsFailure ? removed.Error.Code : null);
        }
        else
        {
            var cancelled = await lifecycle.CancelAsync(seed.SubscriptionId, seed.ManagerId, UserType.Customer,
                "Cancel while upgrade provider is creating", false, CancellationToken.None);
            Assert.True(cancelled.IsSuccess, cancelled.IsFailure ? cancelled.Error.Code : null);
        }

        releaseProvider.SetResult(new PaymentCreationReceipt("provider-id", "https://pay/provider-url", "delayed-track-handle"));
        var started = await requestTask;
        Assert.True(started.IsFailure);
        Assert.Equal("upgrade.payment_creation_review_required", started.Error.Code);
        await using var read = CreateDb();
        var upgrade = await read.SubscriptionTierUpgrades.SingleAsync(x => x.SubscriptionId == seed.SubscriptionId
            && x.RequestIdempotencyKey == request.IdempotencyKey);
        Assert.Equal(VerificationStatus.Submitted, upgrade.Status);
        Assert.Equal("ReviewRequired", upgrade.PaymentCreationState);
        Assert.Equal("pz-delayed-track-handle", upgrade.PaymentId);
        Assert.Equal("https://pay/provider-url", upgrade.PaymentUrl);
        Assert.Equal(RestaurantTier.Basic, (await read.Subscriptions.SingleAsync(x => x.Id == seed.SubscriptionId)).SelectedTier);
    }

    [Fact]
    public async Task Ready_family_upgrade_link_is_hidden_and_reviewed_after_cancellation_before_retry()
    {
        var seed = await SeedPaidFamilyAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        var calls = 0;
        gateway.ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new PaymentCreationReceipt("provider-id", "https://pay/ready-url", "ready-track-handle"));
            });
        var request = new CustomerUpgradeRequest(RestaurantTier.Platinum, "https://app/callback", "https://app/error", IdempotencyKey: "ready-then-cancelled", ClientContractVersion: 1);
        await using var startDb = CreateDb();
        var started = await CreateUpgradeService(startDb, gateway).RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId, request);
        Assert.True(started.IsSuccess, started.IsFailure ? started.Error.Code : null);

        await using var cancelDb = CreateDb();
        var cancelled = await CreateService(cancelDb).CancelAsync(seed.SubscriptionId, seed.ManagerId, UserType.Customer,
            "Cancel before retrying upgrade", false, CancellationToken.None);
        Assert.True(cancelled.IsSuccess, cancelled.IsFailure ? cancelled.Error.Code : null);

        await using var retryDb = CreateDb();
        var retry = await CreateUpgradeService(retryDb, gateway).RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId, request);
        Assert.True(retry.IsFailure);
        Assert.Equal("upgrade.payment_creation_review_required", retry.Error.Code);
        Assert.Equal(1, calls);
        var upgrade = await retryDb.SubscriptionTierUpgrades.SingleAsync(x => x.Id == started.Value.UpgradeId);
        Assert.Equal("ReviewRequired", upgrade.PaymentCreationState);
        Assert.Equal("pz-ready-track-handle", upgrade.PaymentId);
        Assert.Equal("https://pay/ready-url", upgrade.PaymentUrl);
        Assert.Equal(SubscriptionStatus.CancellationPending, (await retryDb.Subscriptions.SingleAsync(x => x.Id == seed.SubscriptionId)).Status);
    }

    [Theory]
    [InlineData(true, 1, "family.invoice_creation_stopped")]
    [InlineData(false, null, "family.client_update_required")]
    [InlineData(false, 0, "family.client_update_required")]
    public async Task New_family_upgrade_policy_or_contract_gate_prevents_reservation(
        bool stopNew, int? clientVersion, string expectedCode)
    {
        var seed = await SeedPaidFamilyAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        var request = new CustomerUpgradeRequest(RestaurantTier.Platinum, "https://app/callback", "https://app/error",
            IdempotencyKey: $"blocked-upgrade-{Guid.NewGuid():N}", ClientContractVersion: clientVersion);
        await using var db = CreateDb();

        var result = await CreateUpgradeService(db, gateway,
            new FamilyInvoiceOptions { StopNewFamilyInvoices = stopNew, MinimumContractVersion = 1 })
            .RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId, request);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        await gateway.DidNotReceive().ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await using var verify = CreateDb();
        Assert.Empty(await verify.SubscriptionTierUpgrades.Where(x => x.SubscriptionId == seed.SubscriptionId).ToListAsync());
        Assert.Empty(await verify.SubscriptionOperations.Where(x => x.SubscriptionId == seed.SubscriptionId
            && x.OperationType == "UPGRADE_PAID").ToListAsync());
    }

    [Fact]
    public async Task Family_upgrade_currency_mismatch_fails_before_initializing_reservation()
    {
        var seed = await SeedPaidFamilyAsync();
        var gateway = Substitute.For<IPaymentGatewayService>();
        var request = new CustomerUpgradeRequest(RestaurantTier.Platinum, "https://app/callback", "https://app/error",
            IdempotencyKey: "upgrade-currency-mismatch", ClientContractVersion: 1);
        await using var db = CreateDb();

        var result = await CreateUpgradeService(db, gateway, methodCurrency: "USD")
            .RequestUpgradeAsync(seed.SubscriptionId, seed.ManagerId, request);

        Assert.True(result.IsFailure);
        Assert.Equal("family.payment_currency_unsupported", result.Error.Code);
        await gateway.DidNotReceive().ExecutePaymentAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await using var verify = CreateDb();
        Assert.Empty(await verify.SubscriptionTierUpgrades.Where(x => x.SubscriptionId == seed.SubscriptionId).ToListAsync());
        Assert.Empty(await verify.SubscriptionOperations.Where(x => x.SubscriptionId == seed.SubscriptionId
            && x.OperationType == "UPGRADE_PAID").ToListAsync());
    }

    private sealed record Seed(Guid GroupId, Guid MemberId, Guid SubscriptionId, Guid ManagerId, Guid PayerId,
        Guid BatchId, IReadOnlyList<DateOnly> ServiceDates);

    private async Task<Seed> SeedPaidFamilyAsync(Guid? payerId = null)
    {
        var manager = Guid.NewGuid();
        var payer = payerId ?? Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = manager, Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Member", RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        var dates = new[] { new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2) };
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = payer, IdempotencyKey = $"purchase-{Guid.NewGuid():N}",
            Status = FamilyCheckoutBatchStatus.Paid, Subtotal = 100m, FamilyDiscountAmount = 20m, FinalPrice = 80m, CurrencyCode = "KWD",
            GatewayPaymentId = $"payment-{Guid.NewGuid():N}", GatewayInvoiceId = $"invoice-{Guid.NewGuid():N}", PaidAtUtc = UtcNow };
        await using var db = CreateDb();
        var program = new Program
        {
            Code = "P-" + Guid.NewGuid().ToString("N")[..8], NameAr = "برنامج", NameEn = "Program",
            Status = ProgramStatus.Active, IsActive = true,
        };
        var bundle = new Bundle
        {
            ProgramId = program.Id, Program = program, Code = "B-" + Guid.NewGuid().ToString("N")[..8],
            NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1, LunchCount = 1,
            Status = ProgramStatus.Active, IsActive = true,
        };
        db.Programs.Add(program);
        db.Bundles.Add(bundle);
        await db.SaveChangesAsync();
        var subscription = new Subscription { CustomerUserId = Guid.NewGuid(), FamilyGroupId = group.Id, FamilyMemberId = member.Id,
            Status = SubscriptionStatus.Active, SelectedTier = RestaurantTier.Basic, DurationDays = dates.Length, IncludeFriday = true,
            ProgramId = program.Id, BundleId = bundle.Id,
            StartDate = dates[0], EndDate = dates[^1], PaidAmount = new Money(80m, "KWD") };
        batch.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = member.Id, SubscriptionId = subscription.Id, CheckoutSelected = true,
            PriceBeforeFamilyDiscount = 100m, FamilyDiscountAllocated = 20m, NetPaidAmount = 80m, CurrencyCode = "KWD", DurationDays = dates.Length, StartDate = dates[0] });
        batch.PaymentAttempts.Add(new FamilyCheckoutPaymentAttempt { AttemptKey = batch.IdempotencyKey, GatewayPaymentId = batch.GatewayPaymentId,
            GatewayInvoiceId = batch.GatewayInvoiceId, VerifiedAtUtc = UtcNow, ResultCode = "Paid" });
        db.FamilyGroups.Add(group); db.FamilyMembers.Add(member); db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        member.MemberSubscriptionId = subscription.Id;
        await db.SaveChangesAsync();
        db.FamilyCheckoutBatches.Add(batch);
        db.FamilyMemberAccesses.Add(new FamilyMemberAccess { FamilyMemberId = member.Id, HasSeparateAccount = true, LinkedCustomerUserId = Guid.NewGuid() });
        db.PaymentWebhookReceipts.Add(new PaymentWebhookReceipt { Gateway = "Payzah", EventKey = batch.GatewayPaymentId!, PaymentId = batch.GatewayPaymentId,
            SubscriptionId = subscription.Id, InvoiceId = batch.GatewayInvoiceId, Amount = batch.FinalPrice, CurrencyCode = batch.CurrencyCode,
            CustomerReference = group.Id.ToString(), Status = "PaymentVerified", VerifiedAtUtc = UtcNow });
        db.SubscriptionDays.AddRange(
            new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = dates[0], Status = SubscriptionDayStatus.Locked72h,
                LockAtUtc = UtcNow.AddHours(-1), SelectionDeadlineUtc = UtcNow.AddDays(1) },
            new SubscriptionDay { SubscriptionId = subscription.Id, ServiceDate = dates[1], Status = SubscriptionDayStatus.Open,
                SelectionDeadlineUtc = UtcNow.AddDays(1) });
        db.CapacityHolds.AddRange(
            new CapacityHold { SubscriptionId = subscription.Id, RestaurantId = Guid.NewGuid(), MenuPlanVersionId = Guid.NewGuid(),
                ServiceDate = dates[0], Status = CapacityHoldStatus.PaymentConfirmed, ExpiresAtUtc = UtcNow.AddDays(1) },
            new CapacityHold { SubscriptionId = subscription.Id, RestaurantId = Guid.NewGuid(), MenuPlanVersionId = Guid.NewGuid(),
                ServiceDate = dates[1], Status = CapacityHoldStatus.PaymentConfirmed, ExpiresAtUtc = UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        return new Seed(group.Id, member.Id, subscription.Id, manager, payer, batch.Id, dates);
    }

    private async Task<Seed> SeedPendingFamilyAsync()
    {
        var manager = Guid.NewGuid();
        var group = new FamilyGroup { Name = "Family", ManagerCustomerUserId = manager, Status = FamilyGroupStatus.Active };
        var member = new FamilyMember { FamilyGroupId = group.Id, DisplayName = "Member", RelationshipCode = "child", NutritionalGoalCode = "maintain" };
        var batch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = manager, IdempotencyKey = "pending-removal",
            Status = FamilyCheckoutBatchStatus.PaymentPending, Subtotal = 100m, FamilyDiscountAmount = 0m, FinalPrice = 100m, CurrencyCode = "KWD" };
        batch.Lines.Add(new FamilyCheckoutBatchLine { FamilyMemberId = member.Id, CheckoutSelected = true, PriceBeforeFamilyDiscount = 100m,
            FamilyDiscountAllocated = 0m, NetPaidAmount = 100m, CurrencyCode = "KWD" });
        await using var db = CreateDb();
        db.FamilyGroups.Add(group); db.FamilyMembers.Add(member); db.FamilyCheckoutBatches.Add(batch);
        db.FamilyMemberAccesses.Add(new FamilyMemberAccess { FamilyMemberId = member.Id, HasSeparateAccount = true, LinkedCustomerUserId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        return new Seed(group.Id, member.Id, Guid.Empty, manager, manager, batch.Id, []);
    }

    private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options,
        Substitute.For<IDomainEventDispatcher>());

    private FamilyCancellationSettlement CreateService(AppDbContext db)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(UtcNow);
        clock.TodayUtc.Returns(new DateOnly(2026, 10, 9));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var policy = new OperationalPolicyService(db, cache, Substitute.For<IAuditLogger>(), clock);
        return new FamilyCancellationSettlement(db, clock, new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db),
            new FamilySubscriptionAccessService(db, new FamilyActionPolicy(db)), NullLocalizedMessageProvider.Instance, policy);
    }

    private SubscriptionUpgradeService CreateUpgradeService(AppDbContext db, IPaymentGatewayService gateway,
        FamilyInvoiceOptions? invoiceOptions = null, string methodCurrency = "KWD")
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(UtcNow);
        clock.TodayUtc.Returns(new DateOnly(2026, 10, 9));
        var quoteBuilder = Substitute.For<ISubscriptionTierUpgradeQuoteBuilder>();
        quoteBuilder.BuildForTargetAsync(Arg.Any<Subscription>(), Arg.Any<RestaurantTier>(), Arg.Any<CancellationToken>())
            .Returns(call => new SubscriptionTierUpgradeOptionQuote(RestaurantTier.Platinum, "بلاتيني", "Platinum", null,
                false, true, 1, 0m, 100m, 0m, 25m, "KWD", 1, 2));
        gateway.GetPaymentMethodsAsync(Arg.Any<decimal>(), Arg.Any<CancellationToken>()).Returns(
            [new PaymentGatewayMethod(3, "PAYZAH", "دفع", "Payzah", null, null, methodCurrency, false)]);
        return new SubscriptionUpgradeService(db, clock, quoteBuilder, gateway,
            familyAccess: new FamilySubscriptionAccessService(db, new FamilyActionPolicy(db)),
            transactions: new PostgresApplicationTransactionExecutor(db), locks: new PostgresApplicationLockManager(db),
            invoiceOptions: invoiceOptions is null ? null : Microsoft.Extensions.Options.Options.Create(invoiceOptions));
    }
}
