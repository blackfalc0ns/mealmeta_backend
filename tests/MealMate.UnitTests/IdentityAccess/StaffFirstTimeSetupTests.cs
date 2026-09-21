using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Application.Features.IdentityAccess.StaffAuth;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class StaffFirstTimeSetupTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var domainDispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, domainDispatcher);
    }

    private static UserAccountDto CreateStaffUser(
        Guid id,
        string phone,
        UserType userType,
        AccountStatus accountStatus,
        bool hasSetPassword) => new(
        Id: id,
        Email: $"{phone.Replace("+", "")}@mealmate.local",
        PhoneNumber: phone,
        FullName: "Test Staff",
        UserType: userType,
        AccountStatus: accountStatus,
        RestaurantId: Guid.NewGuid(),
        CreatedAtUtc: DateTime.UtcNow,
        LastLoginAtUtc: null,
        EmailConfirmed: true,
        PhoneNumberConfirmed: true,
        HasSetPassword: hasSetPassword);

    [Theory]
    [InlineData(StaffRoles.DeliveryManager, UserType.DeliveryManager)]
    [InlineData(StaffRoles.Driver, UserType.Driver)]
    public async Task StaffPhoneLookup_ShouldReturnIsFirstTimeSetupTrue_WhenUserHasNotSetPassword(
        string role,
        UserType userType)
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var phone = "+96590001122";
        var userId = Guid.NewGuid();
        var user = CreateStaffUser(userId, phone, userType, AccountStatus.Active, hasSetPassword: false);

        var users = Substitute.For<IUserAccountService>();
        users.FindByPhoneAsync(phone, Arg.Any<CancellationToken>()).Returns(user);

        var otp = Substitute.For<IOtpService>();
        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        var audit = Substitute.For<IAuditLogger>();
        var messages = NullLocalizedMessageProvider.Instance;

        var handler = new StaffPhoneLookupQueryHandler(users, db, otp, audit, messages);

        // Act
        var result = await handler.Handle(new StaffPhoneLookupQuery(phone, role), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Exists.Should().BeTrue();
        result.Value.IsFirstTimeSetup.Should().BeTrue("user has not set their password yet in the database");
        await otp.Received(1).SendAsync(
            Arg.Is<OtpSendRequest>(r => r.Destination == phone && r.Purpose == OtpPurposes.VerifyPhone && r.UserId == userId),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StaffRoles.DeliveryManager, UserType.DeliveryManager)]
    [InlineData(StaffRoles.Driver, UserType.Driver)]
    public async Task StaffPhoneLookup_ShouldReturnIsFirstTimeSetupFalse_WhenUserHasSetPassword(
        string role,
        UserType userType)
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var phone = "+96590001122";
        var userId = Guid.NewGuid();
        var user = CreateStaffUser(userId, phone, userType, AccountStatus.Active, hasSetPassword: true);

        var users = Substitute.For<IUserAccountService>();
        users.FindByPhoneAsync(phone, Arg.Any<CancellationToken>()).Returns(user);

        var otp = Substitute.For<IOtpService>();
        var audit = Substitute.For<IAuditLogger>();
        var messages = NullLocalizedMessageProvider.Instance;

        var handler = new StaffPhoneLookupQueryHandler(users, db, otp, audit, messages);

        // Act
        var result = await handler.Handle(new StaffPhoneLookupQuery(phone, role), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Exists.Should().BeTrue();
        result.Value.IsFirstTimeSetup.Should().BeFalse("user already set their password and it is stored in the database");
        await otp.DidNotReceive().SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StaffSetPassword_ShouldSetHasPasswordTrue_AndActivateAccount()
    {
        // Arrange
        var phone = "+96590001122";
        var userId = Guid.NewGuid();
        var user = CreateStaffUser(userId, phone, UserType.DeliveryManager, AccountStatus.Registered, hasSetPassword: false);

        var users = Substitute.For<IUserAccountService>();
        users.FindByPhoneAsync(phone, Arg.Any<CancellationToken>()).Returns(user);
        users.ResetPasswordAsync(userId, "token_123", "NewSecurePassword123!", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var sessions = Substitute.For<IAuthSessionService>();
        var dummyTokens = new AuthTokensDto("access_token", DateTime.UtcNow.AddMinutes(15), "refresh_token", DateTime.UtcNow.AddDays(7));
        var dummySession = new AuthSessionDto(
            UserId: userId,
            Email: user.Email,
            PhoneNumber: user.PhoneNumber,
            FullName: user.FullName,
            UserType: user.UserType,
            AccountStatus: AccountStatus.Active,
            RestaurantId: user.RestaurantId,
            Roles: [AppRoles.DeliveryManager],
            Permissions: [],
            Tokens: dummyTokens);
        sessions.IssueSessionAsync(userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<AuthSessionDto>.Success(dummySession));

        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var audit = Substitute.For<IAuditLogger>();
        var db = Substitute.For<IAppDbContext>();

        var handler = new StaffSetPasswordCommandHandler(users, sessions, challenges, audit, db);

        var command = new StaffSetPasswordCommand(
            Phone: phone,
            Role: StaffRoles.DeliveryManager,
            VerificationToken: "token_123",
            NewPassword: "NewSecurePassword123!",
            ConfirmPassword: "NewSecurePassword123!");

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await users.Received(1).SetHasPasswordAsync(userId, true, Arg.Any<CancellationToken>());
        await users.Received(1).UpdateAccountStatusAsync(userId, AccountStatus.Active, Arg.Any<CancellationToken>());
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
