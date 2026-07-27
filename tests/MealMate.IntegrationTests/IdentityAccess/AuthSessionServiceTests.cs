using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Persistence;
using MealMate.Persistence.Common;
using MealMate.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class AuthSessionServiceTests
{
    private const string Secret = "01234567890123456789012345678901";

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // RefreshSessionAsync wraps the rotation in a transaction. The
            // in-memory store doesn't support real transactions; ignore the
            // warning so the no-op transaction envelope doesn't throw.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
    }

    private static AuthSessionService CreateService(
        AppDbContext db,
        IUserAccountService users,
        ITokenService tokens,
        DateTime now)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);
        return new AuthSessionService(
            users, tokens, db, clock,
            Options.Create(new JwtOptions
            {
                Issuer = "test",
                Audience = "test",
                SecretKey = Secret,
                AccessTokenMinutes = 15,
                RefreshTokenDays = 14,
            }));
    }

    private static UserAccountDto ActiveUser(Guid id) => new(
        id, "a@test.com", null, "A", UserType.Customer, AccountStatus.Active,
        null, true, false, DateTime.UtcNow, null);

    private static ITokenService TokenStub() => Substitute.For<ITokenService>();

    private static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    [Fact]
    public async Task IssueSession_persists_hashed_refresh_token_and_updates_last_login()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(ActiveUser(userId));
        users.GetRolesAsync(userId, Arg.Any<CancellationToken>()).Returns(new List<string> { "Customer" });
        users.GetPermissionsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        var tokens = TokenStub();
        tokens.CreateAccessToken(Arg.Any<TokenUser>()).Returns(new AccessToken("access", now.AddMinutes(15)));
        tokens.GenerateRefreshToken().Returns("raw-refresh");

        var svc = CreateService(db, users, tokens, now);
        var session = await svc.IssueSessionAsync(userId, "device", "1.1.1.1", CancellationToken.None);

        session.Tokens.AccessToken.Should().Be("access");
        session.Tokens.RefreshToken.Should().Be("raw-refresh");
        session.Tokens.RefreshTokenExpiresAtUtc.Should().Be(now.AddDays(14));
        session.Roles.Should().Contain("Customer");

        await users.Received(1).UpdateLastLoginAsync(userId, Arg.Any<CancellationToken>());

        // IssueSessionAsync stages the refresh-token insert in the change tracker
        // without saving, mirroring the command-handler save boundary. Persist here
        // so we can assert against the store.
        await db.SaveChangesAsync();
        db.RefreshTokens.Should().ContainSingle();
        // The raw token must never be stored — only its SHA-256 hash.
        var stored = db.RefreshTokens.AsNoTracking().Single();
        stored.TokenHash.Should().Be(HashToken("raw-refresh"));
        stored.RevokedAtUtc.Should().BeNull();
        stored.DeviceInfo.Should().Be("device");
        stored.IpAddress.Should().Be("1.1.1.1");
    }

    // Token rotation with ExecuteUpdateAsync is covered by AuthSessionServiceRefreshPostgresTests.
    [Fact]
    public async Task Refresh_rejects_expired_token()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = HashToken("expired"),
            CreatedAtUtc = now.AddDays(-10),
            ExpiresAtUtc = now.AddMinutes(-1),
        });
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        var svc = CreateService(db, users, TokenStub(), now);

        var result = await svc.RefreshSessionAsync("expired", null, null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidRefreshToken);
    }

    [Fact]
    public async Task Refresh_rejects_unknown_token()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        var svc = CreateService(db, Substitute.For<IUserAccountService>(), TokenStub(), now);

        var result = await svc.RefreshSessionAsync("never-issued", null, null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidRefreshToken);
    }

    [Fact]
    public async Task Refresh_rejects_closed_account()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = HashToken("valid"),
            CreatedAtUtc = now.AddDays(-1),
            ExpiresAtUtc = now.AddDays(7),
        });
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "a@test.com", null, "A", UserType.Customer, AccountStatus.Closed,
            null, true, false, now, null));

        var svc = CreateService(db, users, TokenStub(), now);
        var result = await svc.RefreshSessionAsync("valid", null, null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.AccountClosed);
    }

    [Fact]
    public async Task RevokeRefreshToken_is_noop_on_unknown_token()
    {
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        var svc = CreateService(db, Substitute.For<IUserAccountService>(), TokenStub(), now);

        await svc.RevokeRefreshTokenAsync("unknown", CancellationToken.None);

        db.RefreshTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeRefreshToken_is_noop_on_already_revoked()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = HashToken("revoked"),
            CreatedAtUtc = now.AddDays(-1),
            ExpiresAtUtc = now.AddDays(7),
            RevokedAtUtc = now.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, Substitute.For<IUserAccountService>(), TokenStub(), now);
        var before = db.RefreshTokens.Single().RevokedAtUtc;

        await svc.RevokeRefreshTokenAsync("revoked", CancellationToken.None);

        // Re-read with AsNoTracking to bypass the change tracker and confirm the
        // stored RevokedAtUtc was not overwritten by the no-op revoke path.
        db.RefreshTokens.AsNoTracking().Single().RevokedAtUtc.Should().Be(before);
    }

    [Fact]
    public async Task RevokeAllUserTokens_revokes_only_active_tokens_for_user()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        db.RefreshTokens.AddRange(
            new RefreshToken
            {
                UserId = userA,
                TokenHash = HashToken("a-active"),
                CreatedAtUtc = now.AddDays(-1),
                ExpiresAtUtc = now.AddDays(7),
            },
            new RefreshToken
            {
                UserId = userA,
                TokenHash = HashToken("a-already-revoked"),
                CreatedAtUtc = now.AddDays(-2),
                ExpiresAtUtc = now.AddDays(5),
                RevokedAtUtc = now.AddHours(-3),
            },
            new RefreshToken
            {
                UserId = userB,
                TokenHash = HashToken("b-active"),
                CreatedAtUtc = now.AddDays(-1),
                ExpiresAtUtc = now.AddDays(7),
            });
        await db.SaveChangesAsync();

        var svc = CreateService(db, Substitute.For<IUserAccountService>(), TokenStub(), now);
        await svc.RevokeAllUserTokensAsync(userA, CancellationToken.None);

        db.RefreshTokens.Where(x => x.UserId == userA).Should().AllSatisfy(t => t.RevokedAtUtc.Should().NotBeNull());
        // User B's tokens must be untouched.
        db.RefreshTokens.Single(x => x.UserId == userB).RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Refresh_throws_when_user_no_longer_exists()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateDbContext();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = HashToken("orphan"),
            CreatedAtUtc = now.AddDays(-1),
            ExpiresAtUtc = now.AddDays(7),
        });
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns((UserAccountDto?)null);

        var svc = CreateService(db, users, TokenStub(), now);
        var result = await svc.RefreshSessionAsync("orphan", null, null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidRefreshToken);
    }
}
