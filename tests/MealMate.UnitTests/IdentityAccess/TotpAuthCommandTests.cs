using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.AdminUsers;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class TotpAuthCommandTests
{
    [Fact]
    public async Task TotpSetup_rejects_verify_step_challenge()
    {
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        challenges.ResolveAsync("token", LoginChallengeSteps.EnrollTotp, Arg.Any<CancellationToken>())
            .Returns(Error.Validation(MessageCodes.Auth.TotpSetupNotAllowed, "step mismatch"));

        var handler = new TotpSetupCommandHandler(
            challenges,
            Substitute.For<ITotpAuthenticatorService>(),
            Substitute.For<IUserAccountService>(),
            Substitute.For<IAuditLogger>());

        var result = await handler.Handle(new TotpSetupCommand("token"), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.TotpSetupNotAllowed);
    }

    [Fact]
    public async Task TotpSetup_returns_shared_key_and_uri_for_enroll_challenge()
    {
        var userId = Guid.NewGuid();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();
        var users = Substitute.For<IUserAccountService>();

        challenges.ResolveAsync("token", LoginChallengeSteps.EnrollTotp, Arg.Any<CancellationToken>())
            .Returns(new ResolvedLoginChallenge(Guid.NewGuid(), userId, LoginChallengeSteps.EnrollTotp, 0, 5, DateTime.UtcNow.AddMinutes(5)));
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null));
        totp.IsEnabledAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        totp.GetOrCreateSetupAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new TotpSetupDto(
                "abcd efgh",
                "otpauth://totp/MealMate%20Admin:admin%40test.com?secret=abcdef",
                "MealMate Admin",
                "admin@test.com"));

        var handler = new TotpSetupCommandHandler(challenges, totp, users, Substitute.For<IAuditLogger>());
        var result = await handler.Handle(new TotpSetupCommand("token"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SharedKey.Should().Be("abcd efgh");
        result.Value.AuthenticatorUri.Should().Contain("otpauth://totp/");
        result.Value.Issuer.Should().Be("MealMate Admin");
        result.Value.AccountName.Should().Be("admin@test.com");
    }

    [Fact]
    public async Task TotpVerifyLogin_issues_session_when_code_valid()
    {
        var userId = Guid.NewGuid();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();
        var resolved = new ResolvedLoginChallenge(Guid.NewGuid(), userId, LoginChallengeSteps.VerifyTotp, 0, 5, DateTime.UtcNow.AddMinutes(5));

        var session = new AuthSessionDto(
            userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active, null,
            ["SuperAdmin"], [],
            new AuthTokensDto("access", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(1)));

        challenges.ResolveAsync("token", LoginChallengeSteps.VerifyTotp, Arg.Any<CancellationToken>()).Returns(resolved);
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null));
        totp.VerifyCodeAsync(userId, "123456", Arg.Any<CancellationToken>()).Returns(true);
        challenges.TryConsumeAsync(resolved, Arg.Any<CancellationToken>()).Returns(Result.Success());
        sessions.IssueSessionAsync(userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(session));

        var handler = new TotpVerifyLoginCommandHandler(challenges, totp, sessions, users, audit);
        var result = await handler.Handle(new TotpVerifyLoginCommand("token", "123456", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access");
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "TotpLoginVerified"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TotpVerifyLogin_recovery_code_is_single_use_path()
    {
        var userId = Guid.NewGuid();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();
        var resolved = new ResolvedLoginChallenge(Guid.NewGuid(), userId, LoginChallengeSteps.VerifyTotp, 0, 5, DateTime.UtcNow.AddMinutes(5));

        challenges.ResolveAsync("token", LoginChallengeSteps.VerifyTotp, Arg.Any<CancellationToken>()).Returns(resolved);
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null));
        totp.RedeemRecoveryCodeAsync(userId, "RCODE", Arg.Any<CancellationToken>()).Returns(true);
        challenges.TryConsumeAsync(resolved, Arg.Any<CancellationToken>()).Returns(Result.Success());
        sessions.IssueSessionAsync(userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AuthSessionDto(
                userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active, null,
                [], [],
                new AuthTokensDto("access", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(1)))));

        var handler = new TotpVerifyLoginCommandHandler(challenges, totp, sessions, users, audit);
        var result = await handler.Handle(new TotpVerifyLoginCommand("token", null, "RCODE"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await totp.Received(1).RedeemRecoveryCodeAsync(userId, "RCODE", Arg.Any<CancellationToken>());
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "TotpRecoveryCodeUsed"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TotpConfirmSetup_returns_recovery_codes_and_session()
    {
        var userId = Guid.NewGuid();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();
        var resolved = new ResolvedLoginChallenge(Guid.NewGuid(), userId, LoginChallengeSteps.EnrollTotp, 0, 5, DateTime.UtcNow.AddMinutes(5));

        challenges.ResolveAsync("token", LoginChallengeSteps.EnrollTotp, Arg.Any<CancellationToken>()).Returns(resolved);
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null));
        totp.VerifyCodeAsync(userId, "123456", Arg.Any<CancellationToken>()).Returns(true);
        challenges.TryConsumeAsync(resolved, Arg.Any<CancellationToken>()).Returns(Result.Success());
        totp.CompleteEnrollmentAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<string>>(new[] { "rc1", "rc2" }));
        sessions.IssueSessionAsync(userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AuthSessionDto(
                userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active, null,
                [], [],
                new AuthTokensDto("access", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(1)))));

        var handler = new TotpConfirmSetupCommandHandler(challenges, totp, sessions, users, audit);
        var result = await handler.Handle(new TotpConfirmSetupCommand("token", "123456"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RecoveryCodes.Should().HaveCount(2);
        result.Value.Session.AccessToken.Should().Be("access");
        await totp.Received(1).VerifyCodeAsync(userId, "123456", Arg.Any<CancellationToken>());
        await challenges.Received(1).TryConsumeAsync(resolved, Arg.Any<CancellationToken>());
        await totp.Received(1).CompleteEnrollmentAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TotpConfirmSetup_consumes_challenge_before_enabling_totp()
    {
        var userId = Guid.NewGuid();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();
        var resolved = new ResolvedLoginChallenge(Guid.NewGuid(), userId, LoginChallengeSteps.EnrollTotp, 0, 5, DateTime.UtcNow.AddMinutes(5));
        var callOrder = new List<string>();

        challenges.ResolveAsync("token", LoginChallengeSteps.EnrollTotp, Arg.Any<CancellationToken>()).Returns(resolved);
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null));
        totp.VerifyCodeAsync(userId, "123456", Arg.Any<CancellationToken>()).Returns(true);
        challenges.TryConsumeAsync(resolved, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callOrder.Add("consume");
            return Result.Success();
        });
        totp.CompleteEnrollmentAsync(userId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callOrder.Add("enable");
            return Result.Success<IReadOnlyList<string>>(new[] { "rc1" });
        });
        sessions.IssueSessionAsync(userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AuthSessionDto(
                userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active, null,
                [], [],
                new AuthTokensDto("access", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(1)))));

        var handler = new TotpConfirmSetupCommandHandler(challenges, totp, sessions, users, audit);
        await handler.Handle(new TotpConfirmSetupCommand("token", "123456"), CancellationToken.None);

        callOrder.Should().Equal("consume", "enable");
    }

    [Fact]
    public async Task TotpConfirmSetup_does_not_enable_when_consume_fails()
    {
        var userId = Guid.NewGuid();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var users = Substitute.For<IUserAccountService>();
        var resolved = new ResolvedLoginChallenge(Guid.NewGuid(), userId, LoginChallengeSteps.EnrollTotp, 0, 5, DateTime.UtcNow.AddMinutes(5));

        challenges.ResolveAsync("token", LoginChallengeSteps.EnrollTotp, Arg.Any<CancellationToken>()).Returns(resolved);
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null));
        totp.VerifyCodeAsync(userId, "123456", Arg.Any<CancellationToken>()).Returns(true);
        challenges.TryConsumeAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(Error.Validation(MessageCodes.Auth.TotpChallengeInvalid, "Invalid login challenge."));

        var handler = new TotpConfirmSetupCommandHandler(
            challenges, totp, sessions, users, Substitute.For<IAuditLogger>());
        var result = await handler.Handle(new TotpConfirmSetupCommand("token", "123456"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await totp.DidNotReceive().CompleteEnrollmentAsync(userId, Arg.Any<CancellationToken>());
        await sessions.DidNotReceive().IssueSessionAsync(
            Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TotpConfirmSetup_concurrent_requests_only_one_enrolls_and_issues_session()
    {
        var userId = Guid.NewGuid();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var users = Substitute.For<IUserAccountService>();
        var audit = Substitute.For<IAuditLogger>();
        var resolved = new ResolvedLoginChallenge(Guid.NewGuid(), userId, LoginChallengeSteps.EnrollTotp, 0, 5, DateTime.UtcNow.AddMinutes(5));
        var consumeCount = 0;
        var enableCount = 0;
        var winnerCodes = new[] { "winner-rc" };

        challenges.ResolveAsync("token", LoginChallengeSteps.EnrollTotp, Arg.Any<CancellationToken>()).Returns(resolved);
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null));
        totp.VerifyCodeAsync(userId, "123456", Arg.Any<CancellationToken>()).Returns(true);
        challenges.TryConsumeAsync(resolved, Arg.Any<CancellationToken>()).Returns(_ =>
            Interlocked.Increment(ref consumeCount) == 1
                ? Result.Success()
                : Error.Validation(MessageCodes.Auth.TotpChallengeInvalid, "Invalid login challenge."));
        totp.CompleteEnrollmentAsync(userId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref enableCount);
            return Result.Success<IReadOnlyList<string>>(winnerCodes);
        });
        sessions.IssueSessionAsync(userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AuthSessionDto(
                userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active, null,
                [], [],
                new AuthTokensDto("access", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(1)))));

        var handler = new TotpConfirmSetupCommandHandler(challenges, totp, sessions, users, audit);
        var results = await Task.WhenAll(
            handler.Handle(new TotpConfirmSetupCommand("token", "123456"), CancellationToken.None),
            handler.Handle(new TotpConfirmSetupCommand("token", "123456"), CancellationToken.None));

        results.Count(r => r.IsSuccess).Should().Be(1);
        results.Count(r => r.IsFailure).Should().Be(1);
        enableCount.Should().Be(1);
        results.Single(r => r.IsSuccess).Value.RecoveryCodes.Should().Equal(winnerCodes);
        await sessions.Received(1).IssueSessionAsync(
            userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TotpVerifyLogin_rejects_suspended_account_after_challenge()
    {
        var userId = Guid.NewGuid();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var users = Substitute.For<IUserAccountService>();
        var resolved = new ResolvedLoginChallenge(Guid.NewGuid(), userId, LoginChallengeSteps.VerifyTotp, 0, 5, DateTime.UtcNow.AddMinutes(5));

        challenges.ResolveAsync("token", LoginChallengeSteps.VerifyTotp, Arg.Any<CancellationToken>()).Returns(resolved);
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Suspended,
            null, true, false, DateTime.UtcNow, null));
        totp.VerifyCodeAsync(userId, "123456", Arg.Any<CancellationToken>()).Returns(true);
        challenges.TryConsumeAsync(resolved, Arg.Any<CancellationToken>()).Returns(Result.Success());
        sessions.IssueSessionAsync(userId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Error.Forbidden(MessageCodes.Auth.AccountSuspended, "Account is suspended."));

        var handler = new TotpVerifyLoginCommandHandler(
            challenges, totp, sessions, users, Substitute.For<IAuditLogger>());
        var result = await handler.Handle(new TotpVerifyLoginCommand("token", "123456", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.AccountSuspended);
    }
}

