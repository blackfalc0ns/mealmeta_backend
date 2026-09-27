using System.Security.Cryptography;
using System.Text;
using MealMate.Application.Features.VoiceCalls.Abstractions;
using MealMate.Application.Features.VoiceCalls.Dtos;
using Microsoft.Extensions.Options;

namespace MealMate.Infrastructure.VoiceCalls;

/// <summary>
/// Generates ephemeral time-limited STUN/TURN credentials for Coturn using the standard
/// TURN REST API authentication mechanism (HMAC-SHA1 with shared secret).
/// </summary>
public sealed class TurnCredentialService : ITurnCredentialService
{
    private readonly CoturnOptions _options;

    public TurnCredentialService(IOptions<CoturnOptions> options)
    {
        _options = options.Value;
    }

    public TurnCredentialsResponseDto GenerateCredentials(string userId)
    {
        var sanitizedUserId = string.IsNullOrWhiteSpace(userId) ? Guid.NewGuid().ToString("N") : userId.Trim();
        var expiresAtUtc = DateTime.UtcNow.AddSeconds(_options.TtlSeconds);
        var expiryEpoch = new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds();

        var username = $"{expiryEpoch}:{sanitizedUserId}";

        var secretBytes = Encoding.UTF8.GetBytes(_options.SharedSecret);
        var usernameBytes = Encoding.UTF8.GetBytes(username);

        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(usernameBytes);
        var password = Convert.ToBase64String(hash);

        var iceServers = new List<RtcIceServerDto>();

        // 1. STUN Servers (Discovery of public IP / Server-Reflexive Candidate)
        var stunUrls = new List<string>
        {
            $"stun:{_options.ServerHost}:{_options.StunPort}"
        };
        if (_options.FallbackStunServers is { Length: > 0 })
        {
            stunUrls.AddRange(_options.FallbackStunServers);
        }
        iceServers.Add(new RtcIceServerDto(stunUrls));

        if (_options.Enabled)
        {
            // 2. TURN UDP (Preferred for real-time low-latency audio transmission)
            iceServers.Add(new RtcIceServerDto(
                Urls: new[] { $"turn:{_options.ServerHost}:{_options.TurnPort}?transport=udp" },
                Username: username,
                Credential: password));

            // 3. TURN TCP (Reliable fallback when UDP is blocked by cellular carrier/firewall)
            iceServers.Add(new RtcIceServerDto(
                Urls: new[] { $"turn:{_options.ServerHost}:{_options.TurnPort}?transport=tcp" },
                Username: username,
                Credential: password));

            // 4. TURNS TLS over TCP (Port 5349 - Passes through strict enterprise / hospital firewalls)
            if (_options.IsTlsActive())
            {
                iceServers.Add(new RtcIceServerDto(
                    Urls: new[] { $"turns:{_options.ServerHost}:{_options.TurnsPort}?transport=tcp" },
                    Username: username,
                    Credential: password));
            }
        }

        return new TurnCredentialsResponseDto(
            Username: username,
            Password: password,
            TtlSeconds: _options.TtlSeconds,
            ExpiresAtUtc: expiresAtUtc,
            IceServers: iceServers);
    }
}
