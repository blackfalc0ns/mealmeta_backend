using System;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class RegisterRestaurantDriverCommandTests
{
    [Fact]
    public void Validator_ShouldFail_WhenRequiredFieldsAreMissing()
    {
        var command = new RegisterRestaurantDriverCommand(
            FullNameAr: "",
            FullNameEn: "",
            Phone: "",
            Email: null,
            NationalId: "",
            NationalIdExpiry: DateTime.UtcNow.AddDays(-1), // past date
            VehiclePlate: "",
            VehicleModel: null,
            VehicleType: null,
            LicenseNumber: "",
            LicenseExpiry: DateTime.UtcNow.AddDays(-2), // past date
            VehicleLicenseExpiry: DateTime.UtcNow.AddDays(-3), // past date
            ContractExpiry: DateTime.UtcNow.AddDays(-4), // past date
            RestaurantId: null,
            NationalIdFrontStorageKey: "",
            NationalIdBackStorageKey: "",
            DrivingLicenseFrontStorageKey: "",
            DrivingLicenseBackStorageKey: "",
            VehicleRegistrationStorageKey: "",
            ContractStorageKey: "");

        var validator = new RegisterRestaurantDriverCommandValidator();
        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.FullNameAr));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.Phone));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.NationalId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.NationalIdExpiry));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.LicenseExpiry));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.VehicleLicenseExpiry));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.ContractExpiry));
    }

    [Fact]
    public void Validator_ShouldPass_WhenAllFieldsAndFutureExpiriesAreValid()
    {
        var command = new RegisterRestaurantDriverCommand(
            FullNameAr: "أحمد محمود",
            FullNameEn: "Ahmed Mahmoud",
            Phone: "+966501234567",
            Email: "driver@restaurant.com",
            NationalId: "1020304050",
            NationalIdExpiry: DateTime.UtcNow.AddYears(2),
            VehiclePlate: "1234 ABC",
            VehicleModel: "Toyota Corolla 2022",
            VehicleType: "Car",
            LicenseNumber: "DL-998877",
            LicenseExpiry: DateTime.UtcNow.AddYears(3),
            VehicleLicenseExpiry: DateTime.UtcNow.AddYears(1),
            ContractExpiry: DateTime.UtcNow.AddYears(1),
            RestaurantId: Guid.NewGuid(),
            NationalIdFrontStorageKey: "uploads/drivers/id-front.jpg",
            NationalIdBackStorageKey: "uploads/drivers/id-back.jpg",
            DrivingLicenseFrontStorageKey: "uploads/drivers/lic-front.jpg",
            DrivingLicenseBackStorageKey: "uploads/drivers/lic-back.jpg",
            VehicleRegistrationStorageKey: "uploads/drivers/veh-reg.jpg",
            ContractStorageKey: "uploads/drivers/contract.pdf");

        var validator = new RegisterRestaurantDriverCommandValidator();
        var result = validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
