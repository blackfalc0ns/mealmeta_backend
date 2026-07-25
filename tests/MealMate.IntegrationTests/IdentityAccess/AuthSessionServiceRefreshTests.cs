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
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class AuthSessionServiceRefreshTests
{
    [Fact]
    public async Task Refresh_reuse_of_revoked_token_revokes_all_user_tokens()
    {
        var userId = Guid.NewGuid();
        var revokedRaw = "revoked-refresh-token";
        var activeRaw = "active-refresh-token";
        var now = DateTime.UtcNow;

        await using var db = CreateDbContext();
        db.RefreshTokens.AddRange(
            new RefreshToken
            {
                UserId = userId,
                TokenHash = HashToken(revokedRaw),
                CreatedAtUtc = now.AddDays(-1),
                ExpiresAtUtc = now.AddDays(7),
                RevokedAtUtc = now.AddHours(-1),
            },
            new RefreshToken
            {
                UserId = userId,
                TokenHash = HashToken(activeRaw),
                CreatedAtUtc = now.AddDays(-1),
                ExpiresAtUtc = now.AddDays(7),
            });
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "a@test.com", null, "A", UserType.Customer, AccountStatus.Active,
            null, true, false, now, null));

        var tokens = Substitute.For<ITokenService>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var service = new AuthSessionService(
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

        var result = await service.RefreshSessionAsync(revokedRaw, cancellationToken: CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.InvalidRefreshToken);
        db.RefreshTokens.Where(x => x.UserId == userId).Should().OnlyContain(x => x.RevokedAtUtc != null);
    }

    [Fact]
    public async Task Refresh_rejects_suspended_account()
    {
        var userId = Guid.NewGuid();
        var refreshRaw = "valid-refresh-token";
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
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "a@test.com", null, "A", UserType.Customer, AccountStatus.Suspended,
            null, true, false, now, null));

        var tokens = Substitute.For<ITokenService>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var service = new AuthSessionService(
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

        var result = await service.RefreshSessionAsync(refreshRaw, cancellationToken: CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessageCodes.Auth.AccountSuspended);
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
