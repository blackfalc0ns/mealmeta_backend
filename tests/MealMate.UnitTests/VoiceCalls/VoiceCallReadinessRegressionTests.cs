using FluentAssertions;
using MealMate.Domain.VoiceCalls;
using Xunit;

namespace MealMate.UnitTests.VoiceCalls;

public sealed class VoiceCallReadinessRegressionTests
{
    [Fact]
    public void Heartbeat_OneContinuingPeer_CannotExtendTheOtherPeersLease()
    {
        var now = DateTime.UtcNow;
        var call = new CallSession(Guid.NewGuid(), Guid.NewGuid(), CallParticipantType.Driver,
            Guid.NewGuid(), CallParticipantType.Customer, Guid.NewGuid(), now);
        call.MarkRingingV2(now, now.AddSeconds(30), Guid.NewGuid());
        call.ClaimReceiverDevice(Guid.NewGuid(), now);
        call.MarkConnecting(now);
        call.ReportConnectedSide(true, now);
        call.ReportConnectedSide(false, now);
        call.ApplyHeartbeat(false, now, now.AddSeconds(45));
        call.ApplyHeartbeat(true, now.AddSeconds(40), now.AddSeconds(85));
        call.MediaLeaseExpiresAtUtc.Should().Be(now.AddSeconds(45));
    }
}
