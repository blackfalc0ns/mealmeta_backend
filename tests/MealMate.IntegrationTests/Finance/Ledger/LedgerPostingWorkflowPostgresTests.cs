using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.Finance.Ledger;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Testing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.IntegrationTests.Finance.Ledger;

public sealed class LedgerPostingWorkflowPostgresTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        _connectionString = _database.GetConnectionString();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task ApprovedPersistedSourcePostsOnceAcrossNewContextsAndUsesStableJsonbFingerprint()
    {
        var periodId = Guid.NewGuid();
        var priorPeriodId = Guid.NewGuid();
        await using (var setup = CreateContext())
        {
            await setup.Database.MigrateAsync();
            var entityId = Guid.NewGuid();
            setup.LegalEntities.Add(new LegalEntity
            {
                Id = entityId, LegalNameAr = "كيان اختبار", LegalNameEn = "Test entity",
                RegistrationNo = $"LEDGER-{entityId:N}", Currency = "KWD", Status = LegalEntityStatus.Active,
            });
            setup.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = periodId, LegalEntityId = entityId, StartDate = new DateOnly(2026, 1, 1),
                EndDateExclusive = new DateOnly(2027, 1, 1),
            });
            setup.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = priorPeriodId, LegalEntityId = entityId, StartDate = new DateOnly(2025, 1, 1),
                EndDateExclusive = new DateOnly(2026, 1, 1),
            });
            setup.ChartOfAccounts.AddRange(
                new ChartOfAccount { Id = Guid.NewGuid(), LegalEntityId = entityId, Code = "1000", NameAr = "أصل", NameEn = "Asset", AccountType = AccountType.Asset, NormalBalance = AccountNormalBalance.Debit, IsActive = true, IsPostable = true },
                new ChartOfAccount { Id = Guid.NewGuid(), LegalEntityId = entityId, Code = "3000", NameAr = "حقوق", NameEn = "Equity", AccountType = AccountType.Equity, NormalBalance = AccountNormalBalance.Credit, IsActive = true, IsPostable = true });
            await setup.SaveChangesAsync();
            var accountIds = await setup.ChartOfAccounts.Where(x => x.LegalEntityId == entityId).OrderBy(x => x.Code).Select(x => x.Id).ToListAsync();
            var prior = new JournalEntry
            {
                Id = Guid.NewGuid(), EntryNumber = $"LJ-{Guid.NewGuid():N}", Description = "Prior period balance",
                LegalEntityId = entityId, AccountingPeriodId = priorPeriodId, BusinessDate = new DateOnly(2025, 10, 21),
                CurrencyCode = "KWD", PayloadHash = new string('b', 64),
                Lines = new List<JournalEntryLine>
                {
                    new() { Id = Guid.NewGuid(), LegalEntityId = entityId, AccountId = accountIds[0], Debit = new MealMate.Domain.ValueObjects.Money(7m, "KWD") },
                    new() { Id = Guid.NewGuid(), LegalEntityId = entityId, AccountId = accountIds[1], Credit = new MealMate.Domain.ValueObjects.Money(7m, "KWD") },
                },
            };
            setup.JournalEntries.Add(prior);
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"JournalEntries\" SET \"Status\" = 2, \"PostedAtUtc\" = now() WHERE \"Id\" = {prior.Id}");
        }

        var makerId = Guid.NewGuid();
        var checkerId = Guid.NewGuid();
        LedgerSourceDto submitted;
        var accounts = await ReadAccountsAsync();
        accounts.PeriodId.Should().Be(periodId);
        await using (var makerDb = CreateContext())
        {
            var user = TestAdmin(makerId);
            var sourceService = CreateSourceService(makerDb, user);
            var draft = await sourceService.CreateDraftAsync(new LedgerSourceDraftRequest(accounts.EntityId,
                JournalSourceDocumentType.OpeningBalance, new DateOnly(2026, 10, 21), "Opening balance import",
                "evidence://test/opening-1", new[]
                {
                    new LedgerLineDraft(accounts.DebitId, 10m, null),
                    new LedgerLineDraft(accounts.CreditId, null, 10m),
                }));
            draft.IsSuccess.Should().BeTrue(draft.IsFailure ? draft.Error.Code : null);
            var submit = await sourceService.SubmitAsync(draft.Value.Id,
                new LedgerSourceSubmitRequest(draft.Value.Fingerprint, draft.Value.RowVersion));
            submit.IsSuccess.Should().BeTrue(submit.IsFailure ? submit.Error.Code : null);
            submitted = submit.Value;
        }

        await using (var checkerDb = CreateContext())
        {
            var checker = TestAdmin(checkerId);
            var approval = await CreateSourceService(checkerDb, checker).ApproveAsync(submitted.Id,
                new LedgerSourceDecisionRequest(submitted.Fingerprint, submitted.RowVersion));
            approval.IsSuccess.Should().BeTrue(approval.IsFailure ? approval.Error.Code : null);
            approval.Value.Fingerprint.Should().Be(submitted.Fingerprint);
        }

        var approvedFingerprint = submitted.Fingerprint;
        var failedDeliveryEventId = Guid.NewGuid();
        await InstallOutboxFailureTriggerAsync(enabled: true);
        await using (var failedPostingDb = CreateContext())
        {
            var act = async () => await CreatePostingService(failedPostingDb, TestWorker()).ProcessApprovedSourceDeliveryAsync(
                submitted.Id, failedDeliveryEventId, approvedFingerprint);
            await act.Should().ThrowAsync<DbUpdateException>();
        }
        await InstallOutboxFailureTriggerAsync(enabled: false);
        await using (var rollbackCheck = CreateContext())
        {
            (await rollbackCheck.JournalEntries.CountAsync(x => x.LegalEntityId == accounts.EntityId
                && x.AccountingPeriodId == periodId)).Should().Be(0);
            (await rollbackCheck.JournalSourceLinks.CountAsync(x => x.SourceDocumentId == submitted.Id)).Should().Be(0);
            (await rollbackCheck.PostingIdentities.CountAsync(x => x.LegalEntityId == accounts.EntityId)).Should().Be(0);
            (await rollbackCheck.InboxProcessedEvents.CountAsync(x => x.EventId == failedDeliveryEventId)).Should().Be(0);
            (await rollbackCheck.JournalSourceDocuments.Where(x => x.Id == submitted.Id).Select(x => x.Status).SingleAsync())
                .Should().Be(JournalSourceDocumentStatus.Approved);
        }

        var lockDb = CreateContext();
        await using (lockDb)
        await using (var closeTransaction = await lockDb.Database.BeginTransactionAsync())
        {
            await new PostgresApplicationLockManager(lockDb).LockLedgerPeriodAsync(periodId);
            var postingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var blockedPost = Task.Run(async () =>
            {
                postingStarted.SetResult();
                await using var competingDb = CreateContext();
                return await CreatePostingService(competingDb, TestWorker()).ProcessApprovedSourceDeliveryAsync(
                    submitted.Id, Guid.NewGuid(), approvedFingerprint);
            });
            await postingStarted.Task;
            await WaitForAdvisoryLockWaitAsync();
            var closeService = CreatePeriodService(lockDb, TestAdmin(Guid.NewGuid()));
            var pending = await closeService.RequestCloseAsync(periodId, new LedgerPeriodCommandRequest(0, "Prepare monthly close"));
            pending.IsSuccess.Should().BeTrue(pending.IsFailure ? pending.Error.Code : null);
            await closeTransaction.CommitAsync();
            var blockedResult = await blockedPost;
            blockedResult.IsFailure.Should().BeTrue();
            blockedResult.Error.Code.Should().Be("ledger.period_not_open");
        }
        await using (var reopenDb = CreateContext())
        {
            var reopened = await CreatePeriodService(reopenDb, TestAdmin(Guid.NewGuid())).CancelCloseAsync(periodId,
                new LedgerPeriodCommandRequest(1, "Cancel test close"));
            reopened.IsSuccess.Should().BeTrue(reopened.IsFailure ? reopened.Error.Code : null);
        }

        var concurrentEvents = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var concurrentDeliveries = await Task.WhenAll(concurrentEvents.Select(async deliveryEventId =>
        {
            await using var concurrentDb = CreateContext();
            return await CreatePostingService(concurrentDb, TestWorker()).ProcessApprovedSourceDeliveryAsync(
                submitted.Id, deliveryEventId, approvedFingerprint);
        }));
        concurrentDeliveries.Should().OnlyContain(x => x.IsSuccess);
        concurrentDeliveries.Count(x => !x.Value.WasReplay).Should().Be(1);
        var firstIndex = Array.FindIndex(concurrentDeliveries, x => !x.Value.WasReplay);
        var first = concurrentDeliveries[firstIndex].Value;
        var eventId = concurrentEvents[firstIndex];
        concurrentDeliveries.Should().OnlyContain(x => x.Value.JournalEntryId == first.JournalEntryId);

        await using (var replayDb = CreateContext())
        {
            // JSONB may reorder object properties and normalize numeric notation. The persisted fingerprint must survive.
            var worker = TestWorker();
            var replay = await CreatePostingService(replayDb, worker).ProcessApprovedSourceDeliveryAsync(
                submitted.Id, eventId, approvedFingerprint);
            replay.IsSuccess.Should().BeTrue(replay.IsFailure ? replay.Error.Code : null);
            replay.Value.JournalEntryId.Should().Be(first.JournalEntryId);
            replay.Value.WasReplay.Should().BeTrue();
        }
        LedgerSourceDto reversalSubmitted;
        var reversalMaker = TestAdmin(Guid.NewGuid());
        await using (var reversalMakerDb = CreateContext())
        {
            var reversalService = new JournalReversalService(CreateSourceService(reversalMakerDb, reversalMaker));
            var draft = await reversalService.CreateDraftAsync(first.JournalEntryId, new DateOnly(2026, 10, 22),
                "Correcting opening balance", "evidence://test/reversal-1", accounts.EntityId);
            draft.IsSuccess.Should().BeTrue(draft.IsFailure ? draft.Error.Code : null);
            draft.Value.Lines.Should().HaveCount(2);
            var submit = await CreateSourceService(reversalMakerDb, reversalMaker).SubmitAsync(draft.Value.Id,
                new LedgerSourceSubmitRequest(draft.Value.Fingerprint, draft.Value.RowVersion));
            submit.IsSuccess.Should().BeTrue(submit.IsFailure ? submit.Error.Code : null);
            reversalSubmitted = submit.Value;
        }
        await using (var reversalCheckerDb = CreateContext())
        {
            var approval = await CreateSourceService(reversalCheckerDb, TestAdmin(Guid.NewGuid())).ApproveAsync(
                reversalSubmitted.Id, new LedgerSourceDecisionRequest(reversalSubmitted.Fingerprint, reversalSubmitted.RowVersion));
            approval.IsSuccess.Should().BeTrue(approval.IsFailure ? approval.Error.Code : null);
        }
        Guid reversalJournalId;
        await using (var reversalPostingDb = CreateContext())
        {
            var posted = await CreatePostingService(reversalPostingDb, TestWorker()).ProcessApprovedSourceDeliveryAsync(
                reversalSubmitted.Id, Guid.NewGuid(), reversalSubmitted.Fingerprint);
            posted.IsSuccess.Should().BeTrue(posted.IsFailure ? posted.Error.Code : null);
            reversalJournalId = posted.Value.JournalEntryId;
        }

        await using var verify = CreateContext();
        (await verify.JournalEntries.CountAsync(x => x.LegalEntityId == accounts.EntityId && x.Status == JournalEntryStatus.Posted)).Should().Be(3);
        (await verify.JournalSourceLinks.CountAsync(x => x.SourceDocumentId == submitted.Id)).Should().Be(1);
        (await verify.PostingIdentities.CountAsync(x => x.LegalEntityId == accounts.EntityId)).Should().Be(2);
        (await verify.InboxProcessedEvents.CountAsync(x => x.ConsumerName == "finance.ledger.source" && x.EventId == eventId)).Should().Be(1);
        (await verify.InboxProcessedEvents.CountAsync(x => x.ConsumerName == "finance.ledger.source"
            && concurrentEvents.Contains(x.EventId))).Should().Be(10);
        (await verify.OutboxMessages.CountAsync(x => x.EventType == "LedgerJournalPosted" && x.AggregateId == first.JournalEntryId.ToString())).Should().Be(1);
        var original = await verify.JournalEntries.Include(x => x.Lines).SingleAsync(x => x.Id == first.JournalEntryId);
        var reversal = await verify.JournalEntries.Include(x => x.Lines).SingleAsync(x => x.Id == reversalJournalId);
        original.Status.Should().Be(JournalEntryStatus.Posted);
        reversal.Lines.Single(x => x.Debit is not null).Debit!.Amount.Should().Be(10m);
        reversal.Lines.Single(x => x.Credit is not null).Credit!.Amount.Should().Be(10m);
        (await verify.JournalReversals.CountAsync(x => x.OriginalJournalEntryId == first.JournalEntryId)).Should().Be(1);
        var trialBalance = new TrialBalanceQuery(verify, new LedgerAccess(verify, TestAdmin(Guid.NewGuid())),
            new PostgresApplicationLockManager(verify), new PostgresApplicationTransactionExecutor(verify));
        var balance = await trialBalance.GetAsync(accounts.EntityId, periodId, new DateOnly(2026, 10, 22));
        balance.IsSuccess.Should().BeTrue(balance.IsFailure ? balance.Error.Code : null);
        balance.Value.TotalOpeningDebit.Should().Be(7m);
        balance.Value.TotalOpeningCredit.Should().Be(7m);
        balance.Value.TotalPeriodDebit.Should().Be(20m);
        balance.Value.TotalPeriodCredit.Should().Be(20m);
        balance.Value.TotalClosingDebit.Should().Be(7m);
        balance.Value.TotalClosingCredit.Should().Be(7m);
        balance.Value.SnapshotHash.Should().HaveLength(64);
        var firstPage = await trialBalance.GetAsync(accounts.EntityId, periodId, new DateOnly(2026, 10, 22), skip: 0, take: 1);
        var secondPage = await trialBalance.GetAsync(accounts.EntityId, periodId, new DateOnly(2026, 10, 22), skip: 1, take: 1);
        firstPage.IsSuccess.Should().BeTrue(firstPage.IsFailure ? firstPage.Error.Code : null);
        secondPage.IsSuccess.Should().BeTrue(secondPage.IsFailure ? secondPage.Error.Code : null);
        firstPage.Value.SnapshotHash.Should().Be(balance.Value.SnapshotHash);
        secondPage.Value.SnapshotHash.Should().Be(balance.Value.SnapshotHash);
        await using (var closeDb = CreateContext())
        {
            var service = CreatePeriodService(closeDb, TestAdmin(Guid.NewGuid()));
            var requested = await service.RequestCloseAsync(periodId, new LedgerPeriodCommandRequest(2, "Close after ledger test"));
            requested.IsSuccess.Should().BeTrue(requested.IsFailure ? requested.Error.Code : null);
            var closed = await service.CloseAsync(periodId, new LedgerPeriodCommandRequest(3, "Close after ledger test"));
            closed.IsSuccess.Should().BeTrue(closed.IsFailure ? closed.Error.Code : null);
            closed.Value.State.Should().Be(AccountingPeriodState.Closed);
            closed.Value.ClosePostedJournalCount.Should().Be(2);
            closed.Value.CloseDebitTotal.Should().Be(20m);
            closed.Value.CloseCreditTotal.Should().Be(20m);
        }
        await using (var closedReplayDb = CreateContext())
        {
            var replay = await CreatePostingService(closedReplayDb, TestWorker()).ProcessApprovedSourceDeliveryAsync(
                submitted.Id, Guid.NewGuid(), approvedFingerprint);
            replay.IsSuccess.Should().BeTrue(replay.IsFailure ? replay.Error.Code : null);
            replay.Value.JournalEntryId.Should().Be(first.JournalEntryId);
            replay.Value.WasReplay.Should().BeTrue();
        }
    }

    private async Task<(Guid EntityId, Guid PeriodId, Guid DebitId, Guid CreditId)> ReadAccountsAsync()
    {
        await using var db = CreateContext();
        var entity = await db.LegalEntities.SingleAsync(x => x.RegistrationNo.StartsWith("LEDGER-"));
        var periodId = await db.AccountingPeriods.Where(x => x.LegalEntityId == entity.Id && x.StartDate == new DateOnly(2026, 1, 1))
            .Select(x => x.Id).SingleAsync();
        var accounts = await db.ChartOfAccounts.Where(x => x.LegalEntityId == entity.Id).OrderBy(x => x.Code).ToListAsync();
        return (entity.Id, periodId, accounts[0].Id, accounts[1].Id);
    }

    private JournalSourceService CreateSourceService(AppDbContext db, ICurrentUser user)
    {
        var access = new LedgerAccess(db, user);
        return new JournalSourceService(db, user, new TestClock(), access, new PostgresApplicationTransactionExecutor(db));
    }

    private PostingService CreatePostingService(AppDbContext db, ICurrentUser user)
    {
        var access = new LedgerAccess(db, user);
        var locks = new PostgresApplicationLockManager(db);
        return new PostingService(db, user, new TestClock(), access, new PostingSourceFactory(db),
            new PostgresApplicationTransactionExecutor(db), locks);
    }

    private PeriodLifecycleService CreatePeriodService(AppDbContext db, ICurrentUser user)
    {
        var access = new LedgerAccess(db, user);
        return new PeriodLifecycleService(db, user, new TestClock(), access, new PostgresApplicationLockManager(db),
            new PostgresApplicationTransactionExecutor(db));
    }

    private async Task WaitForAdvisoryLockWaitAsync()
    {
        await using var observer = CreateContext();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var waiters = await observer.Database.SqlQueryRaw<long>("SELECT COUNT(*)::bigint AS \"Value\" FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND wait_event = 'advisory' AND query ILIKE '%pg_advisory_xact_lock%'")
                .SingleAsync();
            if (waiters > 0) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("The posting request did not wait on the shared PostgreSQL period lock.");
    }

    private async Task InstallOutboxFailureTriggerAsync(bool enabled)
    {
        await using var db = CreateContext();
        if (enabled)
            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION mealmate_test_fail_ledger_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'test.forced_ledger_outbox_failure'; END $$;
                CREATE TRIGGER "TR_TestFailLedgerOutbox" BEFORE INSERT ON "OutboxMessages"
                    FOR EACH ROW WHEN (NEW."EventType" = 'LedgerJournalPosted') EXECUTE FUNCTION mealmate_test_fail_ledger_outbox();
                """);
        else
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER IF EXISTS "TR_TestFailLedgerOutbox" ON "OutboxMessages";
                DROP FUNCTION IF EXISTS mealmate_test_fail_ledger_outbox();
                """);
    }

    private ICurrentUser TestAdmin(Guid userId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.UserId.Returns(userId);
        user.UserType.Returns(UserType.Admin);
        user.IsInRole(AppRoles.SuperAdmin).Returns(true);
        user.HasPermission(Arg.Any<string>()).Returns(true);
        return user;
    }

    private ICurrentUser TestWorker()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(false);
        user.UserId.Returns((Guid?)null);
        user.UserType.Returns((UserType?)null);
        user.Roles.Returns(Array.Empty<string>());
        user.Permissions.Returns(Array.Empty<string>());
        user.TenantIds.Returns(Array.Empty<Guid>());
        return user;
    }

    private AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options,
        Substitute.For<IDomainEventDispatcher>());

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        public DateOnly TodayUtc => new(2026, 10, 21);
    }
}
