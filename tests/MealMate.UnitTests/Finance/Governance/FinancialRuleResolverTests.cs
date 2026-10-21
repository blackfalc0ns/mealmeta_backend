using FluentAssertions;
using FluentAssertions.Execution;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Finance.Governance;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Finance;

/// <summary>
/// F01.2 — governed rule resolution: half-open effective windows, saved-version
/// stability for paid snapshots, and conflict exposure for legacy overlaps.
/// </summary>
public sealed class FinancialRuleResolverTests
{
    private static readonly DateTime T0 = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);

    private static AppDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options,
        Substitute.For<IDomainEventDispatcher>());

    private static RuleVersion ApprovedRule(
        Guid legalEntityId,
        string ruleKey,
        string versionCode,
        DateTime fromUtc,
        DateTime? toUtc,
        RuleScopeType scopeType = RuleScopeType.Restaurant,
        Guid? scopeEntityId = null,
        RuleApprovalStatus status = RuleApprovalStatus.Approved)
    {
        var normalizedScopeId = scopeType == RuleScopeType.Global
            ? RuleVersionScope.GlobalScopeId
            : scopeEntityId!.Value.ToString("N");

        return new RuleVersion
        {
            RuleKey = ruleKey,
            VersionCode = versionCode,
            ScopeType = scopeType,
            ScopeEntityId = scopeType == RuleScopeType.Global ? null : scopeEntityId,
            ApprovalStatus = status,
            EffectiveFromUtc = fromUtc,
            EffectiveToUtc = toUtc,
            LegalEntityId = legalEntityId,
            NormalizedScopeId = normalizedScopeId,
            SchemaVersion = 1,
            PayloadHash = "hash-" + versionCode,
            PayloadJson = "{\"v\":\"" + versionCode + "\"}",
        };
    }

    private static async Task<RuleScopeQuery> SeedScope(AppDbContext db, string ruleKey = "cancel.fee")
    {
        var legalEntityId = Guid.NewGuid();
        var scopeEntityId = Guid.NewGuid();
        db.RuleVersions.Add(ApprovedRule(legalEntityId, ruleKey, "v1", T0.AddDays(-10), T0.AddDays(-10).AddDays(20), scopeEntityId: scopeEntityId));
        db.RuleVersions.Add(ApprovedRule(legalEntityId, ruleKey, "v2", T0.AddDays(10), T0.AddDays(10).AddDays(20), scopeEntityId: scopeEntityId));
        await db.SaveChangesAsync();
        return new RuleScopeQuery(legalEntityId, ruleKey, RuleScopeType.Restaurant, scopeEntityId);
    }

    [Fact]
    public async Task EffectiveBoundaryUsesSavedVersion()
    {
        var db = CreateDb();
        var scope = await SeedScope(db);
        var resolver = new FinancialRuleResolver(db);

        using (new AssertionScope())
        {
            (await resolver.ResolveEffectiveAsync(scope, T0.AddDays(-11))).Status.Should().Be(RuleResolutionStatus.Missing, "before the first window opens");
            (await resolver.ResolveEffectiveAsync(scope, T0.AddDays(-10))).Status.Should().Be(RuleResolutionStatus.Resolved, "the start boundary is inclusive");
            (await resolver.ResolveEffectiveAsync(scope, T0.AddDays(-5))).Should().Match<RuleResolutionResult>(r => r.Status == RuleResolutionStatus.Resolved && r.Resolution!.VersionCode == "v1");
            (await resolver.ResolveEffectiveAsync(scope, T0.AddDays(9.5))).Should().Match<RuleResolutionResult>(r => r.Status == RuleResolutionStatus.Resolved && r.Resolution!.VersionCode == "v1");
            (await resolver.ResolveEffectiveAsync(scope, T0.AddDays(10))).Should().Match<RuleResolutionResult>(r => r.Status == RuleResolutionStatus.Resolved && r.Resolution!.VersionCode == "v2", "adjacent windows hand over exactly at the shared boundary");
            (await resolver.ResolveEffectiveAsync(scope, T0.AddDays(19.9))).Should().Match<RuleResolutionResult>(r => r.Status == RuleResolutionStatus.Resolved && r.Resolution!.VersionCode == "v2");
            (await resolver.ResolveEffectiveAsync(scope, T0.AddDays(30))).Status.Should().Be(RuleResolutionStatus.Missing, "the end boundary is exclusive");
        }
    }

    [Fact]
    public async Task ResolveSavedIgnoresNewerVersion_PaidSnapshotStability()
    {
        var db = CreateDb();
        var legalEntityId = Guid.NewGuid();
        var scopeEntityId = Guid.NewGuid();
        var saved = ApprovedRule(legalEntityId, "cancel.fee", "v1", T0.AddDays(-10), T0.AddDays(10), scopeEntityId: scopeEntityId);
        var newer = ApprovedRule(legalEntityId, "cancel.fee", "v2", T0.AddDays(-5), T0.AddDays(25), scopeEntityId: scopeEntityId);
        db.RuleVersions.AddRange(saved, newer);
        await db.SaveChangesAsync();
        var resolver = new FinancialRuleResolver(db);

        var resolved = await resolver.ResolveSavedAsync(saved.Id);

        using (new AssertionScope())
        {
            resolved.Should().NotBeNull();
            resolved!.VersionId.Should().Be(saved.Id);
            resolved.VersionCode.Should().Be("v1");
            resolved.PayloadHash.Should().Be("hash-v1", "a saved rule version never drifts to a newer approval");
        }
    }

    [Fact]
    public async Task ResolveSavedReturnsSupersededVersionForPaidSnapshot()
    {
        var db = CreateDb();
        var superseded = ApprovedRule(Guid.NewGuid(), "cancel.fee", "v1", T0.AddDays(-10), T0, scopeEntityId: Guid.NewGuid(), status: RuleApprovalStatus.Superseded);
        db.RuleVersions.Add(superseded);
        await db.SaveChangesAsync();
        var resolver = new FinancialRuleResolver(db);

        var resolved = await resolver.ResolveSavedAsync(superseded.Id);

        resolved.Should().NotBeNull();
        resolved!.VersionId.Should().Be(superseded.Id);
        resolved.VersionCode.Should().Be("v1");
    }

    [Fact]
    public async Task LegacyOverlapSurfacesAsConflictWithoutLeakingParties()
    {
        var db = CreateDb();
        var legalEntityId = Guid.NewGuid();
        var scopeEntityId = Guid.NewGuid();
        var first = ApprovedRule(legalEntityId, "cancel.fee", "v1", T0.AddDays(-10), T0.AddDays(20), scopeEntityId: scopeEntityId);
        var second = ApprovedRule(legalEntityId, "cancel.fee", "v2", T0, T0.AddDays(25), scopeEntityId: scopeEntityId);
        db.RuleVersions.AddRange(first, second);
        await db.SaveChangesAsync();
        var resolver = new FinancialRuleResolver(db);

        var result = await resolver.ResolveEffectiveAsync(new RuleScopeQuery(legalEntityId, "cancel.fee", RuleScopeType.Restaurant, scopeEntityId), T0.AddDays(5));

        using (new AssertionScope())
        {
            result.Status.Should().Be(RuleResolutionStatus.Conflict);
            result.Resolution.Should().BeNull();
            result.ConflictVersionIds.Should().HaveCount(2);
            result.ConflictVersionIds.Should().Contain(new[] { first.Id, second.Id });
        }
    }

    [Fact]
    public async Task DifferentEntitiesOrRuleCodesResolveIndependently()
    {
        var db = CreateDb();
        var legalEntityId = Guid.NewGuid();
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();
        db.RuleVersions.Add(ApprovedRule(legalEntityId, "cancel.fee", "v1", T0, null, scopeEntityId: entityA));
        db.RuleVersions.Add(ApprovedRule(legalEntityId, "tip.rule", "v1", T0, null, scopeEntityId: entityA));
        db.RuleVersions.Add(ApprovedRule(legalEntityId, "cancel.fee", "v1", T0.AddDays(-1), null, scopeEntityId: entityB));
        await db.SaveChangesAsync();
        var resolver = new FinancialRuleResolver(db);

        using (new AssertionScope())
        {
            var entityA_Cancelfee = await resolver.ResolveEffectiveAsync(new RuleScopeQuery(legalEntityId, "cancel.fee", RuleScopeType.Restaurant, entityA), T0);
            entityA_Cancelfee.Status.Should().Be(RuleResolutionStatus.Resolved);
            entityA_Cancelfee.Resolution!.PayloadHash.Should().Be("hash-v1");

            (await resolver.ResolveEffectiveAsync(new RuleScopeQuery(legalEntityId, "tip.rule", RuleScopeType.Restaurant, entityA), T0)).Status.Should().Be(RuleResolutionStatus.Resolved);
            (await resolver.ResolveEffectiveAsync(new RuleScopeQuery(legalEntityId, "cancel.fee", RuleScopeType.Restaurant, entityB), T0.AddDays(1))).Status.Should().Be(RuleResolutionStatus.Resolved);
            (await resolver.ResolveEffectiveAsync(new RuleScopeQuery(legalEntityId, "tip.rule", RuleScopeType.Restaurant, entityB), T0)).Status.Should().Be(RuleResolutionStatus.Missing, "entity B has no tip rule, and nothing bleeds across scopes");
        }
    }

    [Fact]
    public async Task GlobalScopeResolvesWithoutEntityBinding()
    {
        var db = CreateDb();
        var legalEntityId = Guid.NewGuid();
        db.RuleVersions.Add(ApprovedRule(legalEntityId, "cancel.fee", "v1", T0, T0.AddDays(10), scopeType: RuleScopeType.Global));
        await db.SaveChangesAsync();
        var resolver = new FinancialRuleResolver(db);

        var result = await resolver.ResolveEffectiveAsync(new RuleScopeQuery(legalEntityId, "cancel.fee", RuleScopeType.Global, null), T0.AddDays(9.9));

        result.Status.Should().Be(RuleResolutionStatus.Resolved);
        result.Resolution!.Scope.Should().Be(RuleVersionScope.GlobalScopeId);
    }

    [Fact]
    public async Task UnapprovedVersionsAreNeverResolvable()
    {
        var db = CreateDb();
        var legalEntityId = Guid.NewGuid();
        var scopeEntityId = Guid.NewGuid();
        db.RuleVersions.Add(ApprovedRule(legalEntityId, "cancel.fee", "draft", T0, null, scopeEntityId: scopeEntityId, status: RuleApprovalStatus.Draft));
        db.RuleVersions.Add(ApprovedRule(legalEntityId, "cancel.fee", "pending", T0, null, scopeEntityId: scopeEntityId, status: RuleApprovalStatus.PendingApproval));
        db.RuleVersions.Add(ApprovedRule(legalEntityId, "cancel.fee", "rejected", T0, null, scopeEntityId: scopeEntityId, status: RuleApprovalStatus.Rejected));
        await db.SaveChangesAsync();
        var resolver = new FinancialRuleResolver(db);

        var scope = new RuleScopeQuery(legalEntityId, "cancel.fee", RuleScopeType.Restaurant, scopeEntityId);

        using (new AssertionScope())
        {
            (await resolver.ResolveEffectiveAsync(scope, T0)).Status.Should().Be(RuleResolutionStatus.Missing);
            (await resolver.ResolveSavedAsync(db.RuleVersions.Single(x => x.ApprovalStatus == RuleApprovalStatus.Draft).Id)).Should().BeNull();
        }
    }
}
