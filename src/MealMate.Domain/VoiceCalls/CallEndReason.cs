namespace MealMate.Domain.VoiceCalls;

/// <summary>
/// Explains why a 1-to-1 in-app VoIP call session terminated.
/// </summary>
public enum CallEndReason
{
    CallerEnded = 1,
    ReceiverEnded = 2,
    ReceiverRejected = 3,
    CallerCancelled = 4,
    NoAnswer = 5,
    ConnectionFailed = 6,
    NetworkLost = 7,
    Timeout = 8,
    AppClosed = 9,
    Busy = 10,
}
