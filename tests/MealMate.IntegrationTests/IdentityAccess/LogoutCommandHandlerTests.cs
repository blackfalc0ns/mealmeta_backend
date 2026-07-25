using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class LogoutCommandHandlerTests
{
    [Fact]
    public async Task Logout_persists_refresh_token_revocation()
    {
        var userId = Guid.NewGuid();
        var refreshRaw = "logout-refresh-token";
        var now = DateTime.UtcNow;

        await using var db = CreateDbContext();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = HashToken(refreshRaw),
            CreatedAtUtc = now.AddDays(-1),
            ExpiresAtUtc = now.AddDays(7),
        });
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        var tokens = Substitute.For<ITokenService>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var sessions = new AuthSessionService(
            users,
            tokens,
            db,
            clock,
            Options.Create(new JwtOptions
            {
                Issuer = "test",
                Audience = "test",
                SecretKey = "01234567890123456789012345678901",
            }));

        var audit = Substitute.For<IAuditLogger>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var handler = new LogoutCommandHandler(sessions, currentUser, audit);
        var result = await handler.Handle(new LogoutCommand(refreshRaw), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await db.SaveChangesAsync();
        var token = await db.RefreshTokens.SingleAsync(x => x.TokenHash == HashToken(refreshRaw));
        token.RevokedAtUtc.Should().NotBeNull();
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
