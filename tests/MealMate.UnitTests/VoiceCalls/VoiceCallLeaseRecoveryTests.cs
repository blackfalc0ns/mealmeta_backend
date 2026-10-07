using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Domain.VoiceCalls;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.VoiceCalls;

public sealed class VoiceCallLeaseRecoveryTests
{
    private static CallSession Active(DateTime now)
    {
        var call = new CallSession(Guid.NewGuid(), Guid.NewGuid(), CallParticipantType.Driver,
            Guid.NewGuid(), CallParticipantType.Customer, Guid.NewGuid(), now);
        call.MarkRingingV2(now, now.AddSeconds(30), Guid.NewGuid());
        call.ClaimReceiverDevice(Guid.NewGuid(), now);
        call.MarkConnecting(now);
        call.ReportConnectedSide(true, now);
        call.ReportConnectedSide(false, now);
        return call;
    }

    [Fact]
    public void Activation_StartsLeaseBeforeAnyHeartbeat()
    {
        var now = DateTime.UtcNow;
        Active(now).MediaLeaseExpiresAtUtc.Should().Be(now.AddSeconds(45));
    }

    [Fact]
    public void TerminalHeartbeat_CannotChangeLiveness()
    {
        var now = DateTime.UtcNow;
        var call = Active(now);
        call.End(now.AddSeconds(10));
        var lease = call.MediaLeaseExpiresAtUtc;
        var heartbeat = () => call.ApplyHeartbeat(true, now.AddSeconds(20), now.AddSeconds(65));
        heartbeat.Should().Throw<InvalidOperationException>();
        call.CallerLastHeartbeatAtUtc.Should().BeNull();
        call.MediaLeaseExpiresAtUtc.Should().Be(lease);
    }

    [Fact]
    public async Task ExpiredActivePeerLease_EndsCallAndReleasesBusySlots()
    {
        var now = DateTime.UtcNow;
        var call = Active(now);
        call.ApplyHeartbeat(false, now, now.AddSeconds(45));
        call.ApplyHeartbeat(true, now.AddSeconds(40), now.AddSeconds(85));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        db.CallSessions.Add(call);
        db.VoiceCallBusyParticipants.Add(new VoiceCallBusyParticipant(call.CallerUserId, call.Id, now));
        db.VoiceCallBusyParticipants.Add(new VoiceCallBusyParticipant(call.ReceiverUserId, call.Id, now));
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now.AddSeconds(46));
        var result = await new ExpireVoiceCallCommandHandler(db, clock).Handle(
            new ExpireVoiceCallCommand(call.Id, CallStatus.Active, call.Sequence, now.AddSeconds(45), CallEndReason.NetworkLost), default);
        result.IsSuccess.Should().BeTrue();
        call.Status.Should().Be(CallStatus.Ended);
        call.EndReason.Should().Be(CallEndReason.NetworkLost);
        call.TerminalEventId.Should().NotBeNull();
        (await db.VoiceCallBusyParticipants.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task WorkerSelectedLease_RefreshedByBothPeers_DoesNotEndCall()
    {
        var now = DateTime.UtcNow;
        var call = Active(now);
        call.ApplyHeartbeat(true, now.AddSeconds(40), now.AddSeconds(85));
        call.ApplyHeartbeat(false, now.AddSeconds(40), now.AddSeconds(85));
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        db.CallSessions.Add(call);
        await db.SaveChangesAsync();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(now.AddSeconds(46));
        var result = await new ExpireVoiceCallCommandHandler(db, clock).Handle(
            new ExpireVoiceCallCommand(call.Id, CallStatus.Active, call.Sequence, now.AddSeconds(45), CallEndReason.NetworkLost), default);
        result.IsSuccess.Should().BeTrue();
        call.Status.Should().Be(CallStatus.Active);
        call.TerminalEventId.Should().BeNull();
    }
}
