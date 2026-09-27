using MealMate.Domain.Common;
using MealMate.Domain.DriverDelivery;

namespace MealMate.Domain.VoiceCalls;

/// <summary>
/// Centralized immutable policy for call lifecycle status classifications and signaling permissions.
/// </summary>
public static class CallStatePolicy
{
    public static readonly IReadOnlySet<CallStatus> OngoingStatuses = new HashSet<CallStatus>
    {
        CallStatus.Created,
        CallStatus.Ringing,
        CallStatus.Accepted,
        CallStatus.Connecting,
        CallStatus.Active
    };

    public static readonly IReadOnlySet<CallStatus> TerminalStatuses = new HashSet<CallStatus>
    {
        CallStatus.Rejected,
        CallStatus.Cancelled,
        CallStatus.Missed,
        CallStatus.Failed,
        CallStatus.Ended
    };

    public static readonly IReadOnlySet<CallStatus> WebRtcSignalingStatuses = new HashSet<CallStatus>
    {
        CallStatus.Accepted,
        CallStatus.Connecting,
        CallStatus.Active
    };

    public static bool IsOngoing(CallStatus status) => OngoingStatuses.Contains(status);
    public static bool IsTerminal(CallStatus status) => TerminalStatuses.Contains(status);
    public static bool CanSignalWebRtc(CallStatus status) => WebRtcSignalingStatuses.Contains(status);
}

/// <summary>
/// Represents a 1-to-1 in-app VoIP call session between a Customer and a Driver.
/// Bound to an active delivery <see cref="TripStop"/> context with a strict lifecycle state machine.
/// </summary>
public sealed class CallSession : AuditableEntity
{
    // EF Core parameterless constructor
    private CallSession() { }

    public CallSession(
        Guid tripStopId,
        Guid callerUserId,
        CallParticipantType callerType,
        Guid receiverUserId,
        CallParticipantType receiverType,
        Guid clientRequestId,
        DateTime startedAtUtc)
    {
        if (tripStopId == Guid.Empty)
            throw new ArgumentException("TripStopId must not be empty.", nameof(tripStopId));
        if (callerUserId == Guid.Empty)
            throw new ArgumentException("CallerUserId must not be empty.", nameof(callerUserId));
        if (receiverUserId == Guid.Empty)
            throw new ArgumentException("ReceiverUserId must not be empty.", nameof(receiverUserId));
        if (callerUserId == receiverUserId)
            throw new ArgumentException("Caller and receiver must be different users.", nameof(receiverUserId));
        if (clientRequestId == Guid.Empty)
            throw new ArgumentException("ClientRequestId must not be empty.", nameof(clientRequestId));

        Id = Guid.NewGuid();
        TripStopId = tripStopId;
        CallerUserId = callerUserId;
        CallerType = callerType;
        ReceiverUserId = receiverUserId;
        ReceiverType = receiverType;
        ClientRequestId = clientRequestId;
        StartedAtUtc = startedAtUtc;
        Status = CallStatus.Created;
    }

    /// <summary>
    /// Delivery stop context linking Customer and Driver.
    /// </summary>
    public Guid TripStopId { get; private set; }

    public TripStop? TripStop { get; private set; }

    /// <summary>
    /// Identity and role of the initiating party.
    /// </summary>
    public Guid CallerUserId { get; private set; }

    public CallParticipantType CallerType { get; private set; }

    /// <summary>
    /// Identity and role of the receiving party.
    /// </summary>
    public Guid ReceiverUserId { get; private set; }

    public CallParticipantType ReceiverType { get; private set; }

    /// <summary>
    /// Current lifecycle status of the call session.
    /// </summary>
    public CallStatus Status { get; private set; } = CallStatus.Created;

    /// <summary>
    /// Reason why the call terminated, if terminal.
    /// </summary>
    public CallEndReason? EndReason { get; private set; }

    /// <summary>
    /// When call initiation was requested by the caller.
    /// </summary>
    public DateTime StartedAtUtc { get; private set; }

    /// <summary>
    /// When the call began ringing on the receiver's device.
    /// </summary>
    public DateTime? RingingAtUtc { get; private set; }

    /// <summary>
    /// When the receiver tapped Accept.
    /// </summary>
    public DateTime? AcceptedAtUtc { get; private set; }

    /// <summary>
    /// When WebRTC peer connection setup was initiated by either participant.
    /// </summary>
    public DateTime? ConnectingAtUtc { get; private set; }

    /// <summary>
    /// When WebRTC peer connection was established and two-way audio became active.
    /// </summary>
    public DateTime? ConnectedAtUtc { get; private set; }

