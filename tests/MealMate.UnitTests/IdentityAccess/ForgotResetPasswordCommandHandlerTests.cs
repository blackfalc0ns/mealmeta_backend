using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class ForgotPasswordCommandHandlerTests
{
    private static UserAccountDto User(Guid id, string email) => new(
        id, email, null, "A", UserType.Customer, AccountStatus.Active,
        null, true, false, DateTime.UtcNow, null);

    [Fact]
    public async Task Forgot_password_always_returns_success_to_avoid_account_enumeration()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var audit = Substitute.For<IAuditLogger>();

        users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);

        var handler = new ForgotPasswordCommandHandler(users, otp, audit);
        var result = await handler.Handle(new ForgotPasswordCommand("ghost@test.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await otp.DidNotReceive().SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>());
        await audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Forgot_password_sends_password_reset_otp_when_user_exists()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var audit = Substitute.For<IAuditLogger>();
        var userId = Guid.NewGuid();
        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(User(userId, "a@test.com"));
        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        var handler = new ForgotPasswordCommandHandler(users, otp, audit);
        var result = await handler.Handle(new ForgotPasswordCommand("A@Test.COM"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await otp.Received(1).SendAsync(
            Arg.Is<OtpSendRequest>(r =>
                r.Destination == "a@test.com"
                && r.Channel == "Email"
                && r.Purpose == "PasswordReset"
                && r.UserId == userId),
            Arg.Any<CancellationToken>());
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "PasswordResetRequested" && e.EntityId == userId),
            Arg.Any<CancellationToken>());
    }
}

public class ResetPasswordCommandHandlerTests
{
    private static UserAccountDto User(Guid id, string email) => new(
        id, email, null, "A", UserType.Customer, AccountStatus.Active,
        null, true, false, DateTime.UtcNow, null);

    [Fact]
    public async Task Reset_password_returns_error_when_otp_is_invalid()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();

        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Error.Validation(MessageCodes.Auth.OtpInvalid, "Invalid OTP code."));

        var handler = new ResetPasswordCommandHandler(users, otp, sessions, Substitute.For<IAuthLoginChallengeService>(), audit, Substitute.For<IUnitOfWork>());
        var result = await handler.Handle(
            new ResetPasswordCommand("a@test.com", "000000", "NewPassword1!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.OtpInvalid);
        await users.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await sessions.DidNotReceive().RevokeAllUserTokensAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_password_returns_not_found_when_user_missing_after_otp()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();

        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        users.FindByEmailAsync("ghost@test.com", Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);

        var handler = new ResetPasswordCommandHandler(users, otp, sessions, Substitute.For<IAuthLoginChallengeService>(), audit, Substitute.For<IUnitOfWork>());
        var result = await handler.Handle(
            new ResetPasswordCommand("Ghost@Test.com", "123456", "NewPassword1!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.UserNotFound);
        await users.DidNotReceive().ResetPasswordAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_password_resets_and_revokes_all_sessions_and_audits()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var challenges = Substitute.For<IAuthLoginChallengeService>();
        var audit = Substitute.For<IAuditLogger>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var userId = Guid.NewGuid();
        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(User(userId, "a@test.com"));
        users.GeneratePasswordResetTokenAsync(userId, Arg.Any<CancellationToken>()).Returns("reset-token");
        users.ResetPasswordAsync(userId, "reset-token", "NewPassword1!", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new ResetPasswordCommandHandler(users, otp, sessions, challenges, audit, unitOfWork);
        var result = await handler.Handle(
            new ResetPasswordCommand("A@Test.COM", "123456", "NewPassword1!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await users.Received(1).GeneratePasswordResetTokenAsync(userId, Arg.Any<CancellationToken>());
        await users.Received(1).ResetPasswordAsync(userId, "reset-token", "NewPassword1!", Arg.Any<CancellationToken>());
        await sessions.Received(1).RevokeAllUserTokensAsync(userId, Arg.Any<CancellationToken>());
        await challenges.Received(1).InvalidateAllForUserAsync(userId, Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "PasswordResetCompleted" && e.EntityId == userId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_password_verifies_otp_with_PasswordReset_purpose()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();

        var userId = Guid.NewGuid();
        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(User(userId, "a@test.com"));
        users.GeneratePasswordResetTokenAsync(userId, Arg.Any<CancellationToken>()).Returns("t");
        users.ResetPasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new ResetPasswordCommandHandler(users, otp, sessions, Substitute.For<IAuthLoginChallengeService>(), audit, Substitute.For<IUnitOfWork>());
        await handler.Handle(
            new ResetPasswordCommand("a@test.com", "123456", "NewPassword1!"),
            CancellationToken.None);

        await otp.Received(1).VerifyAsync(
            Arg.Is<OtpVerifyRequest>(r =>
                r.Destination == "a@test.com"
                && r.Purpose == "PasswordReset"
                && r.Code == "123456"),
            Arg.Any<CancellationToken>());
    }
}
