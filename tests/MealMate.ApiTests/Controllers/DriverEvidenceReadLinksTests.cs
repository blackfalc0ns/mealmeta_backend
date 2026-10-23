using System.Text;
using System.Security.Claims;
using FluentAssertions;
using MealMate.Api.Controllers.V1;
using MealMate.Api;
using MealMate.Api.Filters;
using MealMate.Api.Security;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Application.Features.UnifiedSupport;
using MealMate.Application.Features.Uploads;
using MealMate.Domain.Common;
using MealMate.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MealMate.ApiTests.Controllers;

public class DriverEvidenceReadLinksTests
{
    private const string Key = "uploads/drivers/issue-evidence/evidence.webp";
    private static readonly byte[] ValidWebp = [0x52, 0x49, 0x46, 0x46, 0x0c, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];

    [Fact]
    public void WebApiRegistersGlobalFilterAndReadLinkDependencies()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = new string('x', 64), ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test"
        }).Build();
        using var services = new ServiceCollection().AddLogging().AddWebApi(configuration).BuildServiceProvider();
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<DriverEvidenceReadLinks>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<DriverEvidenceReadLinksFilter>().Should().NotBeNull();
        services.GetRequiredService<IOptions<MvcOptions>>().Value.Filters.Should()
            .Contain(filter => filter is TypeFilterAttribute && ((TypeFilterAttribute)filter).ImplementationType == typeof(DriverEvidenceReadLinksFilter));
    }

    [Theory]
    [InlineData(false, 200)]
    [InlineData(true, 400)]
    [InlineData(true, 302)]
    public void FilterDoesNotIssueCapabilitiesForAnonymousOrUnsuccessfulResponses(bool authenticated, int status)
    {
        var links = new DriverEvidenceReadLinks(new EphemeralDataProtectionProvider(), new Clock());
        var dto = new DriverSupportIssueMutationDto(Guid.NewGuid(), "AwaitingReply", [CatalogImageStorage.ToContentUrl(Key)!]);
        ApplyFilter(dto, links, authenticated, status).Result.Value.Should().BeSameAs(dto);
    }

    [Theory]
    [InlineData(Key)]
    [InlineData("uploads/drivers/delivery/proof.webp")]
    public async Task FilterIssuedPhotoCanBeReadWithoutImageAuthorizationHeader(string key)
    {
        var clock = new Clock();
        var links = new DriverEvidenceReadLinks(new EphemeralDataProtectionProvider(), clock);
        var detail = new DriverSupportIssueMutationDto(Guid.NewGuid(), "AwaitingReply", [CatalogImageStorage.ToContentUrl(key)!]);
        var filtered = ApplyFilter(detail, links);
        var url = ((DriverSupportIssueMutationDto)filtered.Result.Value!).EvidenceUrls.Single();
        url.Should().Contain("readToken=");
        filtered.Http.Response.Headers.CacheControl.ToString().Should().Be("no-store");

        var (controller, storage, db, evidenceScans, staffAccess) = BuildReader(links, url);
        await using (db)
        {
            var expectedFolder = key.StartsWith("uploads/drivers/delivery/", StringComparison.OrdinalIgnoreCase)
                ? "delivery" : "issue-evidence";
            db.UploadFileRecords.Add(new UploadFileRecord
            {
                StorageKey = key,
                FileName = "evidence.webp",
                ContentType = "image/webp",
                SizeBytes = ValidWebp.Length,
                Scope = "drivers",
                Folder = expectedFolder,
                UploadedByUserId = Guid.NewGuid(),
                UploadedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var result = await controller.Read(key, storage, db, evidenceScans, staffAccess, default);
            result.Should().BeOfType<FileContentResult>();
            controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        }
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("tampered")]
    [InlineData("different-key")]
    public async Task InvalidCapabilityCannotReadPrivatePhoto(string invalidity)
    {
        var clock = new Clock();
        var links = new DriverEvidenceReadLinks(new EphemeralDataProtectionProvider(), clock);
        var url = links.CreateUrl(Key)!;
        var key = Key;
        if (invalidity == "expired") clock.Now += TimeSpan.FromMinutes(16);
        if (invalidity == "tampered") url += "changed";
        if (invalidity == "different-key") key = "uploads/drivers/issue-evidence/another.webp";
        var (controller, storage, db, evidenceScans, staffAccess) = BuildReader(links, url);
        await using (db)
        {
            db.UploadFileRecords.Add(new UploadFileRecord
            {
                StorageKey = key,
                FileName = "evidence.webp",
                ContentType = "image/webp",
                SizeBytes = ValidWebp.Length,
                Scope = "drivers",
                Folder = "issue-evidence",
                UploadedByUserId = Guid.NewGuid(),
                UploadedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            (await controller.Read(key, storage, db, evidenceScans, staffAccess, default)).Should().BeOfType<UnauthorizedResult>();
            await storage.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData("https://external.example/api/v1/uploads/content?key=uploads%2Fdrivers%2Fissue-evidence%2Fevidence.webp")]
    [InlineData("//external.example/api/v1/uploads/content?key=uploads%2Fdrivers%2Fissue-evidence%2Fevidence.webp")]
    [InlineData("uploads/drivers/issue-evidence/../profile/image.jpg")]
    [InlineData("uploads/drivers/issue-evidence/%2e%2e/profile/image.jpg")]
    [InlineData("/api/v1/uploads/content?key=uploads%2Fdrivers%2Fissue-evidence%2Fevidence.webp&key=another")]
    [InlineData("/api/v1/uploads/content?key=uploads%2Frestaurant%2Fmeals%2Fimage.jpg")]
    public void DoesNotSignUnsafeOrPublicLocations(string value)
    {
        var links = new DriverEvidenceReadLinks(new EphemeralDataProtectionProvider(), new Clock());
        links.CreateUrl(value).Should().Be(value);
    }

    [Fact]
    public void FilterSignsBothAudiencesUploadAndMutationWithoutChangingPublicImages()
    {
        var links = new DriverEvidenceReadLinks(new EphemeralDataProtectionProvider(), new Clock());
        var url = CatalogImageStorage.ToContentUrl(Key)!;
        var driver = new DriverSupportIssueDetailDto(Guid.NewGuid(), "Other", "Other", "Issue", "Description", "AwaitingReply", "Submitted", "Submitted", DateTime.UtcNow,
            null, null, null, null, null, [], [url], null, null, false, null);
        var signedDriver = (DriverSupportIssueDetailDto)ApplyFilter(driver, links).Result.Value!;
        AssertValidUrl(signedDriver.EvidenceUrls.Single(), links);

        var manager = new SupportProblemDetailsDto(Guid.NewGuid(), "Issue", "Other", "Other", null, DateTime.UtcNow, "", null!, null!, "Description",
            [new SupportEvidencePhotoDto(Guid.NewGuid(), url, url, DateTime.UtcNow)], null!, "Submitted", "Submitted");
        var signedManager = (SupportProblemDetailsDto)ApplyFilter(manager, links).Result.Value!;
        AssertValidUrl(signedManager.EvidencePhotos.Single().Url!, links);
        AssertValidUrl(signedManager.EvidencePhotos.Single().ThumbnailUrl!, links);

        var upload = new UploadedFileDto(Key, "evidence.webp", "image/webp", 10, null, url, "issue-evidence", "drivers");
        AssertValidUrl(((UploadedFileDto)ApplyFilter(upload, links).Result.Value!).ReadUrl, links);
        var publicUpload = upload with { StorageKey = "uploads/restaurant/meals/image.jpg", ReadUrl = "/api/v1/uploads/content?key=uploads%2Frestaurant%2Fmeals%2Fimage.jpg" };
        ((UploadedFileDto)ApplyFilter(publicUpload, links).Result.Value!).Should().Be(publicUpload);
    }

    private static void AssertValidUrl(string url, DriverEvidenceReadLinks links)
        => links.Validate(Key, QueryHelpers.ParseQuery(new Uri("https://api.local" + url).Query)["readToken"].ToString()).Should().BeTrue();

    private static (ObjectResult Result, HttpContext Http) ApplyFilter(object dto, DriverEvidenceReadLinks links, bool authenticated = true, int status = 200)
    {
        var http = new DefaultHttpContext();
        if (authenticated) http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test"));
        var result = new ObjectResult(dto) { StatusCode = status };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var context = new ResultExecutingContext(action, [], result, new object());
        new DriverEvidenceReadLinksFilter(links).OnResultExecuting(context);
        return (result, http);
    }

    private static (UploadsController Controller, IFileStorage Storage, AppDbContext Db, SupportEvidenceScanService EvidenceScans, DriverEvidenceAccessService StaffAccess) BuildReader(DriverEvidenceReadLinks links, string url)
    {
        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new MemoryStream(ValidWebp));
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IDomainEventDispatcher>());
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(links).BuildServiceProvider() };
        http.Request.QueryString = new QueryString(new Uri("https://api.local" + url).Query);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);

        var scanner = Substitute.For<IEvidenceMalwareScanner>();
        scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(new EvidenceScanResult(EvidenceScanVerdict.Clean, "clamd", FailureCode: "clean"));
        var receipts = Substitute.For<ISupportEvidenceScanReceiptStore>();
        receipts.GetOrScanAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<Func<CancellationToken, Task<EvidenceScanResult>>>(), Arg.Any<CancellationToken>())
            .Returns(new EvidenceScanResult(EvidenceScanVerdict.Clean, "clamd", FailureCode: "clean"));
        var evidenceScans = new SupportEvidenceScanService(storage, scanner, receipts);
        var staffAccess = new DriverEvidenceAccessService(db, currentUser);

        return (new UploadsController { ControllerContext = new ControllerContext { HttpContext = http } }, storage, db, evidenceScans, staffAccess);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
