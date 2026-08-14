using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MealMate.Infrastructure.Services;

public sealed class ImageKitFileStorage : IFileStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ImageKitOptions _options;
    private readonly ILogger<ImageKitFileStorage> _logger;

    public ImageKitFileStorage(
        IHttpClientFactory httpFactory,
        IOptions<ImageKitOptions> options,
        ILogger<ImageKitFileStorage> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.PrivateKey);

    public async Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        var privateKey = RequirePrivateKey();
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = $"{Guid.NewGuid():N}.bin";
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(bytes));
        var folderPath = "/" + folder.Trim('/');

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(fileContent, "file", safeName);
        form.Add(new StringContent(safeName), "fileName");
        form.Add(new StringContent(folderPath), "folder");
        form.Add(new StringContent("true"), "useUniqueFileName");

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.UploadEndpoint)
        {
            Content = form,
        };
        request.Headers.Authorization = CreateBasicAuth(privateKey);

        var http = _httpFactory.CreateClient("ImageKit");
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("ImageKit upload failed status={Status}", (int)response.StatusCode);
            throw new InvalidOperationException($"ImageKit upload failed with status {(int)response.StatusCode}.");
        }

        var uploaded = JsonSerializer.Deserialize<ImageKitUploadResponse>(body, JsonOptions);
        var filePath = (uploaded?.FilePath ?? $"{folderPath}/{safeName}").Trim().TrimStart('/');
        RememberUrlEndpoint(uploaded?.Url, uploaded?.FilePath);
        var storageKey = !string.IsNullOrWhiteSpace(uploaded?.Url)
            ? uploaded.Url
            : BuildReadUrl(filePath);
        _logger.LogInformation("Uploaded file to ImageKit folder={Folder} bytes={Bytes}", folderPath, bytes.Length);

        return new StoredFile(storageKey, uploaded?.Name ?? safeName, contentType, bytes.LongLength, checksum);
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var url = await GetReadUrlAsync(storageKey, TimeSpan.FromHours(1), cancellationToken);
        var http = _httpFactory.CreateClient("ImageKit");
        var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        // Public catalog images stay on ImageKit; delete is best-effort by path search.
        _logger.LogInformation("ImageKit delete skipped for key prefix {Prefix}", storageKey.Split('/')[0]);
        await Task.CompletedTask;
    }

    public Task<string> GetReadUrlAsync(string storageKey, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (Uri.TryCreate(storageKey, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp))
        {
            return Task.FromResult(storageKey);
        }

        var endpoint = (_options.UrlEndpoint ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                "Storage:ImageKit:UrlEndpoint is required to read ImageKit files. Example: https://ik.imagekit.io/your_id");
        }

        var path = storageKey.Replace('\\', '/').TrimStart('/');
        return Task.FromResult($"{endpoint}/{path}");
    }

    private string BuildReadUrl(string filePath)
    {
        var endpoint = (_options.UrlEndpoint ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(endpoint)
            ? filePath
            : $"{endpoint}/{filePath.TrimStart('/')}";
    }

    private string RequirePrivateKey()
        => string.IsNullOrWhiteSpace(_options.PrivateKey)
            ? throw new InvalidOperationException("Storage:ImageKit:PrivateKey is not configured.")
            : _options.PrivateKey;

    private static AuthenticationHeaderValue CreateBasicAuth(string privateKey)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{privateKey}:"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private void RememberUrlEndpoint(string? url, string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(_options.UrlEndpoint) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var marker = filePath.StartsWith('/') ? filePath : "/" + filePath;
        var index = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index > 0)
        {
            _options.UrlEndpoint = url[..index].TrimEnd('/');
        }
    }

    private sealed class ImageKitUploadResponse
    {
        public string? FileId { get; set; }
        public string? Name { get; set; }
        public string? Url { get; set; }
        public string? FilePath { get; set; }
    }
}
