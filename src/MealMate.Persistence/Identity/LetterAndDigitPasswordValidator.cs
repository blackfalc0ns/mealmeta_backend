using Microsoft.AspNetCore.Identity;

namespace MealMate.Persistence.Identity;

public sealed class LetterAndDigitPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public const string RequiredMessage =
        "Password must contain at least one letter and one number.";

    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        var hasLetter = password?.Any(char.IsLetter) == true;
        var hasDigit = password?.Any(char.IsDigit) == true;

        return Task.FromResult(hasLetter && hasDigit
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequiresLetterAndDigit",
                Description = RequiredMessage,
            }));
    }
}
