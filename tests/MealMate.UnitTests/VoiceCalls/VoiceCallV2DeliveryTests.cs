using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Services;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using MealMate.Domain.VoiceCalls;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MealMate.UnitTests.VoiceCalls;

public sealed class VoiceCallV2DeliveryTests
{
    private static AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());

    private static async Task<(CallSession Call, OutboxLifecycleNotification Event)> Seed(AppDbContext db, string type)
    {
        var now = DateTime.UtcNow;
        var call = new CallSession(Guid.NewGuid(), Guid.NewGuid(), CallParticipantType.Driver,
            Guid.NewGuid(), CallParticipantType.Customer, Guid.NewGuid(), now);
        var callerDevice = new VoiceCallDeviceSession { UserId = call.CallerUserId, InstallationId = Guid.NewGuid().ToString(), ControlProofHash = new string('a', 64), Platform = "android", ExpiresAtUtc = now.AddDays(1) };
        var receiverA = new VoiceCallDeviceSession { UserId = call.ReceiverUserId, InstallationId = Guid.NewGuid().ToString(), ControlProofHash = new string('a', 64), Platform = "android", ExpiresAtUtc = now.AddDays(1) };
        var receiverB = new VoiceCallDeviceSession { UserId = call.ReceiverUserId, InstallationId = Guid.NewGuid().ToString(), ControlProofHash = new string('a', 64), Platform = "android", ExpiresAtUtc = now.AddDays(1) };
        db.VoiceCallDeviceSessions.AddRange(callerDevice, receiverA, receiverB);
        call.MarkRingingV2(now, now.AddSeconds(30), callerDevice.Id);
        if (type != "incoming")
        {
            call.ClaimReceiverDevice(receiverA.Id, now);
            call.MarkConnecting(now);
            call.ReportConnectedSide(true, now);
            call.ReportConnectedSide(false, now);
            call.End(now.AddSeconds(1), CallEndReason.CallerEnded);
        }
        db.CallSessions.Add(call);
        await db.SaveChangesAsync();
        var id = Guid.NewGuid();
        return (call, new(id, "voice-call.lifecycle." + type, "CallSession", call.Id.ToString(), null, null,
            JsonSerializer.Serialize(new { eventId = id, callId = call.Id, sequence = call.Sequence, eventType = type, occurredAtUtc = now }), now));
    }

    private static VoiceCallOutboxHandler Handler(AppDbContext db, IVoiceCallV2PushTransport push, IVoiceCallV2RealtimeTransport realtime)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var services = new ServiceCollection().AddSingleton<IAppDbContext>(db).AddSingleton(clock)
            .AddSingleton(Substitute.For<IVoiceCallPushNotifier>()).AddSingleton(push).AddSingleton(realtime)
            .BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<VoiceCallOutboxHandler>(services);
    }

    [Theory]
    [InlineData("incoming", 2)]
    [InlineData("ended", 3)]
    public async Task EachDeviceGetsOneStableEvent_AndReplayDoesNotResend(string type, int targets)
    {
        await using var db = Db();
        var (_, evt) = await Seed(db, type);
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.Accepted, "real-boundary-test-id"));
        var realtime = Substitute.For<IVoiceCallV2RealtimeTransport>();
        var handler = Handler(db, push, realtime);
        await handler.Handle(evt, CancellationToken.None);
        await handler.Handle(evt, CancellationToken.None);
        await push.Received(targets).SendAsync(Arg.Any<VoiceCallDeviceSession>(),
            Arg.Is<CallEventEnvelope<CallSnapshotDto>>(e => e.EventId == evt.MessageId && e.ProtocolVersion == 2), Arg.Any<CancellationToken>());
        await realtime.Received(targets).SendAsync(Arg.Any<Guid>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingPushRoute_IsSuppressedWithoutFalseProviderAcceptance()
    {
        await using var db = Db();
        var (_, evt) = await Seed(db, "incoming");
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.NoRoute));
        await Handler(db, push, Substitute.For<IVoiceCallV2RealtimeTransport>()).Handle(evt, CancellationToken.None);
        var rows = await db.VoiceCallEventDeliveries.Where(d => d.Channel == VoiceCallEventDelivery.ChannelPush).ToListAsync();
        rows.Should().HaveCount(2).And.OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusSuppressed);
    }
}