public class ResetAdmin2FaCommandTests
{
    [Fact]
    public async Task ResetAdmin2Fa_rejects_non_super_admin()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(false);

        var handler = new ResetAdmin2FaCommandHandler(
            Substitute.For<IUserAccountService>(),
            Substitute.For<ITotpAuthenticatorService>(),
            Substitute.For<IAuthSessionService>(),
            Substitute.For<IAuthLoginChallengeService>(),
            currentUser,
            Substitute.For<IAuditLogger>());

        var result = await handler.Handle(new ResetAdmin2FaCommand(Guid.NewGuid(), "lost device"), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.Forbidden);
    }

    [Fact]
    public async Task ResetAdmin2Fa_resets_totp_revokes_tokens_and_audits()
    {
        var targetId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        var users = Substitute.For<IUserAccountService>();
        var totp = Substitute.For<ITotpAuthenticatorService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var audit = Substitute.For<IAuditLogger>();

        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        users.FindByIdAsync(targetId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            targetId, "admin@test.com", null, "Admin", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null));
        totp.IsEnabledAsync(targetId, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new ResetAdmin2FaCommandHandler(users, totp, sessions, challenges, currentUser, audit);
        var result = await handler.Handle(new ResetAdmin2FaCommand(targetId, "lost device"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await totp.Received(1).ResetAsync(targetId, Arg.Any<CancellationToken>());
        await sessions.Received(1).RevokeAllUserTokensAsync(targetId, Arg.Any<CancellationToken>());
        await challenges.Received(1).InvalidateAllForUserAsync(targetId, Arg.Any<CancellationToken>());
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "Admin2FaReset" && e.Reason == "lost device"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetAdmin2Fa_rejects_unknown_or_non_admin_target()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                Guid.NewGuid(), "x@test.com", null, "X", UserType.Customer, AccountStatus.Active,
                null, true, false, DateTime.UtcNow, null));

        var handler = new ResetAdmin2FaCommandHandler(
            users,
            Substitute.For<ITotpAuthenticatorService>(),
            Substitute.For<IAuthSessionService>(),
            Substitute.For<IAuthLoginChallengeService>(),
            currentUser,
            Substitute.For<IAuditLogger>());

        var result = await handler.Handle(new ResetAdmin2FaCommand(Guid.NewGuid(), "reason"), CancellationToken.None);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.UserNotFound);
    }
}
