using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MealMate.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MealMate.IntegrationTests.Infrastructure;

public sealed class LocalFileStorageEncryptionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"mealmate-storage-tests-{Guid.NewGuid():N}");
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Empty_local_root_uses_the_safe_temporary_fallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:LocalRoot"] = "   ",
                ["Storage:DocumentEncryptionKey"] = Convert.ToBase64String(_key),
            })
            .Build();

        var act = () =>
            new LocalFileStorage(configuration, NullLogger<LocalFileStorage>.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Document_is_encrypted_at_rest_and_round_trips_with_plaintext_metadata()
    {
        var storage = CreateStorage();
        var plaintext = Encoding.UTF8.GetBytes("sensitive-owner-national-id-123456789");

        var stored = await storage.SaveAsync(
            new MemoryStream(plaintext),
            "identity.pdf",
            "application/pdf",
            "documents/Registration/11111111-1111-1111-1111-111111111111");

        var raw = await File.ReadAllBytesAsync(Path.Combine(
            _root,
            stored.StorageKey.Replace('/', Path.DirectorySeparatorChar)));
        raw.Should().NotContainInOrder(plaintext);
        stored.SizeBytes.Should().Be(plaintext.Length);
        stored.Checksum.Should().Be(Convert.ToHexString(SHA256.HashData(plaintext)));

        await using var opened = await storage.OpenReadAsync(stored.StorageKey);
        using var roundTrip = new MemoryStream();
        await opened.CopyToAsync(roundTrip);
        roundTrip.ToArray().Should().Equal(plaintext);

        var getUrl = async () =>
            await storage.GetReadUrlAsync(stored.StorageKey, TimeSpan.FromMinutes(1));
        await getUrl.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Tampered_document_fails_authentication()
    {
        var storage = CreateStorage();
        var stored = await storage.SaveAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("sensitive document")),
            "document.pdf",
            "application/pdf",
            "documents/Registration/22222222-2222-2222-2222-222222222222");
        var fullPath = Path.Combine(
            _root,
            stored.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        var raw = await File.ReadAllBytesAsync(fullPath);
        raw[^1] ^= 0x01;
        await File.WriteAllBytesAsync(fullPath, raw);

        var act = async () => await storage.OpenReadAsync(stored.StorageKey);

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Non_document_file_remains_plaintext()
    {
        var storage = CreateStorage();
        var plaintext = Encoding.UTF8.GetBytes("public asset");

        var stored = await storage.SaveAsync(
            new MemoryStream(plaintext),
            "asset.txt",
            "text/plain",
            "public/assets");
        var raw = await File.ReadAllBytesAsync(Path.Combine(
            _root,
            stored.StorageKey.Replace('/', Path.DirectorySeparatorChar)));

        raw.Should().Equal(plaintext);
        var readUrl = await storage.GetReadUrlAsync(stored.StorageKey, TimeSpan.FromMinutes(1));
        readUrl.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Legacy_plaintext_document_can_still_be_read()
    {
        var storage = CreateStorage();
        const string storageKey = "documents/Registration/legacy/document.pdf";
        var fullPath = Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var plaintext = Encoding.UTF8.GetBytes("legacy plaintext document");
        await File.WriteAllBytesAsync(fullPath, plaintext);

        await using var opened = await storage.OpenReadAsync(storageKey);
        using var contents = new MemoryStream();
        await opened.CopyToAsync(contents);

        contents.ToArray().Should().Equal(plaintext);
    }

    [Fact]
    public async Task Protected_driver_evidence_is_encrypted_and_never_returns_a_direct_url()
    {
        var storage = CreateStorage();
        var plaintext = Encoding.UTF8.GetBytes("private driver evidence");
        var stored = await storage.SaveAsync(new MemoryStream(plaintext), "delivery photo.png", "image/png", "uploads/drivers/delivery");
        var raw = await File.ReadAllBytesAsync(Path.Combine(_root, stored.StorageKey.Replace('/', Path.DirectorySeparatorChar)));
        raw.Should().NotContainInOrder(plaintext);
        await using var opened = await storage.OpenReadAsync(stored.StorageKey);
        using var roundTrip = new MemoryStream();
        await opened.CopyToAsync(roundTrip);
        roundTrip.ToArray().Should().Equal(plaintext);
        var directUrl = async () => await storage.GetReadUrlAsync(stored.StorageKey, TimeSpan.FromMinutes(1));
        await directUrl.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Protected_legacy_plaintext_cannot_return_a_direct_url_and_oversized_files_are_bounded()
    {
        var storage = CreateStorage();
        const string key = "uploads/drivers/delivery/legacy.png";
        var fullPath = Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, Encoding.UTF8.GetBytes("legacy bytes"));
        var directUrl = async () => await storage.GetReadUrlAsync(key, TimeSpan.FromMinutes(1));
        await directUrl.Should().ThrowAsync<InvalidOperationException>();

        using (var oversized = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
            oversized.SetLength(25L * 1024 * 1024 + 34);
        var open = async () => await storage.OpenReadAsync(key);
        await open.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task Local_path_containment_rejects_sibling_prefix_traversal()
    {
        var storage = CreateStorage();
        var open = async () => await storage.OpenReadAsync(".." + Path.DirectorySeparatorChar + Path.GetFileName(_root) + "-outside/secret.bin");
        await open.Should().ThrowAsync<InvalidOperationException>();
    }

    private LocalFileStorage CreateStorage()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:LocalRoot"] = _root,
                ["Storage:DocumentEncryptionKey"] = Convert.ToBase64String(_key),
            })
            .Build();
        return new LocalFileStorage(configuration, NullLogger<LocalFileStorage>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
