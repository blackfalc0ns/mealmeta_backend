using MealMate.Application.Common.Abstractions;

namespace MealMate.Infrastructure.Services;

public sealed class RoutingFileStorage : IFileStorage
{
    private readonly LocalFileStorage _local;
    private readonly ImageKitFileStorage _imageKit;

    public RoutingFileStorage(LocalFileStorage local, ImageKitFileStorage imageKit)
    {
        _local = local;
        _imageKit = imageKit;
    }

    public Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
        => UseImageKitForWrite(folder)
            ? _imageKit.SaveAsync(content, fileName, contentType, folder, cancellationToken)
            : _local.SaveAsync(content, fileName, contentType, folder, cancellationToken);

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
        => UseImageKitForRead(storageKey)
            ? _imageKit.OpenReadAsync(storageKey, cancellationToken)
            : _local.OpenReadAsync(storageKey, cancellationToken);

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        => UseImageKitForRead(storageKey)
            ? _imageKit.DeleteAsync(storageKey, cancellationToken)
            : _local.DeleteAsync(storageKey, cancellationToken);

    public Task<string> GetReadUrlAsync(string storageKey, TimeSpan ttl, CancellationToken cancellationToken = default)
        => UseImageKitForRead(storageKey)
            ? _imageKit.GetReadUrlAsync(storageKey, ttl, cancellationToken)
            : _local.GetReadUrlAsync(storageKey, ttl, cancellationToken);

    private bool UseImageKitForWrite(string folder)
        => _imageKit.IsEnabled && !IsDocument(folder);

    private bool UseImageKitForRead(string storageKey)
        => _imageKit.IsEnabled && !IsDocument(storageKey) && !_local.Exists(storageKey);

    private static bool IsDocument(string value)
    {
        var normalized = value.Replace('\\', '/');
        return normalized.StartsWith("documents/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/documents/", StringComparison.OrdinalIgnoreCase);
    }
}
