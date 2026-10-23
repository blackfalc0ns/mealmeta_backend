using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.V2.Dtos;
using Microsoft.Extensions.Options;

namespace MealMate.Infrastructure.VoiceCalls;

public sealed class ApnsVoiceCallOptions
{
    public const string SectionName = "VoiceCalls:Apns";
    public string? TeamId { get; set; }
    public string? KeyId { get; set; }
    /// <summary>Customer app bundle identifier (without the .voip suffix).</summary>
    public string? BundleId { get; set; }
    public string? DriverBundleId { get; set; }
    public string? PrivateKeyPem { get; set; }
    public string? PrivateKeyPath { get; set; }
    public bool UseSandbox { get; set; }
}

/// <summary>PushKit requests use Apple's provider HTTP/2 API and token authentication.</summary>
public sealed class ApnsVoiceCallV2Provider(HttpClient http, IOptions<ApnsVoiceCallOptions> options,
    IDateTimeProvider clock) : IVoiceCallV2ApnsProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<VoiceCallPushResult> SendAsync(string token, CallEventEnvelope<CallSnapshotDto> envelope,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length % 2 != 0 || !token.All(Uri.IsHexDigit))
            return new(VoiceCallPushOutcome.InvalidRoute);
        var config = options.Value;
        var bundle = envelope.Payload.LocalParticipantRole.Equals("Driver", StringComparison.OrdinalIgnoreCase)
            ? config.DriverBundleId : config.BundleId;
        if (string.IsNullOrWhiteSpace(bundle) || string.IsNullOrWhiteSpace(config.TeamId) || string.IsNullOrWhiteSpace(config.KeyId))
            throw new InvalidOperationException("APNs team, key and recipient application bundle identifiers are required for a VoIP route.");
        var pem = config.PrivateKeyPem;
        if (string.IsNullOrWhiteSpace(pem) && !string.IsNullOrWhiteSpace(config.PrivateKeyPath))
            pem = await File.ReadAllTextAsync(config.PrivateKeyPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(pem)) throw new InvalidOperationException("APNs signing key is required for a VoIP route.");
        var deadline = envelope.Payload.DeadlineAtUtc ?? envelope.ExpiresAtUtc;
        if (envelope.ExpiresAtUtc is { } envelopeExpiry && (deadline is null || envelopeExpiry < deadline))
            deadline = envelopeExpiry;
        if (deadline is null || deadline <= clock.UtcNow) return new(VoiceCallPushOutcome.Suppressed);
        var host = config.UseSandbox ? "api.sandbox.push.apple.com" : "api.push.apple.com";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/3/device/{token}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                aps = new Dictionary<string, int> { ["content-available"] = 1 },
                type = envelope.EventType,
                envelope
            }, Json), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", Sign(config, pem));
        request.Headers.Add("apns-push-type", "voip");
        request.Headers.Add("apns-priority", "10");
        request.Headers.Add("apns-topic", bundle + ".voip");
        request.Headers.Add("apns-id", envelope.EventId.ToString());
        request.Headers.Add("apns-expiration", new DateTimeOffset(DateTime.SpecifyKind(deadline.Value, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            if (!response.Headers.TryGetValues("apns-id", out var ids))
                throw new HttpRequestException("APNs accepted a push without its provider message identifier.");
            return new(VoiceCallPushOutcome.Accepted, ids.Single());
        }
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? reason = null;
        try { using var json = JsonDocument.Parse(body); reason = json.RootElement.GetProperty("reason").GetString(); }
        catch (JsonException) { }
        catch (KeyNotFoundException) { }
        if (reason is "BadDeviceToken" or "Unregistered") return new(VoiceCallPushOutcome.InvalidRoute);
        // DeviceTokenNotForTopic can be a configuration error. Do not delete a valid registry token.
        throw new HttpRequestException($"APNs voice-call request failed ({(int)response.StatusCode}, {reason ?? "unknown"}).", null, response.StatusCode);
    }

    private string Sign(ApnsVoiceCallOptions config, string pem)
    {
        static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = config.KeyId }));
        var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        { iss = config.TeamId, iat = new DateTimeOffset(DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc)).ToUnixTimeSeconds() }));
        var unsigned = header + "." + claims;
        using var key = ECDsa.Create();
        key.ImportFromPem(pem);
        if (key.KeySize != 256) throw new InvalidOperationException("APNs signing key must use the P-256 curve.");
        return unsigned + "." + Encode(key.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
