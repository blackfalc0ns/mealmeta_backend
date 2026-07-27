using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class RefreshTokenCommandHandlerTests
{
    private static AuthSessionDto Session(Guid userId) => new(
        userId, "a@test.com", null, "A", UserType.Customer, AccountStatus.Active, null,
        ["Customer"], Array.Empty<string>(),
        new AuthTokensDto("access", DateTime.UtcNow.AddMinutes(15), "refresh", DateTime.UtcNow.AddDays(1)));

    [Fact]
    public async Task Refresh_returns_new_session_on_success()
    {
        var sessions = Substitute.For<IAuthSessionService>();
        var userId = Guid.NewGuid();
        sessions.RefreshSessionAsync("old", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<AuthSessionDto>.Success(Session(userId)));

        var handler = new RefreshTokenCommandHandler(sessions);
        var result = await handler.Handle(
            new RefreshTokenCommand("old", "device", "1.2.3.4"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access");
        await sessions.Received(1).RefreshSessionAsync("old", "device", "1.2.3.4", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_propagates_failure_without_exposing_token_state()
    {
        var sessions = Substitute.For<IAuthSessionService>();
        sessions.RefreshSessionAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Error.Unauthorized(MessageCodes.Auth.InvalidRefreshToken, "Refresh token is invalid or expired."));

        var handler = new RefreshTokenCommandHandler(sessions);
        var result = await handler.Handle(new RefreshTokenCommand("bad"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidRefreshToken);
    }
}

public class LogoutCommandHandlerTests
{
    [Fact]
    public async Task Logout_with_refresh_token_revokes_only_that_token()
    {
        var sessions = Substitute.For<IAuthSessionService>();
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();

        var handler = new LogoutCommandHandler(sessions, currentUser, audit);
        var result = await handler.Handle(new LogoutCommand("the-refresh"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await sessions.Received(1).RevokeRefreshTokenAsync("the-refresh", Arg.Any<CancellationToken>());
        await sessions.DidNotReceive().RevokeAllUserTokensAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "UserLoggedOut"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logout_without_refresh_token_revokes_all_user_tokens_when_authenticated()
    {
        var sessions = Substitute.For<IAuthSessionService>();
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();
        var userId = Guid.NewGuid();
        currentUser.UserId.Returns((Guid?)userId);

        var handler = new LogoutCommandHandler(sessions, currentUser, audit);
        var result = await handler.Handle(new LogoutCommand(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await sessions.DidNotReceive().RevokeRefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await sessions.Received(1).RevokeAllUserTokensAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logout_without_refresh_token_and_anonymous_is_noop_success()
    {
        var sessions = Substitute.For<IAuthSessionService>();
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new LogoutCommandHandler(sessions, currentUser, audit);
        var result = await handler.Handle(new LogoutCommand(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await sessions.DidNotReceive().RevokeRefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await sessions.DidNotReceive().RevokeAllUserTokensAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logout_audits_even_when_no_token_provided()
    {
        var sessions = Substitute.For<IAuthSessionService>();
        var currentUser = Substitute.For<ICurrentUser>();
        var audit = Substitute.For<IAuditLogger>();
        currentUser.UserId.Returns(Guid.NewGuid());

        var handler = new LogoutCommandHandler(sessions, currentUser, audit);
        await handler.Handle(new LogoutCommand(null), CancellationToken.None);

        await audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "UserLoggedOut"), Arg.Any<CancellationToken>());
    }
}
