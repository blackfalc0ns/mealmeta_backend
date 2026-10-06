using MealMate.Domain.Common;

namespace MealMate.Domain.DriverDelivery;

/// <summary>
/// Append-only evidence for a conditional customer-phone reveal (F14/spec 4.3).
/// Stores grant metadata only — the revealed phone number itself must never be
/// written here, into audit rows, outbox payloads, or logs.
/// </summary>
public sealed class CustomerPhoneRevealGrant : AuditableEntity
{
    private CustomerPhoneRevealGrant() { }

    public CustomerPhoneRevealGrant(
        Guid contactCaseId,
        Guid driverUserId,
        Guid firstAttemptCallId,
        Guid secondAttemptCallId,
        Guid clientRequestId,
        string reason,
        DateTime expiresAtUtc,
        DateTime createdAtUtc)
    {
        if (contactCaseId == Guid.Empty)
            throw new ArgumentException("ContactCaseId must not be empty.", nameof(contactCaseId));
        if (driverUserId == Guid.Empty)
            throw new ArgumentException("DriverUserId must not be empty.", nameof(driverUserId));
        if (firstAttemptCallId == Guid.Empty || secondAttemptCallId == Guid.Empty)
            throw new ArgumentException("Both qualifying attempt call ids are required.");
        if (clientRequestId == Guid.Empty)
            throw new ArgumentException("ClientRequestId must not be empty.", nameof(clientRequestId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));

        ContactCaseId = contactCaseId;
        DriverUserId = driverUserId;
        FirstAttemptCallId = firstAttemptCallId;
        SecondAttemptCallId = secondAttemptCallId;
        ClientRequestId = clientRequestId;
        Reason = reason;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid ContactCaseId { get; private set; }

    public Guid DriverUserId { get; private set; }

    public Guid FirstAttemptCallId { get; private set; }

    public Guid SecondAttemptCallId { get; private set; }

    /// <summary>Idempotency key scoped to (DriverUserId, ClientRequestId).</summary>
    public Guid ClientRequestId { get; private set; }

    public string Reason { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? ConsumedAtUtc { get; private set; }

    public bool IsExpired(DateTime utcNow) => utcNow > ExpiresAtUtc;
}
