using System;
using MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class AdminDeliveryManagerRegistrationsTests
{
    [Fact]
    public void DecisionValidator_ShouldFail_WhenActionIsInvalid()
    {
        var command = new AdminDeliveryManagerDecisionCommand(Guid.NewGuid(), "invalid-action", "Some reason");
        var validator = new AdminDeliveryManagerDecisionCommandValidator();
        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(command.Action));
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("request-changes")]
    public void DecisionValidator_ShouldPass_ForValidActions(string action)
    {
        var command = new AdminDeliveryManagerDecisionCommand(Guid.NewGuid(), action, "Valid reason provided");
        var validator = new AdminDeliveryManagerDecisionCommandValidator();
        var result = validator.Validate(command);

        Assert.True(result.IsValid);
    }
}
