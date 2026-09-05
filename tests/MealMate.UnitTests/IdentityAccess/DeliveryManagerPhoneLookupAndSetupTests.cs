using MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class DeliveryManagerPhoneLookupAndSetupTests
{
    [Fact]
    public void LookupQueryValidator_ShouldFail_WhenPhoneIsEmpty()
    {
        var query = new DeliveryManagerPhoneLookupQuery("");
        var validator = new DeliveryManagerPhoneLookupQueryValidator();
        var result = validator.Validate(query);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void LookupQueryValidator_ShouldPass_WhenPhoneIsProvided()
    {
        var query = new DeliveryManagerPhoneLookupQuery("+96551234567");
        var validator = new DeliveryManagerPhoneLookupQueryValidator();
        var result = validator.Validate(query);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void FirstTimeSetupValidator_ShouldFail_WhenFieldsAreInvalid()
    {
        var command = new DeliveryManagerFirstTimeSetupCommand("", "12", "short");
        var validator = new DeliveryManagerFirstTimeSetupCommandValidator();
        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.Phone));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.OtpCode));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.Password));
    }

    [Fact]
    public void FirstTimeSetupValidator_ShouldPass_WhenFieldsAreValid()
    {
        var command = new DeliveryManagerFirstTimeSetupCommand("+96551234567", "123456", "SecurePass123!");
        var validator = new DeliveryManagerFirstTimeSetupCommandValidator();
        var result = validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
