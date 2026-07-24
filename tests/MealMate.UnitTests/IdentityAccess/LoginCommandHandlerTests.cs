using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class LoginCommandHandlerTests
{
    [Fact]
    public async Task Login_rejects_invalid_password()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();

        var user = new UserAccountDto(
            Guid.NewGuid(), "a@test.com", null, "A", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null);

        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(user);
        users.CheckPasswordAsync(user.Id, "bad", Arg.Any<CancellationToken>()).Returns(false);

        var handler = new LoginCommandHandler(users, sessions, audit);
        var result = await handler.Handle(new LoginCommand("a@test.com", null, "bad"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidCredentials);
    }

    [Fact]
    public async Task Login_rejects_suspended_account()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();

        var user = new UserAccountDto(
            Guid.NewGuid(), "a@test.com", null, "A", UserType.Admin, AccountStatus.Suspended,
            null, true, false, DateTime.UtcNow, null);

        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(user);
        users.CheckPasswordAsync(user.Id, "Password1", Arg.Any<CancellationToken>()).Returns(true);

        var handler = new LoginCommandHandler(users, sessions, audit);
        var result = await handler.Handle(new LoginCommand("a@test.com", null, "Password1"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.AccountSuspended);
    }

    [Fact]
    public async Task Login_issues_session_for_active_user()
    {
        var users = Substitute.For<IUserAccountService>();
        var sessions = Substitute.For<IAuthSessionService>();
        var audit = Substitute.For<IAuditLogger>();

        var user = new UserAccountDto(
            Guid.NewGuid(), "a@test.com", null, "A", UserType.Admin, AccountStatus.Active,
            null, true, false, DateTime.UtcNow, null);

        users.FindByEmailAsync("a@test.com", Arg.Any<CancellationToken>()).Returns(user);
        users.CheckPasswordAsync(user.Id, "Password1", Arg.Any<CancellationToken>()).Returns(true);
        sessions.IssueSessionAsync(user.Id, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AuthSessionDto(
                user.Id, user.Email, null, user.FullName, user.UserType, user.AccountStatus, null,
                ["SuperAdmin"], ["rbac.roles.view"],
                new AuthTokensDto("tok", DateTime.UtcNow.AddMinutes(15), "ref", DateTime.UtcNow.AddDays(1))));

        var handler = new LoginCommandHandler(users, sessions, audit);
        var result = await handler.Handle(new LoginCommand("a@test.com", null, "Password1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("tok");
    }
}
