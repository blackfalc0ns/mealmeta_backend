using System;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class DriverRegistrationModelTests
{
    [Fact]
    public void DriverRegistration_ShouldSupportVehicleModelAndExpiries()
    {
        var reg = new DriverRegistration
        {
            VehicleModel = "Toyota Corolla 2022",
            NationalIdExpiryUtc = DateTime.UtcNow.AddYears(2),
            LicenseExpiryUtc = DateTime.UtcNow.AddYears(3),
            VehicleRegistrationExpiryUtc = DateTime.UtcNow.AddYears(1),
            ContractExpiryUtc = DateTime.UtcNow.AddYears(1)
        };

        var profile = new DriverProfile
        {
            VehicleModel = "Toyota Corolla 2022"
        };

        Assert.Equal("Toyota Corolla 2022", reg.VehicleModel);
        Assert.Equal("Toyota Corolla 2022", profile.VehicleModel);
        Assert.NotNull(reg.NationalIdExpiryUtc);
        Assert.NotNull(reg.LicenseExpiryUtc);
        Assert.NotNull(reg.VehicleRegistrationExpiryUtc);
        Assert.NotNull(reg.ContractExpiryUtc);
        Assert.Equal(19, (int)DocumentType.DrivingLicenseFront);
        Assert.Equal(20, (int)DocumentType.DrivingLicenseBack);
    }
}
