using MealMate.Domain.Common;

namespace MealMate.Domain.AdminDashboard;

public sealed class SystemSetting : AuditableEntity
{
    public string Key { get; set; } = default!;

    public string ValueJson { get; set; } = default!;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}
