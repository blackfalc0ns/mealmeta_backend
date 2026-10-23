using System.Security.Cryptography;
using MealMate.Application.Common.Abstractions;
using MealMate.Domain.Common;

namespace MealMate.Application.Common.Storage;

public sealed record EvidenceReceiptResolution(string Sha256, EvidenceScanResult Scan);

public interface ISupportEvidenceScanReceiptStore
{
    Task<EvidenceScanResult> GetOrScanAsync(Guid uploadFileRecordId, string sha256, long sizeBytes,
        string contentType, int validationVersion, Func<CancellationToken, Task<EvidenceScanResult>> scan,
        CancellationToken cancellationToken);
    Task AppendAsync(Guid uploadFileRecordId, string sha256, long sizeBytes, string contentType,
        int validationVersion, EvidenceScanResult result, CancellationToken cancellationToken);
}

public sealed class SupportEvidenceScanService(
    IFileStorage storage,
    IEvidenceMalwareScanner scanner,
    ISupportEvidenceScanReceiptStore receipts)
{
    public const int ValidationVersion = 1;
    public const long MaxBytes = 25 * 1024 * 1024;

    public async Task<(byte[]? Bytes, string Sha256, EvidenceScanResult Scan)> ReadCleanAsync(
        UploadFileRecord record, CancellationToken cancellationToken)
    {
        try { return await ReadCleanCoreAsync(record, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or CryptographicException)
        {
            return (null, "", new(EvidenceScanVerdict.Unavailable, "storage", "Evidence storage is unavailable.", "storage_unavailable"));
        }
    }

    private async Task<(byte[]? Bytes, string Sha256, EvidenceScanResult Scan)> ReadCleanCoreAsync(
        UploadFileRecord record, CancellationToken cancellationToken)
    {
        if (record.SupportPayloadDeletedAtUtc is not null)
            return (null, "", new(EvidenceScanVerdict.Rejected, "storage", "Evidence payload was already removed.", "payload_deleted"));
        if (!IsCanonicalProtectedKey(record.StorageKey))
            return (null, "", new(EvidenceScanVerdict.Rejected, "validation", "Evidence storage key is invalid.", "storage_key_invalid"));
        if (record.SizeBytes is <= 0 or > MaxBytes)
            return (null, "", new(EvidenceScanVerdict.Rejected, "validation", "Evidence size is invalid."));

        await using var source = await storage.OpenReadAsync(record.StorageKey, cancellationToken);
        await using var buffer = new MemoryStream(checked((int)record.SizeBytes));
        var chunk = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaxBytes)
                return (null, "", new(EvidenceScanVerdict.Rejected, "validation", "Evidence size is invalid."));
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }
        if (buffer.Length != record.SizeBytes)
            return (null, "", new(EvidenceScanVerdict.Rejected, "validation", "Evidence size does not match its upload record."));

        var bytes = buffer.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        if (!HasAllowedSignature(bytes, record.ContentType))
        {
            var invalid = new EvidenceScanResult(EvidenceScanVerdict.Rejected, "validation",
                "Evidence format does not match its declared content type.", "content_invalid");
            await receipts.AppendAsync(record.Id, sha256, bytes.LongLength, record.ContentType,
                ValidationVersion, invalid, cancellationToken);
            return (null, sha256, invalid);
        }
        await using var scanSource = new MemoryStream(bytes, writable: false);
        var result = await receipts.GetOrScanAsync(record.Id, sha256, bytes.LongLength, record.ContentType,
            ValidationVersion, ct => scanner.ScanAsync(scanSource, ct), cancellationToken);
        return result.Verdict == EvidenceScanVerdict.Clean
            ? (bytes, sha256, result)
            : (null, sha256, result);
    }

    public Task RecordScanAsync(UploadFileRecord record, string sha256, EvidenceScanResult result,
        CancellationToken cancellationToken)
        => receipts.AppendAsync(record.Id, sha256, record.SizeBytes, record.ContentType,
            ValidationVersion, result, cancellationToken);

    private static bool HasAllowedSignature(ReadOnlySpan<byte> bytes, string contentType)
        => contentType switch
        {
            "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" or "image/jpg" => bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
            "application/pdf" => bytes.Length >= 5 && bytes[..5].SequenceEqual("%PDF-"u8),
            _ => false,
        };

    private static bool IsCanonicalProtectedKey(string key)
    {
        if (!ProtectedEvidenceStorage.IsProtectedKey(key) || key.Any(char.IsControl)
            || key.Contains('\\') || key.Contains('?') || key.Contains('#') || key.Contains('%')
            || Uri.TryCreate(key, UriKind.Absolute, out _)) return false;
        return key.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."));
    }
}
