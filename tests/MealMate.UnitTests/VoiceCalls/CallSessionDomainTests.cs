using FluentAssertions;
using MealMate.Domain.VoiceCalls;
using Xunit;

namespace MealMate.UnitTests.VoiceCalls;

/// <summary>
/// Domain unit tests for <see cref="CallSession"/> state machine, transitions, timestamps, and duration calculation.
/// </summary>
public sealed class CallSessionDomainTests
{
    private readonly Guid _tripStopId = Guid.NewGuid();
    private readonly Guid _callerUserId = Guid.NewGuid();
    private readonly Guid _receiverUserId = Guid.NewGuid();
    private readonly Guid _clientRequestId = Guid.NewGuid();
    private readonly DateTime _startedAt = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

    private CallSession CreateSession()
    {
        return new CallSession(
            tripStopId: _tripStopId,
            callerUserId: _callerUserId,
            callerType: CallParticipantType.Driver,
            receiverUserId: _receiverUserId,
            receiverType: CallParticipantType.Customer,
            clientRequestId: _clientRequestId,
            startedAtUtc: _startedAt);
    }

    [Fact]
    public void Constructor_ShouldInitializeCallSessionInCreatedStatus()
    {
        // Act
        var session = CreateSession();

        // Assert
        session.Id.Should().NotBeEmpty();
        session.TripStopId.Should().Be(_tripStopId);
        session.CallerUserId.Should().Be(_callerUserId);
        session.CallerType.Should().Be(CallParticipantType.Driver);
        session.ReceiverUserId.Should().Be(_receiverUserId);
        session.ReceiverType.Should().Be(CallParticipantType.Customer);
        session.ClientRequestId.Should().Be(_clientRequestId);
        session.StartedAtUtc.Should().Be(_startedAt);
        session.Status.Should().Be(CallStatus.Created);
        session.EndReason.Should().BeNull();
        session.RingingAtUtc.Should().BeNull();
        session.AcceptedAtUtc.Should().BeNull();
        session.ConnectedAtUtc.Should().BeNull();
        session.EndedAtUtc.Should().BeNull();
        session.DurationSeconds.Should().BeNull();
        session.IsLive.Should().BeTrue();
        session.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Transition_Created_To_Ringing_ShouldSetStatusAndTimestamp()
    {
        // Arrange
        var session = CreateSession();
        var ringingAt = _startedAt.AddSeconds(2);

        // Act
        session.MarkRinging(ringingAt);

        // Assert
        session.Status.Should().Be(CallStatus.Ringing);
        session.RingingAtUtc.Should().Be(ringingAt);
        session.IsLive.Should().BeTrue();
    }

    [Fact]
    public void Transition_Ringing_To_Accepted_ShouldSetStatusAndTimestamp()
    {
        // Arrange
        var session = CreateSession();
        var ringingAt = _startedAt.AddSeconds(2);
        var acceptedAt = _startedAt.AddSeconds(8);
        session.MarkRinging(ringingAt);

        // Act
        session.Accept(acceptedAt);

        // Assert
        session.Status.Should().Be(CallStatus.Accepted);
        session.AcceptedAtUtc.Should().Be(acceptedAt);
        session.IsLive.Should().BeTrue();
    }

    [Fact]
    public void Transition_Accepted_To_Connecting_ShouldSetStatus()
    {
        // Arrange
        var session = CreateSession();
        session.MarkRinging(_startedAt.AddSeconds(2));
        session.Accept(_startedAt.AddSeconds(5));

        // Act
        session.MarkConnecting();

        // Assert
        session.Status.Should().Be(CallStatus.Connecting);
        session.IsLive.Should().BeTrue();
    }

    [Fact]
    public void Transition_Connecting_To_Active_ShouldSetStatusAndConnectedTimestamp()
    {
        // Arrange
        var session = CreateSession();
        session.MarkRinging(_startedAt.AddSeconds(2));
        session.Accept(_startedAt.AddSeconds(5));
        session.MarkConnecting();
        var connectedAt = _startedAt.AddSeconds(7);

        // Act
        session.MarkConnected(connectedAt);

        // Assert
        session.Status.Should().Be(CallStatus.Active);
        session.ConnectedAtUtc.Should().Be(connectedAt);
        session.IsLive.Should().BeTrue();
    }

    [Fact]
    public void Transition_Active_To_Ended_ShouldCalculateDurationStrictlyFromConnectedAtUtc()
    {
        // Arrange
        var session = CreateSession();
        var ringingAt = _startedAt.AddSeconds(2);
        var acceptedAt = _startedAt.AddSeconds(10);
        var connectedAt = _startedAt.AddSeconds(15);
        var endedAt = _startedAt.AddSeconds(135); // 120 seconds after connected

        session.MarkRinging(ringingAt);
        session.Accept(acceptedAt);
        session.MarkConnecting();
        session.MarkConnected(connectedAt);

        // Act
        session.End(endedAt, CallEndReason.CallerEnded);

        // Assert
        session.Status.Should().Be(CallStatus.Ended);
        session.EndReason.Should().Be(CallEndReason.CallerEnded);
        session.EndedAtUtc.Should().Be(endedAt);

        // Duration MUST be EndedAtUtc - ConnectedAtUtc (135 - 15 = 120s)
        // NOT EndedAtUtc - StartedAtUtc (135s)
        // NOT EndedAtUtc - AcceptedAtUtc (125s)
        session.DurationSeconds.Should().Be(120);
        session.DurationSeconds.Should().NotBe(135);
        session.DurationSeconds.Should().NotBe(125);
        session.IsTerminal.Should().BeTrue();
        session.IsLive.Should().BeFalse();
    }

    [Fact]
    public void Transition_Ringing_To_Rejected_ShouldSetStatusAndReason()
    {
        // Arrange
        var session = CreateSession();
        session.MarkRinging(_startedAt.AddSeconds(2));
        var rejectedAt = _startedAt.AddSeconds(6);

        // Act
        session.Reject(rejectedAt, CallEndReason.ReceiverRejected);

        // Assert
        session.Status.Should().Be(CallStatus.Rejected);
        session.EndReason.Should().Be(CallEndReason.ReceiverRejected);
        session.EndedAtUtc.Should().Be(rejectedAt);
        session.DurationSeconds.Should().BeNull();
        session.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Transition_Ringing_To_Cancelled_ShouldSetStatusAndReason()
    {
        // Arrange
        var session = CreateSession();
        session.MarkRinging(_startedAt.AddSeconds(2));
        var cancelledAt = _startedAt.AddSeconds(4);

        // Act
        session.Cancel(cancelledAt);

        // Assert
        session.Status.Should().Be(CallStatus.Cancelled);
        session.EndReason.Should().Be(CallEndReason.CallerCancelled);
        session.EndedAtUtc.Should().Be(cancelledAt);
        session.DurationSeconds.Should().BeNull();
        session.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Transition_Ringing_To_Missed_ShouldSetStatusAndReason()
    {
        // Arrange
        var session = CreateSession();
        session.MarkRinging(_startedAt.AddSeconds(2));
        var missedAt = _startedAt.AddSeconds(30);

        // Act
        session.MarkMissed(missedAt);

        // Assert
        session.Status.Should().Be(CallStatus.Missed);
        session.EndReason.Should().Be(CallEndReason.NoAnswer);
        session.EndedAtUtc.Should().Be(missedAt);
        session.DurationSeconds.Should().BeNull();
        session.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Transition_Connecting_To_Failed_ShouldSetStatusAndReason()
    {
        // Arrange
        var session = CreateSession();
        session.MarkRinging(_startedAt.AddSeconds(2));
        session.Accept(_startedAt.AddSeconds(5));
        session.MarkConnecting();
        var failedAt = _startedAt.AddSeconds(12);

        // Act
        session.MarkFailed(failedAt, CallEndReason.ConnectionFailed);

        // Assert
        session.Status.Should().Be(CallStatus.Failed);
        session.EndReason.Should().Be(CallEndReason.ConnectionFailed);
        session.EndedAtUtc.Should().Be(failedAt);
        session.DurationSeconds.Should().BeNull();
        session.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Invalid_Ended_To_Active_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var session = CreateSession();
        session.MarkRinging(_startedAt.AddSeconds(2));
        session.Accept(_startedAt.AddSeconds(5));
        session.MarkConnecting();
        session.MarkConnected(_startedAt.AddSeconds(7));
        session.End(_startedAt.AddSeconds(20), CallEndReason.CallerEnded);

        // Act & Assert
        var act = () => session.MarkConnected(_startedAt.AddSeconds(25));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot transition to Active*");
    }

    [Fact]
    public void Invalid_Rejected_To_Accepted_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var session = CreateSession();
        session.MarkRinging(_startedAt.AddSeconds(2));
        session.Reject(_startedAt.AddSeconds(4));

        // Act & Assert
        var act = () => session.Accept(_startedAt.AddSeconds(5));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot accept call from status 'Rejected'*");
    }

    [Fact]
    public void Invalid_Missed_To_Active_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var session = CreateSession();
        session.MarkRinging(_startedAt.AddSeconds(2));
        session.MarkMissed(_startedAt.AddSeconds(30));

        // Act & Assert
        var act = () => session.MarkConnected(_startedAt.AddSeconds(32));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot transition to Active from status 'Missed'*");
    }

    [Fact]
    public void Invalid_Cancelled_To_Accepted_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var session = CreateSession();
        session.MarkRinging(_startedAt.AddSeconds(2));
        session.Cancel(_startedAt.AddSeconds(5));

        // Act & Assert
        var act = () => session.Accept(_startedAt.AddSeconds(6));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot accept call from status 'Cancelled'*");
    }

    [Fact]
    public void Invalid_Failed_To_Active_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var session = CreateSession();
        session.MarkRinging(_startedAt.AddSeconds(2));
        session.Accept(_startedAt.AddSeconds(5));
        session.MarkConnecting();
        session.MarkFailed(_startedAt.AddSeconds(10));

        // Act & Assert
        var act = () => session.MarkConnected(_startedAt.AddSeconds(12));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot transition to Active from status 'Failed'*");
    }
}
