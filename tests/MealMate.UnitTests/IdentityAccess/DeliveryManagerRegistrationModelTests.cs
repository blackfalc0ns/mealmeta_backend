using System;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class DeliveryManagerRegistrationModelTests
{
    [Fact]
    public void DeliveryManagerRegistration_ShouldHoldAllRequiredFieldsAndDefaultStatus()
    {
        var restaurantId = Guid.NewGuid();
        var reg = new DeliveryManagerRegistration
        {
            RestaurantId = restaurantId,
            FullName = "نورة خالد العجمي",
            Phone = "+96551234567",
            Email = "noura.dispatch@burgerfactory.kw",
            NationalId = "296051201234",
            NationalIdExpiryUtc = DateTime.UtcNow.AddYears(2),
            ContractExpiryUtc = DateTime.UtcNow.AddYears(1)
        };

        Assert.Equal(restaurantId, reg.RestaurantId);
        Assert.Equal("نورة خالد العجمي", reg.FullName);
        Assert.Equal("+96551234567", reg.Phone);
        Assert.Equal("noura.dispatch@burgerfactory.kw", reg.Email);
        Assert.Equal("296051201234", reg.NationalId);
        Assert.NotNull(reg.NationalIdExpiryUtc);
        Assert.NotNull(reg.ContractExpiryUtc);
        Assert.Equal(VerificationStatus.Submitted, reg.Status);
        Assert.Equal(DeliveryManagerCoverageType.AllRestaurantCoverage, reg.CoverageType);

        reg.CoverageType = DeliveryManagerCoverageType.Governorate;
        reg.CoverageScopeId = "gov-hawally";
        reg.CoverageScopeName = "حولي";
        Assert.Equal(DeliveryManagerCoverageType.Governorate, reg.CoverageType);
        Assert.Equal("gov-hawally", reg.CoverageScopeId);
        Assert.Equal("حولي", reg.CoverageScopeName);
    }
}
