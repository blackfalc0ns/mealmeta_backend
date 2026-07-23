using MealMate.Domain.Common;

namespace MealMate.Domain.CentralAccounting;

public sealed class FinancialSetting : AuditableEntity
{
    public string SettingKey { get; set; } = default!;

    public Guid? RuleVersionId { get; set; }

    public string ValueJson { get; set; } = default!;

    public bool IsActive { get; set; } = true;
}
