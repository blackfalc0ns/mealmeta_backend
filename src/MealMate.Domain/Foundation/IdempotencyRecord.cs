using MealMate.Domain.Common;

namespace MealMate.Domain.Foundation;

/// <summary>
/// Stores Idempotency-Key outcomes so repeated create/action requests are safe.
/// Append-only operational cache (not soft-deleted).
/// </summary>
public sealed class IdempotencyRecord : BaseEntity
{
    public string Key { get; set; } = default!;

    public string RequestHash { get; set; } = default!;

    public string? ResponseBody { get; set; }

    public int? HttpStatusCode { get; set; }

    public Guid? UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
}
