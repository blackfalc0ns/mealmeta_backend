using MealMate.Application.Common.Abstractions;
using MealMate.Domain.CentralAccounting;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.Finance.Governance;

public enum ReportingReadinessState : byte
{
    NotConfigured = 0,
    Blocked = 1,
    InternallyReady = 2,
    ExternallyEligible = 3,
}

public sealed record ReportingReadinessResult(ReportingReadinessState State, IReadOnlyList<string> Blockers, string ProfileCode);

public interface IReportingReadinessEvaluator
{
    Task<ReportingReadinessResult> EvaluateAsync(Guid legalEntityId, string profileCode, string reportType, DateOnly periodDate, CancellationToken cancellationToken = default);
}

public sealed class ReportingReadinessEvaluator : IReportingReadinessEvaluator
{
    public const string InternalProfileCode = "INTERNAL";
    public const string QaydProfileCode = "KW_MOCI_QAYD";
    public const string IfsahProfileCode = "KW_CMA_IFSAH";

    private static readonly IReadOnlyDictionary<string, string> ProfileAuthority = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [QaydProfileCode] = "MOCI",
        [IfsahProfileCode] = "CMA",
    };

    private readonly IAppDbContext _db;

    public ReportingReadinessEvaluator(IAppDbContext db) => _db = db;

    public async Task<ReportingReadinessResult> EvaluateAsync(Guid legalEntityId, string profileCode, string reportType, DateOnly periodDate, CancellationToken cancellationToken = default)
    {
        var legalEntityIsActive = await _db.LegalEntities.AsNoTracking()
            .AnyAsync(x => x.Id == legalEntityId && x.Status == LegalEntityStatus.Active, cancellationToken);
        if (!legalEntityIsActive)
        {
            return new ReportingReadinessResult(ReportingReadinessState.Blocked, ["legal_entity_not_active_or_missing"], profileCode);
        }

        var profile = await _db.ReportingProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.LegalEntityId == legalEntityId && x.Code == profileCode && x.ReportType == reportType, cancellationToken);
        if (profile is null)
        {
            return new ReportingReadinessResult(ReportingReadinessState.NotConfigured, ["profile_not_configured"], profileCode);
        }

        var blockers = new List<string>();
        if (profile.Status != ReportingProfileStatus.Approved)
        {
            blockers.Add("profile_not_approved");
        }

        bool isInternalTrack = profileCode.Equals(InternalProfileCode, StringComparison.OrdinalIgnoreCase);
        bool isKnownExternalTrack = !isInternalTrack && ProfileAuthority.ContainsKey(profileCode);

        if (isInternalTrack)
        {
            var state = blockers.Count > 0 ? ReportingReadinessState.Blocked : ReportingReadinessState.InternallyReady;
            return new ReportingReadinessResult(state, blockers, profileCode);
        }

        if (!isKnownExternalTrack)
        {
            blockers.Add("profile_code_unknown");
            return new ReportingReadinessResult(ReportingReadinessState.Blocked, blockers, profileCode);
        }

        switch (profile.Applicability)
        {
            case ReportingApplicability.Unknown:
                blockers.Add("applicability_unknown");
                break;
            case ReportingApplicability.NotApplicable:
                blockers.Add("applicability_not_applicable");
                break;
            case ReportingApplicability.Applicable:
                break;
        }

        // F01 stores a package reference only. F09 introduces the verified package registry;
        // until then, a caller-provided non-null id cannot establish external filing readiness.
        blockers.Add("taxonomy_package_unverified");

        var authority = ProfileAuthority[profileCode];
        var verifiedRequirementCount = await _db.RegulatoryRequirements.AsNoTracking()
            .Where(x => x.Authority == authority && x.VerifiedByUserId != null)
            .CountAsync(cancellationToken);
        if (verifiedRequirementCount == 0)
        {
            blockers.Add("regulatory_requirement_unverified");
        }
        else
        {
            var periodStartUtc = new DateTime(periodDate.Year, periodDate.Month, periodDate.Day, 0, 0, 0, DateTimeKind.Utc);
            var requirementInEffect = await _db.RegulatoryRequirements.AsNoTracking()
                .Where(x => x.Authority == authority
                    && x.VerifiedByUserId != null
                    && x.EffectiveFromUtc <= periodStartUtc
                    && (x.EffectiveToUtc == null || periodStartUtc < x.EffectiveToUtc))
                .AnyAsync(cancellationToken);
            if (!requirementInEffect)
            {
                blockers.Add("requirement_not_in_effect_for_period");
            }
        }

        var externalState = blockers.Count > 0 ? ReportingReadinessState.Blocked : ReportingReadinessState.ExternallyEligible;
        return new ReportingReadinessResult(externalState, blockers, profileCode);
    }
}
