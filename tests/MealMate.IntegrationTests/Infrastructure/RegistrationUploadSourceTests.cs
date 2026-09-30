using System.Net;
using System.Text;
using FluentAssertions;
using MealMate.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MealMate.IntegrationTests.Infrastructure;

public sealed class RegistrationUploadSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"registration-source-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Configured_source_reads_registration_document_from_upload_server()
    {
        var response = new SourceResponseHandler(HttpStatusCode.OK, "original document");
        var storage = CreateStorage(response, "https://upload-origin.test");

        await using var stream = await storage.OpenReadAsync("uploads/drivers/driver-registration/id.jpg");
        using var reader = new StreamReader(stream);

        (await reader.ReadToEndAsync()).Should().Be("original document");
        response.RequestUri.Should().Be(new Uri("https://upload-origin.test/api/v1/uploads/content?key=uploads%2Fdrivers%2Fdriver-registration%2Fid.jpg"));
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_source_file_does_not_use_a_local_copy()
    {
        var response = new SourceResponseHandler(HttpStatusCode.NotFound, "");
        var storage = CreateStorage(response, "https://upload-origin.test");
        var saved = await storage.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes("local copy")),
            "id.jpg", "image/jpeg", "uploads/drivers/driver-registration");

        var read = async () => await storage.OpenReadAsync(saved.StorageKey);

        await read.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Without_source_configuration_existing_local_uploads_round_trip()
    {
        var response = new SourceResponseHandler(HttpStatusCode.NotFound, "");
        var storage = CreateStorage(response, null);
        var saved = await storage.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes("local document")),
            "id.jpg", "image/jpeg", "uploads/drivers/driver-registration");

        await using var stream = await storage.OpenReadAsync(saved.StorageKey);
        using var reader = new StreamReader(stream);

        (await reader.ReadToEndAsync()).Should().Be("local document");
        response.RequestUri.Should().BeNull();
    }

    private RoutingFileStorage CreateStorage(SourceResponseHandler response, string? source)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:LocalRoot"] = _root,
            ["Storage:RegistrationUploads:SourceApiBaseUrl"] = source
        }).Build();
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(response));
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(factory)
            .AddSingleton(new LocalFileStorage(configuration, NullLogger<LocalFileStorage>.Instance))
            .AddSingleton(new ImageKitFileStorage(factory, Options.Create(new ImageKitOptions()), NullLogger<ImageKitFileStorage>.Instance))
            .BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<RoutingFileStorage>(services);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class SourceResponseHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
        }
    }
}
