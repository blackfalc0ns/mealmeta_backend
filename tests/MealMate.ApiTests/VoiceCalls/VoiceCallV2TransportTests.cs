using System.Security.Claims;
using FluentAssertions;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Application.Features.VoiceCalls.Services;
using MealMate.Domain.VoiceCalls;
using MealMate.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Options;
using Microsoft.Extensions.Options;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;

namespace MealMate.ApiTests.VoiceCalls;

public sealed class VoiceCallV2TransportTests
{
    private static AppDbContext Database() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());

    private static (VoiceCallV2Hub Hub, IGroupManager Groups) Hub(AppDbContext db, Guid userId)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var context = Substitute.For<HubCallerContext>();
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "tests")));
        context.Items.Returns(new Dictionary<object, object?>());
        context.ConnectionId.Returns(Guid.NewGuid().ToString());
        var clients = Substitute.For<IHubCallerClients>();
        clients.Caller.Returns(Substitute.For<ISingleClientProxy>());
        var groups = Substitute.For<IGroupManager>();
        return (new VoiceCallV2Hub(db, new VoiceCallDeviceSessionService(db, clock), NullLogger<VoiceCallV2Hub>.Instance,
            new VoiceCallV2ConnectionRegistry(), clock, Options.Create(new VoiceCallOptions { V2Enabled = true }))
        { Context = context, Clients = clients, Groups = groups }, groups);
    }

    private static async Task<(Guid Id, string Proof)> Register(AppDbContext db, Guid userId)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var result = await new VoiceCallDeviceSessionService(db, clock).RegisterAsync(userId,
            new(Guid.NewGuid().ToString(), "android", 2), default);
        return (result.Value.DeviceSessionId, result.Value.ControlProof);
    }

    [Fact]
    public async Task RebindingRemovesPreviousDeviceGroup()
    {
        await using var db = Database();
        var user = Guid.NewGuid();
        var first = await Register(db, user);
        var second = await Register(db, user);
        var (hub, groups) = Hub(db, user);
        await hub.BindDeviceSession(first.Id, first.Proof);
        await hub.BindDeviceSession(second.Id, second.Proof);
        await groups.Received(1).RemoveFromGroupAsync(hub.Context.ConnectionId, VoiceCallV2Hub.GroupForDevice(first.Id), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoundSessionIsRevalidatedBeforeMedia(bool revoked)
    {
        await using var db = Database();
        var user = Guid.NewGuid();
        var device = await Register(db, user);
        var (hub, _) = Hub(db, user);
        await hub.BindDeviceSession(device.Id, device.Proof);
        var row = await db.Set<VoiceCallDeviceSession>().SingleAsync();
        if (revoked) row.RevokedAtUtc = DateTime.UtcNow;
        else row.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        var act = () => hub.SendOffer(Guid.NewGuid(), new("offer", "v=0"), Guid.NewGuid(), 0);
        await act.Should().ThrowAsync<HubException>().WithMessage("*device session*valid*");
    }

    [Fact]
    public async Task IceLimitCountsUtf8Bytes()
    {
        await using var db = Database();
        var user = Guid.NewGuid();
        var device = await Register(db, user);
        var (hub, _) = Hub(db, user);
        await hub.BindDeviceSession(device.Id, device.Proof);
        var act = () => hub.SendIceCandidate(Guid.NewGuid(), new(new string('ع', 3000)), Guid.NewGuid(), 0);
        await act.Should().ThrowAsync<HubException>().WithMessage("*exceeds maximum permitted size*");
    }

    [Fact]
    public async Task OfferRejectsAnswerSdpType()
    {
        await using var db = Database();
        var user = Guid.NewGuid();
        var device = await Register(db, user);
        var (hub, _) = Hub(db, user);
        await hub.BindDeviceSession(device.Id, device.Proof);
        var act = () => hub.SendOffer(Guid.NewGuid(), new("answer", "v=0"), Guid.NewGuid(), 0);
        await act.Should().ThrowAsync<HubException>().WithMessage("*SDP type*");
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, -1)]
    public async Task RtcRejectsMissingIdentityOrNegativeGeneration(bool emptyId, int generation)
    {
        await using var db = Database();
        var user = Guid.NewGuid();
        var device = await Register(db, user);
        var (hub, _) = Hub(db, user);
        await hub.BindDeviceSession(device.Id, device.Proof);
        var act = () => hub.SendOffer(Guid.NewGuid(), new("offer", "v=0"),
            emptyId ? Guid.Empty : Guid.NewGuid(), generation);
        await act.Should().ThrowAsync<HubException>().WithMessage("*identity*generation*");
    }

    [Fact]
    public async Task ActualSignalRRelayPreservesRtcIdentityAndAcceptsBoundarySdp()
    {
        await using var db = Database();
        var caller = Guid.NewGuid();
        var receiver = Guid.NewGuid();
        var callerDevice = await Register(db, caller);
        var receiverDevice = await Register(db, receiver);
        var call = new CallSession(Guid.NewGuid(), caller, CallParticipantType.Customer, receiver,
            CallParticipantType.Driver, Guid.NewGuid(), DateTime.UtcNow);
        call.MarkRingingV2(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1), callerDevice.Id);
        call.ClaimReceiverDevice(receiverDevice.Id, DateTime.UtcNow);
        db.CallSessions.Add(call);
        await db.SaveChangesAsync();
        using var server = Server(db);
        using var sender = await Connect(server, caller);
        using var peer = await Connect(server, receiver);
        await Invoke(sender, "BindDeviceSession", callerDevice.Id, callerDevice.Proof);
        await Invoke(peer, "BindDeviceSession", receiverDevice.Id, receiverDevice.Proof);
        var messageId = Guid.NewGuid();
        var sdp = new string('\u0001', VoiceCallV2Hub.MaxSdpPayloadBytes);
        var peerMessage = Read(peer);
        await Invoke(sender, "SendOffer", call.Id, new RtcSessionDescriptionDto("offer", sdp), messageId, 3);
        var message = await peerMessage;
        message.GetProperty("target").GetString().Should().Be("rtc:offer");
        var envelope = message.GetProperty("arguments")[0];
        envelope.GetProperty("messageId").GetGuid().Should().Be(messageId);
        envelope.GetProperty("negotiationGeneration").GetInt32().Should().Be(3);
        envelope.GetProperty("senderDeviceSessionId").GetGuid().Should().Be(callerDevice.Id);
        envelope.GetProperty("payload").GetProperty("sdp").GetString().Should().Be(sdp);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualLifecycleWireTargetsOnlyCurrentValidBinding(bool revoked)
    {
        await using var db = Database();
        var user = Guid.NewGuid();
        var first = await Register(db, user);
        var second = await Register(db, user);
        using var server = Server(db);
        using var socket = await Connect(server, user);
        await Invoke(socket, "BindDeviceSession", first.Id, first.Proof);
        await Invoke(socket, "BindDeviceSession", second.Id, second.Proof);
        using var scope = server.Services.CreateScope();
        var transport = scope.ServiceProvider.GetRequiredService<IVoiceCallV2RealtimeTransport>();
        var callId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var payload = new CallSnapshotDto(callId, Guid.NewGuid(), 2, 4, "Ended", "Hangup", now,
            now, now, now, 1, null, second.Id, "Receiver");
        var stale = new CallEventEnvelope<CallSnapshotDto>(Guid.NewGuid(), 2, callId, 4, "ended", now, now, null, payload);
        // An active device group can be queued even if no local connection remains.
        (await transport.SendAsync(first.Id, stale, default)).Should().BeTrue();
        var expected = stale with { EventId = Guid.NewGuid() };
        (await transport.SendAsync(second.Id, expected, default)).Should().BeTrue();
        var message = await Read(socket);
        message.GetProperty("target").GetString().Should().Be("call:event");
        var envelope = message.GetProperty("arguments")[0];
        envelope.GetProperty("eventId").GetGuid().Should().Be(expected.EventId);
        envelope.GetProperty("sequence").GetInt64().Should().Be(4);
        envelope.GetProperty("protocolVersion").GetInt32().Should().Be(2);
        envelope.GetProperty("payload").GetProperty("localParticipantRole").GetString().Should().Be("Receiver");
        // A transport originating on another node has no local binding entry.
        // Routing must still reach the lifetime-manager's device group. This
        // tests independence from the registry; it does not exercise Redis.
        var otherNodeTransport = new VoiceCallV2RealtimeTransport(db,
            server.Services.GetRequiredService<IHubContext<VoiceCallV2Hub>>(),
            new VoiceCallV2ConnectionRegistry(), server.Services.GetRequiredService<IDateTimeProvider>(),
            server.Services.GetRequiredService<IOptions<VoiceCallOptions>>());
        var otherEvent = expected with { EventId = Guid.NewGuid() };
        (await otherNodeTransport.SendAsync(second.Id, otherEvent, default)).Should().BeTrue();
        (await Read(socket)).GetProperty("arguments")[0].GetProperty("eventId").GetGuid().Should().Be(otherEvent.EventId);
        var row = await db.Set<VoiceCallDeviceSession>().SingleAsync(s => s.Id == second.Id);
        if (revoked) row.RevokedAtUtc = DateTime.UtcNow;
        else row.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        (await transport.SendAsync(second.Id, expected, default)).Should().BeFalse();
        (await transport.SendAsync(second.Id, expected, default)).Should().BeFalse();
        (await transport.SendAsync(Guid.NewGuid(), expected, default)).Should().BeFalse();
        server.Services.GetRequiredService<IOptions<VoiceCallOptions>>().Value.V2Enabled = false;
        (await transport.SendAsync(first.Id, expected, default)).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualRtcWireSuppressesRevokedOrExpiredPeer(bool revoked)
    {
        await using var db = Database();
        var caller = Guid.NewGuid();
        var receiver = Guid.NewGuid();
        var callerDevice = await Register(db, caller);
        var receiverDevice = await Register(db, receiver);
        var call = new CallSession(Guid.NewGuid(), caller, CallParticipantType.Customer, receiver,
            CallParticipantType.Driver, Guid.NewGuid(), DateTime.UtcNow);
        call.MarkRingingV2(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1), callerDevice.Id);
        call.ClaimReceiverDevice(receiverDevice.Id, DateTime.UtcNow);
        db.CallSessions.Add(call);
        await db.SaveChangesAsync();
        using var server = Server(db);
        using var sender = await Connect(server, caller);
        using var peer = await Connect(server, receiver);
        await Invoke(sender, "BindDeviceSession", callerDevice.Id, callerDevice.Proof);
        await Invoke(peer, "BindDeviceSession", receiverDevice.Id, receiverDevice.Proof);
        var row = await db.Set<VoiceCallDeviceSession>().SingleAsync(s => s.Id == receiverDevice.Id);
        if (revoked) row.RevokedAtUtc = DateTime.UtcNow;
        else row.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        await Invoke(sender, "SendOffer", call.Id, new RtcSessionDescriptionDto("offer", "v=0"), Guid.NewGuid(), 0);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var receive = () => peer.ReceiveAsync(new ArraySegment<byte>(new byte[8192]), timeout.Token);
        await receive.Should().ThrowAsync<OperationCanceledException>();
    }

    private static TestServer Server(AppDbContext db) => new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddAuthorization();
            services.AddSingleton<IAppDbContext>(db);
            var clock = Substitute.For<IDateTimeProvider>();
            clock.UtcNow.Returns(_ => DateTime.UtcNow);
            services.AddSingleton(clock);
            services.AddScoped<IVoiceCallDeviceSessionService, VoiceCallDeviceSessionService>();
            services.AddSingleton<VoiceCallV2ConnectionRegistry>();
            services.AddScoped<IVoiceCallV2RealtimeTransport, VoiceCallV2RealtimeTransport>();
            services.Configure<VoiceCallOptions>(options => options.V2Enabled = true);
            services.AddSignalR().AddHubOptions<VoiceCallV2Hub>(options =>
                options.MaximumReceiveMessageSize = VoiceCallV2Hub.MaximumReceiveMessageSize);
        })
        .Configure(app =>
        {
            app.Use((context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                { new Claim(ClaimTypes.NameIdentifier, context.Request.Query["user"].ToString()) }, "tests"));
                return next();
            });
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints => endpoints.MapHub<VoiceCallV2Hub>("/calls"));
        }));

    private static async Task<WebSocket> Connect(TestServer server, Guid user)
    {
        var socket = await server.CreateWebSocketClient().ConnectAsync(new Uri($"ws://localhost/calls?user={user}"), default);
        await Write(socket, new { protocol = "json", version = 1 });
        await Read(socket);
        return socket;
    }

    private static async Task Invoke(WebSocket socket, string target, params object[] args)
    {
        var id = Guid.NewGuid().ToString();
        await Write(socket, new { type = 1, invocationId = id, target, arguments = args });
        while (true)
        {
            var message = await Read(socket);
            if (!message.TryGetProperty("type", out var type) || type.GetInt32() != 3) continue;
            message.GetProperty("invocationId").GetString().Should().Be(id);
            message.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
            return;
        }
    }

    private static Task Write(WebSocket socket, object value) => socket.SendAsync(new ArraySegment<byte>(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + "\u001e")), WebSocketMessageType.Text, true, default);

    private static async Task<JsonElement> Read(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var bytes = WireBuffers.GetOrCreateValue(socket);
        var buffer = new byte[8192];
        while (true)
        {
            var separator = bytes.IndexOf(0x1e);
            if (separator >= 0)
            {
                var message = JsonDocument.Parse(bytes.Take(separator).ToArray()).RootElement.Clone();
                bytes.RemoveRange(0, separator + 1);
                return message;
            }
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("SignalR connection closed before expected message.");
            bytes.AddRange(buffer.Take(result.Count));
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WebSocket, List<byte>> WireBuffers = new();
}