    /// <summary>
    /// When the call terminated (terminal state).
    /// </summary>
    public DateTime? EndedAtUtc { get; private set; }

    /// <summary>
    /// Total duration of active two-way audio communication in seconds.
    /// Calculated strictly as: <see cref="EndedAtUtc"/> - <see cref="ConnectedAtUtc"/>.
    /// </summary>
    public int? DurationSeconds { get; private set; }

    /// <summary>
    /// Client-generated idempotency key preventing duplicate call creation on tap retries.
    /// </summary>
    public Guid ClientRequestId { get; private set; }

    /// <summary>
    /// Optimistic concurrency token (PostgreSQL xmin system column).
    /// </summary>
    public uint Version { get; set; }

    public bool IsLive => CallStatePolicy.IsOngoing(Status);

    public bool IsTerminal => CallStatePolicy.IsTerminal(Status);

    public void MarkRinging(DateTime utcNow)
    {
        if (Status != CallStatus.Created)
        {
            throw new InvalidOperationException($"Cannot transition to Ringing from status '{Status}'. Call must be in Created status.");
        }

        Status = CallStatus.Ringing;
        RingingAtUtc = utcNow;
    }

    public void Accept(DateTime utcNow)
    {
        if (Status != CallStatus.Ringing)
        {
            throw new InvalidOperationException($"Cannot accept call from status '{Status}'. Call must be in Ringing status.");
        }

        Status = CallStatus.Accepted;
        AcceptedAtUtc = utcNow;
    }

    public void MarkConnecting(DateTime? utcNow = null)
    {
        if (Status != CallStatus.Accepted)
        {
            throw new InvalidOperationException($"Cannot transition to Connecting from status '{Status}'. Call must be in Accepted status.");
        }

        Status = CallStatus.Connecting;
        ConnectingAtUtc = utcNow ?? DateTime.UtcNow;
    }

    public void MarkConnected(DateTime utcNow)
    {
        if (Status != CallStatus.Connecting)
        {
            throw new InvalidOperationException($"Cannot transition to Active from status '{Status}'. Call must be in Connecting status.");
        }

        Status = CallStatus.Active;
        ConnectedAtUtc = utcNow;
    }

    public void Reject(DateTime utcNow, CallEndReason reason = CallEndReason.ReceiverRejected)
    {
        if (Status != CallStatus.Created && Status != CallStatus.Ringing)
        {
            throw new InvalidOperationException($"Cannot reject call from status '{Status}'. Only Created or Ringing calls can be rejected.");
        }

        Status = CallStatus.Rejected;
        EndReason = reason;
        EndedAtUtc = utcNow;
    }

    public void Cancel(DateTime utcNow)
    {
        if (Status != CallStatus.Created && Status != CallStatus.Ringing)
        {
            throw new InvalidOperationException($"Cannot cancel call from status '{Status}'. Only Created or Ringing calls can be cancelled.");
        }

        Status = CallStatus.Cancelled;
        EndReason = CallEndReason.CallerCancelled;
        EndedAtUtc = utcNow;
    }

    public void MarkMissed(DateTime utcNow)
    {
        if (Status != CallStatus.Created && Status != CallStatus.Ringing)
        {
            throw new InvalidOperationException($"Cannot mark call as Missed from status '{Status}'. Only Created or Ringing calls can time out as missed.");
        }

        Status = CallStatus.Missed;
        EndReason = CallEndReason.NoAnswer;
        EndedAtUtc = utcNow;
    }

    public void MarkFailed(DateTime utcNow, CallEndReason reason = CallEndReason.ConnectionFailed)
    {
        if (Status != CallStatus.Created && Status != CallStatus.Ringing && Status != CallStatus.Accepted && Status != CallStatus.Connecting)
        {
            throw new InvalidOperationException($"Cannot mark call as Failed from status '{Status}'. Only pre-active calls can fail negotiation.");
        }

        Status = CallStatus.Failed;
        EndReason = reason;
        EndedAtUtc = utcNow;
    }

    public void End(DateTime utcNow, CallEndReason reason = CallEndReason.CallerEnded)
    {
        if (Status != CallStatus.Active)
        {
            throw new InvalidOperationException($"Cannot end call from status '{Status}'. Only an Active call can transition to Ended.");
        }

        if (!ConnectedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Active call cannot be ended without a valid ConnectedAtUtc timestamp.");
        }

        Status = CallStatus.Ended;
        EndReason = reason;
        EndedAtUtc = utcNow;

        var elapsed = utcNow - ConnectedAtUtc.Value;
        DurationSeconds = (int)Math.Max(0, Math.Round(elapsed.TotalSeconds));
    }
}
