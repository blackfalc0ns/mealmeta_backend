using MealMate.Domain.Common;

namespace MealMate.Domain.VoiceCalls;

/// <summary>
/// One row per participant of an ongoing call (PK = UserId), making a busy user a
/// database-level fact: an account connected on one side of a call cannot enter
/// another call as caller OR receiver. Rows are deleted only inside the terminal
/// transaction of their call.
/// </summary>
public sealed class VoiceCallBusyParticipant : BaseEntity
{
    private VoiceCallBusyParticipant() { }

    public VoiceCallBusyParticipant(Guid userId, Guid callId, DateTime createdAtUtc)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (callId == Guid.Empty)
            throw new ArgumentException("CallId must not be empty.", nameof(callId));

        UserId = userId;
        CallId = callId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid UserId { get; private set; }

    public Guid CallId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
