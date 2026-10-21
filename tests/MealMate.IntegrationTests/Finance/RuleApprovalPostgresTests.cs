using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Finance.Governance;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Testing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.IntegrationTests.Finance;

public sealed class RuleApprovalPostgresTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase _database = new();
    private string _connectionString = null!;
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _makerId = Guid.NewGuid();
    private readonly Guid _checkerId = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        _connectionString = _database.GetConnectionString();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        db.LegalEntities.Add(new LegalEntity
        {
            Id = _legalEntityId, LegalNameAr = "اختبار", LegalNameEn = "Test Entity",
            RegistrationNo = "TEST-F01", Currency = "KWD", Status = LegalEntityStatus.Active,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task JsonbRoundTripKeepsCanonicalSubmissionFingerprintAndAllowsIndependentApproval()
    {
        var created = await CreateDraftAsync("{ \"minimumUnexecutedExclusive\": 2, \"minimumExecutedDays\": 4 }");
        FinancialRuleSubmitResult submitted;
        await using (var makerDb = CreateContext())
        {
            submitted = (await Service(makerDb, _makerId).SubmitForApprovalAsync(created.RuleVersionId)).Value;
        }

        await using var checkerDb = CreateContext();
        var reloaded = await checkerDb.RuleVersions.AsNoTracking().SingleAsync(x => x.Id == created.RuleVersionId);
        reloaded.PayloadJson.Should().NotContain("  ");
        var approved = await Service(checkerDb, _checkerId).ApproveAsync(reloaded.Id, submitted.SubmissionFingerprint, submitted.RowVersion);

        approved.IsSuccess.Should().BeTrue(approved.IsFailure ? approved.Error.Code : null);
        approved.Value.Status.Should().Be(RuleApprovalStatus.Approved);
        approved.Value.CheckerUserId.Should().Be(_checkerId);
        (await checkerDb.OutboxMessages.CountAsync(x => x.EventType == "FinancialRuleVersionApproved" && x.AggregateId == created.RuleVersionId.ToString())).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentOverlappingApprovalsPersistOnlyOneApprovedRule()
    {
        var first = await CreateDraftAsync("{\"minimumExecutedDays\":4,\"minimumUnexecutedExclusive\":2}", "v1");
        var second = await CreateDraftAsync("{\"minimumExecutedDays\":5,\"minimumUnexecutedExclusive\":3}", "v2");
        var firstSubmission = await SubmitAsync(first.RuleVersionId);
        var secondSubmission = await SubmitAsync(second.RuleVersionId);

        var outcomes = await Task.WhenAll(
            ApproveAsync(first.RuleVersionId, firstSubmission, Guid.NewGuid()),
            ApproveAsync(second.RuleVersionId, secondSubmission, Guid.NewGuid()));

        outcomes.Count(x => x.IsSuccess).Should().Be(1);
        outcomes.Count(x => x.IsFailure && x.Error.Code == "finance.rule_scope_overlap").Should().Be(1);
        await using var verifyDb = CreateContext();
        (await verifyDb.RuleVersions.CountAsync(x => x.LegalEntityId == _legalEntityId && x.ApprovalStatus == RuleApprovalStatus.Approved)).Should().Be(1);
        (await verifyDb.FinancialAuditTrails.CountAsync(x => x.Action == "FinancialRuleVersionApproved")).Should().Be(1);
        (await verifyDb.FinancialAuditTrails.CountAsync(x => x.Action == "FinancialRuleApprovalDenied" && x.EntityType == "RuleVersion")).Should().Be(1);
        (await verifyDb.OutboxMessages.CountAsync(x => x.EventType == "FinancialRuleVersionApproved")).Should().Be(1);
    }

    [Fact]
    public async Task ApprovalWaitsForThePostgresFinancialScopeLock()
    {
        var draft = await CreateDraftAsync("{\"minimumExecutedDays\":4,\"minimumUnexecutedExclusive\":2}");
        var submission = await SubmitAsync(draft.RuleVersionId);
        await using var lockDb = CreateContext();
        await using var heldTransaction = await lockDb.Database.BeginTransactionAsync();
        await new PostgresApplicationLockManager(lockDb).LockFinancialRuleScopeAsync(
            _legalEntityId, "CANCELLATION", RuleScopeType.Global, null);

        var approvalTask = ApproveAsync(draft.RuleVersionId, submission, _checkerId);
        var lockWaitObserved = false;
        await using (var observer = CreateContext())
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var waiting = await observer.Database.SqlQueryRaw<long>(
                    "SELECT count(*)::bigint AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query ILIKE '%pg_advisory_xact_lock%' AND pid <> pg_backend_pid()")
                    .SingleAsync();
                if (waiting > 0) { lockWaitObserved = true; break; }
                await Task.Delay(25);
            }
        }

        lockWaitObserved.Should().BeTrue("the independent approver must block on PostgreSQL's scope advisory lock");
        approvalTask.IsCompleted.Should().BeFalse();
        await heldTransaction.CommitAsync();
        var outcome = await approvalTask;
        outcome.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AdditiveHardeningMigrationCanRollBackAndReapplyOnTheIsolatedDatabase()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync("20261021052758_F01_FinancialFoundationGovernance");
        var presentBeforeReapply = await db.Database.SqlQueryRaw<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'RuleVersions' AND column_name = 'SubmittedContentHash') AS \"Value\"").SingleAsync();
        presentBeforeReapply.Should().BeFalse();
        await db.Database.MigrateAsync();
        var presentAfterReapply = await db.Database.SqlQueryRaw<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'RuleVersions' AND column_name = 'SubmittedContentHash') AS \"Value\"").SingleAsync();
        presentAfterReapply.Should().BeTrue();
    }

    [Fact]
    public async Task SimulationValidatesCandidateButPersistsNoRuleOrAudit()
    {
        await using var db = CreateContext();
        var queries = new FinanceGovernanceQueries(db);
        var request = new FinancialRuleCreateRequest(_legalEntityId, "CANCELLATION", "simulate-only", RuleScopeType.Global, null,
            1, "{\"minimumExecutedDays\":4,\"minimumUnexecutedExclusive\":2}", "simulation", _now.AddDays(1), null);
        var beforeRules = await db.RuleVersions.CountAsync(x => x.LegalEntityId == _legalEntityId);
        var beforeAudit = await db.FinancialAuditTrails.CountAsync(x => x.EntityType == "RuleVersion");
        var beforeOutbox = await db.OutboxMessages.CountAsync(x => x.EventType.StartsWith("FinancialRule"));

        var result = await queries.SimulateAsync(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsValid.Should().BeTrue();
        (await db.RuleVersions.CountAsync(x => x.LegalEntityId == _legalEntityId)).Should().Be(beforeRules);
        (await db.FinancialAuditTrails.CountAsync(x => x.EntityType == "RuleVersion")).Should().Be(beforeAudit);
        (await db.OutboxMessages.CountAsync(x => x.EventType.StartsWith("FinancialRule"))).Should().Be(beforeOutbox);

        var invalid = await queries.SimulateAsync(request with { LegalEntityId = Guid.NewGuid() });
        invalid.Value.IsValid.Should().BeFalse();
    }

    private async Task<FinancialRuleVersionCreated> CreateDraftAsync(string payload, string version = "v1")
    {
        await using var db = CreateContext();
        var request = new FinancialRuleCreateRequest(_legalEntityId, "CANCELLATION", version, RuleScopeType.Global, null,
            1, payload, "F01 threshold rule", _now.AddDays(-1), null);
        var created = await Service(db, _makerId).CreateDraftAsync(request);
        created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.Code : null);
        return created.Value;
    }

    private async Task<FinancialRuleSubmitResult> SubmitAsync(Guid id)
    {
        await using var db = CreateContext();
        var result = await Service(db, _makerId).SubmitForApprovalAsync(id);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<MealMate.Application.Common.Models.Result<FinancialRuleApproveResult>> ApproveAsync(Guid id, FinancialRuleSubmitResult submission, Guid checker)
    {
        await using var db = CreateContext();
        return await Service(db, checker).ApproveAsync(id, submission.SubmissionFingerprint, submission.RowVersion);
    }

    private GovernedFinancialRuleService Service(AppDbContext db, Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.IsAuthenticated.Returns(true);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(_now);
        return new GovernedFinancialRuleService(db, currentUser, clock,
            new PostgresApplicationTransactionExecutor(db), new PostgresApplicationLockManager(db));
    }

    private AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connectionString).Options,
        Substitute.For<IDomainEventDispatcher>());
}
