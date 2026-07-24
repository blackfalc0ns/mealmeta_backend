namespace MealMate.Application.Common.Abstractions;

public sealed record StoredFile(
    string StorageKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Checksum = null);

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Returns a time-limited local path/URL for download. Cloud providers may return signed URLs.</summary>
    Task<string> GetReadUrlAsync(string storageKey, TimeSpan ttl, CancellationToken cancellationToken = default);
}
