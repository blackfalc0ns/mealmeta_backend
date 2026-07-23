using MealMate.Domain.Common;

namespace MealMate.Domain.RestaurantOperations;

public sealed class Menu : AuditableEntity
{
    public Guid RestaurantId { get; set; }

    public Guid? ProgramId { get; set; }

    public string NameAr { get; set; } = default!;

    public string NameEn { get; set; } = default!;

    public bool IsActive { get; set; } = true;

    public ICollection<Meal> Meals { get; set; } = new List<Meal>();
}
