using MealMate.Domain.Common;
using MealMate.Domain.ValueObjects;

namespace MealMate.Domain.CentralAccounting;

public sealed class OperatingExpense : AuditableEntity
{
    public string Category { get; set; } = default!;

    public Money Amount { get; set; } = default!;

    public DateOnly IncurredOn { get; set; }

    public string? Description { get; set; }

    public string Status { get; set; } = default!;
}
