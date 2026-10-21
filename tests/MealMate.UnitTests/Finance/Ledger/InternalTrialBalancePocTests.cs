using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.Finance.Ledger;
using MealMate.Application.Features.Finance.Governance;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.Finance.Ledger;

public sealed class InternalTrialBalancePocTests
{
    [Fact]
    public void MappingIsMarkedAndValidatesOnlyInternalTrialBalanceShape()
    {
        var result = new LedgerTrialBalanceResult(Guid.NewGuid(), Guid.NewGuid(), 4, new DateOnly(2026, 10, 21),
            [new LedgerTrialBalanceRow(Guid.NewGuid(), "1000", "أصل", "Asset", 1m, 0m, 0m, 0m, 1m, 0m),
             new LedgerTrialBalanceRow(Guid.NewGuid(), "2000", "التزام", "Liability", 0m, 1m, 0m, 0m, 0m, 1m)],
            1m, 1m, 0m, 0m, 1m, 1m, 2, "snapshot-test");
        var xml = InternalTrialBalancePoc.Map(result);

        xml.Should().Contain("TEST_ONLY");
        xml.Should().Contain("urn:mealmate:finance:internal-trial-balance-poc:v1");
        InternalTrialBalancePoc.Validate(xml).Should().BeTrue();
        InternalTrialBalancePoc.Validate(xml.Replace("TEST_ONLY", "XBRL", StringComparison.Ordinal)).Should().BeFalse();
        InternalTrialBalancePoc.Validate("<xbrl><context/><unit/></xbrl>").Should().BeFalse();
        InternalTrialBalancePoc.Validate(xml.Replace("<period-debit>0.000</period-debit>", "<period-debit>0.001</period-debit>", StringComparison.Ordinal)).Should().BeFalse();
        InternalTrialBalancePoc.Validate(xml.Replace("<opening-debit>", "<opening-other>", StringComparison.Ordinal)).Should().BeFalse();
        var partial = result with { Rows = [], TotalRowCount = 1 };
        FluentActions.Invoking(() => InternalTrialBalancePoc.Map(partial)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task MappingDoesNotPromoteExternalReportingReadiness()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IDomainEventDispatcher>());
        var entity = new LegalEntity
        {
            LegalNameAr = "كيان اختبار", LegalNameEn = "Test entity", RegistrationNo = "POC-" + Guid.NewGuid().ToString("N"),
            Currency = "KWD", Status = LegalEntityStatus.Active,
        };
        db.LegalEntities.Add(entity);
        db.ReportingProfiles.AddRange(
            new ReportingProfile { LegalEntityId = entity.Id, Code = ReportingReadinessEvaluator.InternalProfileCode, ReportType = "MonthlyFinancialReport", Status = ReportingProfileStatus.Approved, Applicability = ReportingApplicability.Applicable },
            new ReportingProfile { LegalEntityId = entity.Id, Code = ReportingReadinessEvaluator.QaydProfileCode, ReportType = "MonthlyFinancialReport", Status = ReportingProfileStatus.Approved, Applicability = ReportingApplicability.Applicable, PackageId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        var evaluator = new ReportingReadinessEvaluator(db);
        var before = await evaluator.EvaluateAsync(entity.Id, ReportingReadinessEvaluator.QaydProfileCode, "MonthlyFinancialReport", new DateOnly(2026, 10, 21));
        var xml = InternalTrialBalancePoc.Map(new LedgerTrialBalanceResult(entity.Id, Guid.NewGuid(), 0, new DateOnly(2026, 10, 21), [], 0, 0, 0, 0, 0, 0, 0, "empty-snapshot"));
        InternalTrialBalancePoc.Validate(xml).Should().BeTrue();
        var after = await evaluator.EvaluateAsync(entity.Id, ReportingReadinessEvaluator.QaydProfileCode, "MonthlyFinancialReport", new DateOnly(2026, 10, 21));
        var internalResult = await evaluator.EvaluateAsync(entity.Id, ReportingReadinessEvaluator.InternalProfileCode, "MonthlyFinancialReport", new DateOnly(2026, 10, 21));

        after.Should().BeEquivalentTo(before);
        after.State.Should().Be(ReportingReadinessState.Blocked);
        after.Blockers.Should().Contain("taxonomy_package_unverified");
        internalResult.State.Should().Be(ReportingReadinessState.InternallyReady);
    }
}
