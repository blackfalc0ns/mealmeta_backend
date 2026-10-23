using MealMate.Application.Common.Messaging;

namespace MealMate.Application.Features.VoiceCalls.V2.Dtos;

/// <summary>
/// v2 protocol wire contracts (spec 7.2). Field names are frozen by golden
/// serialization tests — camelCase over JSON — and must not drift between REST,
/// Hub and push payloads. Phone numbers, SDP/ICE storage and client secrets are
/// banned from every persisted/relayed lifecycle payload.
/// </summary>
public readonly record struct ProtocolVersion
{
    public const int Current = 2;
}

/// <summary>Authenticated device-scoped control context resolved from headers/Hub bindings.</summary>
public sealed record CallControlContext(Guid UserId, Guid DeviceSessionId);

/// <summary>
/// Authoritative server snapshot of a call for one participant. Incoming pushes
/// may include driver display context, but never phone numbers or credentials.
/// </summary>
public sealed record CallSnapshotDto(
    Guid CallId,
    Guid TripStopId,
    int ProtocolVersion,
    long Sequence,
    string Status,
    string? EndReason,
    DateTime StartedAtUtc,
    DateTime? AcceptedAtUtc,
    DateTime? ConnectedAtUtc,
    DateTime? EndedAtUtc,
    int? DurationSeconds,
    DateTime? DeadlineAtUtc,
    Guid? WinningDeviceSessionId,
    string LocalParticipantRole)
{
    public string? DriverName { get; init; }
    public string? CallerName => DriverName;
    public string? DriverRole { get; init; }
    public string? CallerRole => DriverRole;
    public string? DriverImageUrl { get; init; }
    public string? AvatarUrl => DriverImageUrl;
    public string? OrderCode { get; init; }
    public string? OrderNumber => OrderCode;
    public string? DeliveryAddress { get; init; }
}

/// <summary>
/// Lifecycle event envelope. Ephemeral RTC media (SDP/ICE) never travels in this
/// shape — see <see cref="RtcRelayEnvelope{T}"/>.
/// </summary>
public sealed record CallEventEnvelope<T>(
    Guid EventId,
    int ProtocolVersion,
    Guid CallId,
    long Sequence,
    string EventType,
    DateTime OccurredAtUtc,
    DateTime SentAtUtc,
    DateTime? ExpiresAtUtc,
    T Payload)
{
    // Mirror the same display values for mobile clients reading either level.
    public string? DriverName => (Payload as CallSnapshotDto)?.DriverName;
    public string? CallerName => DriverName;
    public string? DriverRole => (Payload as CallSnapshotDto)?.DriverRole;
    public string? CallerRole => DriverRole;
    public string? DriverImageUrl => (Payload as CallSnapshotDto)?.DriverImageUrl;
    public string? AvatarUrl => DriverImageUrl;
    public string? OrderCode => (Payload as CallSnapshotDto)?.OrderCode;
    public string? OrderNumber => OrderCode;
    public string? DeliveryAddress => (Payload as CallSnapshotDto)?.DeliveryAddress;
}

/// <summary>
/// Ephemeral WebRTC relay envelope, scoped to the two winning device sessions.
/// The server never stores Payload contents.
/// </summary>
public sealed record RtcRelayEnvelope<T>(
    Guid MessageId,
    Guid CallId,
    int NegotiationGeneration,
    Guid SenderDeviceSessionId,
    DateTime SentAtUtc,
    T Payload);

/// <summary>
/// Marker for v2 commands whose persistence is owned by the mutation path (T08+).
/// Inherits ISelfPersistingCommand (V07): v2 mutations commit inside their own
/// lock/transaction, so UnitOfWorkBehavior must never run a second save.
/// </summary>
public interface IVoiceCallV2Command : ISelfPersistingCommand;
