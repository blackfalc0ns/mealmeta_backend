using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using MealMate.Application.Features.VoiceCalls.Commands;
using MealMate.Application.Features.VoiceCalls.BackgroundJobs;
using MealMate.Domain.VoiceCalls;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T14/T15 acceptance (AC57–AC60, AC61–AC64): the full F14 chain over real HTTP —
/// qualified no-answer → Hold (notes-validated) → resume (location-checked) → second
/// qualified attempt → conditional reveal (grant/audit/no-phone-leak/replay).
/// </summary>
public sealed class DeliveryContactWorkflowApiTests
{
    private static IReadOnlyDictionary<string, string?> V2On { get; } =
        new Dictionary<string, string?>
        {
            ["VoiceCalls:V2Enabled"] = "true",
            ["VoiceCalls:RetryCooldownSeconds"] = "0"
        };

    private static async Task<VoiceCallTestHost> CreateAsync()
    {
        var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);
        await h.RegisterDeviceAsync(h.DriverDeviceA, "driver-v2-a");
        await h.RegisterDeviceAsync(h.CustomerDeviceA, "customer-v2-a");
        await h.RegisterDeviceAsync(h.CustomerDeviceB, "customer-v2-b");
        return h;
    }

    /// <summary>Drives one real driver→customer call to a qualified Missed: initiate,
    /// receiver ringing-ack, deadline expiry through the worker leg, outbox dispatch.</summary>
    private static async Task RunQualifiedMissedAsync(VoiceCallTestHost h)
    {
        var initiate = await h.DriverDeviceA.PostAsJsonAsync(
            "/api/v2/voice-calls/initiate",
            new { tripStopId = h.TripStopId, clientRequestId = Guid.NewGuid() });
        initiate.EnsureSuccessStatusCode();
        var callId = (await initiate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("callId").GetGuid();

        // Receiver acknowledges the incoming UI (the qualifying receipt).
        (await h.CustomerDeviceA.PostAsync($"/api/v2/voice-calls/{callId}/ringing-ack", content: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Age the ring so the observed ring ≥ 20s (the qualifying threshold), with the
        // deadline just past — exactly what a real 30s ring timeout produces.
        DateTime storedDeadline; long storedSequence;
        await using (var db = await h.OpenDbAsync())
        {
            var call = await db.CallSessions.FirstAsync(c => c.Id == callId);
            typeof(Domain.VoiceCalls.CallSession)
                .GetProperty(nameof(Domain.VoiceCalls.CallSession.RingingAtUtc))!
                .SetValue(call, DateTime.UtcNow.AddSeconds(-25));
            typeof(Domain.VoiceCalls.CallSession)
                .GetProperty(nameof(Domain.VoiceCalls.CallSession.RingExpiresAtUtc))!
                .SetValue(call, DateTime.UtcNow.AddSeconds(-1));
            await db.SaveChangesAsync();
        }

        await using (var db = await h.OpenDbAsync())
        {
            var stored = await db.CallSessions.AsNoTracking().FirstAsync(c => c.Id == callId);
            storedDeadline = stored.RingExpiresAtUtc!.Value;
            storedSequence = stored.Sequence;
        }

        using (var scope = h.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var expired = await sender.Send(new ExpireVoiceCallCommand(
                callId, CallStatus.Ringing, storedSequence, storedDeadline, CallEndReason.NoAnswer));
            expired.IsSuccess.Should().BeTrue();
            expired.Value.Status.Should().Be("Missed");
        }

        // Dispatch the missed lifecycle event durably — the consumer records the attempt.
        await h.RunOutboxAsync();
    }

    [Fact]
    public async Task FullF14Chain_QualifiedMissed_Hold_Resume_SecondMissed_Reveal()
    {
        var h = await CreateAsync();

        // ── First qualified miss ──
        await RunQualifiedMissedAsync(h);

        await using var db = await h.OpenDbAsync();
        (await db.DeliveryContactAttempts.CountAsync(a => a.QualifiesAsNoAnswer)).Should().Be(1,
            "the missed call with a ringing receipt qualifies once");

        // ── Hold: notes validation + creation + idempotency ──
        var badHold = await h.DriverDeviceA.PostAsJsonAsync(
            $"/api/v2/delivery-contact/{h.TripStopId}/hold", new { notes = "قص" });
        badHold.StatusCode.Should().Be(HttpStatusCode.BadRequest, "notes below 5 characters are rejected");

        var hold = await h.DriverDeviceA.PostAsJsonAsync(
            $"/api/v2/delivery-contact/{h.TripStopId}/hold",
            new { notes = "العميل لم يرد على المكالمة، سأعيد المحاولة بعد إعادة الترتيب" });
        hold.StatusCode.Should().Be(HttpStatusCode.OK);
        var caseAfterHold = await hold.Content.ReadFromJsonAsync<JsonElement>();
        caseAfterHold.GetProperty("holdActive").GetBoolean().Should().BeTrue();
        caseAfterHold.GetProperty("canRevealPhone").GetBoolean().Should().BeFalse(
            "only two qualified attempts with a resumed hold unlock the reveal");

        var duplicateHold = await h.DriverDeviceA.PostAsJsonAsync(
            $"/api/v2/delivery-contact/{h.TripStopId}/hold", new { notes = "محاولة hold مكررة" });
        duplicateHold.StatusCode.Should().Be(HttpStatusCode.Conflict, "only one open hold per stop");

        // ── Resume ──
        var resume = await h.DriverDeviceA.PostAsync($"/api/v2/delivery-contact/{h.TripStopId}/resume", content: null);
        resume.StatusCode.Should().Be(HttpStatusCode.OK,
            "the seeded driver fix is fresh, accurate and within the radius");
        var caseAfterResume = await resume.Content.ReadFromJsonAsync<JsonElement>();
        caseAfterResume.GetProperty("holdResumed").GetBoolean().Should().BeTrue();

        // Stop must remain PickedUp — a hold is an operational substate, not a delivery failure.
        await using (var verify = await h.OpenDbAsync())
        {
            var stop = await verify.TripStops.FirstAsync(st => st.Id == h.TripStopId);
            stop.Status.Should().Be(Domain.Enums.TripStopStatus.PickedUp);
        }

        // ── Second qualified miss ──
        await RunQualifiedMissedAsync(h);
        await using (var verify = await h.OpenDbAsync())
        {
            (await verify.DeliveryContactAttempts.CountAsync(a => a.QualifiesAsNoAnswer)).Should().Be(2);
        }

        // ── Reveal: ack missing → 400; valid chain → phone; replay → same grant ──
        var noAck = await h.DriverDeviceA.PostAsJsonAsync(
            $"/api/v2/delivery-contact/{h.TripStopId}/reveal-phone",
            new { clientRequestId = Guid.NewGuid(), reason = "تأكيد التسليم بعد المحاولتين", acknowledgedPrivacyWarning = false });
        noAck.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        string customerPhone;
        await using (var read = await h.OpenDbAsync())
        {
            customerPhone = await read.SubscriptionDays
                .Where(day => day.Id == h.SubscriptionDayId)
                .Join(read.Subscriptions, day => day.SubscriptionId, sub => sub.Id, (day, sub) => sub.CustomerUserId)
                .Join(read.Users, uid => uid, u => u.Id, (uid, u) => u.PhoneNumber)
                .FirstAsync();
        }

        var revealKey = Guid.NewGuid();
        var reveal = await h.DriverDeviceA.PostAsJsonAsync(
            $"/api/v2/delivery-contact/{h.TripStopId}/reveal-phone",
            new { clientRequestId = revealKey, reason = "تأكيد التسليم بعد المحاولتين", acknowledgedPrivacyWarning = true });
        reveal.StatusCode.Should().Be(HttpStatusCode.OK);
        reveal.Headers.CacheControl.NoStore.Should().BeTrue("the revealed number must never be cached");
        var revealBody = await reveal.Content.ReadFromJsonAsync<JsonElement>();
        revealBody.GetProperty("customerPhone").GetString().Should().Be(customerPhone,
            "the real stored customer phone is returned once, via the audited grant");

        var replay = await h.DriverDeviceA.PostAsJsonAsync(
            $"/api/v2/delivery-contact/{h.TripStopId}/reveal-phone",
            new { clientRequestId = revealKey, reason = "تأكيد التسليم بعد المحاولتين", acknowledgedPrivacyWarning = true });
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await replay.Content.ReadAsStringAsync()).RootElement
            .GetProperty("grantId").GetGuid()
            .Should().Be(revealBody.GetProperty("grantId").GetGuid(),
                "the same key resolves to the same persisted grant");

        // ── Privacy: the phone never lands in audit or outbox payloads ──
        await using (var verify = await h.OpenDbAsync())
        {
            var grantId = revealBody.GetProperty("grantId").GetGuid();
            var auditReasons = await verify.AuditLogs
                .Where(a => a.Action == "CustomerPhoneRevealed")
                .Select(a => a.Reason)
                .ToListAsync();
            auditReasons.Should().NotBeEmpty();
            auditReasons.Should().OnlyContain(r => !r.Contains(customerPhone),
                "the audit trail records grant/call ids, never the phone number");

            var outboxPayloads = await verify.OutboxMessages
                .Where(o => o.EventType == "voice-call.contact.phone-revealed")
                .Select(o => o.PayloadJson)
                .ToListAsync();
            outboxPayloads.Should().NotBeEmpty("the admin notification is staged durably");
            outboxPayloads.Should().OnlyContain(p => !p.Contains(customerPhone),
                "the admin notification carries metadata only");
        }
    }

    [Fact]
    public async Task Reveal_BeforeQualifiedChain_IsRefused()
    {
        var h = await CreateAsync();

        // No attempts at all.
        var tooEarly = await h.DriverDeviceA.PostAsJsonAsync(
            $"/api/v2/delivery-contact/{h.TripStopId}/reveal-phone",
            new { clientRequestId = Guid.NewGuid(), reason = "تأكيد التسليم", acknowledgedPrivacyWarning = true });
        tooEarly.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await tooEarly.Content.ReadAsStringAsync()).Should().Contain("contact.not_qualified");

        // One attempt: hold unlocks, reveal does not.
        await RunQualifiedMissedAsync(h);
        (await h.DriverDeviceA.PostAsJsonAsync(
                $"/api/v2/delivery-contact/{h.TripStopId}/hold", new { notes = "محاولة أولى بلا رد" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var afterOne = await h.DriverDeviceA.PostAsJsonAsync(
            $"/api/v2/delivery-contact/{h.TripStopId}/reveal-phone",
            new { clientRequestId = Guid.NewGuid(), reason = "تأكيد التسليم", acknowledgedPrivacyWarning = true });
        afterOne.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "one qualified attempt never unlocks the phone");
    }

    [Fact]
    public async Task TechnicalFailure_NeverQualifies()
    {
        var h = await CreateAsync();

        // A call that fails negotiation (no ringing receipt at all) must not count.
        var initiate = await h.DriverDeviceA.PostAsJsonAsync(
            "/api/v2/voice-calls/initiate",
            new { tripStopId = h.TripStopId, clientRequestId = Guid.NewGuid() });
        initiate.EnsureSuccessStatusCode();
        var callId = (await initiate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("callId").GetGuid();

        using (var scope = h.Services.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var failed = await sender.Send(new ExpireVoiceCallCommand(
                callId, CallStatus.Ringing, 1, null, CallEndReason.ConnectionFailed));
            failed.IsSuccess.Should().BeTrue();
        }

        await h.RunOutboxAsync();

        await using var db = await h.OpenDbAsync();
        (await db.DeliveryContactAttempts.AnyAsync(a => a.CallId == callId)).Should().BeFalse(
            "failed (non-missed) calls produce no contact attempt whatsoever");
        (await db.DeliveryContactCases.Where(c => c.QualifiedMissedCount > 0).AnyAsync()).Should().BeFalse(
            "a call the receiver never saw must not count toward the no-answer evidence");
    }

    [Fact]
    public async Task Customer_CannotHoldOrReveal()
    {
        var h = await CreateAsync();
        await RunQualifiedMissedAsync(h);

        var hold = await h.CustomerDeviceA.PostAsJsonAsync(
            $"/api/v2/delivery-contact/{h.TripStopId}/hold", new { notes = "العميل لا يستطيع إنشاء hold" });
        hold.StatusCode.Should().Match(x => x == HttpStatusCode.Forbidden || x == HttpStatusCode.NotFound,
            "hold is the assigned driver's action");

        var reveal = await h.CustomerDeviceA.PostAsJsonAsync(
            $"/api/v2/delivery-contact/{h.TripStopId}/reveal-phone",
            new { clientRequestId = Guid.NewGuid(), reason = "محاولة كشف", acknowledgedPrivacyWarning = true });
        reveal.StatusCode.Should().Match(x => x == HttpStatusCode.Forbidden || x == HttpStatusCode.NotFound,
            "reveal is the assigned driver's action");
    }
}
