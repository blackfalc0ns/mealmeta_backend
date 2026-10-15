using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.FamilyManager;
using MealMate.Application.Features.IdentityAccess.Customers;
using MealMate.Application.Features.IdentityAccess.DriverRegistration;
using MealMate.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class CustomerSelfServiceTests
{
    [Fact]
    public async Task Customer_owns_password_change_independently_of_family_grants()
    {
        var userId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.UserType.Returns(UserType.Customer);
        var users = Substitute.For<IUserAccountService>();
        users.ChangePasswordAsync(userId, "OldPass123", "NewPass123", Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();
        var familyPolicy = Substitute.For<IFamilyActionPolicy>();
        var handler = new ChangeCustomerPasswordCommandHandler(
            Substitute.For<IAppDbContext>(), currentUser, users, sessions, audit, familyPolicy);

        var result = await handler.Handle(new ChangeCustomerPasswordCommand("OldPass123", "NewPass123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await users.Received(1).ChangePasswordAsync(userId, "OldPass123", "NewPass123", Arg.Any<CancellationToken>());
        await familyPolicy.DidNotReceiveWithAnyArgs().EvaluateAsync(default, default, default, default);
        await sessions.Received(1).RevokeAllUserTokensAsync(userId, Arg.Any<CancellationToken>());
    }

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
