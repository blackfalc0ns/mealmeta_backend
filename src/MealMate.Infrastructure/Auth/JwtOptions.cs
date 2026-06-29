using System.ComponentModel.DataAnnotations;

namespace MealMate.Infrastructure.Auth;

/// <summary>Bound from the <c>Jwt</c> configuration section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = default!;

    [Required]
    public string Audience { get; set; } = default!;

    /// <summary>Signing key. Keep in user-secrets / environment, never in source.</summary>
    [Required]
    [MinLength(32)]
    public string SecretKey { get; set; } = default!;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 14;
}
