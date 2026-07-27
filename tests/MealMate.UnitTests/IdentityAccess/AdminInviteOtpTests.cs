using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.AdminUsers;
using MealMate.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class AdminInviteOtpTests
{
    [Fact]
    public async Task InviteAdmin_sends_admin_invite_otp()
    {
        var users = Substitute.For<IUserAccountService>();
        var db = Substitute.For<IAppDbContext>();
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();
        var otp = Substitute.For<IOtpService>();
        var clock = Substitute.For<IDateTimeProvider>();

        var userId = Guid.NewGuid();
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.IsInRole(AppRoles.SuperAdmin).Returns(true);
        clock.UtcNow.Returns(DateTime.UtcNow);

        users.CreateAsync(Arg.Any<CreateUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<UserAccountDto>.Success(new UserAccountDto(
                userId, "new@test.com", null, "New Admin", UserType.Admin, AccountStatus.Registered,
                null, false, false, DateTime.UtcNow, null)));
        users.AddToRoleAsync(userId, AppRoles.OperationsAdmin, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        otp.SendAsync(Arg.Any<OtpSendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new InviteAdminCommandHandler(users, db, currentUser, audit, otp, clock);
        var result = await handler.Handle(
            new InviteAdminCommand("new@test.com", "New Admin", AppRoles.OperationsAdmin, null, "test"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await otp.Received(1).SendAsync(
            Arg.Is<OtpSendRequest>(r => r.Purpose == "AdminInvite"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptAdminInvite_verifies_admin_invite_purpose()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var audit = Substitute.For<IAuditLogger>();

        var userId = Guid.NewGuid();
        users.FindByEmailAsync("new@test.com", Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, "new@test.com", null, "New Admin", UserType.Admin, AccountStatus.Registered,
                null, false, false, DateTime.UtcNow, null));
        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        users.GeneratePasswordResetTokenAsync(userId, Arg.Any<CancellationToken>()).Returns("token");
        users.ResetPasswordAsync(userId, "token", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new AcceptAdminInviteCommandHandler(users, otp, audit);
        var result = await handler.Handle(
            new AcceptAdminInviteCommand("new@test.com", "123456", "NewPassword1!"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await otp.Received(1).VerifyAsync(
            Arg.Is<OtpVerifyRequest>(r => r.Purpose == "AdminInvite"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptAdminInvite_rejects_non_pending_account()
    {
        var users = Substitute.For<IUserAccountService>();
        var otp = Substitute.For<IOtpService>();
        var audit = Substitute.For<IAuditLogger>();

        var userId = Guid.NewGuid();
        users.FindByEmailAsync("new@test.com", Arg.Any<CancellationToken>())
            .Returns(new UserAccountDto(
                userId, "new@test.com", null, "New Admin", UserType.Admin, AccountStatus.Active,
                null, true, false, DateTime.UtcNow, null));
        otp.VerifyAsync(Arg.Any<OtpVerifyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var handler = new AcceptAdminInviteCommandHandler(users, otp, audit);
        var result = await handler.Handle(
            new AcceptAdminInviteCommand("new@test.com", "123456", "NewPassword1!"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await users.DidNotReceive().ResetPasswordAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
