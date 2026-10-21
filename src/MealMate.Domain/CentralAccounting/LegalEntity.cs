using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.CentralAccounting;

public sealed class LegalEntity : AuditableEntity
{
    public string LegalNameAr { get; set; } = default!;

    public string LegalNameEn { get; set; } = default!;

    public string RegistrationNo { get; set; } = default!;

    public string Currency { get; set; } = "KWD";

    public string BusinessTimezone { get; set; } = "Asia/Kuwait";

    public LegalEntityStatus Status { get; set; } = LegalEntityStatus.Draft;
}
