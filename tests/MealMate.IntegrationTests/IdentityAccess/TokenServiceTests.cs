using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Xunit;

namespace MealMate.IntegrationTests.IdentityAccess;

public class TokenServiceTests
{
    private const string Secret = "01234567890123456789012345678901"; // 32 chars

    private static TokenService NewService(DateTime? now = null)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var options = Options.Create(new JwtOptions
        {
            Issuer = "https://api.mealmate.test",
            Audience = "mealmate-clients",
            SecretKey = Secret,
            RefreshTokenDays = 14,
        });
        return new TokenService(options, clock);
    }

    private static TokenValidationParameters ValidationParams() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        // Lifetime is asserted separately against ExpiresAtUtc/ValidTo; the JWT
        // handler validates lifetime against the real system clock (not the
        // injected IDateTimeProvider), so we disable it here to keep claim
        // structure assertions deterministic.
        ValidateLifetime = false,
        ValidateIssuerSigningKey = true,
        ValidIssuer = "https://api.mealmate.test",
        ValidAudience = "mealmate-clients",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
        ClockSkew = TimeSpan.Zero,
    };

    [Fact]
    public void CreateAccessToken_includes_core_claims_and_is_valid()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var svc = NewService(now);
        var userId = Guid.NewGuid();
        var tokenUser = new TokenUser(
            userId,
            UserType.Admin,
            new[] { "SuperAdmin", "OperationsAdmin" },
            new[] { "rbac.roles.view", "rbac.roles.manage" });

        var access = svc.CreateAccessToken(tokenUser);

        // ExpiresAtUtc is derived from the injected clock, not the real wall clock.
        access.ExpiresAtUtc.Should().Be(now.AddHours(2));
        var handler = new JwtSecurityTokenHandler();
        // Match the API host (MapInboundClaims = false) so custom claim types such
        // as "sub"/"mm_user_type" are preserved instead of remapped to long URIs.
        handler.InboundClaimTypeMap.Clear();
        var principal = handler.ValidateToken(access.Value, ValidationParams(), out _);

        principal.FindFirst(AppClaimTypes.UserId)!.Value.Should().Be(userId.ToString());
        principal.FindFirst(AppClaimTypes.UserType)!.Value.Should().Be("Admin");
        principal.FindFirst(JwtRegisteredClaimNames.Jti)!.Value.Should().NotBeNullOrEmpty();
        principal.Claims.Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value).Should().Contain(new[] { "SuperAdmin", "OperationsAdmin" });
        principal.Claims.Where(c => c.Type == AppClaimTypes.Permission)
            .Select(c => c.Value).Should().Contain(new[] { "rbac.roles.view", "rbac.roles.manage" });
    }

    [Fact]
    public void CreateAccessToken_includes_restaurant_id_claim_when_present()
    {
        var svc = NewService();
        var restaurantId = Guid.NewGuid();
        var tokenUser = new TokenUser(
            Guid.NewGuid(), UserType.Restaurant, Array.Empty<string>(), Array.Empty<string>(), restaurantId);

        var access = svc.CreateAccessToken(tokenUser);
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var principal = handler.ValidateToken(access.Value, ValidationParams(), out _);

        principal.FindFirst(AppClaimTypes.RestaurantId)!.Value.Should().Be(restaurantId.ToString());
    }

    [Fact]
    public void CreateAccessToken_omits_restaurant_id_claim_when_absent()
    {
        var svc = NewService();
        var tokenUser = new TokenUser(
            Guid.NewGuid(), UserType.Customer, Array.Empty<string>(), Array.Empty<string>(), null);

        var access = svc.CreateAccessToken(tokenUser);
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var principal = handler.ValidateToken(access.Value, ValidationParams(), out _);

        principal.FindFirst(AppClaimTypes.RestaurantId).Should().BeNull();
    }

    [Fact]
    public void CreateAccessToken_includes_all_scope_and_tenant_claims()
    {
        var svc = NewService();
        var scopeIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var tenantIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var tokenUser = new TokenUser(
            Guid.NewGuid(),
            UserType.Admin,
            new[] { "CountryAdmin" },
            Array.Empty<string>(),
            ScopeIds: scopeIds,
            TenantIds: tenantIds);

        var access = svc.CreateAccessToken(tokenUser);
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var principal = handler.ValidateToken(access.Value, ValidationParams(), out _);

        principal.FindAll(AppClaimTypes.ScopeId)
            .Select(claim => Guid.Parse(claim.Value))
            .Should().BeEquivalentTo(scopeIds);
        principal.FindAll(AppClaimTypes.TenantId)
            .Select(claim => Guid.Parse(claim.Value))
            .Should().BeEquivalentTo(tenantIds);
    }

    [Fact]
    public void CreateAccessToken_omits_scope_and_tenant_claims_when_absent()
    {
        var svc = NewService();
        var tokenUser = new TokenUser(
            Guid.NewGuid(),
            UserType.Admin,
            Array.Empty<string>(),
            Array.Empty<string>());

        var access = svc.CreateAccessToken(tokenUser);
        var handler = new JwtSecurityTokenHandler();
        handler.InboundClaimTypeMap.Clear();
        var principal = handler.ValidateToken(access.Value, ValidationParams(), out _);

        principal.FindAll(AppClaimTypes.ScopeId).Should().BeEmpty();
        principal.FindAll(AppClaimTypes.TenantId).Should().BeEmpty();
    }

    [Fact]
    public void CreateAccessToken_rejects_validation_against_wrong_key()
    {
        var svc = NewService();
        var access = svc.CreateAccessToken(
            new TokenUser(Guid.NewGuid(), UserType.Customer, Array.Empty<string>(), Array.Empty<string>()));

        var wrongKey = ValidationParams();
        wrongKey.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("DIFFERENT_KEY_32_CHARS_MIN_LENGTH!!!"));

        var act = () => new JwtSecurityTokenHandler().ValidateToken(access.Value, wrongKey, out _);
        act.Should().Throw<SecurityTokenInvalidSignatureException>();
    }

    [Fact]
    public void CreateAccessToken_default_expiry_is_two_hours()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var svc = NewService(now);
        var access = svc.CreateAccessToken(
            new TokenUser(Guid.NewGuid(), UserType.Customer, Array.Empty<string>(), Array.Empty<string>()));

        access.ExpiresAtUtc.Should().Be(now.AddHours(2));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(access.Value);
        token.ValidTo.Should().Be(now.AddHours(2));
    }

    [Fact]
    public void GenerateRefreshToken_is_unique_and_64_bytes()
    {
        var svc = NewService();
        var a = svc.GenerateRefreshToken();
        var b = svc.GenerateRefreshToken();

        a.Should().NotBeNullOrEmpty();
        b.Should().NotBeNullOrEmpty();
        a.Should().NotBe(b);

        // 64 random bytes -> 88 base64 chars
        a.Length.Should().Be(88);
        Convert.FromBase64String(a).Length.Should().Be(64);
    }
}
