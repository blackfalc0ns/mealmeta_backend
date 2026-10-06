using System.Security.Cryptography;
using MealMate.Application.Common.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MealMate.Infrastructure.Services;

/// <summary>Development-only OTP stub. Never logs the OTP value.</summary>
public sealed class LoggingOtpDeliveryService : IOtpDeliveryService
{
    private readonly ILogger<LoggingOtpDeliveryService> _logger;

    public LoggingOtpDeliveryService(ILogger<LoggingOtpDeliveryService> logger)
        => _logger = logger;

    public Task DeliverAsync(
        string channel,
        string destination,
        string code,
        string purpose,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? templateVariables = null,
        string? language = null)
    {
        _logger.LogWarning(
            "OTP STUB delivery channel={Channel} destination={Destination} purpose={Purpose}",
            channel,
            destination,
            purpose);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Production-safe OTP delivery stub. Wire real SMS/email providers here;
/// this implementation intentionally does nothing and never logs secrets.
/// </summary>
public sealed class SilentOtpDeliveryService : IOtpDeliveryService
{
    public Task DeliverAsync(
        string channel,
        string destination,
        string code,
        string purpose,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? templateVariables = null,
        string? language = null)
        => Task.CompletedTask;
}

public sealed class LocalFileStorage : IFileStorage
{
    private static readonly byte[] EnvelopeMagic = "MMDE"u8.ToArray();
    private const byte EnvelopeVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _root;
    private readonly byte[]? _documentEncryptionKey;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IConfiguration configuration, ILogger<LocalFileStorage> logger)
    {
        var configuredRoot = configuration["Storage:LocalRoot"];
        _root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Path.GetTempPath(), "mealmate-uploads")
            : configuredRoot;
        var configuredKey = configuration["Storage:DocumentEncryptionKey"];
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            try
            {
                _documentEncryptionKey = Convert.FromBase64String(configuredKey);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "Storage:DocumentEncryptionKey must be a base64-encoded 256-bit key.",
                    ex);
            }

            if (_documentEncryptionKey.Length != 32)
            {
                throw new InvalidOperationException(
                    "Storage:DocumentEncryptionKey must decode to exactly 32 bytes.");
            }
        }

        Directory.CreateDirectory(_root);
        _logger = logger;
    }

    public async Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        var safeName = Path.GetFileName(fileName);
        var key = $"{folder.Trim('/')}/{Guid.NewGuid():N}_{safeName}";
        var fullPath = Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        long plaintextSize;
        string checksum;

        try
        {
            if (IsDocumentKey(key))
            {
                var encryptionKey = RequireDocumentEncryptionKey();
                await using var plaintext = new MemoryStream();
                await content.CopyToAsync(plaintext, cancellationToken);
                plaintextSize = plaintext.Length;

                var plaintextBytes = plaintext.ToArray();
                var hash = SHA256.HashData(plaintextBytes);
                checksum = Convert.ToHexString(hash);

                var nonce = RandomNumberGenerator.GetBytes(NonceSize);
                var ciphertext = new byte[plaintextBytes.Length];
                var tag = new byte[TagSize];
                using (var aes = new AesGcm(encryptionKey, TagSize))
                {
                    aes.Encrypt(
                        nonce,
                        plaintextBytes,
                        ciphertext,
                        tag,
                        System.Text.Encoding.UTF8.GetBytes(key));
                }

                await using var output = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous);
                await output.WriteAsync(EnvelopeMagic, cancellationToken);
                output.WriteByte(EnvelopeVersion);
                await output.WriteAsync(nonce, cancellationToken);
                await output.WriteAsync(tag, cancellationToken);
                await output.WriteAsync(ciphertext, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            else
            {
                await using (var output = new FileStream(
                                 tempPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 81920,
                                 FileOptions.Asynchronous))
                {
                    await content.CopyToAsync(output, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                    plaintextSize = output.Length;
                }

                await using var read = File.OpenRead(tempPath);
                var hash = await SHA256.HashDataAsync(read, cancellationToken);
                checksum = Convert.ToHexString(hash);
            }

            File.Move(tempPath, fullPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        if (!IsDocumentKey(key))
        {
            var info = new FileInfo(fullPath);
            plaintextSize = info.Length;
        }

        _logger.LogInformation("Saved file {Key} ({Bytes} bytes)", key, plaintextSize);
        return new StoredFile(key, safeName, contentType, plaintextSize, checksum);
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(storageKey);
        if (!IsDocumentKey(storageKey))
        {
            return File.OpenRead(fullPath);
        }

        var envelope = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        if (!HasEnvelopeMagic(envelope))
        {
            // Backward compatibility for documents saved before encryption was enabled.
            return new MemoryStream(envelope, writable: false);
        }
        if (!HasEncryptedEnvelope(envelope))
        {
            throw new CryptographicException("Encrypted document envelope version is unsupported.");
        }

        var encryptionKey = RequireDocumentEncryptionKey();
        var nonceOffset = EnvelopeMagic.Length + 1;
        var tagOffset = nonceOffset + NonceSize;
        var ciphertextOffset = tagOffset + TagSize;
        if (envelope.Length < ciphertextOffset)
        {
            throw new CryptographicException("Encrypted document envelope is truncated.");
        }

        var plaintext = new byte[envelope.Length - ciphertextOffset];
        using (var aes = new AesGcm(encryptionKey, TagSize))
        {
            aes.Decrypt(
                envelope.AsSpan(nonceOffset, NonceSize),
                envelope.AsSpan(ciphertextOffset),
                envelope.AsSpan(tagOffset, TagSize),
                plaintext,
                System.Text.Encoding.UTF8.GetBytes(storageKey));
        }

        return new MemoryStream(plaintext, writable: false);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public async Task<string> GetReadUrlAsync(string storageKey, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(storageKey);
        if (IsDocumentKey(storageKey))
        {
            var header = new byte[EnvelopeMagic.Length + 1];
            await using var stream = File.OpenRead(fullPath);
            var bytesRead = await stream.ReadAsync(header, cancellationToken);
            if (bytesRead >= EnvelopeMagic.Length && HasEnvelopeMagic(header))
            {
                throw new InvalidOperationException(
                    "Encrypted documents cannot be exposed through a direct local file URL.");
            }
        }

        // Local stub: return absolute file path. Cloud providers return signed URLs.
        return fullPath;
    }

    private byte[] RequireDocumentEncryptionKey()
        => _documentEncryptionKey
           ?? throw new InvalidOperationException(
               "Storage:DocumentEncryptionKey is required to store or read encrypted documents.");

    private static bool IsDocumentKey(string storageKey)
        => storageKey.Replace('\\', '/').StartsWith("documents/", StringComparison.OrdinalIgnoreCase);

    private static bool HasEncryptedEnvelope(ReadOnlySpan<byte> bytes)
        => bytes.Length >= EnvelopeMagic.Length + 1
           && HasEnvelopeMagic(bytes)
           && bytes[EnvelopeMagic.Length] == EnvelopeVersion;

    private static bool HasEnvelopeMagic(ReadOnlySpan<byte> bytes)
        => bytes.Length >= EnvelopeMagic.Length
           && bytes[..EnvelopeMagic.Length].SequenceEqual(EnvelopeMagic);

    private string Resolve(string storageKey)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(Path.GetFullPath(_root), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid storage key.");
        }

        return fullPath;
    }

    public bool Exists(string storageKey)
    {
        try
        {
            return File.Exists(Resolve(storageKey));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Cheap local-disk existence check; overrides the read-probe default.</summary>
    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
        => Task.FromResult(Exists(storageKey));
}
