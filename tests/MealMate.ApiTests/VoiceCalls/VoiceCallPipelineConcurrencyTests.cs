using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Domain.Enums;
using MealMate.Domain.VoiceCalls;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T03 acceptance (AC08/AC09): concurrent state transitions through the real
/// MediatR pipeline on PostgreSQL xmin never leak a second-save failure, and a
/// reconciled conflict does not poison subsequent commands sharing the context.
/// </summary>
public sealed class VoiceCallPipelineConcurrencyTests
{
    private static async Task<Guid> SeedRingingCallAsync(VoiceCallTestHost h)
    {
        await using var db = await h.OpenDbAsync();
        var call = new CallSession(
            h.TripStopId,
            h.DriverUserId,
            CallParticipantType.Driver,
            h.CustomerUserId,
            CallParticipantType.Customer,
            Guid.NewGuid(),
            h.Clock.UtcNow);
        call.MarkRinging(h.Clock.UtcNow);
        db.CallSessions.Add(call);
        await db.SaveChangesAsync();
        return call.Id;
    }

    [Fact]
    public async Task TwoConcurrentAnswers_ThroughHttp_NeverLeakConcurrencyException()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();
        var callId = await SeedRingingCallAsync(h);

        // Two receiver devices answer at the same time over real HTTP + pipeline.
        var a = h.CustomerDeviceA.PostAsync($"/api/v1/voice-calls/{callId}/answer", content: null);
        var b = h.CustomerDeviceB.PostAsync($"/api/v1/voice-calls/{callId}/answer", content: null);
        await Task.WhenAll(a, b);

        var statusA = (int)a.Result.StatusCode;
        var statusB = (int)b.Result.StatusCode;

        statusA.Should().BeLessThan(500, "a losing concurrent answer must reconcile cleanly, not crash");
        statusB.Should().BeLessThan(500, "a losing concurrent answer must reconcile cleanly, not crash");
        (statusA is (int)HttpStatusCode.OK or (int)HttpStatusCode.Conflict or (int)HttpStatusCode.NotFound).Should().BeTrue();
        (statusB is (int)HttpStatusCode.OK or (int)HttpStatusCode.Conflict or (int)HttpStatusCode.NotFound).Should().BeTrue();

        await using var db = await h.OpenDbAsync();
        var call = await db.CallSessions.FirstAsync(c => c.Id == callId);
        call.Status.Should().Be(CallStatus.Accepted);
        call.AcceptedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ReconciledConflict_DoesNotPoisonNextCommandInSameScope()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();
        var callId = await SeedRingingCallAsync(h);

        // Worker-batch style: one scope handles several commands in sequence, exactly
        // like the recovery worker (system command, no HTTP identity).
        using var scope = h.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // Steal the row's xmin from another context, so the in-scope command conflicts.
        await using (var other = await h.OpenDbAsync())
        {
            var winner = await other.CallSessions.FirstAsync(c => c.Id == callId);
            winner.Accept(h.Clock.UtcNow.AddSeconds(1));
            await other.SaveChangesAsync();
        }

        // The loser reconciles to the winner's state or reports a clean business
        // failure — either way it must not throw and not poison the shared context.
        var act = async () => await sender.Send(new MarkCallFailedCommand(
            callId, CallEndReason.ConnectionFailed, IsSystem: true));
        await act.Should().NotThrowAsync("a reconciled conflict must surface as a business Result, never as a pipeline exception");

        // Next command in the SAME scope: a normal command that relies on UnitOfWorkBehavior.
        // The report-issue command now requires a real delivery stop (order.not_found
        // otherwise) and an allow-listed issue type; the pipeline regression target is
        // the save, so use the host's seeded stop.
        var issueResult = await sender.Send(new ReportDriverIssueCommand(
            h.TripStopId,
            IssueType: "Other",
            Description: "post-conflict command"));

        issueResult.IsSuccess.Should().BeTrue("a reconciled conflict must not poison subsequent commands in the same context");

        await using var db = await h.OpenDbAsync();
        var auditExists = await db.AuditLogs.AnyAsync(
            a => a.Action == "DriverReportedIssue" && a.EntityId == h.TripStopId);
        auditExists.Should().BeTrue("the normal command persisted through the pipeline");
    }

    [Fact]
    public async Task NormalCommand_StillSavesThroughUnitOfWorkBehavior()
    {
        await using var h = await VoiceCallTestHost.CreateAsync();

        using var scope = h.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var result = await sender.Send(new ReportDriverIssueCommand(
            h.TripStopId,
            IssueType: "Other",
            Description: "behavior save regression"));

        result.IsSuccess.Should().BeTrue();
        await using var db = await h.OpenDbAsync();
        var exists = await db.AuditLogs.AnyAsync(a => a.Action == "DriverReportedIssue" && a.EntityId == h.TripStopId);
        exists.Should().BeTrue("non-voice commands keep their UnitOfWorkBehavior save");
    }
}
