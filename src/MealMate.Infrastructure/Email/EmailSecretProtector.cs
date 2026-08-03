using System.Security.Cryptography;
using MealMate.Application.Common.Abstractions;
using Microsoft.Extensions.Configuration;

namespace MealMate.Infrastructure.Email;

/// <summary>
/// AES-256-GCM encryption for email provider secrets.
/// Master key sourced from configuration <c>Email:SecretsEncryptionKey</c> (base64, 32 bytes).
/// Ciphertext format: base64( nonce(12) || tag(16) || ciphertext ).
/// </summary>
public sealed class EmailSecretProtector : IEmailSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly byte[] _masterKey;

    public EmailSecretProtector(IConfiguration configuration)
    {
        var b64 = configuration["Email:SecretsEncryptionKey"]
            ?? throw new InvalidOperationException(
                "Email:SecretsEncryptionKey is not configured. Provide a base64-encoded 32-byte key.");

        _masterKey = Convert.FromBase64String(b64);

        if (_masterKey.Length != KeySize)
            throw new InvalidOperationException(
                $"Email:SecretsEncryptionKey must be exactly {KeySize} bytes (got {_masterKey.Length}).");
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("Cannot encrypt null or empty plaintext.", nameof(plaintext));

        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // nonce || tag || ciphertext
        var blob = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, blob, NonceSize + TagSize, cipherBytes.Length);

        return Convert.ToBase64String(blob);
    }

    public string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64))
            throw new ArgumentException("Cannot decrypt null or empty value.", nameof(encryptedBase64));

        var blob = Convert.FromBase64String(encryptedBase64);

        if (blob.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted blob is too short.");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipherBytes = new byte[blob.Length - NonceSize - TagSize];

        Buffer.BlockCopy(blob, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(blob, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(blob, NonceSize + TagSize, cipherBytes, 0, cipherBytes.Length);

        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return System.Text.Encoding.UTF8.GetString(plainBytes);
    }

    public string? MaskLastFour(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        if (plaintext.Length <= 4) return "****";
        return string.Concat("****", plaintext.AsSpan(plaintext.Length - 4));
    }
}
