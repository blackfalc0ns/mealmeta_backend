using System;
using MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class RegisterRestaurantDeliveryManagerCommandTests
{
    [Fact]
    public void Validator_ShouldFail_WhenRequiredFieldsAreMissingOrExpiriesInPast()
    {
        var command = new RegisterRestaurantDeliveryManagerCommand(
            FullName: "",
            Phone: "",
            Email: "not-an-email",
            NationalId: "",
            NationalIdExpiry: DateTime.UtcNow.AddDays(-1), // past date
            ContractExpiry: DateTime.UtcNow.AddDays(-2),   // past date
            CoverageType: DeliveryManagerCoverageType.Governorate,
            CoverageScopeId: "",
            CoverageScopeName: "",
            RestaurantId: null,
            NationalIdFrontStorageKey: "",
            NationalIdBackStorageKey: "",
            ContractStorageKey: "");

        var validator = new RegisterRestaurantDeliveryManagerCommandValidator();
        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.FullName));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.Phone));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.Email));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.NationalId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.NationalIdExpiry));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.ContractExpiry));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.NationalIdFrontStorageKey));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.NationalIdBackStorageKey));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.ContractStorageKey));
    }

    [Fact]
    public void Validator_ShouldPass_WhenAllFieldsAndFutureExpiriesAreValid()
    {
        var command = new RegisterRestaurantDeliveryManagerCommand(
            FullName: "نورة خالد العجمي",
            Phone: "+96551234567",
            Email: "noura.dispatch@burgerfactory.kw",
            NationalId: "296051201234",
            NationalIdExpiry: DateTime.UtcNow.AddYears(2),
            ContractExpiry: DateTime.UtcNow.AddYears(1),
            CoverageType: DeliveryManagerCoverageType.Governorate,
            CoverageScopeId: "gov-hawally",
            CoverageScopeName: "حولي",
            RestaurantId: Guid.NewGuid(),
            NationalIdFrontStorageKey: "uploads/delivery-managers/id-front.jpg",
            NationalIdBackStorageKey: "uploads/delivery-managers/id-back.jpg",
            ContractStorageKey: "uploads/delivery-managers/contract.pdf");

        var validator = new RegisterRestaurantDeliveryManagerCommandValidator();
        var result = validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
