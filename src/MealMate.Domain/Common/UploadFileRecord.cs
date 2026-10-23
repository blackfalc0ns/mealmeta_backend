namespace MealMate.Domain.Common;

/// <summary>
/// Ownership metadata for every file stored through the generic upload endpoint.
/// Written at upload time by <c>UploadFileCommandHandler</c> so later consumers
/// (e.g. delivery proof binding) can verify that the storage key really belongs
/// to the uploading user — a non-empty key string or the folder name alone is
/// never proof of ownership. Legacy files uploaded before this table exists have
/// no record and are rejected by ownership-checked consumers.
/// </summary>
public sealed class UploadFileRecord : BaseEntity
{
    /// <summary>Storage key exactly as returned by IFileStorage (unique).</summary>
    public string StorageKey { get; set; } = default!;

    public string FileName { get; set; } = default!;

    public string ContentType { get; set; } = default!;

    public long SizeBytes { get; set; }

    /// <summary>Scope segment as requested by the caller (e.g. drivers).</summary>
    public string? Scope { get; set; }

    /// <summary>Folder segment as requested by the caller (e.g. delivery).</summary>
    public string? Folder { get; set; }

    /// <summary>Identity user id of the authenticated uploader.</summary>
    public Guid? UploadedByUserId { get; set; }

    public DateTime UploadedAtUtc { get; set; }

    /// <summary>Last UTC attempt to remove an unreferenced support-evidence payload.</summary>
    public DateTime? SupportCleanupLastAttemptAtUtc { get; set; }

    /// <summary>UTC time when the support-evidence payload was confirmed deleted; the upload row remains for receipt audit history.</summary>
    public DateTime? SupportPayloadDeletedAtUtc { get; set; }
}
