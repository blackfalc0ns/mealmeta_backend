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
        CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IConfiguration configuration, ILogger<LocalFileStorage> logger)
    {
        _root = configuration["Storage:LocalRoot"]
                ?? Path.Combine(Path.GetTempPath(), "mealmate-uploads");
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

        await using var fs = File.Create(fullPath);
        await content.CopyToAsync(fs, cancellationToken);

        string? checksum = null;
        await using (var read = File.OpenRead(fullPath))
        {
            var hash = await SHA256.HashDataAsync(read, cancellationToken);
            checksum = Convert.ToHexString(hash);
        }

        var info = new FileInfo(fullPath);
        _logger.LogInformation("Saved file {Key} ({Bytes} bytes)", key, info.Length);
        return new StoredFile(key, safeName, contentType, info.Length, checksum);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(storageKey);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
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

    public Task<string> GetReadUrlAsync(string storageKey, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        // Local stub: return absolute file path. Cloud providers return signed URLs.
        return Task.FromResult(Resolve(storageKey));
    }

    private string Resolve(string storageKey)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(Path.GetFullPath(_root), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid storage key.");
        }

        return fullPath;
    }
}
