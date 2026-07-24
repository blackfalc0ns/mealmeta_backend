using MealMate.Domain.Common;

namespace MealMate.Domain.IdentityAccess;

/// <summary>One-time password challenge for phone/email verification or password reset.</summary>
public sealed class OtpChallenge : BaseEntity
{
    public Guid? UserId { get; set; }

    /// <summary>Normalized destination (E.164 phone or lowercased email).</summary>
    public string Destination { get; set; } = default!;

    /// <summary>Channel: Phone | Email.</summary>
    public string Channel { get; set; } = default!;

    /// <summary>Purpose: VerifyPhone | VerifyEmail | PasswordReset | Login.</summary>
    public string Purpose { get; set; } = default!;

    public string CodeHash { get; set; } = default!;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ConsumedAtUtc { get; set; }

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 5;
}
