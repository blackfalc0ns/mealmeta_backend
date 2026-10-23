using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.DriverDelivery.DriverApp;

public sealed record AddDriverSupportIssuePhotosCommand(Guid IssueId, IReadOnlyList<string>? StorageKeys) : ICommand<DriverSupportIssueMutationDto>;

public sealed class AddDriverSupportIssuePhotosHandler : ICommandHandler<AddDriverSupportIssuePhotosCommand, DriverSupportIssueMutationDto>
{
    private readonly IAppDbContext db; private readonly ICurrentUser user; private readonly ILocalizedMessageProvider messages;
    private readonly IApplicationTransactionExecutor transactions; private readonly IApplicationLockManager locks; private readonly IFileStorage storage;
    private readonly MealMate.Application.Common.Storage.SupportEvidenceScanService evidenceScans;
    public AddDriverSupportIssuePhotosHandler(IAppDbContext db, ICurrentUser user, ILocalizedMessageProvider messages,
        IApplicationTransactionExecutor transactions, IApplicationLockManager locks, IFileStorage storage,
        MealMate.Application.Common.Storage.SupportEvidenceScanService evidenceScans)
        => (this.db, this.user, this.messages, this.transactions, this.locks, this.storage, this.evidenceScans) = (db, user, messages, transactions, locks, storage, evidenceScans);

