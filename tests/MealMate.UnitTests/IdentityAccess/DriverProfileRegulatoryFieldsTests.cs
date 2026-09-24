using System;
using FluentAssertions;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public sealed class DriverProfileRegulatoryFieldsTests
{
    [Fact]
    public void DriverProfile_ShouldStoreAndExposeRegulatoryAndVehicleFields()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var nationalIdExpiry = now.AddYears(2);
        var licenseExpiry = now.AddYears(1);
        var vehicleRegExpiry = now.AddMonths(10);
        var contractExpiry = now.AddMonths(6);
        var dob = new DateTime(1993, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var profile = new DriverProfile
        {
            UserId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            FullNameAr = "محمد عبد الله",
            FullNameEn = "Mohammed Abdullah",
            NationalId = "293041512345",
            NationalIdExpiryUtc = nationalIdExpiry,
            LicenseNumber = "DL-998877",
            LicenseExpiryUtc = licenseExpiry,
            VehiclePlate = "12-3456",
            VehicleType = "Car",
            VehicleModel = "Toyota Camry",
            VehicleYear = 2023,
            VehicleColor = "White",
            IsVehicleOwned = true,
            VehicleRegistrationExpiryUtc = vehicleRegExpiry,
            ContractExpiryUtc = contractExpiry,
            Nationality = "Kuwaiti",
            DateOfBirthUtc = dob,
            VerificationStatus = VerificationStatus.Approved,
            IsAvailable = true
        };

        // Assert
        profile.NationalIdExpiryUtc.Should().Be(nationalIdExpiry);
        profile.LicenseExpiryUtc.Should().Be(licenseExpiry);
        profile.VehicleRegistrationExpiryUtc.Should().Be(vehicleRegExpiry);
        profile.ContractExpiryUtc.Should().Be(contractExpiry);
        profile.VehicleYear.Should().Be(2023);
        profile.VehicleColor.Should().Be("White");
        profile.IsVehicleOwned.Should().BeTrue();
        profile.Nationality.Should().Be("Kuwaiti");
        profile.DateOfBirthUtc.Should().Be(dob);
    }
}
