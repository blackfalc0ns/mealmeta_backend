using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.V2.Commands;
using MealMate.Application.Features.VoiceCalls.Services;
using MealMate.Application.Features.VoiceCalls.Queries;
using MealMate.Application.Features.VoiceCalls.Dtos;
using MealMate.Domain.VoiceCalls;
using MealMate.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MealMate.UnitTests.VoiceCalls;

public sealed class VoiceCallV2ControlRaceTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    private static IDateTimeProvider Clock()
    {
        var clock = Substitute.For<IDateTimeProvider>(); clock.UtcNow.Returns(Now); return clock;
    }
    private static AppDbContext Db(DbContextOptions<AppDbContext> options) => new(options, Substitute.For<IDomainEventDispatcher>());
    private static VoiceCallDeviceSession Device(Guid user) => new()
    {
        UserId = user, InstallationId = Guid.NewGuid().ToString(), Platform = "android",
        ControlProofHash = "persisted-test-proof", CapabilityVersion = 2, ExpiresAtUtc = Now.AddHours(1)
    };
    private static async Task<(CallSession Call, VoiceCallDeviceSession Caller, VoiceCallDeviceSession Receiver, VoiceCallDeviceSession Other)> Seed(AppDbContext db)
    {
        var caller = Device(Guid.NewGuid()); var receiver = Device(Guid.NewGuid()); var other = Device(receiver.UserId);
        var call = new CallSession(Guid.NewGuid(), caller.UserId, CallParticipantType.Driver, receiver.UserId,
            CallParticipantType.Customer, Guid.NewGuid(), Now);
        call.MarkRingingV2(Now, Now.AddSeconds(30), caller.Id);
        db.CallSessions.Add(call); db.VoiceCallDeviceSessions.AddRange(caller, receiver, other);
        await db.SaveChangesAsync(); return (call, caller, receiver, other);
    }
    private static async Task<Result> Execute(string action, AppDbContext db, Guid call, VoiceCallDeviceContext actor, IVoiceCallParticipantLockService locks)
    {
        return action switch
        {
            "answer" => await new AnswerVoiceCallV2CommandHandler(db, Clock(), locks).Handle(new(call, actor), default),
            "reject" => await new RejectVoiceCallV2CommandHandler(db, Clock(), locks).Handle(new(call, actor), default),
            "cancel" => await new CancelVoiceCallV2CommandHandler(db, Clock(), locks).Handle(new(call, actor), default),
            "end" => await new EndVoiceCallV2CommandHandler(db, Clock(), locks).Handle(new(call, actor), default),
            "connecting" => await new ReportMediaStateV2CommandHandler(db, Clock(), locks).Handle(new(call, actor, false), default),
            "connected" => await new ReportMediaStateV2CommandHandler(db, Clock(), locks).Handle(new(call, actor, true), default),
            "heartbeat" => await new HeartbeatVoiceCallV2CommandHandler(db, Clock(), lockService: locks).Handle(new(call, actor), default),
            "ringAck" => await new AcknowledgeRingingV2CommandHandler(db, Clock(), locks).Handle(new(call, actor), default),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    [Theory]
    [InlineData("answer")]
    [InlineData("reject")]
    [InlineData("end")]
    [InlineData("connecting")]
    [InlineData("connected")]
    public async Task Device_losing_answer_while_waiting_for_lock_cannot_mutate_winners_call(string action)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = Db(options); var seed = await Seed(db);
        db.ChangeTracker.Clear();
        var locks = new BeforeLock(async () =>
        {
            await using var otherContext = Db(options);
            var current = await otherContext.CallSessions.SingleAsync();
            current.ClaimReceiverDevice(seed.Receiver.Id, Now);
            if (action == "connected") current.MarkConnecting(Now);
            await otherContext.SaveChangesAsync();
        });
        var result = await Execute(action, db, seed.Call.Id, new(seed.Other.UserId, seed.Other.Id), locks);
        Assert.True(result.IsFailure);
        Assert.Equal(action == "answer" ? "calls.answered_elsewhere" : "voice.device_control_denied", result.Error.Code);
        await using var verify = Db(options);
        var stored = await verify.CallSessions.SingleAsync();
        Assert.Equal(action == "connected" ? CallStatus.Connecting : CallStatus.Accepted, stored.Status);
        Assert.Equal(seed.Receiver.Id, stored.ReceiverDeviceSessionId);
        Assert.Equal(action == "connected" ? 3 : 2, stored.Sequence);
        Assert.Empty(await verify.OutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData("answer")]
    [InlineData("reject")]
    [InlineData("cancel")]
    [InlineData("end")]
    [InlineData("connecting")]
    [InlineData("connected")]
    public async Task Session_revoked_after_precheck_but_before_lock_cannot_mutate(string action)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = Db(options); var seed = await Seed(db);
        if (action is "end" or "connecting" or "connected") seed.Call.ClaimReceiverDevice(seed.Receiver.Id, Now);
        if (action == "connected") seed.Call.MarkConnecting(Now);
        await db.SaveChangesAsync();
        var expectedStatus = seed.Call.Status;
        db.ChangeTracker.Clear();
        var actor = action == "cancel" ? seed.Caller : seed.Receiver;
        var locks = new BeforeLock(async () =>
        {
            await using var otherContext = Db(options);
            var device = await otherContext.VoiceCallDeviceSessions.SingleAsync(d => d.Id == actor.Id);
            device.RevokedAtUtc = Now;
            await otherContext.SaveChangesAsync();
        });
        var result = await Execute(action, db, seed.Call.Id, new(actor.UserId, actor.Id), locks);
        Assert.True(result.IsFailure);
        Assert.Equal("voice.device_control_denied", result.Error.Code);
        await using var verify = Db(options);
        Assert.Equal(expectedStatus, (await verify.CallSessions.SingleAsync()).Status);
        Assert.Empty(await verify.OutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData("heartbeat")]
    [InlineData("ringAck")]
    public async Task Expired_control_session_cannot_write_liveness_or_receipts(string action)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = Db(options); var seed = await Seed(db);
        if (action == "heartbeat")
        {
            seed.Call.ClaimReceiverDevice(seed.Receiver.Id, Now);
            seed.Call.MarkConnecting(Now);
            seed.Call.ReportConnectedSide(true, Now); seed.Call.ReportConnectedSide(false, Now);
        }
        seed.Receiver.ExpiresAtUtc = Now;
        await db.SaveChangesAsync();
        var result = await Execute(action, db, seed.Call.Id, new(seed.Receiver.UserId, seed.Receiver.Id), new BeforeLock(() => Task.CompletedTask));
        Assert.True(result.IsFailure);
        Assert.Equal("voice.device_control_denied", result.Error.Code);
        Assert.Null(seed.Call.ReceiverLastHeartbeatAtUtc);
        Assert.Empty(await db.VoiceCallDeviceReceipts.ToListAsync());
    }

    private sealed class BeforeLock(Func<Task> before) : IVoiceCallParticipantLockService
    {
        public int Invocations;
        public (Guid First, Guid Second) LastParticipants;
        public async Task<T> ExecuteInParticipantLockAsync<T>(Guid a, Guid b, Func<CancellationToken,Task<T>> action,
            Func<CancellationToken,Task<T?>>? reconcileOnUniqueViolationAsync = null, CancellationToken cancellationToken = default)
        { Invocations++; LastParticipants = (a, b); await before(); return await action(cancellationToken); }
    }

    [Fact]
    public async Task Registration_rotation_and_revocation_take_owner_lock_and_old_proofs_fail()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = Db(options);
        var user = Guid.NewGuid();
        var locks = new BeforeLock(() => Task.CompletedTask);
        var service = new VoiceCallDeviceSessionService(db, Clock(), locks);
        var first = (await service.RegisterAsync(user, new("same-installation", "android", 2), default)).Value;
        var rotated = (await service.RegisterAsync(user, new("same-installation", "android", 2), default)).Value;
        Assert.True((await service.ResolveAsync(user, first.DeviceSessionId, first.ControlProof, default)).IsFailure);
        Assert.True((await service.ResolveAsync(user, rotated.DeviceSessionId, rotated.ControlProof, default)).IsSuccess);
        await service.RevokeAsync(user, rotated.DeviceSessionId, default);
        Assert.True((await service.ResolveAsync(user, rotated.DeviceSessionId, rotated.ControlProof, default)).IsFailure);
        Assert.Equal(3, locks.Invocations);
        Assert.Equal((user, user), locks.LastParticipants);
        Assert.Empty(await db.VoiceCallDeviceSessions.Where(d => d.RevokedAtUtc == null).ToListAsync());
    }

    [Theory]
    [InlineData("loser")]
    [InlineData("ringing")]
    [InlineData("revoked")]
    public async Task Ice_credentials_are_not_available_to_nonwinning_or_revoked_devices_or_before_answer(string state)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = Db(options); var seed = await Seed(db);
        var actor = state == "loser" ? seed.Other : state == "ringing" ? seed.Caller : seed.Receiver;
        if (state != "ringing") seed.Call.ClaimReceiverDevice(seed.Receiver.Id, Now);
        if (state == "revoked") actor.RevokedAtUtc = Now;
        await db.SaveChangesAsync();
        var turn = Substitute.For<ITurnCredentialService>();
        turn.GenerateCredentials(Arg.Any<string>()).Returns(new TurnCredentialsResponseDto("test-user", "test-secret", 60, Now.AddMinutes(1), Array.Empty<RtcIceServerDto>()));
        var result = await new GetTurnCredentialsForCallV2QueryHandler(db, turn, Clock()).Handle(new(seed.Call.Id, actor.UserId, actor.Id), default);
        Assert.True(result.IsFailure);
        Assert.Empty(turn.ReceivedCalls());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(50)]
    public async Task Ring_ack_after_deadline_cannot_create_receipt_or_heard_timestamp(int seconds)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = Db(options); var seed = await Seed(db);
        var clock = Clock(); clock.UtcNow.Returns(Now.AddSeconds(seconds));
        var result = await new AcknowledgeRingingV2CommandHandler(db, clock).Handle(new(seed.Call.Id, new(seed.Receiver.UserId, seed.Receiver.Id)), default);
        Assert.True(result.IsFailure);
        Assert.Equal("calls.invalid_state", result.Error.Code);
        Assert.Empty(await db.VoiceCallDeviceReceipts.ToListAsync());
        Assert.Null((await db.CallSessions.AsNoTracking().SingleAsync()).ReceiverRingingAckAtUtc);
    }

    [Fact]
    public async Task Ring_ack_existing_receipt_replay_after_deadline_preserves_original_time()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = Db(options); var seed = await Seed(db);
        var clock = Clock();
        var handler = new AcknowledgeRingingV2CommandHandler(db, clock);
        var command = new AcknowledgeRingingV2Command(seed.Call.Id, new(seed.Receiver.UserId, seed.Receiver.Id));
        Assert.True((await handler.Handle(command, default)).IsSuccess);
        clock.UtcNow.Returns(Now.AddSeconds(50));
        Assert.True((await handler.Handle(command, default)).IsSuccess);
        Assert.Single(await db.VoiceCallDeviceReceipts.ToListAsync());
        Assert.Equal(Now, (await db.CallSessions.AsNoTracking().SingleAsync()).ReceiverRingingAckAtUtc);
    }

    [Fact]
    public async Task Initiation_rechecks_device_revoked_while_waiting_for_participant_lock()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = Db(options);
        var caller = Device(Guid.NewGuid()); var receiver = Guid.NewGuid(); var stop = Guid.NewGuid();
        db.VoiceCallDeviceSessions.Add(caller); await db.SaveChangesAsync();
        var eligibility = Substitute.For<IDeliveryContactEligibilityService>();
        eligibility.ResolveParticipantsAsync(stop, Arg.Any<CancellationToken>()).Returns(Result.Success(new VoiceCallContactParticipants(stop, Guid.NewGuid(), Guid.NewGuid(), caller.UserId, receiver)));
        eligibility.EvaluateAsync(stop, caller.UserId, Arg.Any<CancellationToken>()).Returns(Result.Success(new DeliveryContactEligibilityDto(true, "contact.eligible", 1, 1, false, false, false, null)));
        var locks = new BeforeLock(async () =>
        {
            await using var other = Db(options);
            var persisted = await other.VoiceCallDeviceSessions.SingleAsync(); persisted.RevokedAtUtc = Now;
            await other.SaveChangesAsync();
        });
        var result = await new InitiateVoiceCallV2CommandHandler(db, Clock(), eligibility, locks).Handle(new(stop, Guid.NewGuid(), new(caller.UserId, caller.Id)), default);
        Assert.True(result.IsFailure);
        Assert.Equal("voice.device_control_denied", result.Error.Code);
        Assert.Empty(await db.CallSessions.AsNoTracking().ToListAsync());
        Assert.Empty(await db.OutboxMessages.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_heartbeat_during_setup_preserves_setup_deadline_and_lifecycle_sequence(bool connecting)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = Db(options); var seed = await Seed(db);
        seed.Call.ClaimReceiverDevice(seed.Receiver.Id, Now);
        if (connecting) seed.Call.MarkConnecting(Now);
        seed.Call.SetSetupDeadline(Now.AddSeconds(20));
        await db.SaveChangesAsync();
        var sequence = seed.Call.Sequence;
        var result = await new HeartbeatVoiceCallV2CommandHandler(db, Clock()).Handle(new(seed.Call.Id, new(seed.Receiver.UserId, seed.Receiver.Id)), default);
        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddSeconds(20), seed.Call.SetupExpiresAtUtc);
        Assert.Equal(sequence, seed.Call.Sequence);
        Assert.Equal(Now, seed.Call.ReceiverLastHeartbeatAtUtc);
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }
}
