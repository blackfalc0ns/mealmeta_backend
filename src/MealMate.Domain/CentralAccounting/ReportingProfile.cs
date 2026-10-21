using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CentralAccounting;

/// <summary>
/// Per reporting track (e.g. INTERNAL / KW_MOCI_QAYD / KW_CMA_IFSAH). Readiness of one
/// profile never unlocks another; each track is evaluated and approved separately.
/// </summary>
public sealed class ReportingProfile : AuditableEntity
{
    public Guid LegalEntityId { get; set; }

    public LegalEntity LegalEntity { get; set; } = default!;

    public string Code { get; set; } = default!;

    public string ReportType { get; set; } = default!;

    public ReportingApplicability Applicability { get; set; } = ReportingApplicability.Unknown;

    /// <summary>Verified taxonomy package registry id (populated from F09; never self-declared).</summary>
    public Guid? PackageId { get; set; }

    public string? EntryPointId { get; set; }

    public string? ValidatorProfile { get; set; }

    public ReportingProfileStatus Status { get; set; } = ReportingProfileStatus.Draft;
}
