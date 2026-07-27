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
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Sdk;

namespace MealMate.IntegrationTests.IdentityAccess;

public sealed class AuthSessionServiceRefreshPostgresTests : IAsyncLifetime
{
    private const string Secret = "01234567890123456789012345678901";

    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            throw new XunitException(
                "Docker is required for AuthSessionServiceRefreshPostgresTests. " +
                "Start Docker Desktop or the Docker daemon and re-run the IdentityAccess integration tests.",
                ex);
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task Refresh_rotates_token_and_links_replaced_by()
    {
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var db = await CreateDbContextAsync(_container.GetConnectionString());
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = HashToken("old"),
            CreatedAtUtc = now.AddDays(-1),
            ExpiresAtUtc = now.AddDays(7),
        });
        await db.SaveChangesAsync();

        var users = Substitute.For<IUserAccountService>();
        users.FindByIdAsync(userId, Arg.Any<CancellationToken>()).Returns(new UserAccountDto(
            userId, "a@test.com", null, "A", UserType.Customer, AccountStatus.Active,
            null, true, false, now, null));
        users.GetRolesAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        users.GetPermissionsAsync(userId, Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());

        var tokens = Substitute.For<ITokenService>();
        tokens.CreateAccessToken(Arg.Any<TokenUser>()).Returns(new AccessToken("new-access", now.AddMinutes(15)));
        tokens.GenerateRefreshToken().Returns("new-refresh");

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now);

        var svc = new AuthSessionService(
            users,
            tokens,
            db,
            clock,
            Options.Create(new JwtOptions
            {
                Issuer = "test",
                Audience = "test",
                SecretKey = Secret,
                AccessTokenMinutes = 15,
                RefreshTokenDays = 14,
            }));

        var result = await svc.RefreshSessionAsync("old", "device2", "2.2.2.2", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Tokens.AccessToken.Should().Be("new-access");
        result.Value.Tokens.RefreshToken.Should().Be("new-refresh");

        var old = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == HashToken("old"));
        old.RevokedAtUtc.Should().NotBeNull();
        var fresh = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == HashToken("new-refresh"));
        fresh.RevokedAtUtc.Should().BeNull();
        old.ReplacedByTokenId.Should().Be(fresh.Id);
        fresh.ExpiresAtUtc.Should().BeCloseTo(now.AddDays(14), TimeSpan.FromSeconds(1));
    }

    private static async Task<AppDbContext> CreateDbContextAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        var db = new AppDbContext(options, Substitute.For<IDomainEventDispatcher>());
        await db.Database.MigrateAsync();
        return db;
    }

    private static string HashToken(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
