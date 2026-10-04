using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;
using MealMate.Domain.Enums;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
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

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static UserAccountDto ManagerDto(
        AccountStatus status = AccountStatus.Active,
        bool hasSetPassword = false) => new(
        Guid.NewGuid(),
        "manager.dispatch@burgerfactory.kw",
        "+96551234567",
        "نورة خالد العجمي",
        UserType.DeliveryManager,
        status,
        Guid.NewGuid(),
        EmailConfirmed: false,
        PhoneNumberConfirmed: false,
        DateTime.UtcNow,
        LastLoginAtUtc: null,
        HasSetPassword: hasSetPassword);

    [Fact]
    public async Task Lookup_ActiveManagerWithoutPassword_SendsOtpAndReportsFirstTimeSetup()
    {
        var db = CreateDb();
        var manager = ManagerDto(status: AccountStatus.Active, hasSetPassword: false);

        var users = Substitute.For<IUserAccountService>();
        users.FindByPhoneAsync(manager.PhoneNumber!, Arg.Any<CancellationToken>()).Returns(manager);

        var otp = Substitute.For<IOtpService>();
        var audit = Substitute.For<IAuditLogger>();

        var handler = new DeliveryManagerPhoneLookupQueryHandler(users, db, otp, audit);

        var result = await handler.Handle(new DeliveryManagerPhoneLookupQuery("+96551234567"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsFirstTimeSetup.Should().BeTrue(
            "an Active account without a password still needs first-time mobile setup");

        await otp.Received(1).SendAsync(
            Arg.Is<OtpSendRequest>(r => r.Destination == "+96551234567" && r.Purpose == OtpPurposes.VerifyPhone),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lookup_ActiveManagerWithPassword_DoesNotSendOtp()
    {
        var db = CreateDb();
        var manager = ManagerDto(status: AccountStatus.Active, hasSetPassword: true);

        var users = Substitute.For<IUserAccountService>();
        users.FindByPhoneAsync(manager.PhoneNumber!, Arg.Any<CancellationToken>()).Returns(manager);

        var otp = Substitute.For<IOtpService>();
        var audit = Substitute.For<IAuditLogger>();

        var handler = new DeliveryManagerPhoneLookupQueryHandler(users, db, otp, audit);

        var result = await handler.Handle(new DeliveryManagerPhoneLookupQuery("+96551234567"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsFirstTimeSetup.Should().BeFalse();
        await otp.DidNotReceive().SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FirstTimeSetup_AfterOtpVerification_IssuesSessionWithoutChangingAccountStatus()
    {
        var db = CreateDb();
        var manager = ManagerDto(status: AccountStatus.Suspended, hasSetPassword: false);

        var users = Substitute.For<IUserAccountService>();
        users.FindByPhoneAsync(manager.PhoneNumber!, Arg.Any<CancellationToken>()).Returns(manager);
        users.GeneratePasswordResetTokenAsync(manager.Id, Arg.Any<CancellationToken>()).Returns("reset-token");
        users.ResetPasswordAsync(manager.Id, "reset-token", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var otp = Substitute.For<IOtpService>();
        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        var sessions = Substitute.For<IAuthSessionService>();
        sessions.IssueSessionAsync(manager.Id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AuthSessionDto(
                manager.Id,
                manager.Email,
                manager.PhoneNumber,
                manager.FullName,
                manager.UserType,
                manager.AccountStatus,
                manager.RestaurantId,
                new[] { "DeliveryManager" },
                Array.Empty<string>(),
                new AuthTokensDto("access-token", DateTime.UtcNow.AddHours(1), "refresh-token", DateTime.UtcNow.AddDays(14)))));

        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var audit = Substitute.For<IAuditLogger>();

        var handler = new DeliveryManagerFirstTimeSetupCommandHandler(users, otp, sessions, challenges, audit, db);

        var result = await handler.Handle(
            new DeliveryManagerFirstTimeSetupCommand("+96551234567", "123456", "SecurePass123!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await users.Received(1).SetPhoneConfirmedAsync(manager.Id, true, Arg.Any<CancellationToken>());
        await users.DidNotReceive().UpdateAccountStatusAsync(
            manager.Id, AccountStatus.Active, Arg.Any<CancellationToken>());

        db.ChangeTracker.Clear();
        (await db.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task FirstTimeSetup_SuspendedAccount_KeepsSuspendedStatusAfterSetup()
    {
        var db = CreateDb();
        var manager = ManagerDto(status: AccountStatus.Suspended, hasSetPassword: false);

        var users = Substitute.For<IUserAccountService>();
        users.FindByPhoneAsync(manager.PhoneNumber!, Arg.Any<CancellationToken>()).Returns(manager);
        users.GeneratePasswordResetTokenAsync(manager.Id, Arg.Any<CancellationToken>()).Returns("reset-token");
        users.ResetPasswordAsync(manager.Id, "reset-token", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var otp = Substitute.For<IOtpService>();
        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        var sessions = Substitute.For<IAuthSessionService>();
        sessions.IssueSessionAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AuthSessionDto(
                manager.Id,
                manager.Email,
                manager.PhoneNumber,
                manager.FullName,
                manager.UserType,
                AccountStatus.Suspended,
                manager.RestaurantId,
                new[] { "DeliveryManager" },
                Array.Empty<string>(),
                new AuthTokensDto("access-token", DateTime.UtcNow.AddHours(1), "refresh-token", DateTime.UtcNow.AddDays(14)))));

        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var audit = Substitute.For<IAuditLogger>();

        var handler = new DeliveryManagerFirstTimeSetupCommandHandler(users, otp, sessions, challenges, audit, db);

        var result = await handler.Handle(
            new DeliveryManagerFirstTimeSetupCommand("+96551234567", "123456", "SecurePass123!"),
            CancellationToken.None);

        // The session token carries the real suspended status and the operational
        // authorization handler blocks any non-Active account on every request.
        result.IsSuccess.Should().BeTrue();
        result.Value.AccountStatus.Should().Be(AccountStatus.Suspended,
            "setup must never silently reactivate a suspended account");

        await users.DidNotReceive().UpdateAccountStatusAsync(
            Arg.Any<Guid>(), Arg.Any<AccountStatus>(), Arg.Any<CancellationToken>());
    }
}
