using MealMate.Domain.Common;

namespace MealMate.Domain.VoiceCalls;

/// <summary>
/// Durable per-(event, channel, device) delivery record for lifecycle events.
/// Provider acceptance is recorded distinctly from device acknowledgement —
/// AcceptedByProvider must never be presented as DeliveredToDevice.
/// </summary>
public sealed class VoiceCallEventDelivery : AuditableEntity
{
    public const string StatusPending = "Pending";
    public const string StatusAcceptedByProvider = "AcceptedByProvider";
    public const string StatusSuppressed = "Suppressed";
    public const string StatusFailed = "Failed";
    public const string StatusDispatchedToHub = "DispatchedToHub";

    public const string ChannelPush = "Push";
    public const string ChannelSignalR = "SignalR";

    private VoiceCallEventDelivery() { }

    public VoiceCallEventDelivery(
        Guid eventId,
        string channel,
        Guid? recipientDeviceSessionId,
        Guid callId,
        long sequence,
        DateTime createdAtUtc)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("EventId must not be empty.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Channel must not be empty.", nameof(channel));

        EventId = eventId;
        Channel = channel;
        RecipientDeviceSessionId = recipientDeviceSessionId;
        CallId = callId;
        Sequence = sequence;
        Status = StatusPending;
        AttemptCount = 0;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid EventId { get; private set; }

    public string Channel { get; private set; }

    /// <summary>Null targets the account-wide channel (account devices without a bound session).</summary>
    public Guid? RecipientDeviceSessionId { get; private set; }

    public Guid CallId { get; private set; }

    public long Sequence { get; private set; }

    public string Status { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTime? NextAttemptAtUtc { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public void MarkAccepted(string? providerMessageId, DateTime utcNow)
    {
        if (Status == StatusAcceptedByProvider)
        {
            return; // idempotent re-acceptance
        }

        Status = StatusAcceptedByProvider;
        ProviderMessageId = providerMessageId;
        ModifiedAtUtc = utcNow;
    }

    public void MarkSuppressed(DateTime utcNow)
    {
        Status = StatusSuppressed;
        ModifiedAtUtc = utcNow;
    }

    public void MarkDispatchedToHub(DateTime utcNow)
    {
        Status = StatusDispatchedToHub;
        ModifiedAtUtc = utcNow;
    }

    public void MarkFailedForRetry(DateTime nextAttemptAtUtc, DateTime utcNow)
    {
        Status = StatusFailed;
        AttemptCount++;
        NextAttemptAtUtc = nextAttemptAtUtc;
        ModifiedAtUtc = utcNow;
    }

    public void ResetForRetry(DateTime utcNow)
    {
        Status = StatusPending;
        NextAttemptAtUtc = null;
        ModifiedAtUtc = utcNow;
    }
}
