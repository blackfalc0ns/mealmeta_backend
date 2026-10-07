using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using MealMate.Application.Features.VoiceCalls.BackgroundJobs;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Application.Features.VoiceCalls.Options;
using MealMate.Domain.VoiceCalls;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

public sealed class VoiceCallLeaseRecoveryApiTests
{
    private static Task<VoiceCallTestHost> CreateHostAsync() => VoiceCallTestHost.CreateAsync(
        extraSettings: new Dictionary<string, string?> { ["VoiceCalls:V2Enabled"] = "true" });

    private static async Task<CallSession> SeedActiveAsync(VoiceCallTestHost host)
    {
        await host.RegisterDeviceAsync(host.DriverDeviceA, "lease-driver");
        await host.RegisterDeviceAsync(host.CustomerDeviceA, "lease-customer");
        var now = host.Clock.UtcNow;
        var call = new CallSession(host.TripStopId, host.DriverUserId, CallParticipantType.Driver,
            host.CustomerUserId, CallParticipantType.Customer, Guid.NewGuid(), now);
        call.MarkRingingV2(now, now.AddSeconds(30), host.RegisteredDeviceSessions["lease-driver"]);
        call.ClaimReceiverDevice(host.RegisteredDeviceSessions["lease-customer"], now);
        call.MarkConnecting(now);
        call.ReportConnectedSide(true, now);
        call.ReportConnectedSide(false, now);
        await using var db = await host.OpenDbAsync();
        db.CallSessions.Add(call);
        db.VoiceCallBusyParticipants.Add(new VoiceCallBusyParticipant(call.CallerUserId, call.Id, now));
        db.VoiceCallBusyParticipants.Add(new VoiceCallBusyParticipant(call.ReceiverUserId, call.Id, now));
        await db.SaveChangesAsync();
        return call;
    }

    [Fact]
    public async Task HeartbeatWinsXminRace_ExpiryRollsBackTerminalEventAndBusyRelease()
    {
        await using var host = await CreateHostAsync();
        var seeded = await SeedActiveAsync(host);
        var originalDeadline = seeded.MediaLeaseExpiresAtUtc;
        await using var staleWorker = await host.OpenDbAsync();
        await staleWorker.CallSessions.FirstAsync(c => c.Id == seeded.Id);
        host.Clock.Advance(TimeSpan.FromSeconds(40));
        await using (var heartbeatDb = await host.OpenDbAsync())
        {
            var winner = await heartbeatDb.CallSessions.FirstAsync(c => c.Id == seeded.Id);
            winner.ApplyHeartbeat(true, host.Clock.UtcNow, host.Clock.UtcNow.AddSeconds(45));
            winner.ApplyHeartbeat(false, host.Clock.UtcNow, host.Clock.UtcNow.AddSeconds(45));
            await heartbeatDb.SaveChangesAsync();
        }
        host.Clock.Advance(TimeSpan.FromSeconds(6));
        var result = await new ExpireVoiceCallCommandHandler(staleWorker, host.Clock).Handle(
            new ExpireVoiceCallCommand(seeded.Id, CallStatus.Active, seeded.Sequence, originalDeadline, CallEndReason.NetworkLost), default);
        result.IsSuccess.Should().BeFalse();
        staleWorker.ChangeTracker.Entries().Should().BeEmpty("a losing recovery cannot poison later work in the same scope");
        await staleWorker.SaveChangesAsync();
        await using var verify = await host.OpenDbAsync();
        var call = await verify.CallSessions.SingleAsync(c => c.Id == seeded.Id);
        call.Status.Should().Be(CallStatus.Active);
        call.TerminalEventId.Should().BeNull();
        (await verify.VoiceCallBusyParticipants.CountAsync(b => b.CallId == seeded.Id)).Should().Be(2);
        (await verify.OutboxMessages.AnyAsync(m => m.AggregateId == seeded.Id.ToString() && m.EventType == "voice-call.lifecycle.ended")).Should().BeFalse();
    }

    [Fact]
    public async Task StaleAcceptedTimeout_AfterActivation_DoesNotReleaseBusy()
    {
        await using var host = await CreateHostAsync();
        var call = await SeedActiveAsync(host);
        host.Clock.Advance(TimeSpan.FromSeconds(25));
        await using var worker = await host.OpenDbAsync();
        var result = await new ExpireVoiceCallCommandHandler(worker, host.Clock).Handle(
            new ExpireVoiceCallCommand(call.Id, CallStatus.Accepted, 2, call.AcceptedAtUtc!.Value.AddSeconds(20), CallEndReason.ConnectionFailed), default);
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(CallStatus.Active.ToString());
        (await worker.VoiceCallBusyParticipants.CountAsync(b => b.CallId == call.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Worker_OneSilentPeer_EndsActiveCallWithDurableEvent()
    {
        await using var host = await CreateHostAsync();
        var seeded = await SeedActiveAsync(host);
        host.Clock.Advance(TimeSpan.FromSeconds(40));
        await using (var heartbeatDb = await host.OpenDbAsync())
        {
            var call = await heartbeatDb.CallSessions.SingleAsync(c => c.Id == seeded.Id);
            call.ApplyHeartbeat(true, host.Clock.UtcNow, host.Clock.UtcNow.AddSeconds(45));
            await heartbeatDb.SaveChangesAsync();
        }
        host.Clock.Advance(TimeSpan.FromSeconds(6));
        // The worker and handler receive the same deterministic clock as production DI.
        using var services = new ServiceCollection().AddSingleton<MealMate.Application.Common.Abstractions.IDateTimeProvider>(host.Clock).BuildServiceProvider();
        var options = Options.Create(new VoiceCallOptions());
        var job = new VoiceCallRecoveryJob(services.GetRequiredService<IServiceScopeFactory>(), options, NullLogger<VoiceCallRecoveryJob>.Instance);
        await using var workerDb = await host.OpenDbAsync();
        var sender = NSubstitute.Substitute.For<ISender>();
        sender.Send(NSubstitute.Arg.Any<ExpireVoiceCallCommand>(), NSubstitute.Arg.Any<CancellationToken>())
            .Returns(info => new ExpireVoiceCallCommandHandler(workerDb, host.Clock, options)
                .Handle(info.Arg<ExpireVoiceCallCommand>(), info.Arg<CancellationToken>()));
        (await job.ProcessAbandonedActiveCallsAsync(workerDb, sender, default)).Should().Be(1);
        await using var verify = await host.OpenDbAsync();
        var ended = await verify.CallSessions.SingleAsync(c => c.Id == seeded.Id);
        ended.Status.Should().Be(CallStatus.Ended);
        ended.EndReason.Should().Be(CallEndReason.NetworkLost);
        ended.TerminalEventId.Should().NotBeNull();
        (await verify.VoiceCallBusyParticipants.AnyAsync(b => b.CallId == seeded.Id)).Should().BeFalse();
        (await verify.OutboxMessages.CountAsync(m => m.AggregateId == seeded.Id.ToString() && m.EventType == "voice-call.lifecycle.ended")).Should().Be(1);
    }
}
