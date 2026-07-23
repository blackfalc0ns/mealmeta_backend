using MealMate.Domain.Common;

namespace MealMate.Domain.ProgramsSubscriptions;

public sealed class Bundle : AuditableEntity
{
    public Guid ProgramId { get; set; }

    public Program Program { get; set; } = default!;

    public string Code { get; set; } = default!;

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public int MealsPerDay { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}
