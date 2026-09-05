using MealMate.Application.Features.IdentityAccess.Auth;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class DriverPhoneLookupAndSetupTests
{
    [Fact]
    public void LookupQueryValidator_ShouldFail_WhenPhoneIsEmpty()
    {
        var query = new DriverPhoneLookupQuery("");
        var validator = new DriverPhoneLookupQueryValidator();
        var result = validator.Validate(query);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void LookupQueryValidator_ShouldPass_WhenPhoneIsProvided()
    {
        var query = new DriverPhoneLookupQuery("+966501234567");
        var validator = new DriverPhoneLookupQueryValidator();
        var result = validator.Validate(query);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void FirstTimeSetupValidator_ShouldFail_WhenFieldsAreInvalid()
    {
        var command = new DriverFirstTimeSetupCommand("", "123", "short");
        var validator = new DriverFirstTimeSetupCommandValidator();
        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.Phone));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.OtpCode));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.Password));
    }

    [Fact]
    public void FirstTimeSetupValidator_ShouldPass_WhenFieldsAreValid()
    {
        var command = new DriverFirstTimeSetupCommand("+966501234567", "123456", "SecurePass123!");
        var validator = new DriverFirstTimeSetupCommandValidator();
        var result = validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
