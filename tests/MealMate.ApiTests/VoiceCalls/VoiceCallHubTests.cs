using System.Security.Claims;
using FluentAssertions;
using MealMate.Api.Realtime;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Domain.VoiceCalls;
using MealMate.Persistence;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

public sealed class VoiceCallHubTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MealMate_VoiceHub_Tests_{Guid.NewGuid():N}")
            .Options;

        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        return new AppDbContext(options, dispatcher);
    }

    private static (VoiceCallHub Hub, IClientProxy TargetGroupProxy, ISingleClientProxy CallerProxy) SetupHub(
        AppDbContext db,
        Guid? currentUserId,
        ISender? sender = null)
    {
        var hubContext = Substitute.For<HubCallerContext>();
        var clients = Substitute.For<IHubCallerClients>();
        var targetGroupProxy = Substitute.For<IClientProxy>();
        var callerProxy = Substitute.For<ISingleClientProxy>();

        clients.Group(Arg.Any<string>()).Returns(targetGroupProxy);
        clients.Caller.Returns(callerProxy);

        var claims = new List<Claim>();
        if (currentUserId.HasValue)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, currentUserId.Value.ToString()));
        }

        var identity = currentUserId.HasValue
            ? new ClaimsIdentity(claims, "TestAuth")
            : new ClaimsIdentity();

        hubContext.User.Returns(new ClaimsPrincipal(identity));
        hubContext.ConnectionId.Returns($"conn-{Guid.NewGuid():N}");

        var logger = Substitute.For<ILogger<VoiceCallHub>>();
        var hub = new VoiceCallHub(db, sender ?? Substitute.For<ISender>(), logger)
        {
            Context = hubContext,
            Clients = clients
        };

        return (hub, targetGroupProxy, callerProxy);
    }

    [Fact]
    public async Task VoiceCallHub_SendOffer_CallerToReceiver_RelaysSuccessfully()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        session.Accept(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var (hub, targetGroupProxy, _) = SetupHub(db, callerId);
        var offerDto = new RtcSessionDescriptionDto("offer", "v=0\r\no=alice 2890844526 2890844526 IN IP4 host.example.com\r\ns=-\r\nt=0 0\r\n");

        // Act
        await hub.SendOffer(session.Id, offerDto);

        // Assert
        await targetGroupProxy.Received(1).SendCoreAsync(
            VoiceCallHub.EventRtcOffer,
            Arg.Is<object[]>(args => args.Length == 1 && args[0] is RtcSignalingEnvelopeDto<RtcSessionDescriptionDto>),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VoiceCallHub_SendAnswer_ReceiverToCaller_RelaysSuccessfully()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        session.Accept(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var (hub, targetGroupProxy, _) = SetupHub(db, receiverId);
        var answerDto = new RtcSessionDescriptionDto("answer", "v=0\r\no=bob 2890844527 2890844527 IN IP4 host.example.com\r\ns=-\r\nt=0 0\r\n");

        // Act
        await hub.SendAnswer(session.Id, answerDto);

        // Assert
        await targetGroupProxy.Received(1).SendCoreAsync(
            VoiceCallHub.EventRtcAnswer,
            Arg.Is<object[]>(args => args.Length == 1 && args[0] is RtcSignalingEnvelopeDto<RtcSessionDescriptionDto>),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VoiceCallHub_SendIceCandidate_AllowedInActiveCallForIceRestart()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        session.Accept(DateTime.UtcNow);
        session.MarkConnecting();
        session.MarkConnected(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var (hub, targetGroupProxy, _) = SetupHub(db, callerId);
        var candidateDto = new RtcIceCandidateDto("candidate:1 1 UDP 2130706431 192.168.1.1 50000 typ host", "audio", 0);

        // Act
        await hub.SendIceCandidate(session.Id, candidateDto);

        // Assert
        await targetGroupProxy.Received(1).SendCoreAsync(
            VoiceCallHub.EventRtcIceCandidate,
            Arg.Is<object[]>(args => args.Length == 1 && args[0] is RtcSignalingEnvelopeDto<RtcIceCandidateDto>),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VoiceCallHub_SendOffer_UnrelatedUser_ThrowsHubException()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();
        var unrelatedUserId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        session.Accept(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var (hub, _, _) = SetupHub(db, unrelatedUserId);
        var offerDto = new RtcSessionDescriptionDto("offer", "v=0\r\n");

        // Act
        var act = () => hub.SendOffer(session.Id, offerDto);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*not an authorized participant*");
    }

    [Fact]
    public async Task VoiceCallHub_SendOffer_TerminalCall_ThrowsHubException()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        session.Cancel(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var (hub, _, _) = SetupHub(db, callerId);
        var offerDto = new RtcSessionDescriptionDto("offer", "v=0\r\n");

        // Act
        var act = () => hub.SendOffer(session.Id, offerDto);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Cannot relay SDP offer in call status 'Cancelled'*");
    }

    [Fact]
    public async Task VoiceCallHub_SendOffer_AnonymousUser_ThrowsHubException()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var (hub, _, _) = SetupHub(db, null);
        var offerDto = new RtcSessionDescriptionDto("offer", "v=0\r\n");

        // Act
        var act = () => hub.SendOffer(Guid.NewGuid(), offerDto);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Unauthorized*");
    }

    [Fact]
    public async Task VoiceCallHub_SendOffer_OversizedSdp_ThrowsHubException()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var (hub, _, _) = SetupHub(db, callerId);
        var hugeSdp = new string('x', 70000);
        var offerDto = new RtcSessionDescriptionDto("offer", hugeSdp);

        // Act
        var act = () => hub.SendOffer(Guid.NewGuid(), offerDto);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*exceeds maximum permitted size*");
    }

    [Fact]
    public async Task VoiceCallHub_SendIceCandidate_OversizedCandidate_ThrowsHubException()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var (hub, _, _) = SetupHub(db, callerId);
        var hugeCandidate = new string('c', 5000);
        var candidateDto = new RtcIceCandidateDto(hugeCandidate);

        // Act
        var act = () => hub.SendIceCandidate(Guid.NewGuid(), candidateDto);

        // Assert
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*exceeds maximum permitted size*");
    }

    [Fact]
    public async Task VoiceCallHub_OnDisconnectedAsync_DoesNotEndCallSession()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        session.Accept(DateTime.UtcNow);
        session.MarkConnecting();
        session.MarkConnected(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var (hub, _, _) = SetupHub(db, callerId);

        // Act
        await hub.OnDisconnectedAsync(new Exception("Network dropped"));

        // Assert
        var reloaded = await db.CallSessions.FindAsync(session.Id);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be(CallStatus.Active);
    }

    [Fact]
    public async Task VoiceCallHub_SendOfferAndAnswer_DuringActiveCall_PermitsIceRestartRenegotiation()
    {
        // Arrange: Call is already Active
        await using var db = CreateInMemoryDb();
        var callerId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();

        var session = new CallSession(
            Guid.NewGuid(), callerId, CallParticipantType.Customer,
            receiverId, CallParticipantType.Driver,
            Guid.NewGuid(), DateTime.UtcNow);
        session.MarkRinging(DateTime.UtcNow);
        session.Accept(DateTime.UtcNow);
        session.MarkConnecting();
        session.MarkConnected(DateTime.UtcNow);
        db.CallSessions.Add(session);
        await db.SaveChangesAsync();

        var (callerHub, receiverGroupProxy, _) = SetupHub(db, callerId);
        var (receiverHub, callerGroupProxy, _) = SetupHub(db, receiverId);

        var restartOffer = new RtcSessionDescriptionDto("offer", "v=0\r\no=- 12345 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=ice-options:trickle restart\r\n");
        var restartAnswer = new RtcSessionDescriptionDto("answer", "v=0\r\no=- 54321 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=ice-options:trickle\r\n");

        // Act 1: Caller sends new SDP Offer for ICE restart during Active session
        await callerHub.SendOffer(session.Id, restartOffer);

        // Assert 1: Relayed to receiver
        await receiverGroupProxy.Received(1).SendCoreAsync(
            VoiceCallHub.EventRtcOffer,
            Arg.Is<object[]>(args => args.Length == 1 && args[0] is RtcSignalingEnvelopeDto<RtcSessionDescriptionDto>),
            Arg.Any<CancellationToken>());

        // Act 2: Receiver replies with SDP Answer during Active session
        await receiverHub.SendAnswer(session.Id, restartAnswer);

        // Assert 2: Relayed to caller
        await callerGroupProxy.Received(1).SendCoreAsync(
            VoiceCallHub.EventRtcAnswer,
            Arg.Is<object[]>(args => args.Length == 1 && args[0] is RtcSignalingEnvelopeDto<RtcSessionDescriptionDto>),
            Arg.Any<CancellationToken>());

        // Call state must remain Active
        var reloaded = await db.CallSessions.FindAsync(session.Id);
        reloaded!.Status.Should().Be(CallStatus.Active);
    }
}
