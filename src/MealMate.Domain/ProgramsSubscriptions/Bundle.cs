using MealMate.Domain.Common;
using MealMate.Domain.Enums;

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

    public ProgramStatus Status { get; set; } = ProgramStatus.Draft;

    public string? StatusReason { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<PackageContentVersion> ContentVersions { get; set; } = new List<PackageContentVersion>();
}

