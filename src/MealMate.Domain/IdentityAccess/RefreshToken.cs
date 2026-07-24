using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

/// <summary>
/// Persisted refresh token. The raw token is never stored — only a SHA-256 hash.
/// Rotation replaces the previous token and links via <see cref="ReplacedByTokenId"/>.
/// </summary>
public sealed class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = default!;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public string? DeviceInfo { get; set; }

    public string? IpAddress { get; set; }

    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;
}
