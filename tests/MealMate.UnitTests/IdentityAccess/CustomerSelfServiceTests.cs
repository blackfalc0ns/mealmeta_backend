using FluentAssertions;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class CustomerSelfServiceTests
{
    [Theory]
    [InlineData(-91, 0)]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(0, 181)]
    public void Customer_update_validator_rejects_invalid_coordinates(
        decimal latitude,
        decimal longitude)
    {
        var command = new UpdateCustomerProfileCommand(
            null, null, null, null, latitude, longitude, null);

        var result = new UpdateCustomerProfileCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "Driver", "0500000000")]
    [InlineData("السائق", "", "0500000000")]
    [InlineData("السائق", "Driver", "")]
    public void Driver_update_validator_rejects_empty_required_values(
        string fullNameAr,
        string fullNameEn,
        string phone)
    {
        var command = new UpdateDriverRegistrationCommand(
            Guid.NewGuid(),
            fullNameAr,
            fullNameEn,
            phone,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        var result = new UpdateDriverRegistrationCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
    }
}
