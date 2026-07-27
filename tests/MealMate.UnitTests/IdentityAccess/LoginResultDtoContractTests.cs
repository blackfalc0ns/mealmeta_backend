using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using Xunit;

namespace MealMate.UnitTests.IdentityAccess;

public class LoginResultDtoContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Pending_admin_login_serializes_login_challenge_expiry()
    {
        var expiresAtUtc = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var dto = LoginResultDto.FromPendingChallenge(
            new UserAccountDto(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "admin@test.com",
                null,
                "Admin",
                UserType.Admin,
                AccountStatus.Active,
                null,
                true,
                false,
                DateTime.UtcNow,
                null),
            ["SuperAdmin"],
            "challenge-token",
            "enroll_totp",
            expiresAtUtc);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, JsonOptions));
        var root = json.RootElement;

        root.GetProperty("loginChallengeToken").GetString().Should().Be("challenge-token");
        root.GetProperty("loginChallengeExpiresAtUtc").GetDateTime().Should().Be(expiresAtUtc);
        root.GetProperty("nextStep").GetString().Should().Be("enroll_totp");
        root.TryGetProperty("accessToken", out _).Should().BeTrue();
        root.GetProperty("accessToken").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Full_session_omits_login_challenge_expiry()
    {
        var dto = LoginResultDto.FromSession(new AuthResponseDto(
            Guid.NewGuid(),
            "user@test.com",
            null,
            "User",
            UserType.Customer,
            AccountStatus.Active,
            null,
            [],
            [],
            "access-token",
            DateTime.UtcNow.AddMinutes(15),
            "refresh-token",
            DateTime.UtcNow.AddDays(14)));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, JsonOptions));
        var root = json.RootElement;

        root.GetProperty("accessToken").GetString().Should().Be("access-token");
        root.GetProperty("loginChallengeToken").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("loginChallengeExpiresAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
