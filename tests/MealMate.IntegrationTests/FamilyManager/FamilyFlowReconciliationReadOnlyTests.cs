using FluentAssertions;
using FamilyFlowReconciliation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.FamilyCheckout;
using MealMate.Application.Features.ProgramsSubscriptions;
using MealMate.Domain.CustomerFinance;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ProgramsSubscriptions;
using MealMate.Domain.CalendarMeals;
using MealMate.Domain.ValueObjects;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace MealMate.IntegrationTests.FamilyManager;

public sealed class FamilyFlowReconciliationReadOnlyTests : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await _container.StartAsync();
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
        await SeedEvidenceAsync(db);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Report_is_repeatable_read_only_and_rejects_direct_write()
    {
        var before = await ReadBusinessStateAsync();
        var first = await ReconciliationRunner.RunAsync(_container.GetConnectionString(), "testcontainer");
        var second = await ReconciliationRunner.RunAsync(_container.GetConnectionString(), "testcontainer");
        var after = await ReadBusinessStateAsync();

        first.ReportFingerprint.Should().Be(second.ReportFingerprint);
        first.BusinessDate.Should().Be(second.BusinessDate);
        first.ReadOnlyEnforced.Should().BeTrue();
        after.Should().Be(before);
        first.Groups.Should().Be(1);
        first.Members.Should().Be(4);
        first.SelectedUnlinked.Should().Be(1);
        first.PaidBatches.Should().Be(2);
        first.PaidBatchesNeedingReview.Should().Be(0);
        first.PaidReadyWithoutDays.Should().Be(3);
        first.PaidSubscriptionsMissingBatchEvidence.Should().Be(1);
        first.SuspiciousOpenConfirmations.Should().Be(1);
        first.ProfilePurchaseAddressDifferences.Should().Be(1);
        first.CancellationReviews.Should().Be(1);
        first.Findings.Should().Contain(x => x.Code == "selected_member_account_missing");
        first.Findings.Should().Contain(x => x.Code == "paid_ready_member_without_service_days");
        first.Findings.Should().Contain(x => x.Code == "paid_preparing_calendar_not_materialized"
            && x.Evidence == "future-paid-preparing-no-days-yet-informational");
        first.Findings.Should().Contain(x => x.Code == "family_paid_subscription_missing_immutable_batch_evidence"
            && x.Evidence == "payment-verified-or-paid-state-without-coherent-batch-payer-attempt-receipt-and-net-allocation");
        first.Findings.Should().Contain(x => x.Code == "profile_purchase_address_difference");

        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await readOnly.ExecuteNonQueryAsync();
        var write = new NpgsqlCommand("UPDATE \"FamilyGroups\" SET \"Name\" = \"Name\" WHERE false", connection, transaction);
        var attempt = async () => { await write.ExecuteNonQueryAsync(); };
        await attempt.Should().ThrowAsync<PostgresException>();
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Admin_history_keeps_batch_proof_when_canonical_line_is_outside_the_requested_parent_page()
    {
        Guid groupId;
        Guid selectedMemberId;
        Guid siblingMemberId;
        Guid paidBatchId;
        await using (var seedDb = CreateDb())
        {
            var batch = await seedDb.FamilyCheckoutBatches.Include(x => x.Lines)
                .SingleAsync(x => x.IdempotencyKey == "report-paid");
            var originalLine = batch.Lines.OrderBy(x => x.CreatedAtUtc).First();
            groupId = batch.FamilyGroupId;
            paidBatchId = batch.Id;
            selectedMemberId = originalLine.FamilyMemberId;
            siblingMemberId = batch.Lines.Single(x => x.FamilyMemberId != selectedMemberId).FamilyMemberId;
        }

        await using var db = CreateDb();
        var handlers = new AdminFamilyGroupHandlers(db, Substitute.For<IUserAccountService>(), Substitute.For<IDateTimeProvider>());
        var firstPage = await handlers.Handle(new GetAdminFamilyGroupHistoryQuery(groupId, Page: 1, PageSize: 1), default);
        firstPage.IsSuccess.Should().BeTrue();
        var historyRows = new List<AdminFamilyGroupHistoryRowDto>();
        for (var pageNumber = 1; pageNumber <= firstPage.Value!.TotalCount; pageNumber++)
        {
            var result = await handlers.Handle(new GetAdminFamilyGroupHistoryQuery(groupId, Page: pageNumber, PageSize: 1), default);
            result.IsSuccess.Should().BeTrue();
            historyRows.AddRange(result.Value!.Items);
        }
        var historicalRow = historyRows.Should().ContainSingle(x => x.MemberId == selectedMemberId && x.BatchId == paidBatchId).Which;
        historyRows.Should().ContainSingle(x => x.MemberId == siblingMemberId && x.BatchId == historicalRow.BatchId);
        historicalRow.MemberId.Should().Be(selectedMemberId);
        historicalRow.EvidenceStatus.Should().Be("VerifiedPaid");
        historicalRow.NetPaidAmount.Should().Be(20m);
        historicalRow.CurrencyCode.Should().Be("KWD");
        historicalRow.ReceiptCount.Should().Be(1);
        historicalRow.Receipts.Should().ContainSingle().Which.VerifiedAmount.Should().Be(20m);
    }

    [Fact]
    public async Task Cancellation_admin_feed_paginates_merged_sources_in_postgres_and_safely_handles_extreme_page()
    {
        await using (var seedDb = CreateDb())
        {
            var subscription = await seedDb.Subscriptions.FirstAsync(x => x.Status == SubscriptionStatus.Active);
            seedDb.SubscriptionOperations.Add(new SubscriptionOperation { SubscriptionId = subscription.Id,
                OperationType = "CANCELLATION_PROCESSED", NewStatus = SubscriptionStatus.Cancelled,
                PerformedByUserId = subscription.CustomerUserId, PerformedByUserType = UserType.Admin,
                TimestampUtc = DateTime.UtcNow.AddDays(1), Reason = "legacy fixture" });
            await seedDb.SaveChangesAsync();
        }

        await using var db = CreateDb();
        var users = Substitute.For<IUserAccountService>();
        users.GetUserIdentitiesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => ((IReadOnlyCollection<Guid>)call[0]!).Select(id => new UserIdentityDto(id, "Test actor", null, null)).ToList());
        var handler = new SubscriptionOperationsHandlers(db, Substitute.For<IAuditLogger>(), Substitute.For<IDateTimeProvider>(),
            Substitute.For<ISubscriptionTierUpgradeQuoteBuilder>(), users);

        var first = await handler.Handle(new GetSubscriptionCancellationRequestsQuery(1, 1), default);
        var second = await handler.Handle(new GetSubscriptionCancellationRequestsQuery(2, 1), default);
        var extreme = await handler.Handle(new GetSubscriptionCancellationRequestsQuery(int.MaxValue, 100), default);

        first.IsSuccess.Should().BeTrue();
        first.Value!.TotalCount.Should().Be(2);
        first.Value.Items.Should().ContainSingle().Which.IsFamilySettlement.Should().BeFalse();
        second.Value!.Items.Should().ContainSingle().Which.IsFamilySettlement.Should().BeTrue();
        extreme.Value!.TotalCount.Should().Be(2);
        extreme.Value.Items.Should().BeEmpty();
    }

    private async Task<string> ReadBusinessStateAsync()
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        var tables = new[] { "FamilyGroups", "FamilyMembers", "FamilyMemberAccesses", "Subscriptions", "FamilyCheckoutBatches",
            "FamilyCheckoutBatchLines", "FamilyCheckoutPaymentAttempts", "PaymentWebhookReceipts", "SubscriptionDays",
            "SubscriptionCancellations", "RefundCalculations", "CustomerCashWallets", "WalletTransactions", "CustomerAddresses",
            "FamilySubscriptionDeliveryAmendments" };
        var evidence = new List<string>();
        foreach (var table in tables)
        {
            await using var command = new NpgsqlCommand($"SELECT count(*)::text || '|' || md5(coalesce(string_agg(to_jsonb(t)::text, E'\\n' ORDER BY to_jsonb(t)::text), '')) FROM \"{table}\" t", connection);
            evidence.Add($"{table}:{Convert.ToString(await command.ExecuteScalarAsync())}");
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join("\n", evidence))));
    }

    private static async Task SeedEvidenceAsync(AppDbContext db)
    {
        var manager = Guid.NewGuid();
        var profileAddress = new MealMate.Domain.IdentityAccess.CustomerAddress { UserId = manager, Label = "Current",
            AddressLine = "Current profile line", RegionId = Guid.NewGuid() };
        var purchaseAddress = new MealMate.Domain.IdentityAccess.CustomerAddress { UserId = manager, Label = "Purchase",
            AddressLine = "Frozen purchase line", RegionId = Guid.NewGuid() };
        var program = new MealMate.Domain.ProgramsSubscriptions.Program { Code = "report-only", NameAr = "خطة", NameEn = "Plan", IsActive = true };
        var bundle = new Bundle { ProgramId = program.Id, Code = "report-only", NameAr = "باقة", NameEn = "Bundle", MealsPerDay = 1 };
        var group = new FamilyGroup { ManagerCustomerUserId = manager, Name = "Report-only", Status = FamilyGroupStatus.Active };
        var selected = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Member, DisplayName = "Selected",
            RelationshipCode = "child", NutritionalGoalCode = "balanced", SetupStatus = FamilyMemberSetupStatus.Ready,
            DeliveryAddressId = profileAddress.Id };
        var serviceMember = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Member, DisplayName = "Service",
            RelationshipCode = "child", NutritionalGoalCode = "balanced", SetupStatus = FamilyMemberSetupStatus.Ready };
        var preparingMember = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Member, DisplayName = "Preparing",
            RelationshipCode = "child", NutritionalGoalCode = "balanced", SetupStatus = FamilyMemberSetupStatus.Ready };
        var orphanMember = new FamilyMember { FamilyGroupId = group.Id, Role = FamilyMemberRole.Member, DisplayName = "Orphan paid timestamp",
            RelationshipCode = "child", NutritionalGoalCode = "balanced", SetupStatus = FamilyMemberSetupStatus.Ready };
        group.Members.Add(selected);
        group.Members.Add(serviceMember);
        group.Members.Add(preparingMember);
        group.Members.Add(orphanMember);
        var pending = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = manager,
            IdempotencyKey = "report-pending", Status = FamilyCheckoutBatchStatus.PaymentPending,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5) };
        pending.Lines.Add(new FamilyCheckoutBatchLine { BatchId = pending.Id, FamilyMemberId = selected.Id,
            CheckoutSelected = true, CreatedAtUtc = pending.CreatedAtUtc });
        var paid = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = manager,
            IdempotencyKey = "report-paid", Status = FamilyCheckoutBatchStatus.Paid, FinalPrice = 20m,
            Subtotal = 20m, CurrencyCode = "KWD", GatewayPaymentId = "report-payment", GatewayInvoiceId = "report-invoice",
            PaidAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow };
        paid.Lines.Add(new FamilyCheckoutBatchLine { BatchId = paid.Id, FamilyMemberId = selected.Id,
            CheckoutSelected = true, PriceBeforeFamilyDiscount = 20m, NetPaidAmount = 20m, CurrencyCode = "KWD",
            DeliveryAddressId = purchaseAddress.Id, AddressLine = purchaseAddress.AddressLine, CreatedAtUtc = paid.CreatedAtUtc });
        paid.Lines.Add(new FamilyCheckoutBatchLine { BatchId = paid.Id, FamilyMemberId = serviceMember.Id,
            CheckoutSelected = true, PriceBeforeFamilyDiscount = 0m, NetPaidAmount = 0m, CurrencyCode = "KWD",
            CreatedAtUtc = paid.CreatedAtUtc.AddMinutes(1) });
        paid.PaymentAttempts.Add(new FamilyCheckoutPaymentAttempt { BatchId = paid.Id, AttemptKey = "report-attempt",
            GatewayPaymentId = paid.GatewayPaymentId, GatewayInvoiceId = paid.GatewayInvoiceId, ResultCode = "Paid",
            VerifiedAtUtc = paid.PaidAtUtc, RequestedAtUtc = paid.CreatedAtUtc, CreatedAtUtc = paid.CreatedAtUtc });
        var paidReceipt = new PaymentWebhookReceipt { Gateway = "ReportFixture", EventKey = "report-payment",
            PaymentId = paid.GatewayPaymentId, InvoiceId = paid.GatewayInvoiceId, Amount = 20m, CurrencyCode = "KWD",
            Status = "FulfillmentCompleted", VerifiedAtUtc = paid.PaidAtUtc, ProcessedAtUtc = paid.PaidAtUtc!.Value };
        var paidReady = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = serviceMember.Id, Status = SubscriptionStatus.Active,
            PaymentVerifiedAtUtc = DateTime.UtcNow, FulfillmentCompletedAtUtc = DateTime.UtcNow, PaidAmount = new Money(20m, "KWD"),
            StartDate = new DateOnly(2026, 10, 20), EndDate = new DateOnly(2026, 11, 20), DurationDays = 26 };
        paid.Lines.Single(x => x.FamilyMemberId == serviceMember.Id).SubscriptionId = paidReady.Id;
        var futurePreparing = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = preparingMember.Id, Status = SubscriptionStatus.Paid,
            PaymentVerifiedAtUtc = DateTime.UtcNow, PaidAmount = new Money(20m, "KWD"),
            StartDate = new DateOnly(2026, 10, 20), EndDate = new DateOnly(2026, 11, 20), DurationDays = 26 };
        var orphanPaid = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = orphanMember.Id, Status = SubscriptionStatus.Paid,
            PaymentVerifiedAtUtc = DateTime.UtcNow, PaidAmount = new Money(20m, "KWD"),
            StartDate = new DateOnly(2026, 10, 21), EndDate = new DateOnly(2026, 11, 21), DurationDays = 26 };
        var openDaySubscription = new Subscription { CustomerUserId = manager, ProgramId = program.Id, BundleId = bundle.Id,
            FamilyGroupId = group.Id, FamilyMemberId = selected.Id, Status = SubscriptionStatus.Active,
            PaymentVerifiedAtUtc = DateTime.UtcNow, PaidAmount = new Money(20m, "KWD"),
            StartDate = new DateOnly(2026, 10, 20), EndDate = new DateOnly(2026, 11, 20), DurationDays = 26 };
        paid.Lines.Single(x => x.FamilyMemberId == selected.Id).SubscriptionId = openDaySubscription.Id;
        var preparingBatch = new FamilyCheckoutBatch { FamilyGroupId = group.Id, PayerCustomerUserId = manager,
            IdempotencyKey = "report-future-preparing", Status = FamilyCheckoutBatchStatus.Paid, FinalPrice = 20m,
            Subtotal = 20m, CurrencyCode = "KWD", GatewayPaymentId = "report-future-payment", GatewayInvoiceId = "report-future-invoice",
            PaidAtUtc = DateTime.UtcNow, CreatedAtUtc = DateTime.UtcNow };
        preparingBatch.Lines.Add(new FamilyCheckoutBatchLine { BatchId = preparingBatch.Id, FamilyMemberId = preparingMember.Id,
            SubscriptionId = futurePreparing.Id, CheckoutSelected = true, PriceBeforeFamilyDiscount = 20m,
            NetPaidAmount = 20m, CurrencyCode = "KWD", CreatedAtUtc = preparingBatch.CreatedAtUtc });
        preparingBatch.PaymentAttempts.Add(new FamilyCheckoutPaymentAttempt { BatchId = preparingBatch.Id, AttemptKey = "report-future-attempt",
            GatewayPaymentId = preparingBatch.GatewayPaymentId, GatewayInvoiceId = preparingBatch.GatewayInvoiceId,
            ResultCode = "Paid", VerifiedAtUtc = preparingBatch.PaidAtUtc, RequestedAtUtc = preparingBatch.CreatedAtUtc,
            CreatedAtUtc = preparingBatch.CreatedAtUtc });
        var preparingReceipt = new PaymentWebhookReceipt { Gateway = "ReportFixture", EventKey = "report-future-payment",
            PaymentId = preparingBatch.GatewayPaymentId, InvoiceId = preparingBatch.GatewayInvoiceId, Amount = 20m,
            CurrencyCode = "KWD", Status = "PaymentVerified", VerifiedAtUtc = preparingBatch.PaidAtUtc,
            ProcessedAtUtc = preparingBatch.PaidAtUtc!.Value };
        var day = new SubscriptionDay { SubscriptionId = openDaySubscription.Id, ServiceDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5),
            Status = SubscriptionDayStatus.Open, ConfirmedAtUtc = DateTime.UtcNow };
        var cancellation = new SubscriptionCancellation { SubscriptionId = openDaySubscription.Id, CustomerUserId = manager,
            FamilyGroupId = group.Id, SettlementKind = "FamilyCancellation", Status = "ReviewRequired",
            RequestedAtUtc = DateTime.UtcNow, PayerCustomerUserId = manager, ActorUserId = manager };
        cancellation.RefundCalculations.Add(new RefundCalculation { CancellationId = cancellation.Id,
            RefundEligiblePaid = new Money(20m, "KWD"), UnusedCashValue = new Money(10m, "KWD"),
            CancellationFee = new Money(1m, "KWD"), FinalCashRefund = new Money(9m, "KWD"), CalculatedAtUtc = DateTime.UtcNow });
        var wallet = new CustomerCashWallet { CustomerUserId = manager, Balance = new Money(9m, "KWD"), IsActive = true };
        var walletTransaction = new WalletTransaction { WalletId = wallet.Id, WalletType = WalletType.Cash, CustomerUserId = manager,
            Type = WalletTransactionType.Refund, Amount = new Money(9m, "KWD"), BalanceAfter = wallet.Balance,
            OccurredAtUtc = DateTime.UtcNow, ReferenceType = "report-fixture" };
        var amendment = new FamilySubscriptionDeliveryAmendment { SubscriptionId = openDaySubscription.Id, Version = 1,
            ActorUserId = manager, EffectiveFromDate = day.ServiceDate, AddressId = purchaseAddress.Id,
            AddressLabel = purchaseAddress.Label, AddressLine = purchaseAddress.AddressLine, RegionId = purchaseAddress.RegionId!.Value,
            AffectedDayIdsJson = "[]", CreatedAtUtc = DateTime.UtcNow };
        db.AddRange(program, bundle, group, profileAddress, purchaseAddress, pending, paid, paidReceipt, paidReady, futurePreparing,
            orphanPaid, preparingBatch, preparingReceipt,
            openDaySubscription, day, cancellation, wallet, walletTransaction, amendment,
            new FamilyMemberAccess { FamilyMemberId = selected.Id, HasSeparateAccount = false }, preparingMember, orphanMember);
        await db.SaveChangesAsync();
    }

    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString(),
            npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name)).Options;
        return new AppDbContext(options, new NoOpDomainEventDispatcher());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
