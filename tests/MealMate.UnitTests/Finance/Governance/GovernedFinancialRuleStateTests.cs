using FluentAssertions;
using FluentAssertions.Execution;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.Finance.Governance;
using MealMate.Domain.Enums;
using MealMate.Domain.CentralAccounting;
using MealMate.Persistence;
using MealMate.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Finance.Governance;

/// <summary>
/// F01.3 — governed maker/checker decision flow over financial rule versions:
/// identity, self-approval ban, payload pinning, overlap gate, and idempotency.
/// </summary>
public sealed class GovernedFinancialRuleStateTests : IAsyncDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);

    private readonly AppDbContext _db = null!;
    private readonly GovernedFinancialRuleService _makerService;
    private readonly GovernedFinancialRuleService _checkerService;
    private readonly GovernedFinancialRuleService _secondCheckerService;

    private static readonly Guid MakerId = Guid.NewGuid();
    private static readonly Guid CheckerId = Guid.NewGuid();
    private static readonly Guid OtherCheckerId = Guid.NewGuid();

    private readonly FinancialRuleCreateRequest _baseRequest;

    public GovernedFinancialRuleStateTests()
    {
        var makerUser = CreateCurrentUser(MakerId);
        var checkerUser = CreateCurrentUser(CheckerId);
        var otherCheckerUser = CreateCurrentUser(OtherCheckerId);
        var clock = CreateFixedClock();
        var txExecutor = new PassThroughTransactionExecutor();

        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(new AuditableEntityInterceptor(makerUser, clock))
                .Options,
            Substitute.For<IDomainEventDispatcher>());

        var legalEntity = new LegalEntity
        {
            LegalNameAr = "كيان اختبار معتمد",
            LegalNameEn = "Authorized Test Entity",
            RegistrationNo = "TEST-FIN-ENTITY-01",
            Currency = "KWD",
            BusinessTimezone = "Asia/Kuwait",
            Status = LegalEntityStatus.Active,
        };
        _db.LegalEntities.Add(legalEntity);
        _db.SaveChanges();
        _baseRequest = new FinancialRuleCreateRequest(
            legalEntity.Id,
            "cancel.fee",
            "v1",
            RuleScopeType.Restaurant,
            Guid.NewGuid(),
            1,
            "{\"maxFees\":40}",
            "first governed fee rule",
            T0,
            T0.AddDays(30));

        _makerService = new GovernedFinancialRuleService(_db, makerUser, clock, txExecutor, Substitute.For<IApplicationLockManager>());
        _checkerService = new GovernedFinancialRuleService(_db, checkerUser, clock, txExecutor, Substitute.For<IApplicationLockManager>());
        _secondCheckerService = new GovernedFinancialRuleService(_db, otherCheckerUser, clock, txExecutor, Substitute.For<IApplicationLockManager>());
    }

    private static ICurrentUser CreateCurrentUser(Guid userId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns((Guid?)userId);
        user.IsAuthenticated.Returns(true);
        return user;
    }

    private static IDateTimeProvider CreateFixedClock()
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(T0);
        return clock;
    }

    private async Task<MealMate.Application.Common.Models.Result<FinancialRuleApproveResult>> ApproveAsync(
        GovernedFinancialRuleService service,
        Guid ruleVersionId)
    {
        var row = await _db.RuleVersions.SingleAsync(x => x.Id == ruleVersionId);
        return await service.ApproveAsync(ruleVersionId, row.SubmittedContentHash ?? string.Empty, row.RowVersion);
    }

    private async Task<MealMate.Application.Common.Models.Result<FinancialRuleRejectResult>> RejectAsync(
        GovernedFinancialRuleService service,
        Guid ruleVersionId,
        string reason = "Contract review rejected")
    {
        var row = await _db.RuleVersions.SingleAsync(x => x.Id == ruleVersionId);
        return await service.RejectAsync(ruleVersionId, row.SubmittedContentHash ?? string.Empty, row.RowVersion, reason);
    }

    [Fact]
    public async Task MakerCheckerFlowRecordsFullAuditAndSingleApprovalOutbox()
    {
        var created = await _makerService.CreateDraftAsync(_baseRequest);
        created.IsSuccess.Should().BeTrue();

        var submitted = await _makerService.SubmitForApprovalAsync(created.Value.RuleVersionId);
        submitted.IsSuccess.Should().BeTrue();
        submitted.Value.SubmittedPayloadHash.Should().NotBeEmpty();

        var approved = await ApproveAsync(_checkerService, created.Value.RuleVersionId);
        approved.IsSuccess.Should().BeTrue();

        using (new AssertionScope())
        {
            var row = await _db.RuleVersions.SingleAsync(x => x.Id == created.Value.RuleVersionId);
            row.ApprovalStatus.Should().Be(RuleApprovalStatus.Approved);
            row.CheckerUserId.Should().Be(CheckerId);
            row.CreatedByUserId.Should().Be(MakerId);

            var auditActions = await _db.FinancialAuditTrails
                .Where(x => x.EntityId == row.Id)
                .OrderBy(x => x.OccurredAtUtc)
                .Select(x => x.Action)
                .ToListAsync();
            auditActions.Should().BeEquivalentTo(["FinancialRuleDraftCreated", "FinancialRuleVersionSubmitted", "FinancialRuleVersionApproved"]);

            var outbox = await _db.OutboxMessages.SingleAsync(x => x.EventType == "FinancialRuleVersionApproved");
            outbox.DeduplicationKey.Should().Be($"financial-rule-approved:{row.Id:N}");
        }
    }

    [Fact]
    public async Task ANonCreatorCannotSubmitAnotherMakersDraft()
    {
        var created = await _makerService.CreateDraftAsync(_baseRequest);
        created.IsSuccess.Should().BeTrue();

        var submittedByDifferentUser = await _checkerService.SubmitForApprovalAsync(created.Value.RuleVersionId);

        submittedByDifferentUser.IsSuccess.Should().BeFalse();
        submittedByDifferentUser.Error.Code.Should().Be("finance.creator_only_submit");
    }

    [Fact]
    public async Task UnknownPayloadSchemaIsRejectedAtDraftCreation()
    {
        var request = _baseRequest with { SchemaVersion = 77 };

        var created = await _makerService.CreateDraftAsync(request);

        created.IsSuccess.Should().BeFalse();
        created.Error.Code.Should().Be("finance.rule_schema_version_unsupported");
    }

    [Fact]
    public async Task InvalidIdentityAndNonUtcWindowsAreRejectedAtDraftCreation()
    {
        var missingEntity = await _makerService.CreateDraftAsync(_baseRequest with { LegalEntityId = Guid.NewGuid() });
        var invalidScope = await _makerService.CreateDraftAsync(_baseRequest with { ScopeType = (RuleScopeType)999 });
        var invalidTime = await _makerService.CreateDraftAsync(_baseRequest with { EffectiveFromUtc = DateTime.SpecifyKind(T0, DateTimeKind.Unspecified) });
        var subMicrosecondTime = await _makerService.CreateDraftAsync(_baseRequest with { EffectiveFromUtc = T0.AddTicks(1) });

        missingEntity.Error.Code.Should().Be("finance.rule_legal_entity_inactive_or_missing");
        invalidScope.Error.Code.Should().Be("finance.rule_scope_invalid");
        invalidTime.Error.Code.Should().Be("finance.rule_window_invalid");
        subMicrosecondTime.Error.Code.Should().Be("finance.rule_window_invalid");
    }

    [Fact]
    public async Task UntypedOrUnknownRulePayloadIsRejectedAtDraftCreation()
    {
        var invalidType = await _makerService.CreateDraftAsync(_baseRequest with { PayloadJson = "{\"maxFees\":\"40\"}" });
        var unknownRule = await _makerService.CreateDraftAsync(_baseRequest with { RuleKey = "unregistered.rule" });

        invalidType.IsSuccess.Should().BeFalse();
        invalidType.Error.Code.Should().Be("finance.invalid_payload");
        unknownRule.IsSuccess.Should().BeFalse();
        unknownRule.Error.Code.Should().Be("finance.rule_contract_unsupported");
    }

    [Fact]
    public async Task CancellationSchemaRequiresItsTypedDayBoundaryFields()
    {
        var missingField = await _makerService.CreateDraftAsync(_baseRequest with
        {
            RuleKey = "CANCELLATION",
            PayloadJson = "{\"minimumExecutedDays\":3}",
        });
        var valid = await _makerService.CreateDraftAsync(_baseRequest with
        {
            RuleKey = "CANCELLATION",
            VersionCode = "cancel-schema-v1",
            PayloadJson = "{\"minimumExecutedDays\":3,\"minimumUnexecutedExclusive\":3}",
        });

        missingField.Error.Code.Should().Be("finance.invalid_payload");
        valid.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovedGovernedFieldsCannotBeEditedButLifecycleCanAdvance()
    {
        var created = await _makerService.CreateDraftAsync(_baseRequest);
        await _makerService.SubmitForApprovalAsync(created.Value.RuleVersionId);
        var approved = await ApproveAsync(_checkerService, created.Value.RuleVersionId);
        approved.IsSuccess.Should().BeTrue();

        var row = await _db.RuleVersions.SingleAsync(x => x.Id == created.Value.RuleVersionId);
        row.ApprovalStatus = RuleApprovalStatus.Active;
        await _db.SaveChangesAsync();
        row.ApprovalStatus = RuleApprovalStatus.Superseded;
        await _db.SaveChangesAsync();
        row.PayloadJson = "{\"maxFees\":41}";
        var edit = async () => await _db.SaveChangesAsync();

        await edit.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ApprovedGovernedRuleCannotBeDowngradedOrDeleted()
    {
        var created = await _makerService.CreateDraftAsync(_baseRequest);
        await _makerService.SubmitForApprovalAsync(created.Value.RuleVersionId);
        (await ApproveAsync(_checkerService, created.Value.RuleVersionId)).IsSuccess.Should().BeTrue();
        var row = await _db.RuleVersions.SingleAsync(x => x.Id == created.Value.RuleVersionId);
        row.ApprovalStatus = RuleApprovalStatus.Draft;

        var downgrade = async () => await _db.SaveChangesAsync();

        await downgrade.Should().ThrowAsync<InvalidOperationException>();
        _db.ChangeTracker.Clear();
        row = await _db.RuleVersions.SingleAsync(x => x.Id == created.Value.RuleVersionId);
        _db.RuleVersions.Remove(row);
        var deletion = async () => await _db.SaveChangesAsync();
        await deletion.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SelfApprovalIsAlwaysRejected()
    {
        var created = await _makerService.CreateDraftAsync(_baseRequest);
        await _makerService.SubmitForApprovalAsync(created.Value.RuleVersionId);

        var selfApproved = await ApproveAsync(_makerService, created.Value.RuleVersionId);

        using (new AssertionScope())
        {
            selfApproved.IsFailure.Should().BeTrue();
            selfApproved.Error.Code.Should().Be("finance.self_approval");

            var row = await _db.RuleVersions.SingleAsync(x => x.Id == created.Value.RuleVersionId);
            row.ApprovalStatus.Should().Be(RuleApprovalStatus.PendingApproval);
            row.CheckerUserId.Should().BeNull();
        }
    }

    [Fact]
    public async Task PayloadChangesAfterSubmitStaleTheApproval()
    {
        var created = await _makerService.CreateDraftAsync(_baseRequest);
        await _makerService.SubmitForApprovalAsync(created.Value.RuleVersionId);

        var row = await _db.RuleVersions.SingleAsync(x => x.Id == created.Value.RuleVersionId);
        row.PayloadJson = "{\"maxFees\":99}";
        row.RowVersion++;
        await _db.SaveChangesAsync();

        var approved = await ApproveAsync(_checkerService, created.Value.RuleVersionId);

        using (new AssertionScope())
        {
            approved.IsFailure.Should().BeTrue();
            approved.Error.Code.Should().Be("finance.stale_payload");
            (await _db.RuleVersions.SingleAsync(x => x.Id == created.Value.RuleVersionId)).ApprovalStatus.Should().Be(RuleApprovalStatus.PendingApproval);
        }
    }

    [Fact]
    public async Task ApprovalRequiresTheSubmissionFingerprintAndRowVersion()
    {
        var created = await _makerService.CreateDraftAsync(_baseRequest);
        var submitted = await _makerService.SubmitForApprovalAsync(created.Value.RuleVersionId);

        var approved = await _checkerService.ApproveAsync(created.Value.RuleVersionId, submitted.Value.SubmissionFingerprint, submitted.Value.RowVersion - 1);

        approved.IsFailure.Should().BeTrue();
        approved.Error.Code.Should().Be("finance.stale_payload");
        (await _db.RuleVersions.SingleAsync(x => x.Id == created.Value.RuleVersionId)).ApprovalStatus.Should().Be(RuleApprovalStatus.PendingApproval);
    }

    [Fact]
    public async Task OverlappingApprovedWindowsAreRejectedAtApproval()
    {
        var first = await _makerService.CreateDraftAsync(_baseRequest);
        await _makerService.SubmitForApprovalAsync(first.Value.RuleVersionId);
        (await ApproveAsync(_checkerService, first.Value.RuleVersionId)).IsSuccess.Should().BeTrue();

        var overlapping = _baseRequest with
        {
            VersionCode = "v2",
            EffectiveFromUtc = T0.AddDays(15),
            EffectiveToUtc = T0.AddDays(45),
        };
        var second = await _makerService.CreateDraftAsync(overlapping);
        await _makerService.SubmitForApprovalAsync(second.Value.RuleVersionId);

        var approval = await ApproveAsync(_checkerService, second.Value.RuleVersionId);

        using (new AssertionScope())
        {
            approval.IsFailure.Should().BeTrue();
            approval.Error.Code.Should().Be("finance.rule_scope_overlap");

            var row = await _db.RuleVersions.SingleAsync(x => x.Id == second.Value.RuleVersionId);
            row.ApprovalStatus.Should().Be(RuleApprovalStatus.PendingApproval);
        }
    }

    [Fact]
    public async Task AdjacentApprovedWindowsDoNotOverlap()
    {
        var first = await _makerService.CreateDraftAsync(_baseRequest);
        await _makerService.SubmitForApprovalAsync(first.Value.RuleVersionId);
        (await ApproveAsync(_checkerService, first.Value.RuleVersionId)).IsSuccess.Should().BeTrue();

        var adjacent = _baseRequest with
        {
            VersionCode = "v2",
            EffectiveFromUtc = _baseRequest.EffectiveToUtc!.Value,
            EffectiveToUtc = _baseRequest.EffectiveToUtc.Value.AddDays(30),
        };
        var second = await _makerService.CreateDraftAsync(adjacent);
        await _makerService.SubmitForApprovalAsync(second.Value.RuleVersionId);

        (await ApproveAsync(_checkerService, second.Value.RuleVersionId)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovingBeforeSubmissionIsStateConflict()
    {
        var created = await _makerService.CreateDraftAsync(_baseRequest);

        var approved = await ApproveAsync(_checkerService, created.Value.RuleVersionId);

        approved.IsFailure.Should().BeTrue();
        approved.Error.Code.Should().Be("finance.state_conflict");
    }

    [Fact]
    public async Task ReapprovalIsIdempotentForSameCheckerAndConflictsForOthers()
    {
        var created = await _makerService.CreateDraftAsync(_baseRequest);
        var submission = await _makerService.SubmitForApprovalAsync(created.Value.RuleVersionId);
        (await _checkerService.ApproveAsync(created.Value.RuleVersionId, submission.Value.SubmissionFingerprint, submission.Value.RowVersion)).IsSuccess.Should().BeTrue();

        using (new AssertionScope())
        {
            (await _checkerService.ApproveAsync(created.Value.RuleVersionId, submission.Value.SubmissionFingerprint, submission.Value.RowVersion)).IsSuccess.Should().BeTrue("the identical checker request is a safe no-op");
            (await _secondCheckerService.ApproveAsync(created.Value.RuleVersionId, submission.Value.SubmissionFingerprint, submission.Value.RowVersion)).Error.Code.Should().Be("finance.state_conflict");
        }
    }

    [Fact]
    public async Task DuplicateVersionCodeIsRejected()
    {
        (await _makerService.CreateDraftAsync(_baseRequest)).IsSuccess.Should().BeTrue();

        var duplicate = await _makerService.CreateDraftAsync(_baseRequest);

        using (new AssertionScope())
        {
            duplicate.IsFailure.Should().BeTrue();
            duplicate.Error.Code.Should().Be("finance.duplicate_version_code");
        }
    }

    [Fact]
    public async Task RejectionRequiresCheckerDifferentFromMaker()
    {
        var created = await _makerService.CreateDraftAsync(_baseRequest);
        await _makerService.SubmitForApprovalAsync(created.Value.RuleVersionId);

        var rejected = await RejectAsync(_checkerService, created.Value.RuleVersionId);

        using (new AssertionScope())
        {
            rejected.IsSuccess.Should().BeTrue();
            (await _db.RuleVersions.SingleAsync(x => x.Id == created.Value.RuleVersionId)).ApprovalStatus.Should().Be(RuleApprovalStatus.Rejected);
            (await RejectAsync(_makerService, created.Value.RuleVersionId)).Error.Code.Should().Be("finance.state_conflict", "an already rejected row cannot be rejected again by a different checker");
        }
    }

    [Fact]
    public async Task RejectionRequiresReasonAndCurrentSubmissionFingerprint()
    {
        var created = await _makerService.CreateDraftAsync(_baseRequest);
        await _makerService.SubmitForApprovalAsync(created.Value.RuleVersionId);

        var noReason = await RejectAsync(_checkerService, created.Value.RuleVersionId, " ");
        var row = await _db.RuleVersions.SingleAsync(x => x.Id == created.Value.RuleVersionId);
        var stale = await _checkerService.RejectAsync(created.Value.RuleVersionId, row.SubmittedContentHash!, row.RowVersion - 1, "Contract review rejected");

        noReason.Error.Code.Should().Be("finance.rejection_reason_required");
        stale.Error.Code.Should().Be("finance.stale_payload");
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
    }

    private sealed class PassThroughTransactionExecutor : IApplicationTransactionExecutor
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