    public async Task<Result<DriverSupportIssueMutationDto>> Handle(AddDriverSupportIssuePhotosCommand request, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserType != UserType.Driver || !user.UserId.HasValue)
            return Error.Forbidden("DRIVER_SCOPE_REQUIRED", messages.GetMessage("driver_support_issue.forbidden"));
        if (!(await db.GetActiveAccountUserIdsAsync(new[] { user.UserId.Value }, ct)).Contains(user.UserId.Value))
            return Error.Forbidden("DRIVER_ACCOUNT_INACTIVE", messages.GetMessage("driver.account_inactive"));
        if (request.StorageKeys is null || request.StorageKeys.Count == 0 || request.StorageKeys.Any(k => string.IsNullOrWhiteSpace(k) || k.Length > 500))
            return Error.Validation("DRIVER_SUPPORT_PHOTOS_INVALID", messages.GetMessage("driver_support_issue.photos_invalid"));
        return await transactions.ExecuteResultAsync(ct2 => AttachAsync(request, ct2), ct);
    }

    private async Task<Result<DriverSupportIssueMutationDto>> AttachAsync(AddDriverSupportIssuePhotosCommand request, CancellationToken ct)
    {
        var first = await db.Complaints.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.IssueId, ct);
        if (first is null) return NotFound();
        await locks.LockSupportComplaintAsync(first.Id, ct);
        var complaint = await db.Complaints.Include(x => x.EvidencePhotos).FirstOrDefaultAsync(x => x.Id == request.IssueId, ct);
        if (complaint is null || complaint.DriverProfileId is null || !DriverSupportIssueProjection.IsSupportedCategory(complaint.Category)
            || !await db.DriverProfiles.AnyAsync(d => d.Id == complaint.DriverProfileId && d.UserId == user.UserId && !d.IsDeleted, ct)) return NotFound();
        if (complaint.Status is ComplaintStatus.Resolved or ComplaintStatus.Closed or ComplaintStatus.Rejected)
            return Error.Conflict("DRIVER_SUPPORT_ISSUE_TERMINAL", messages.GetMessage("driver_support_issue.terminal"));
        var keys = request.StorageKeys!.Distinct(StringComparer.Ordinal).ToArray();
        var records = await db.UploadFileRecords.Where(x => keys.Contains(x.StorageKey) && x.UploadedByUserId == user.UserId).ToListAsync(ct);
        if (records.Count != keys.Length || records.Any(x => x.ContentType is not ("image/jpeg" or "image/png" or "image/webp")
            || !IsDriverEvidenceKey(x.StorageKey)))
            return Error.Validation("DRIVER_SUPPORT_PHOTO_UPLOAD_INVALID", messages.GetMessage("driver_support_issue.photo_upload_invalid"));
        foreach (var record in records)
        {
            if (!await storage.ExistsAsync(record.StorageKey, ct)) return Error.Validation("DRIVER_SUPPORT_PHOTO_MISSING", messages.GetMessage("driver_support_issue.photo_missing"));
            if ((await evidenceScans.ReadCleanAsync(record, ct)).Bytes is null)
                return Error.Validation("DRIVER_SUPPORT_PHOTO_UPLOAD_INVALID", messages.GetMessage("driver_support_issue.photo_upload_invalid"));
        }
        var existing = complaint.EvidencePhotos.Select(x => x.StoragePath).ToHashSet(StringComparer.Ordinal);
        var added = keys.Where(k => !existing.Contains(k)).ToArray();
        if (existing.Count + added.Length > 3) return Error.Validation("DRIVER_SUPPORT_PHOTO_LIMIT", messages.GetMessage("driver_support_issue.photo_limit"));
        if (added.Length > 0 && !complaint.RestaurantId.HasValue) return Error.Conflict("DRIVER_SUPPORT_ISSUE_INCOMPLETE", messages.GetMessage("driver_support_issue.incomplete"));
        var now = DateTime.UtcNow;
        var progressEvents = await db.AuditLogs.AsNoTracking().Where(a => a.EntityId == complaint.Id && a.Action == "DriverSupportProgressUpdated").ToListAsync(ct);
        var hasContactStage = progressEvents.Any(a => DriverSupportIssueProjection.StageFromAuditJson(a.AfterJson) == "ContactingCustomer");
        foreach (var key in added) { var record = records.Single(x => x.StorageKey == key); var photo = new ComplaintEvidencePhoto { ComplaintId = complaint.Id, StoragePath = key, ContentType = record.ContentType, UploadedAtUtc = now }; complaint.EvidencePhotos.Add(photo); db.Set<ComplaintEvidencePhoto>().Add(photo); }
        if (added.Length > 0)
        {
            db.AuditLogs.Add(new AuditLog { ActorUserId = user.UserId, Action = "DriverSupportPhotosAdded", EntityType = nameof(Complaint), EntityId = complaint.Id, Reason = $"Added {added.Length} evidence photo(s).", OccurredAtUtc = now, SourceChannel = "DriverApp" });
            db.OutboxMessages.Add(new MealMate.Domain.Common.OutboxMessage { EventType = "driver-support-issue-changed.v1", AggregateType = nameof(Complaint), AggregateId = complaint.Id.ToString("D"), DeduplicationKey = $"driver-support-issue:{complaint.Id:N}:photos:{string.Join("-", added.Select(k => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(k))).Substring(0, 8)))}", PayloadJson = JsonSerializer.Serialize(new { IssueId = complaint.Id, RestaurantId = complaint.RestaurantId ?? Guid.Empty, DriverProfileId = complaint.DriverProfileId, Category = complaint.Category, Event = "photos-added" }), CreatedAtUtc = now });
            await db.SaveChangesAsync(ct);
        }
        return Result.Success(new DriverSupportIssueMutationDto(complaint.Id, DriverSupportIssueProjection.Bucket(complaint.Status, hasContactStage), complaint.EvidencePhotos.Select(x => MealMate.Application.Common.Storage.CatalogImageStorage.ToContentUrl(x.StoragePath) ?? string.Empty).ToArray()));
    }
    // The owned UploadFileRecord is matched by the original key before validating its folder.
    private static bool IsDriverEvidenceKey(string key)
    {
        if (key.Any(char.IsWhiteSpace) || key.Contains('\\') || key.Contains('?') || key.Contains('#')
            || Uri.TryCreate(key, UriKind.Absolute, out _)) return false;
        var segments = key.Split('/');
        foreach (var segment in segments)
        {
            var decoded = Uri.UnescapeDataString(segment);
            if (decoded.Length == 0 || decoded is "." or ".." || decoded.Contains('/')
                || decoded.Contains('\\') || decoded.Contains('%') || decoded.Any(char.IsControl)) return false;
        }
        return segments.Length > 3 && segments[0] == "uploads"
            && segments[1] == "drivers"
            && segments[2] is "issue-evidence" or "delivery";
    }
    private Result<DriverSupportIssueMutationDto> NotFound() => Error.NotFound("DRIVER_SUPPORT_ISSUE_NOT_FOUND", messages.GetMessage("driver_support_issue.not_found"));
}
