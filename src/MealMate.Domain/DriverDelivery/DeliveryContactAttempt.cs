using MealMate.Domain.Common;
using MealMate.Domain.VoiceCalls;

namespace MealMate.Domain.DriverDelivery;

/// <summary>
/// One persisted attempt per finished call (unique CallId), carrying the qualified
/// no-answer verdict. Manual UI entries and technical failures can never create
/// qualifying evidence.
/// </summary>
public sealed class DeliveryContactAttempt : AuditableEntity
{
    /// <summary>Driver→Customer NoAnswer with a qualifying observed ring (F14 evidence).</summary>
    public const string OutcomeQualifiedNoAnswer = "QualifiedNoAnswer";
    public const string OutcomeTechnicalNoAnswer = "TechnicalNoAnswer";
    public const string OutcomeAnswered = "Answered";
    public const string OutcomeRejected = "Rejected";
    public const string OutcomeCancelled = "Cancelled";

    private DeliveryContactAttempt() { }

    public DeliveryContactAttempt(
        Guid callId,
        Guid contactCaseId,
        Guid initiatorUserId,
        CallParticipantType initiatorType,
        string outcome,
        bool qualifiesAsNoAnswer,
        int ordinal,
        DateTime startedAtUtc,
        DateTime endedAtUtc)
    {
        if (callId == Guid.Empty)
            throw new ArgumentException("CallId must not be empty.", nameof(callId));
        if (contactCaseId == Guid.Empty)
            throw new ArgumentException("ContactCaseId must not be empty.", nameof(contactCaseId));

        CallId = callId;
        ContactCaseId = contactCaseId;
        InitiatorUserId = initiatorUserId;
        InitiatorType = initiatorType;
        Outcome = outcome;
        QualifiesAsNoAnswer = qualifiesAsNoAnswer;
        Ordinal = ordinal;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        CreatedAtUtc = endedAtUtc;
    }

    public Guid CallId { get; private set; }

    public Guid ContactCaseId { get; private set; }

    public Guid InitiatorUserId { get; private set; }

    public CallParticipantType InitiatorType { get; private set; }

    public string Outcome { get; private set; }

    public bool QualifiesAsNoAnswer { get; private set; }

    /// <summary>1-based position within the contact case (across cycles, append-only).</summary>
    public int Ordinal { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime EndedAtUtc { get; private set; }
}
