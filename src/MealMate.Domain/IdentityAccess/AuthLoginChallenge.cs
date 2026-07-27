using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

/// <summary>
/// Short-lived login challenge issued after successful admin password verification.
/// Raw token is never stored — only a SHA-256 hash.
/// </summary>
public sealed class AuthLoginChallenge : BaseEntity
{
    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = default!;

    /// <summary>Challenge purpose: <c>enroll_totp</c> or <c>verify_totp</c>.</summary>
    public string NextStep { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public DateTime? ConsumedAtUtc { get; set; }

    public string? DeviceInfo { get; set; }

    public string? IpAddress { get; set; }
}
