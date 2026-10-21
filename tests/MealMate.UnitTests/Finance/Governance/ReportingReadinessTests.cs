using FluentAssertions;
using FluentAssertions.Execution;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Finance.Governance;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Finance.Governance;

/// <summary>
/// F01.4 — reporting profile separation and external-filing readiness blockers.
/// </summary>
public sealed class ReportingReadinessTests : IAsyncDisposable
{
    private static readonly DateOnly Period = new(2026, 11, 1);

    private readonly AppDbContext _db;

    public ReportingReadinessTests()
    {
        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            Substitute.For<IDomainEventDispatcher>());
    }

    private async Task<Guid> SeedActiveLegalEntityAsync()
    {
        var legalEntity = new LegalEntity
        {
            LegalNameAr = "شركة وجبات الكويت القابضة",
            LegalNameEn = "MealMate Kuwait Holding",
            RegistrationNo = "CR-2026-000111",
            Currency = "KWD",
            BusinessTimezone = "Asia/Kuwait",
            Status = LegalEntityStatus.Active,
        };
        _db.LegalEntities.Add(legalEntity);
        await _db.SaveChangesAsync();
        return legalEntity.Id;
    }

    private static ReportingProfile Profile(Guid legalEntityId, string code, ReportingApplicability applicability, Guid? packageId = null) =>
        new()
        {
            LegalEntityId = legalEntityId,
            Code = code,
            ReportType = "MonthlyFinancialReport",
            Applicability = applicability,
            PackageId = packageId,
            Status = ReportingProfileStatus.Approved,
        };

    private static RegulatoryRequirement Requirement(string authority, string requirementCode, DateTime effectiveFromUtc, Guid? verifiedBy) =>
        new()
        {
            Authority = authority,
            RequirementCode = requirementCode,
            SourceUrl = "https://moci.gov.kw/example",
            CapturedAtUtc = DateTime.UtcNow.AddDays(-2),
            VerifiedByUserId = verifiedBy,
            EvidenceHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            EffectiveFromUtc = effectiveFromUtc,
            EffectiveToUtc = null,
        };

    [Fact]
    public async Task InternalReadyDoesNotUnlockQayd()
    {
        var legalEntityId = await SeedActiveLegalEntityAsync();
        _db.ReportingProfiles.Add(Profile(legalEntityId, ReportingReadinessEvaluator.InternalProfileCode, ReportingApplicability.Applicable));
        _db.ReportingProfiles.Add(Profile(legalEntityId, ReportingReadinessEvaluator.QaydProfileCode, ReportingApplicability.Applicable));
        _db.SaveChanges();
        var evaluator = new ReportingReadinessEvaluator(_db);

        var internalResult = await evaluator.EvaluateAsync(legalEntityId, ReportingReadinessEvaluator.InternalProfileCode, "MonthlyFinancialReport", Period);
        var qaydResult = await evaluator.EvaluateAsync(legalEntityId, ReportingReadinessEvaluator.QaydProfileCode, "MonthlyFinancialReport", Period);

        using (new AssertionScope())
        {
            internalResult.State.Should().Be(ReportingReadinessState.InternallyReady, "an internal monthly report keeps running while external metadata is incomplete");
            internalResult.Blockers.Should().BeEmpty();

            qaydResult.State.Should().Be(ReportingReadinessState.Blocked, "the separate government track stays blocked");
            qaydResult.Blockers.Should().Contain("taxonomy_package_unverified");
        }
    }

    [Fact]
    public async Task UnknownApplicabilityBlocksExternalFiling()
    {
        var legalEntityId = await SeedActiveLegalEntityAsync();
        var packageId = Guid.NewGuid();
        var verifierId = Guid.NewGuid();
        _db.ReportingProfiles.Add(Profile(legalEntityId, ReportingReadinessEvaluator.QaydProfileCode, ReportingApplicability.Unknown, packageId));
        _db.RegulatoryRequirements.Add(Requirement("MOCI", "MOCI_QAYD_2026", DateTime.UtcNow.AddDays(-10), verifierId));
        _db.SaveChanges();
        var evaluator = new ReportingReadinessEvaluator(_db);

        var result = await evaluator.EvaluateAsync(legalEntityId, ReportingReadinessEvaluator.QaydProfileCode, "MonthlyFinancialReport", Period);

        using (new AssertionScope())
        {
            result.State.Should().Be(ReportingReadinessState.Blocked);
            result.Blockers.Should().Contain("applicability_unknown");
            result.Blockers.Should().Contain("taxonomy_package_unverified", "F01 has no verified package registry, so any external profile remains blocked");
            result.Blockers.Should().NotContain("regulatory_requirement_unverified");
        }
    }

    [Fact]
    public async Task UnverifiedPackageIdNeverUnlocksExternalTrack()
    {
        var legalEntityId = await SeedActiveLegalEntityAsync();
        var packageId = Guid.NewGuid();
        _db.ReportingProfiles.Add(Profile(legalEntityId, ReportingReadinessEvaluator.QaydProfileCode, ReportingApplicability.Applicable, packageId));
        _db.RegulatoryRequirements.Add(Requirement("MOCI", "MOCI_QAYD_2026", Period.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Utc).AddDays(-1), Guid.NewGuid()));
        _db.SaveChanges();
        var evaluator = new ReportingReadinessEvaluator(_db);

        var result = await evaluator.EvaluateAsync(legalEntityId, ReportingReadinessEvaluator.QaydProfileCode, "MonthlyFinancialReport", Period);

        using (new AssertionScope())
        {
            result.State.Should().Be(ReportingReadinessState.Blocked);
            result.Blockers.Should().Contain("taxonomy_package_unverified", "a caller-provided identifier is not evidence from the verified package registry");
        }
    }

    [Fact]
    public async Task RequirementVersionOutsideTheReportingPeriodBlocksExternalFiling()
    {
        var legalEntityId = await SeedActiveLegalEntityAsync();
        var packageId = Guid.NewGuid();
        _db.ReportingProfiles.Add(Profile(legalEntityId, ReportingReadinessEvaluator.QaydProfileCode, ReportingApplicability.Applicable, packageId));
        _db.RegulatoryRequirements.Add(Requirement("MOCI", "MOCI_QAYD_2026", Period.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Utc).AddDays(5), Guid.NewGuid()));
        _db.SaveChanges();
        var evaluator = new ReportingReadinessEvaluator(_db);

        var result = await evaluator.EvaluateAsync(legalEntityId, ReportingReadinessEvaluator.QaydProfileCode, "MonthlyFinancialReport", Period);

        using (new AssertionScope())
        {
            result.State.Should().Be(ReportingReadinessState.Blocked);
            result.Blockers.Should().Contain("requirement_not_in_effect_for_period");
        }
    }

    [Fact]
    public async Task UnverifiedRequirementMetadataNeverCountsAsEvidence()
    {
        var legalEntityId = await SeedActiveLegalEntityAsync();
        _db.ReportingProfiles.Add(Profile(legalEntityId, ReportingReadinessEvaluator.QaydProfileCode, ReportingApplicability.Applicable, Guid.NewGuid()));
        _db.RegulatoryRequirements.Add(Requirement("MOCI", "MOCI_QAYD_2026", DateTime.UtcNow.AddDays(-10), verifiedBy: null));
        _db.SaveChanges();
        var evaluator = new ReportingReadinessEvaluator(_db);

        var result = await evaluator.EvaluateAsync(legalEntityId, ReportingReadinessEvaluator.QaydProfileCode, "MonthlyFinancialReport", Period);

        using (new AssertionScope())
        {
            result.State.Should().Be(ReportingReadinessState.Blocked);
            result.Blockers.Should().Contain("regulatory_requirement_unverified");
        }
    }

    [Fact]
    public async Task MissingProfileIsNotConfigured()
    {
        var legalEntityId = await SeedActiveLegalEntityAsync();
        var evaluator = new ReportingReadinessEvaluator(_db);

        var result = await evaluator.EvaluateAsync(legalEntityId, ReportingReadinessEvaluator.IfsahProfileCode, "MonthlyFinancialReport", Period);

        using (new AssertionScope())
        {
            result.State.Should().Be(ReportingReadinessState.NotConfigured);
            result.Blockers.Should().Contain("profile_not_configured");
        }
    }

    [Fact]
    public async Task ReportingReadinessSelectsTheRequestedReportType()
    {
        var legalEntityId = await SeedActiveLegalEntityAsync();
        var monthly = Profile(legalEntityId, ReportingReadinessEvaluator.InternalProfileCode, ReportingApplicability.Applicable);
        var annual = Profile(legalEntityId, ReportingReadinessEvaluator.InternalProfileCode, ReportingApplicability.Applicable);
        annual.ReportType = "AnnualFinancialReport";
        annual.Status = ReportingProfileStatus.Draft;
        _db.ReportingProfiles.AddRange(monthly, annual);
        await _db.SaveChangesAsync();
        var evaluator = new ReportingReadinessEvaluator(_db);

        var result = await evaluator.EvaluateAsync(legalEntityId, ReportingReadinessEvaluator.InternalProfileCode, "MonthlyFinancialReport", Period);

        result.State.Should().Be(ReportingReadinessState.InternallyReady);
    }

    [Fact]
    public async Task InactiveLegalEntityBlocksEveryTrack()
    {
        var legalEntity = new LegalEntity
        {
            LegalNameAr = "كيان موقوف",
            LegalNameEn = "Suspended Entity",
            RegistrationNo = "CR-2026-000222",
            Currency = "KWD",
            BusinessTimezone = "Asia/Kuwait",
            Status = LegalEntityStatus.Suspended,
        };
        _db.LegalEntities.Add(legalEntity);
        _db.ReportingProfiles.Add(Profile(legalEntity.Id, ReportingReadinessEvaluator.InternalProfileCode, ReportingApplicability.Applicable));
        _db.SaveChanges();
        var evaluator = new ReportingReadinessEvaluator(_db);

        var result = await evaluator.EvaluateAsync(legalEntity.Id, ReportingReadinessEvaluator.InternalProfileCode, "MonthlyFinancialReport", Period);

        using (new AssertionScope())
        {
            result.State.Should().Be(ReportingReadinessState.Blocked);
            result.Blockers.Should().Contain("legal_entity_not_active_or_missing");
        }
    }

    [Fact]
    public async Task DraftProfileStaysBlocked()
    {
        var legalEntityId = await SeedActiveLegalEntityAsync();
        var draftProfile = Profile(legalEntityId, ReportingReadinessEvaluator.InternalProfileCode, ReportingApplicability.Applicable);
        draftProfile.Status = ReportingProfileStatus.Draft;
        _db.ReportingProfiles.Add(draftProfile);
        _db.SaveChanges();
        var evaluator = new ReportingReadinessEvaluator(_db);

        var result = await evaluator.EvaluateAsync(legalEntityId, ReportingReadinessEvaluator.InternalProfileCode, "MonthlyFinancialReport", Period);

        using (new AssertionScope())
        {
            result.State.Should().Be(ReportingReadinessState.Blocked);
            result.Blockers.Should().Contain("profile_not_approved");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
    }
}
