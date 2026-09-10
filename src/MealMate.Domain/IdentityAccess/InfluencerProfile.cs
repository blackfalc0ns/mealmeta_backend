using MealMate.Domain.Common;
using MealMate.Domain.Enums;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.IdentityAccess;

public sealed class InfluencerProfile : AuditableEntity
{
    public Guid UserId { get; set; }

    public string DisplayName { get; set; } = default!;

    public string? InfluencerCode { get; set; }

    public string? LinkedCustomerUserIdRaw { get; set; }

    public Guid? LinkedCustomerUserId { get; set; }

    public Percentage? DefaultCommissionRate { get; set; }

    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Draft;

    public bool IsActive { get; set; }

    public string? Iban { get; set; }

    public string? BankName { get; set; }

    public string? AccountHolderName { get; set; }

    public string? SocialLinksJson { get; set; }

    public bool HasCompletedSocialOnboarding { get; set; }
}
