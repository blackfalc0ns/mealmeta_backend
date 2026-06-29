using MealMate.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace MealMate.Persistence.Identity;

/// <summary>
/// ASP.NET Core Identity user with MealMate-specific fields. Identity is an
/// infrastructure concern, so this type lives in Persistence, not Domain.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string? FullName { get; set; }

    public UserType UserType { get; set; }

    public AccountStatus AccountStatus { get; set; } = AccountStatus.Registered;

    /// <summary>Set when the user acts on behalf of a restaurant (ownership scope).</summary>
    public Guid? RestaurantId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }
}
