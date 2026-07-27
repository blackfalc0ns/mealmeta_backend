using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class LoginCommandHandlerTests
{
    private static LoginCommandHandler CreateHandler(
        IUserAccountService users,
        IAuthSessionService sessions,
        IAuthLoginChallengeService? challenges = null,
        ITotpAuthenticatorService? totp = null,
        IAuditLogger? audit = null,
        IUnitOfWork? unitOfWork = null)
    {
        challenges ??= Substitute.For<IAuthLoginChallengeService>();
        totp ??= Substitute.For<ITotpAuthenticatorService>();
        audit ??= Substitute.For<IAuditLogger>();
        unitOfWork ??= Substitute.For<IUnitOfWork>();
        return new LoginCommandHandler(users, sessions, challenges, totp, audit, unitOfWork);
    }

    [Fact]
    public async Task Login_rejects_invalid_password()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var user = new UserAccountDto(
            Guid.NewGuid(), "a@test.com", null, "A", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null);

        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(user);
        users.VerifyPasswordOrDummyAsync(user.Id, "bad", Arg.Any<CancellationToken>()).Returns(false);

        var handler = CreateHandler(users, sessions, audit: audit, unitOfWork: unitOfWork);
        var result = await handler.Handle(new LoginCommand("a@test.com", null, "bad"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidCredentials);
        await users.Received(1).RegisterFailedLoginAsync(user.Id, Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_rejects_suspended_account()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();

        var user = new UserAccountDto(
            Guid.NewGuid(), "a@test.com", null, "A", UserType.Admin, AccountStatus.Suspended,
            null, true, false, DateTime.UtcNow, null);

        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(user);
        users.VerifyPasswordOrDummyAsync(user.Id, "Password1", Arg.Any<CancellationToken>()).Returns(true);

        var handler = CreateHandler(users, sessions);
        var result = await handler.Handle(new LoginCommand("a@test.com", null, "Password1"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.AccountSuspended);
    }

    [Fact]
    public async Task Login_admin_without_totp_returns_enroll_challenge_without_session()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();
        var audit = Substitute.For<IAuditLogger>();

        var user = new UserAccountDto(
            Guid.NewGuid(), "a@test.com", null, "A", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null);

        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(user);
        users.VerifyPasswordOrDummyAsync(user.Id, "Password1", Arg.Any<CancellationToken>()).Returns(true);
        totp.IsEnabledAsync(user.Id, Arg.Any<CancellationToken>()).Returns(false);
        users.GetRolesAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new List<string> { "SuperAdmin" });
        var challengeExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
        challenges.CreateAsync(Arg.Any<CreateLoginChallengeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreatedLoginChallenge(
                Guid.NewGuid(),
                "challenge-token",
                LoginChallengeSteps.EnrollTotp,
                user.Id,
                challengeExpiresAtUtc));

        var handler = CreateHandler(users, sessions, challenges, totp, audit);
        var result = await handler.Handle(new LoginCommand("a@test.com", null, "Password1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsAuthenticated.Should().BeFalse();
        result.Value.LoginChallengeToken.Should().Be("challenge-token");
        result.Value.LoginChallengeExpiresAtUtc.Should().Be(challengeExpiresAtUtc);
        result.Value.NextStep.Should().Be(LoginChallengeSteps.EnrollTotp);
        result.Value.AccessToken.Should().BeNull();
        result.Value.Roles.Should().Contain("SuperAdmin");
        await sessions.DidNotReceive().IssueSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "AdminLoginMfaRequired"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_admin_with_totp_returns_verify_challenge_without_session()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();

        var user = new UserAccountDto(
            Guid.NewGuid(), "a@test.com", null, "A", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null);

        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(user);
        users.VerifyPasswordOrDummyAsync(user.Id, "Password1", Arg.Any<CancellationToken>()).Returns(true);
        totp.IsEnabledAsync(user.Id, Arg.Any<CancellationToken>()).Returns(true);
        users.GetRolesAsync(user.Id, Arg.Any<CancellationToken>()).Returns(new List<string> { "SuperAdmin" });
        var challengeExpiresAtUtc = DateTime.UtcNow.AddMinutes(5);
        challenges.CreateAsync(Arg.Any<CreateLoginChallengeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreatedLoginChallenge(
                Guid.NewGuid(),
                "verify-token",
                LoginChallengeSteps.VerifyTotp,
                user.Id,
                challengeExpiresAtUtc));

        var handler = CreateHandler(users, sessions, challenges, totp);
        var result = await handler.Handle(new LoginCommand("a@test.com", null, "Password1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.NextStep.Should().Be(LoginChallengeSteps.VerifyTotp);
        result.Value.LoginChallengeToken.Should().Be("verify-token");
        result.Value.LoginChallengeExpiresAtUtc.Should().Be(challengeExpiresAtUtc);
        result.Value.AccessToken.Should().BeNull();
        await sessions.DidNotReceive().IssueSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_rejects_closed_account()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();

        var user = new UserAccountDto(
            Guid.NewGuid(), "a@test.com", null, "A", UserType.Customer, AccountStatus.Closed,
            null, true, false, DateTime.UtcNow, null);

        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(user);
        users.VerifyPasswordOrDummyAsync(user.Id, "Password1", Arg.Any<CancellationToken>()).Returns(true);

        var handler = CreateHandler(users, sessions);
        var result = await handler.Handle(new LoginCommand("a@test.com", null, "Password1"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.AccountClosed);
        await sessions.DidNotReceive().IssueSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_rejects_locked_out_account_with_generic_invalid_credentials()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var user = new UserAccountDto(
            Guid.NewGuid(), "a@test.com", null, "A", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null);

        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(user);
        users.VerifyPasswordOrDummyAsync(user.Id, "Password1", Arg.Any<CancellationToken>()).Returns(true);
        users.IsLockedOutAsync(user.Id, Arg.Any<CancellationToken>()).Returns(true);

        var handler = CreateHandler(users, sessions, audit: audit, unitOfWork: unitOfWork);
        var result = await handler.Handle(new LoginCommand("a@test.com", null, "Password1"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidCredentials);
        await users.Received(1).VerifyPasswordOrDummyAsync(user.Id, "Password1", Arg.Any<CancellationToken>());
        await users.DidNotReceive().RegisterFailedLoginAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await sessions.DidNotReceive().IssueSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "LoginFailed" && e.EntityId == user.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_with_unknown_user_returns_invalid_credentials_and_audits_without_enumeration()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        users.FindByEmailAsync("nobody@test.com", Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);
        users.VerifyPasswordOrDummyAsync(null, "Password1", Arg.Any<CancellationToken>()).Returns(false);

        var handler = CreateHandler(users, sessions, audit: audit, unitOfWork: unitOfWork);
        var result = await handler.Handle(new LoginCommand("nobody@test.com", null, "Password1"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidCredentials);
        await users.Received(1).VerifyPasswordOrDummyAsync(null, "Password1", Arg.Any<CancellationToken>());
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "LoginFailed" && e.EntityId == null),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await users.DidNotReceive().RegisterFailedLoginAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_by_phone_resolves_user_and_issues_session()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();

        var user = new UserAccountDto(
            Guid.NewGuid(), null, "+201000000000", "A", UserType.Customer, AccountStatus.Active,
            null, false, true, DateTime.UtcNow, null);

        users.FindByPhoneAsync("+201000000000", Arg.Any<CancellationToken>()).Returns(user);
        users.VerifyPasswordOrDummyAsync(user.Id, "Password1", Arg.Any<CancellationToken>()).Returns(true);
        sessions.IssueSessionAsync(user.Id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AuthSessionDto(
                user.Id, null, user.PhoneNumber, user.FullName, user.UserType, user.AccountStatus, null,
                Array.Empty<string>(), Array.Empty<string>(),
                new AuthTokensDto("tok", DateTime.UtcNow.AddMinutes(15), "ref", DateTime.UtcNow.AddDays(1)))));

        var handler = CreateHandler(users, sessions);
        var result = await handler.Handle(new LoginCommand(null, "+201000000000", "Password1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PhoneNumber.Should().Be("+201000000000");
        result.Value.AccessToken.Should().Be("tok");
        await users.Received(1).FindByPhoneAsync("+201000000000", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_resets_access_failed_count_on_successful_password_verification()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();

        var user = new UserAccountDto(
            Guid.NewGuid(), "a@test.com", null, "A", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null);

        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(user);
        users.VerifyPasswordOrDummyAsync(user.Id, "Password1", Arg.Any<CancellationToken>()).Returns(true);
        totp.IsEnabledAsync(user.Id, Arg.Any<CancellationToken>()).Returns(false);
        users.GetRolesAsync(user.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        challenges.CreateAsync(Arg.Any<CreateLoginChallengeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreatedLoginChallenge(
                Guid.NewGuid(),
                "token",
                LoginChallengeSteps.EnrollTotp,
                user.Id,
                DateTime.UtcNow.AddMinutes(5)));

        var handler = CreateHandler(users, sessions, challenges, totp);
        await handler.Handle(new LoginCommand("a@test.com", null, "Password1"), CancellationToken.None);

        await users.Received(1).ResetAccessFailedCountAsync(user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_normalizes_email_to_lowercase_when_looking_up()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();

        users.FindByEmailAsync("upper@test.com", Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);
        users.VerifyPasswordOrDummyAsync(null, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var handler = CreateHandler(users, sessions);
        await handler.Handle(new LoginCommand("UPPER@TEST.COM", null, "Password1"), CancellationToken.None);

        await users.Received(1).FindByEmailAsync("upper@test.com", Arg.Any<CancellationToken>());
    }
}
