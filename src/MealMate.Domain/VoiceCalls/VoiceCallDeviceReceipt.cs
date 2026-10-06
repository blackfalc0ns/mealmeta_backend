using MealMate.Domain.Common;

namespace MealMate.Domain.VoiceCalls;

/// <summary>
/// Per-(call, device) protocol receipts: ring acknowledgements and terminal event
/// acknowledgement watermarks. Deduplicates device events and classifies which
/// devices actually presented the incoming UI — without claiming media ownership.
/// </summary>
public sealed class VoiceCallDeviceReceipt : AuditableEntity
{
    private VoiceCallDeviceReceipt() { }

    public VoiceCallDeviceReceipt(Guid callId, Guid deviceSessionId, DateTime ringingAckAtUtc)
    {
        if (callId == Guid.Empty)
            throw new ArgumentException("CallId must not be empty.", nameof(callId));
        if (deviceSessionId == Guid.Empty)
            throw new ArgumentException("DeviceSessionId must not be empty.", nameof(deviceSessionId));

        CallId = callId;
        DeviceSessionId = deviceSessionId;
        RingingAckAtUtc = ringingAckAtUtc;
    }

    public Guid CallId { get; private set; }

    public Guid DeviceSessionId { get; private set; }

    public DateTime? RingingAckAtUtc { get; private set; }

    /// <summary>Highest terminal lifecycle sequence this device acknowledged.</summary>
    public long LastTerminalAckSequence { get; private set; }

    public void AcknowledgeTerminal(long sequence)
    {
        if (sequence > LastTerminalAckSequence)
        {
            LastTerminalAckSequence = sequence;
        }
    }
}
