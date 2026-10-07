using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Domain.VoiceCalls;
using Microsoft.EntityFrameworkCore;
using MediatR;
using MealMate.Application.Features.VoiceCalls.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// Phase-4 acceptance: conditional v2 expiry — answer refused after RingExpiresAtUtc
/// inside the write boundary; the stale-timeout worker never kills a progressing call;
/// a terminal call releases Busy so the next call starts (no overlap while ringing).
/// </summary>
public sealed class VoiceCallV2ExpiryApiTests
{
    private static IReadOnlyDictionary<string, string?> V2On { get; } =
        new Dictionary<string, string?> { ["VoiceCalls:V2Enabled"] = "true" };

    private static async Task<(VoiceCallTestHost Host, Guid CallId)> CreateRingingAsync()
    {
        var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);
        await h.RegisterDeviceAsync(h.DriverDeviceA, "driver-v2-a");
        await h.RegisterDeviceAsync(h.CustomerDeviceA, "customer-v2-a");
        await h.RegisterDeviceAsync(h.CustomerDeviceB, "customer-v2-b");

        var response = await h.DriverDeviceA.PostAsJsonAsync(
            "/api/v2/voice-calls/initiate",
            new { tripStopId = h.TripStopId, clientRequestId = Guid.NewGuid() });
        response.EnsureSuccessStatusCode();
        var callId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("callId").GetGuid();
        return (h, callId);
    }

    [Fact]
    public async Task Answer_AfterRingDeadline_IsRefused_AndWorkerMarksMissed()
    {
        var (h, callId) = await CreateRingingAsync();

        // Force the deadline into the past (ring window elapsed); capture the exact
        // stored deadline so the conditional expiry predicate matches bit-for-bit.
        DateTime storedDeadline;
        long storedSequence;
        await using (var db = await h.OpenDbAsync())
        {
            var ringingCall = await db.CallSessions.FirstAsync(c => c.Id == callId);
            typeof(Domain.VoiceCalls.CallSession)
                .GetProperty(nameof(Domain.VoiceCalls.CallSession.RingExpiresAtUtc))!
                .SetValue(ringingCall, DateTime.UtcNow.AddSeconds(-1));
            await db.SaveChangesAsync();
        }

        // Re-read from PostgreSQL: timestamptz truncates sub-microsecond ticks, and the
        // conditional predicate must compare the STORED value bit-for-bit.
        await using (var db = await h.OpenDbAsync())
        {
            var stored = await db.CallSessions.AsNoTracking().FirstAsync(c => c.Id == callId);
            storedDeadline = stored.RingExpiresAtUtc!.Value;
            storedSequence = stored.Sequence;
        }

        var answer = await h.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/answer", content: null);
        answer.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "answering after RingExpiresAtUtc is refused inside the write boundary");

        // The recovery worker lands the Missed terminal through the conditional expiry
        // (status+sequence+deadline verified at write time).
        using var scope = h.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var missed = await sender.Send(new ExpireVoiceCallCommand(
            callId, CallStatus.Ringing, storedSequence, storedDeadline, CallEndReason.NoAnswer));
        missed.IsSuccess.Should().BeTrue("the expired ringing call expires cleanly; got {0}", missed.Error?.Description);

        await using var verify = await h.OpenDbAsync();
        var call = await verify.CallSessions.FirstAsync(c => c.Id == callId);
        call.Status.Should().Be(CallStatus.Missed);
        (await verify.VoiceCallBusyParticipants.CountAsync(b => b.CallId == callId)).Should().Be(0);
    }

    [Fact]
    public async Task ConditionalExpiry_StaleTimeout_DoesNotKillProgressingCall()
    {
        var (h, callId) = await CreateRingingAsync();

        // Answer and progress to Connecting BEFORE the worker fires.
        (await h.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/answer", content: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await h.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/reports/connecting", content: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Worker snapshot taken while Ringing, then the call moved on.
        Guid callIdSnapshot; CallStatus statusSnapshot; long sequenceSnapshot; DateTime? deadlineSnapshot;
        await using (var db = await h.OpenDbAsync())
        {
            var staleSnapshot = await db.CallSessions.FirstAsync(c => c.Id == callId);
            callIdSnapshot = staleSnapshot.Id;
            statusSnapshot = CallStatus.Ringing;
            sequenceSnapshot = staleSnapshot.Sequence - 1;
            deadlineSnapshot = staleSnapshot.RingExpiresAtUtc;
        }

        using var scope = h.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var expiry = await sender.Send(new ExpireVoiceCallCommand(
            callIdSnapshot, statusSnapshot, sequenceSnapshot, deadlineSnapshot, CallEndReason.NoAnswer));

        expiry.IsSuccess.Should().BeTrue();
        expiry.Value.Status.Should().Be("Connecting",
            "a stale timeout must never terminate a call that progressed past Ringing");

        await using var verify = await h.OpenDbAsync();
        var call = await verify.CallSessions.FirstAsync(c => c.Id == callId);
        call.Status.Should().Be(CallStatus.Connecting);
        (await verify.VoiceCallBusyParticipants.AnyAsync(b => b.CallId == callId)).Should().BeTrue(
            "the ongoing call keeps its reservations");
    }

    [Fact]
    public async Task Worker_ExpiredRingingCall_MarksMissed_AndNextCallStarts()
    {
        var (h, callId) = await CreateRingingAsync();

        // Age the ring past the deadline and run the REAL recovery worker leg.
        await using (var db = await h.OpenDbAsync())
        {
            var ringingCall = await db.CallSessions.FirstAsync(c => c.Id == callId);
            typeof(Domain.VoiceCalls.CallSession)
                .GetProperty(nameof(Domain.VoiceCalls.CallSession.RingExpiresAtUtc))!
                .SetValue(ringingCall, DateTime.UtcNow.AddSeconds(-2));
            await db.SaveChangesAsync();
        }

        using (var scope = h.Services.CreateScope())
        {
            var options = Microsoft.Extensions.Options.Options.Create(
                new MealMate.Application.Features.VoiceCalls.Options.VoiceCallOptions { RingTimeoutSeconds = 30, MaxBatchSize = 10 });
            var job = new VoiceCallRecoveryJob(
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                options,
                scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<VoiceCallRecoveryJob>>());
            var recovered = await job.ProcessExpiredRingingCallsAsync(
                scope.ServiceProvider.GetRequiredService<MealMate.Application.Common.Abstractions.IAppDbContext>(),
                scope.ServiceProvider.GetRequiredService<ISender>(),
                CancellationToken.None);
            recovered.Should().BeGreaterThanOrEqualTo(1, "the expired ringing call is recovered");
        }

        await using var verify = await h.OpenDbAsync();
        var missedCall = await verify.CallSessions.FirstAsync(c => c.Id == callId);
        missedCall.Status.Should().Be(CallStatus.Missed);
        (await verify.VoiceCallBusyParticipants.CountAsync(b => b.CallId == callId)).Should().Be(0);

        // The next call can start immediately — no permanent reservations.
        var next = await h.DriverDeviceA.PostAsJsonAsync(
            "/api/v2/voice-calls/initiate",
            new { tripStopId = h.TripStopId, clientRequestId = Guid.NewGuid() });
        next.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
