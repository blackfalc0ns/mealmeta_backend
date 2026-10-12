using System.Text.Json;
using FluentAssertions;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Outbox;
using MealMate.Application.Features.CustomerPush;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_outbox_worker_retries_route_failure_then_respects_refresh_and_terminal(bool ended)
    {
        var store = Guid.NewGuid().ToString();
        await using var db = SharedDb(store);
        var (call, evt) = await Seed(db, "incoming");
        await BindReceiverSessionsToRouteAsync(db, call, "worker-install", "old-worker-route");
        var msg = new MealMate.Domain.Common.OutboxMessage {
            EventType = evt.EventType, AggregateType = "CallSession", AggregateId = call.Id.ToString(),
            PayloadJson = evt.PayloadJson, CreatedAtUtc = DateTime.UtcNow, EventVersion = 2
        };
        db.OutboxMessages.Add(msg);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(_ => now);
        var fresh = false;
        var pushes = new List<Guid?>();
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(ci => {
                pushes.Add(ci.Arg<VoiceCallDeviceSession>().FcmDeviceTokenId);
                return fresh ? new VoiceCallPushResult(VoiceCallPushOutcome.Accepted)
                    : new VoiceCallPushResult(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.InactiveFcmRoute);
            });
        var hub = Substitute.For<IVoiceCallV2RealtimeTransport>();
        var mediator = Substitute.For<MediatR.IMediator>();
        mediator.Publish(Arg.Any<OutboxLifecycleNotification>(), Arg.Any<CancellationToken>())
            .Returns(async ci => {
                await using var context = SharedDb(store);
                await new VoiceCallOutboxHandler(context, clock, Substitute.For<IVoiceCallPushNotifier>(),
                    v2Push: push, v2Realtime: hub)
                    .Handle(ci.Arg<OutboxLifecycleNotification>(), ci.Arg<CancellationToken>());
            });
        using var services = new ServiceCollection()
            .AddScoped<IAppDbContext>(_ => SharedDb(store))
            .AddSingleton(mediator).BuildServiceProvider();
        using var worker = new MealMate.Persistence.Outbox.OutboxBackgroundJob(
            services.GetRequiredService<IServiceScopeFactory>(), clock: clock);
        (await worker.ProcessPendingMessagesAsync(CancellationToken.None)).Should().Be(0);
        await using (var verify = SharedDb(store))
        {
            var pending = await verify.OutboxMessages.SingleAsync();
            pending.ProcessedAtUtc.Should().BeNull();
            pending.NextAttemptAtUtc.Should().NotBeNull();
            pending.RetryCount.Should().Be(1);
        }
        await using (var refresh = SharedDb(store))
        {
            await RefreshHandler(refresh, call.ReceiverUserId).Handle(
                new("new-worker-route", MealMate.Domain.Enums.MobilePlatform.Android, "worker-install"), default);
            if (ended)
            {
                var terminal = await refresh.CallSessions.SingleAsync();
                terminal.Cancel(now);
                terminal.AdvanceSequence();
                await refresh.SaveChangesAsync();
            }
        }
        fresh = true;
        now = now.AddSeconds(6);
        var priorCalls = pushes.Count;
        (await worker.ProcessPendingMessagesAsync(CancellationToken.None)).Should().Be(1);
        await using var settled = SharedDb(store);
        (await settled.OutboxMessages.SingleAsync()).ProcessedAtUtc.Should().NotBeNull();
        if (ended) pushes.Count.Should().Be(priorCalls);
        else
        {
            var freshId = (await settled.CustomerDeviceTokens.SingleAsync(t => t.Token == "new-worker-route")).Id;
            pushes.Skip(priorCalls).Should().HaveCount(2).And.OnlyContain(id => id == freshId);
            (await worker.ProcessPendingMessagesAsync(CancellationToken.None)).Should().Be(0);
        }
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("missing")]
    public async Task Session_invalidated_during_hub_send_never_uses_the_old_push_target(string invalidation)
    {
        var store = Guid.NewGuid().ToString();
        await using var db = SharedDb(store);
        var (call, evt) = await Seed(db, "incoming");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hub = Substitute.For<IVoiceCallV2RealtimeTransport>();
        hub.SendAsync(Arg.Any<Guid>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(async _ => { started.TrySetResult(); await release.Task; return false; });
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.Accepted));
        var dispatch = Handler(db, push, hub).Handle(evt, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using (var other = SharedDb(store))
        {
            var sessions = await other.VoiceCallDeviceSessions.Where(s => s.UserId == call.ReceiverUserId).ToListAsync();
            foreach (var session in sessions)
            {
                if (invalidation == "revoked") session.RevokedAtUtc = DateTime.UtcNow;
                else if (invalidation == "expired") session.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
                else other.VoiceCallDeviceSessions.Remove(session);
            }
            await other.SaveChangesAsync();
        }
        release.SetResult();
        await dispatch;
        Assert.Empty(push.ReceivedCalls());
    }

    [Fact]
    public async Task Recoverable_incoming_route_failure_demands_durable_outbox_retry()
    {
        await using var db = Db();
        var (_, evt) = await Seed(db, "incoming");
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.InactiveFcmRoute));
        await FluentActions.Awaiting(() => Handler(db, push, Substitute.For<IVoiceCallV2RealtimeTransport>()).Handle(evt, default))
            .Should().ThrowAsync<AggregateException>();
        var rows = await db.VoiceCallEventDeliveries.Where(d => d.Channel == VoiceCallEventDelivery.ChannelPush).ToListAsync();
        rows.Should().OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusFailed && d.NextAttemptAtUtc != null);
    }

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
        realtime.SendAsync(Arg.Any<Guid>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = Handler(db, push, realtime);
        await handler.Handle(evt, CancellationToken.None);
        await handler.Handle(evt, CancellationToken.None);
        await push.Received(targets).SendAsync(Arg.Any<VoiceCallDeviceSession>(),
            Arg.Is<CallEventEnvelope<CallSnapshotDto>>(e => e.EventId == evt.MessageId && e.ProtocolVersion == 2), Arg.Any<CancellationToken>());
        await realtime.Received(targets).SendAsync(Arg.Any<Guid>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>());
        // Accepted targets are never marked suppressed, and replay must not resend them.
        var rows = await db.VoiceCallEventDeliveries.ToListAsync();
        rows.Should().HaveCount(targets * 2);
        rows.Where(d => d.Channel == VoiceCallEventDelivery.ChannelPush)
            .Should().OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusAcceptedByProvider && d.ReasonCode == null);
        rows.Where(d => d.Channel == VoiceCallEventDelivery.ChannelSignalR)
            .Should().OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusDispatchedToHub);
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

    [Fact]
    public async Task Transport_reason_code_is_persisted_on_the_delivery_row()
    {
        await using var db = Db();
        var (_, evt) = await Seed(db, "incoming");
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.InactiveFcmRoute));
        await FluentActions.Awaiting(() => Handler(db, push, Substitute.For<IVoiceCallV2RealtimeTransport>()).Handle(evt, CancellationToken.None))
            .Should().ThrowAsync<AggregateException>();
        var rows = await db.VoiceCallEventDeliveries.Where(d => d.Channel == VoiceCallEventDelivery.ChannelPush).ToListAsync();
        rows.Should().OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusFailed
            && d.ReasonCode == VoiceCallPushReasons.InactiveFcmRoute);
    }

    [Fact]
    public async Task Unbound_hub_is_recorded_as_hub_unbound_without_provider_claim()
    {
        await using var db = Db();
        var (_, evt) = await Seed(db, "incoming");
        var realtime = Substitute.For<IVoiceCallV2RealtimeTransport>();
        realtime.SendAsync(Arg.Any<Guid>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.MissingFcmReference));
        await FluentActions.Awaiting(() => Handler(db, push, realtime).Handle(evt, CancellationToken.None))
            .Should().ThrowAsync<AggregateException>();
        var rows = await db.VoiceCallEventDeliveries.Where(d => d.Channel == VoiceCallEventDelivery.ChannelSignalR).ToListAsync();
        rows.Should().HaveCount(2).And.OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusSuppressed
            && d.ReasonCode == VoiceCallPushReasons.HubUnbound);
    }

    private static OutboxLifecycleNotification LifecycleEvent(CallSession call, string type, long sequence)
    {
        var id = Guid.NewGuid();
        return new(id, "voice-call.lifecycle." + type, "CallSession", call.Id.ToString(), null, null,
            JsonSerializer.Serialize(new { eventId = id, callId = call.Id, sequence, eventType = type, occurredAtUtc = DateTime.UtcNow }),
            DateTime.UtcNow);
    }

    [Fact]
    public async Task Old_incoming_is_suppressed_after_receiver_accepts()
    {
        await using var db = Db();
        var (call, notification) = await Seed(db, "incoming");
        var receiver = await db.VoiceCallDeviceSessions.FirstAsync(
            d => d.UserId == call.ReceiverUserId);
        call.ClaimReceiverDevice(receiver.Id, DateTime.UtcNow);
        await db.SaveChangesAsync();
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        var hub = Substitute.For<IVoiceCallV2RealtimeTransport>();
        await Handler(db, push, hub).Handle(notification, default);
        Assert.Empty(push.ReceivedCalls());
        Assert.Empty(hub.ReceivedCalls());
        var rows = await db.VoiceCallEventDeliveries.ToListAsync();
        rows.Should().NotBeEmpty().And.OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusSuppressed
            && d.ReasonCode == VoiceCallPushReasons.ObsoleteLifecycle);
    }

    [Fact]
    public async Task Superseded_answered_is_never_dispatched_and_the_newer_revision_still_arrives()
    {
        await using var db = Db();
        var (call, _) = await Seed(db, "incoming");
        var receiver = await db.VoiceCallDeviceSessions.FirstAsync(d => d.UserId == call.ReceiverUserId);

        // Committed production order: answered (2), connecting (3), connected (4).
        call.ClaimReceiverDevice(receiver.Id, DateTime.UtcNow);
        await db.SaveChangesAsync();
        var answeredEvent = LifecycleEvent(call, "answered", 2);

        call.MarkConnecting(DateTime.UtcNow);
        call.AdvanceSequence(); // the stager advances at commit time for "connecting"
        call.ReportConnectedSide(true, DateTime.UtcNow);
        call.ReportConnectedSide(false, DateTime.UtcNow); // flips to Active, sequence 4
        await db.SaveChangesAsync();
        var connectedEvent = LifecycleEvent(call, "connected", 4);

        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(new VoiceCallPushResult(VoiceCallPushOutcome.Accepted, "late-worker"));
        var hub = Substitute.For<IVoiceCallV2RealtimeTransport>();
        hub.SendAsync(Arg.Any<Guid>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = Handler(db, push, hub);

        // The delayed worker processes the stale answered event first: it must not
        // open ringing or roll the state back on any device.
        await handler.Handle(answeredEvent, CancellationToken.None);
        Assert.Empty(push.ReceivedCalls());
        Assert.Empty(hub.ReceivedCalls());
        (await db.VoiceCallEventDeliveries.ToListAsync()).Should().OnlyContain(
            d => d.Status == VoiceCallEventDelivery.StatusSuppressed
                 && d.ReasonCode == VoiceCallPushReasons.ObsoleteLifecycle);

        // The newer committed event is not lost: it reaches every device of both
        // participants carrying the current revision.
        await handler.Handle(connectedEvent, CancellationToken.None);
        await push.Received(3).SendAsync(Arg.Any<VoiceCallDeviceSession>(),
            Arg.Is<CallEventEnvelope<CallSnapshotDto>>(e => e.EventType == "connected"
                && e.Sequence == 4 && e.Payload.Sequence == 4 && e.Payload.Status == "Active"),
            Arg.Any<CancellationToken>());
        await hub.Received(3).SendAsync(Arg.Any<Guid>(),
            Arg.Is<CallEventEnvelope<CallSnapshotDto>>(e => e.Sequence == 4 && e.EventType == "connected"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delivered_envelope_is_internally_consistent_with_the_committed_revision()
    {
        await using var db = Db();
        var (call, evt) = await Seed(db, "incoming");
        var captured = new List<CallEventEnvelope<CallSnapshotDto>>();
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured.Add(ci.Arg<CallEventEnvelope<CallSnapshotDto>>());
                return new VoiceCallPushResult(VoiceCallPushOutcome.Accepted, "golden");
            });
        var hub = Substitute.For<IVoiceCallV2RealtimeTransport>();
        hub.SendAsync(Arg.Any<Guid>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        await Handler(db, push, hub).Handle(evt, CancellationToken.None);

        captured.Should().HaveCount(2);
        foreach (var envelope in captured)
        {
            envelope.CallId.Should().Be(call.Id);
            envelope.Sequence.Should().Be(call.Sequence);
            envelope.EventType.Should().Be("incoming");
            envelope.Payload.CallId.Should().Be(call.Id);
            envelope.Payload.Sequence.Should().Be(envelope.Sequence, "the snapshot revision must match the envelope revision");
            envelope.Payload.Status.Should().Be("Ringing", "eventType must stay consistent with the snapshot state");
            envelope.OccurredAtUtc.Should().BeOnOrBefore(envelope.SentAtUtc);
            envelope.ExpiresAtUtc.Should().Be(call.RingExpiresAtUtc, "an incoming ring never outlives its deadline");
        }
    }

    [Fact]
    public async Task Provider_failure_retries_the_same_event_and_never_resends_accepted_targets()
    {
        await using var db = Db();
        var (_, evt) = await Seed(db, "incoming");
        var attempts = 0;
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                return attempts <= 2
                    ? throw new HttpRequestException("provider unavailable")
                    : new VoiceCallPushResult(VoiceCallPushOutcome.Accepted, "after-retry");
            });
        var hub = Substitute.For<IVoiceCallV2RealtimeTransport>();
        hub.SendAsync(Arg.Any<Guid>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = Handler(db, push, hub);

        // First pass: both push targets fail — the handler must demand a retry
        // instead of acknowledging the event.
        await FluentActions.Awaiting(() => handler.Handle(evt, CancellationToken.None))
            .Should().ThrowAsync<AggregateException>();

        // Retry delivers both targets.
        await handler.Handle(evt, CancellationToken.None);
        attempts.Should().Be(4);

        // A further replay resends nothing that was already accepted.
        await handler.Handle(evt, CancellationToken.None);
        attempts.Should().Be(4);
        (await db.VoiceCallEventDeliveries.Where(d => d.Channel == VoiceCallEventDelivery.ChannelPush).ToListAsync())
            .Should().OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusAcceptedByProvider);
    }

    private static AppDbContext SharedDb(string store) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(store).Options, Substitute.For<IDomainEventDispatcher>());

    private static async Task BindReceiverSessionsToRouteAsync(AppDbContext db, CallSession call, string installation, string tokenText)
    {
        var user = call.ReceiverUserId;
        var route = new MealMate.Domain.IdentityAccess.CustomerDeviceToken
        {
            UserId = user, Token = tokenText, Platform = MealMate.Domain.Enums.MobilePlatform.Android,
            DeviceId = installation, IsActive = true
        };
        db.CustomerDeviceTokens.Add(route);
        var sessions = await db.VoiceCallDeviceSessions.Where(s => s.UserId == user).ToListAsync();
        foreach (var session in sessions)
        {
            session.InstallationId = installation;
            session.FcmDeviceTokenId = route.Id;
        }

        await db.SaveChangesAsync();
    }

    private static RegisterCustomerDeviceTokenCommandHandler RefreshHandler(AppDbContext db, Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        return new(db, currentUser, clock);
    }

    [Fact]
    public async Task Route_refresh_during_hub_dispatch_still_delivers_the_live_ring()
    {
        var store = Guid.NewGuid().ToString();
        await using var db = SharedDb(store);
        var (call, evt) = await Seed(db, "incoming");
        await BindReceiverSessionsToRouteAsync(db, call, "install-refresh", "initial-fcm");
        var initialRouteId = (await db.CustomerDeviceTokens.SingleAsync(t => t.Token == "initial-fcm")).Id;

        var hubStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHub = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var realtime = Substitute.For<IVoiceCallV2RealtimeTransport>();
        realtime.SendAsync(Arg.Any<Guid>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                hubStarted.TrySetResult();
                await releaseHub.Task;
                return false;
            });
        var pushedRoutes = new List<Guid?>();
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                pushedRoutes.Add(ci.Arg<VoiceCallDeviceSession>().FcmDeviceTokenId);
                return new VoiceCallPushResult(VoiceCallPushOutcome.Accepted, "fresh-delivery");
            });

        // The dispatcher hangs inside the hub send with the pre-refresh devices loaded.
        var dispatch = Handler(db, push, realtime).Handle(evt, CancellationToken.None);
        await hubStarted.Task;

        // The token refresh completes on a second context while the send is in flight.
        await using var refreshDb = SharedDb(store);
        (await RefreshHandler(refreshDb, call.ReceiverUserId).Handle(
            new RegisterCustomerDeviceTokenCommand("fresh-fcm", MealMate.Domain.Enums.MobilePlatform.Android, "install-refresh"),
            CancellationToken.None)).IsSuccess.Should().BeTrue();

        releaseHub.SetResult();
        await dispatch;

        // The still-live ring reached the refreshed route and never the stale one.
        pushedRoutes.Should().NotBeEmpty();
        pushedRoutes.Should().OnlyContain(r => r != initialRouteId);
        var freshId = await refreshDb.CustomerDeviceTokens.Where(t => t.Token == "fresh-fcm").Select(t => t.Id).SingleAsync();
        pushedRoutes.Should().Contain(freshId);
        (await db.VoiceCallEventDeliveries.Where(d => d.Channel == VoiceCallEventDelivery.ChannelPush).ToListAsync())
            .Should().OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusAcceptedByProvider);
    }

    [Fact]
    public async Task Suppressed_stale_route_is_recovered_on_replay_after_a_route_refresh()
    {
        var store = Guid.NewGuid().ToString();
        await using var db = SharedDb(store);
        var (call, evt) = await Seed(db, "incoming");
        await BindReceiverSessionsToRouteAsync(db, call, "install-recover", "initial-fcm");

        var routeRefreshed = false;
        var pushedRoutes = new List<Guid?>();
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                pushedRoutes.Add(ci.Arg<VoiceCallDeviceSession>().FcmDeviceTokenId);
                return routeRefreshed
                    ? new VoiceCallPushResult(VoiceCallPushOutcome.Accepted, "after-refresh")
                    : new VoiceCallPushResult(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.InactiveFcmRoute);
            });
        var realtime = Substitute.For<IVoiceCallV2RealtimeTransport>();
        realtime.SendAsync(Arg.Any<Guid>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = Handler(db, push, realtime);

        // A recoverable route failure must leave the durable outbox pending.
        await FluentActions.Awaiting(() => handler.Handle(evt, CancellationToken.None))
            .Should().ThrowAsync<AggregateException>();
        var pushedRows = await db.VoiceCallEventDeliveries.Where(d => d.Channel == VoiceCallEventDelivery.ChannelPush).ToListAsync();
        pushedRows.Should().OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusFailed
            && d.ReasonCode == VoiceCallPushReasons.InactiveFcmRoute);

        // The refresh lands, then the outbox replays the same committed event.
        await using var refreshDb = SharedDb(store);
        (await RefreshHandler(refreshDb, call.ReceiverUserId).Handle(
            new RegisterCustomerDeviceTokenCommand("fresh-fcm", MealMate.Domain.Enums.MobilePlatform.Android, "install-recover"),
            CancellationToken.None)).IsSuccess.Should().BeTrue();
        routeRefreshed = true;
        await handler.Handle(evt, CancellationToken.None);

        // The replay attempted exactly the recovered route; the delivery is accepted.
        var freshId = await refreshDb.CustomerDeviceTokens.Where(t => t.Token == "fresh-fcm").Select(t => t.Id).SingleAsync();
        pushedRoutes.Should().Contain(freshId);
        (await db.VoiceCallEventDeliveries.Where(d => d.Channel == VoiceCallEventDelivery.ChannelPush).ToListAsync())
            .Should().OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusAcceptedByProvider);
    }

    [Fact]
    public async Task Recoverable_reset_never_revives_an_incoming_after_terminal()
    {
        var store = Guid.NewGuid().ToString();
        await using var db = SharedDb(store);
        var (call, evt) = await Seed(db, "incoming");
        await BindReceiverSessionsToRouteAsync(db, call, "install-terminal", "initial-fcm");

        var routeRefreshed = false;
        var pushedRoutes = new List<Guid?>();
        var push = Substitute.For<IVoiceCallV2PushTransport>();
        push.SendAsync(Arg.Any<VoiceCallDeviceSession>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var session = ci.Arg<VoiceCallDeviceSession>();
                pushedRoutes.Add(session.FcmDeviceTokenId);
                if (session.FcmDeviceTokenId is null)
                {
                    return new VoiceCallPushResult(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.MissingFcmReference);
                }

                return routeRefreshed
                    ? new VoiceCallPushResult(VoiceCallPushOutcome.Accepted, "after-refresh")
                    : new VoiceCallPushResult(VoiceCallPushOutcome.NoRoute, ReasonCode: VoiceCallPushReasons.InactiveFcmRoute);
            });
        var realtime = Substitute.For<IVoiceCallV2RealtimeTransport>();
        realtime.SendAsync(Arg.Any<Guid>(), Arg.Any<CallEventEnvelope<CallSnapshotDto>>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = Handler(db, push, realtime);

        // Retry requested while stale; a later answer must still prevent revival.
        await FluentActions.Awaiting(() => handler.Handle(evt, CancellationToken.None))
            .Should().ThrowAsync<AggregateException>();
        var pushCalls = pushedRoutes.Count;
        pushCalls.Should().BeGreaterThan(0);

        // …then the call is answered (revision advances) and only afterwards does the
        // route refresh land. The replay must not deliver the now-obsolete incoming.
        var receiver = await db.VoiceCallDeviceSessions.FirstAsync(d => d.UserId == call.ReceiverUserId);
        call.ClaimReceiverDevice(receiver.Id, DateTime.UtcNow);
        await db.SaveChangesAsync();
        await using var refreshDb = SharedDb(store);
        (await RefreshHandler(refreshDb, call.ReceiverUserId).Handle(
            new RegisterCustomerDeviceTokenCommand("fresh-fcm", MealMate.Domain.Enums.MobilePlatform.Android, "install-terminal"),
            CancellationToken.None)).IsSuccess.Should().BeTrue();
        routeRefreshed = true;

        await handler.Handle(evt, CancellationToken.None);

        pushedRoutes.Count.Should().Be(pushCalls, "an obsolete incoming is never re-sent, even to a refreshed route");
        var receiverSessionIds = await db.VoiceCallDeviceSessions
            .Where(s => s.UserId == call.ReceiverUserId).Select(s => s.Id).ToListAsync();
        var receiverRows = await db.VoiceCallEventDeliveries
            .Where(d => d.Channel == VoiceCallEventDelivery.ChannelPush && d.RecipientDeviceSessionId != null
                        && receiverSessionIds.Contains(d.RecipientDeviceSessionId.Value))
            .ToListAsync();
        receiverRows.Should().NotBeEmpty().And.OnlyContain(d => d.Status == VoiceCallEventDelivery.StatusSuppressed
            && d.ReasonCode == VoiceCallPushReasons.ObsoleteLifecycle);
    }
}
