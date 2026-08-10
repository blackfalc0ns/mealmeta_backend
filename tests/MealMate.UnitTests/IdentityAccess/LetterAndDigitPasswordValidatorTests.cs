using FluentAssertions;
using MealMate.Persistence.Identity;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class LetterAndDigitPasswordValidatorTests
{
    private readonly LetterAndDigitPasswordValidator _validator = new();

    [Theory]
    [InlineData("12345678")]
    [InlineData("abcdefgh")]
    public async Task Validate_rejects_passwords_without_letters_and_numbers(string password)
    {
        var result = await _validator.ValidateAsync(null!, new ApplicationUser(), password);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle(error =>
            error.Code == "PasswordRequiresLetterAndDigit"
            && error.Description == LetterAndDigitPasswordValidator.RequiredMessage);
    }

    [Theory]
    [InlineData("abc12345")]
    [InlineData("Password123")]
    public async Task Validate_accepts_passwords_with_letters_and_numbers(string password)
    {
        var result = await _validator.ValidateAsync(null!, new ApplicationUser(), password);

        result.Succeeded.Should().BeTrue();
    }
}
