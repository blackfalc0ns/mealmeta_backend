using MealMate.Application.Common.Abstractions;
using Microsoft.Extensions.Configuration;

namespace MealMate.Infrastructure.Services;

public sealed class RoutingFileStorage : IFileStorage
{
    private readonly LocalFileStorage _local;
    private readonly ImageKitFileStorage _imageKit;
    private readonly IHttpClientFactory _httpFactory;
    private readonly Uri? _registrationUploadsSource;

    public RoutingFileStorage(LocalFileStorage local, ImageKitFileStorage imageKit,
        IConfiguration configuration, IHttpClientFactory httpFactory)
    {
        _local = local;
        _imageKit = imageKit;
        _httpFactory = httpFactory;
        var source = configuration["Storage:RegistrationUploads:SourceApiBaseUrl"];
        if (!string.IsNullOrWhiteSpace(source))
        {
            if (!Uri.TryCreate(source.TrimEnd('/') + "/", UriKind.Absolute, out var sourceUri)
                || (sourceUri.Scheme != Uri.UriSchemeHttps
                    && !(sourceUri.Scheme == Uri.UriSchemeHttp && sourceUri.IsLoopback))
                || !string.IsNullOrEmpty(sourceUri.UserInfo)
                || !string.IsNullOrEmpty(sourceUri.Query)
                || !string.IsNullOrEmpty(sourceUri.Fragment))
            {
                throw new InvalidOperationException("Storage:RegistrationUploads:SourceApiBaseUrl must be an HTTPS API origin (HTTP is allowed only on loopback).");
            }
            _registrationUploadsSource = sourceUri;
        }
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

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var normalizedKey = storageKey.Replace('\\', '/').TrimStart('/');
        if (_registrationUploadsSource is not null
            && normalizedKey.StartsWith("uploads/drivers/driver-registration/", StringComparison.OrdinalIgnoreCase))
        {
            var url = new Uri(_registrationUploadsSource,
                $"api/v1/uploads/content?key={Uri.EscapeDataString(normalizedKey)}");
            var content = await _httpFactory.CreateClient("RegistrationUploadsSource")
                .GetByteArrayAsync(url, cancellationToken);
            return new MemoryStream(content, writable: false);
        }

        return UseImageKitForRead(storageKey)
            ? await _imageKit.OpenReadAsync(storageKey, cancellationToken)
            : await _local.OpenReadAsync(storageKey, cancellationToken);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        => UseImageKitForRead(storageKey)
            ? _imageKit.DeleteAsync(storageKey, cancellationToken)
            : _local.DeleteAsync(storageKey, cancellationToken);

    public Task<string> GetReadUrlAsync(string storageKey, TimeSpan ttl, CancellationToken cancellationToken = default)
        => UseImageKitForRead(storageKey)
            ? _imageKit.GetReadUrlAsync(storageKey, ttl, cancellationToken)
            : _local.GetReadUrlAsync(storageKey, ttl, cancellationToken);

    public async Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
        => UseImageKitForRead(storageKey)
            ? await ((MealMate.Application.Common.Abstractions.IFileStorage)_imageKit).ExistsAsync(storageKey, cancellationToken)
            : _local.Exists(storageKey);

    private bool UseImageKitForWrite(string folder)
        => _imageKit.IsEnabled && !IsDocument(folder);

    private bool UseImageKitForRead(string storageKey)
    {
        if (!_imageKit.IsEnabled)
        {
            return false;
        }

        if (IsImageKitUrl(storageKey))
        {
            return true;
        }

        return !IsDocument(storageKey) && !_local.Exists(storageKey);
    }

    private static bool IsImageKitUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Host.Contains("imagekit.io", StringComparison.OrdinalIgnoreCase);

    private static bool IsDocument(string value)
    {
        var normalized = value.Replace('\\', '/');
        return normalized.StartsWith("documents/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/documents/", StringComparison.OrdinalIgnoreCase)
            || MealMate.Application.Common.Storage.ProtectedEvidenceStorage.IsProtectedKey(normalized);
    }
}
