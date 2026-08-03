using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.Foundation;

/// <summary>
/// Operating Tenant aggregate root. Represents an isolated operating environment
/// (e.g. a country deployment) within the MealMate multi-tenancy control plane.
/// </summary>
public sealed class Tenant : AuditableEntity
{
    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public Guid CountryId { get; set; }

    public Country Country { get; set; } = default!;

    public string Domain { get; set; } = default!;

    public string CurrencyCode { get; set; } = default!;

    public string TimeZoneId { get; set; } = default!;

    /// <summary>Default language for the tenant: "ar" or "en".</summary>
    public string DefaultLanguage { get; set; } = "ar";

    public TenantStatus Status { get; set; } = TenantStatus.Setup;

    /// <summary>
    /// Explicit active flag kept consistent with <see cref="Status"/>.
    /// Active only when status is <see cref="TenantStatus.Active"/>.
    /// </summary>
    public bool IsActive { get; set; }
}
