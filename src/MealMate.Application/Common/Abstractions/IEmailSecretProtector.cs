namespace MealMate.Application.Common.Abstractions;

/// <summary>
/// Encrypts and decrypts email provider secrets (API keys, webhook signing secrets)
/// using AES-256-GCM with a server master key. Never logs plaintext values.
/// </summary>
public interface IEmailSecretProtector
{
    /// <summary>Encrypts a plaintext secret. Returns a base64-encoded ciphertext blob (nonce || tag || ciphertext).</summary>
    string Encrypt(string plaintext);

    /// <summary>Decrypts a previously encrypted base64 blob back to plaintext.</summary>
    string Decrypt(string encryptedBase64);

    /// <summary>Returns a safe mask (last 4 chars) for display, or null if the input is null/empty.</summary>
    string? MaskLastFour(string? plaintext);
}
