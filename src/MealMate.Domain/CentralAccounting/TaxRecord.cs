using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CentralAccounting;

public sealed class TaxRecord : AuditableEntity
{
    public string Code { get; set; } = default!;

    public string Name { get; set; } = default!;

    public Percentage Rate { get; set; } = default!;

    public bool IsActive { get; set; } = true;
}
