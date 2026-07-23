using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.IdentityAccess;

public sealed class RestaurantRegistration : AuditableEntity
{
    public Guid? SubmittedByUserId { get; set; }

    public string LegalNameAr { get; set; } = default!;

    public string LegalNameEn { get; set; } = default!;

    public string TradeNameAr { get; set; } = default!;

    public string TradeNameEn { get; set; } = default!;

    public string ContactPhone { get; set; } = default!;

    public string ContactEmail { get; set; } = default!;

    public Guid? CountryId { get; set; }

    public Guid? RegionId { get; set; }

    public VerificationStatus Status { get; set; } = VerificationStatus.Submitted;

    public string? RejectionReason { get; set; }

    public string? ChangeRequestNotes { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public Guid? ResultingRestaurantId { get; set; }
}
