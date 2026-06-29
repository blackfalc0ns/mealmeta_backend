using Microsoft.AspNetCore.Identity;

namespace MealMate.Persistence.Identity;

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
    }

    public ApplicationRole(string roleName)
        : base(roleName)
    {
    }

    public string? Description { get; set; }
}
