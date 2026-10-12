using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MealMate.ApiTests.VoiceCalls.Support;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MealMate.ApiTests.VoiceCalls;

/// <summary>
/// T06 acceptance (AC22–AC25): device-control sessions are owner-bound, the proof
/// is issued once and stored only as a hash, rotation/revocation cut old sessions
/// off, and a session id alone never grants control. (Hub binding subcases land
/// with the v2 Hub in T12.)
/// </summary>
public sealed class VoiceCallDeviceSessionApiTests
{
    private static IReadOnlyDictionary<string, string?> V2On { get; } =
        new Dictionary<string, string?> { ["VoiceCalls:V2Enabled"] = "true" };

    private static async Task<MealMate.Persistence.AppDbContext> OpenDbAsync(VoiceCallTestHost h) => await h.OpenDbAsync();

    [Fact]
    public async Task RegisterDevice_WithoutAuthentication_IsRejected()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);

        var anonymous = h.CreateAnonymousClient();
        var response = await anonymous.PostAsJsonAsync(
            "/api/v2/voice-calls/devices/register",
            new { installationId = "install-1", platform = "android", capabilityVersion = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RegisterDevice_ReturnsProofOnce_AndStoresOnlyHash()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);

        var (sessionId, proof) = await h.RegisterDeviceAsync(h.DriverDeviceA, "driver-install-1");

        proof.Should().NotBeNullOrWhiteSpace();

        await using var db = await OpenDbAsync(h);
        var session = await db.VoiceCallDeviceSessions.SingleAsync(s => s.Id == sessionId);
        session.UserId.Should().Be(h.DriverUserId);
        session.ControlProofHash.Should().NotBeNullOrEmpty();
        session.ControlProofHash.Should().NotContain(proof, "the raw proof must never be persisted");
        session.ControlProofHash.Should().HaveLength(64, "SHA-256 hex");
        session.RevokedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ForeignFcmToken_CannotBeClaimedByAnotherUser()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);

        // The driver registers a real FCM registry token of their own.
        Guid driverTokenId;
        await using (var db = await OpenDbAsync(h))
        {
            var token = new Domain.IdentityAccess.DriverDeviceToken
            {
                UserId = h.DriverUserId,
                Token = "fcm-token-" + Guid.NewGuid().ToString("N"),
                Platform = Domain.Enums.MobilePlatform.Android,
                IsActive = true,
                LastSeenAtUtc = DateTime.UtcNow
            };
            db.DriverDeviceTokens.Add(token);
            await db.SaveChangesAsync();
            driverTokenId = token.Id;
        }

        // The outsider (customer identity of a different account) tries to claim it.
        var response = await h.OutsiderClient.PostAsJsonAsync(
            "/api/v2/voice-calls/devices/register",
            new { installationId = "outsider-install", platform = "android", capabilityVersion = 2, fcmDeviceTokenId = driverTokenId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a push token registered to another account can never be bound to a foreign session");
    }

    [Fact]
    public async Task Rotation_RevokesPreviousSessionForSameInstallation()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);

        var (firstId, firstProof) = await h.RegisterDeviceAsync(h.CustomerDeviceA, "customer-install-1");
        var (secondId, secondProof) = await h.RegisterDeviceAsync(h.CustomerDeviceA, "customer-install-1");

        secondId.Should().NotBe(firstId);

        using var scope = h.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IVoiceCallDeviceSessionService>();

        var firstResolve = await service.ResolveAsync(h.CustomerUserId, firstId, firstProof, CancellationToken.None);
        firstResolve.IsSuccess.Should().BeFalse("rotation revokes the previous session");

        var secondResolve = await service.ResolveAsync(h.CustomerUserId, secondId, secondProof, CancellationToken.None);
        secondResolve.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RevokedSession_CannotResolve()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);
        var (sessionId, proof) = await h.RegisterDeviceAsync(h.DriverDeviceA, "driver-install-2");

        var revoke = await h.DriverDeviceA.PostAsync($"/api/v2/voice-calls/devices/{sessionId}/revoke", content: null);
        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = h.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IVoiceCallDeviceSessionService>();
        var resolve = await service.ResolveAsync(h.DriverUserId, sessionId, proof, CancellationToken.None);
        resolve.IsSuccess.Should().BeFalse("a revoked session cannot control calls");
    }

    [Fact]
    public async Task WrongOrMissingProof_CannotResolve()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);
        var (sessionId, proof) = await h.RegisterDeviceAsync(h.CustomerDeviceA, "customer-install-2");

        using var scope = h.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IVoiceCallDeviceSessionService>();

        var wrongProof = await service.ResolveAsync(h.CustomerUserId, sessionId, proof[..^2] + "xx", CancellationToken.None);
        wrongProof.IsSuccess.Should().BeFalse();

        var copiedSessionIdOnly = await service.ResolveAsync(h.CustomerUserId, sessionId, "", CancellationToken.None);
        copiedSessionIdOnly.IsSuccess.Should().BeFalse("a copied session id without proof grants nothing");

        var outsiderWithStolenId = await service.ResolveAsync(
            h.OutsiderUserId, sessionId, proof, CancellationToken.None);
        outsiderWithStolenId.IsSuccess.Should().BeFalse("ownership is checked against the JWT subject");
    }

    [Fact]
    public async Task ExpiredSession_CannotResolve()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);
        var (sessionId, proof) = await h.RegisterDeviceAsync(h.DriverDeviceA, "driver-install-3");

        await using (var db = await OpenDbAsync(h))
        {
            var session = await db.VoiceCallDeviceSessions.SingleAsync(s => s.Id == sessionId);
            session.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var scope = h.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IVoiceCallDeviceSessionService>();
        var resolve = await service.ResolveAsync(h.DriverUserId, sessionId, proof, CancellationToken.None);
        resolve.IsSuccess.Should().BeFalse("an expired session cannot control calls");
    }

    [Fact]
    public async Task FcmTokenRefresh_rebinds_the_live_session_without_rotating_it()
    {
        await using var h = await VoiceCallTestHost.CreateAsync(extraSettings: V2On);

        // The customer registers an FCM route, then a v2 voice session bound to it.
        Guid firstTokenId;
        await using (var db = await OpenDbAsync(h))
        {
            var token = new Domain.IdentityAccess.CustomerDeviceToken
            {
                UserId = h.CustomerUserId,
                Token = "fcm-customer-" + Guid.NewGuid().ToString("N"),
                Platform = Domain.Enums.MobilePlatform.Android,
                DeviceId = "customer-install-refresh",
                IsActive = true,
                LastSeenAtUtc = DateTime.UtcNow
            };
            db.CustomerDeviceTokens.Add(token);
            await db.SaveChangesAsync();
            firstTokenId = token.Id;
        }

        var (sessionId, proof) = await h.RegisterDeviceAsync(h.CustomerDeviceA, "customer-install-refresh", firstTokenId);
        string originalHash;
        await using (var db = await OpenDbAsync(h))
        {
            originalHash = (await db.VoiceCallDeviceSessions.SingleAsync(s => s.Id == sessionId)).ControlProofHash;
        }

        // The app refreshes its FCM token through the real REST route, same installation.
        var refresh = await h.CustomerDeviceA.PutAsJsonAsync(
            "/api/v1/customers/me/device-tokens",
            new { token = "fcm-customer-refreshed-" + Guid.NewGuid().ToString("N"), platform = "Android", deviceId = "customer-install-refresh" });
        refresh.EnsureSuccessStatusCode();
        var refreshBody = await refresh.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var newTokenId = refreshBody.GetProperty("fcmDeviceTokenId").GetGuid();
        newTokenId.Should().NotBe(firstTokenId, "a new FCM token gets its own registry row");

        await using (var verify = await OpenDbAsync(h))
        {
            var session = await verify.VoiceCallDeviceSessions.SingleAsync(s => s.Id == sessionId);
            session.FcmDeviceTokenId.Should().Be(newTokenId,
                "the live session of the same installation follows the refreshed route");
            session.ControlProofHash.Should().Be(originalHash, "route refresh never rotates the control session");
            session.RevokedAtUtc.Should().BeNull();

            var oldRoute = await verify.CustomerDeviceTokens.SingleAsync(t => t.Id == firstTokenId);
            oldRoute.IsActive.Should().BeFalse("the superseded route of the same installation is deactivated");
        }

        // Session identity is intact: the same id + proof still resolves.
        using var scope = h.Services.CreateScope();
        var sessionService = scope.ServiceProvider.GetRequiredService<IVoiceCallDeviceSessionService>();
        var resolve = await sessionService.ResolveAsync(h.CustomerUserId, sessionId, proof, CancellationToken.None);
        resolve.IsSuccess.Should().BeTrue("token refresh must not disturb an active call's control session");
    }
}
