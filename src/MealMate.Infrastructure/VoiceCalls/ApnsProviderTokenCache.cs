using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MealMate.Infrastructure.VoiceCalls;

/// <summary>
/// Process-wide APNs authentication cache, shared across typed HTTP client instances.
/// Reuses provider JWTs for fifty minutes; changing the signer or moving the clock
/// before issuance requires a new token. Neither private keys nor device data are retained.
/// </summary>
public sealed class ApnsProviderTokenCache
{
    private readonly object _gate = new();
    private readonly Dictionary<(string TeamId, string KeyId), Entry> _tokens = new();
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(50);
    private sealed record Entry(string PublicKeyFingerprint, DateTime IssuedAtUtc, string Token);

    public string GetToken(string teamId, string keyId, string privateKeyPem, DateTime utcNow)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        if (key.KeySize != 256) throw new InvalidOperationException("APNs signing key must use the P-256 curve.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
        var identity = (teamId, keyId);
        lock (_gate)
        {
            if (_tokens.TryGetValue(identity, out var cached)
                && cached.PublicKeyFingerprint == fingerprint
                && utcNow >= cached.IssuedAtUtc && utcNow - cached.IssuedAtUtc < RefreshAfter)
                return cached.Token;
            static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = keyId }));
            var claims = Encode(JsonSerializer.SerializeToUtf8Bytes(new
            { iss = teamId, iat = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds() }));
            var unsigned = header + "." + claims;
            var token = unsigned + "." + Encode(key.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            _tokens[identity] = new(fingerprint, utcNow, token);
            return token;
        }
    }
}
