using MealMate.Domain.Common;
using MealMate.Domain.Enums;

namespace MealMate.Domain.RestaurantOperations;

public enum MenuPlanSource { AdminTemplate = 1, RestaurantCreated = 2 }

public sealed class MenuPlan : AuditableEntity
{
    public Guid? RestaurantId { get; set; }
    public string NameAr { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    public MenuPlanSource Source { get; set; }
    public bool IsTemplate { get; set; }
    public ScheduleMode Mode { get; set; } = ScheduleMode.Cycle;
    public ICollection<MenuPlanVersion> Versions { get; set; } = new List<MenuPlanVersion>();
    public ICollection<MenuPlanProgram> Programs { get; set; } = new List<MenuPlanProgram>();
}

public sealed class MenuPlanVersion : AuditableEntity
{
    public Guid MenuPlanId { get; set; }
    public MenuPlan MenuPlan { get; set; } = default!;
    public int VersionNumber { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public VerificationStatus Status { get; set; } = VerificationStatus.Draft;
    public string? ReviewNotes { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public bool IsPaused { get; set; }
    public DateTime? PausedAtUtc { get; set; }
    /// <summary>JSON array of MenuDay IDs for order after the current loop. Null/empty = none.</summary>
    public string? PendingSequenceJson { get; set; }
    public ICollection<MenuDay> Days { get; set; } = new List<MenuDay>();
    public ICollection<WeekdayMealAssignment> WeekdayAssignments { get; set; } = new List<WeekdayMealAssignment>();
}

public sealed class MenuDay : AuditableEntity
{
    public Guid MenuPlanVersionId { get; set; }
    public MenuPlanVersion MenuPlanVersion { get; set; } = default!;
    public int DayNumber { get; set; }
    public string NameAr { get; set; } = default!;
    public string NameEn { get; set; } = default!;
    /// <summary>False for deferred new days until pending sequence activates.</summary>
    public bool IsActiveInCycle { get; set; } = true;
    public ICollection<MenuDayMeal> Meals { get; set; } = new List<MenuDayMeal>();
}

public sealed class MenuDayMeal : AuditableEntity
{
    public Guid MenuDayId { get; set; }
    public MenuDay MenuDay { get; set; } = default!;
    public Guid MealId { get; set; }
    public Meal Meal { get; set; } = default!;
    public Guid ProgramId { get; set; }
    public string Slot { get; set; } = default!;
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class WeekdayMealAssignment : AuditableEntity
{
    public Guid MenuPlanVersionId { get; set; }
    public MenuPlanVersion MenuPlanVersion { get; set; } = default!;
    public DayOfWeek DayOfWeek { get; set; }
    public Guid ProgramId { get; set; }
    public Guid MealId { get; set; }
    public Meal Meal { get; set; } = default!;
    public string Slot { get; set; } = default!;
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class MenuPlanProgram : AuditableEntity
{
    public Guid MenuPlanId { get; set; }
    public MenuPlan MenuPlan { get; set; } = default!;
    public Guid ProgramId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsPrimaryOwner { get; set; }
}

public sealed class MenuPlanAudit : AuditableEntity
{
    public Guid MenuPlanVersionId { get; set; }
    public string Action { get; set; } = default!;
    public string? Notes { get; set; }
    public Guid? PerformedByUserId { get; set; }
}

public sealed class ProgramMealSlotRequirement : AuditableEntity
{
    public Guid ProgramId { get; set; }
    public string Slot { get; set; } = default!;
    public bool IsRequired { get; set; } = true;
}

/// <summary>
/// Admin-owned per-program meal nutrition envelopes (MAIN/SNACK), sourced from Nutrition Spec.
/// </summary>
public sealed class ProgramNutritionStandard : AuditableEntity
{
    public Guid ProgramId { get; set; }

    /// <summary>MAIN (breakfast/lunch/dinner) or SNACK.</summary>
    public string MealCategory { get; set; } = default!;

    public decimal KcalMin { get; set; }
    public decimal KcalMax { get; set; }
    public decimal ProteinGMin { get; set; }
    public decimal ProteinGMax { get; set; }

    /// <summary>TOTAL or NET.</summary>
    public string CarbMode { get; set; } = "TOTAL";

    public decimal CarbsGMin { get; set; }
    public decimal CarbsGMax { get; set; }
    public decimal FatGMin { get; set; }
    public decimal FatGMax { get; set; }
    public decimal FiberGMin { get; set; }

    public decimal CookedProteinGMin { get; set; }
    public decimal CookedProteinGMax { get; set; }
    public decimal CookedCarbsGMin { get; set; }
    public decimal CookedCarbsGMax { get; set; }

    /// <summary>Optional daily Net Carbs cap (Keto). Same value on MAIN and SNACK rows.</summary>
    public decimal? KetoDayNetCarbsLimit { get; set; }

    public string StandardVersion { get; set; } = "NUTRITION-v1.0";

    public bool IsActive { get; set; } = true;
}
