using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Features.DriverDelivery.DriverApp;
using MealMate.Application.Features.DriverDelivery.Dispatcher;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using MediatR;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed record OperationalSupportIssueDto(
    Guid Id, string Category, string Title, string Description, string Status,
    Guid? RestaurantId, Guid? SubscriptionDayId, Guid? OrderId, Guid? DriverProfileId,
    DateTime SubmittedAtUtc, bool IsEscalated, string? EscalationReason, DateTime? EscalatedAtUtc,
    IReadOnlyList<OperationalSupportEvidenceDto> Evidence);

public sealed record OperationalSupportEvidenceDto(Guid Id, string ReadPath);
public sealed record OperationalSupportEvidenceFile(byte[] Bytes, string FileName, string ContentType);

public sealed record OperationalSupportIssuePageDto(
    IReadOnlyList<OperationalSupportIssueDto> Items, int PageNumber, int PageSize, int TotalCount);

public sealed record EscalateOperationalIssueRequest(string Reason, string IdempotencyKey);
public sealed record OperationalSupportProgressRequest(string Stage, string? Notes = null);
public sealed record OperationalSupportResolutionRequest(string? ResolutionNotes);

public sealed class DriverOperationalSupportService(
    IAppDbContext db,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IApplicationTransactionExecutor transactions,
    IApplicationLockManager locks,
    IFileStorage storage,
    MealMate.Application.Common.Storage.SupportEvidenceScanService evidenceScans)
{
    private const string EscalationAction = "UnifiedSupportOperationalIssueEscalated";
    private readonly SupportCaseAccess _access = new(db, currentUser);
    private readonly DriverComplaintCaseAdapter _adapter = new(db, currentUser, new SupportCaseAccess(db, currentUser));

    public async Task<Result<OperationalSupportIssuePageDto>> ListAsync(int pageNumber, int pageSize, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null) return Error.Unauthorized(SupportCaseProblemCodes.AuthenticationRequired);
        if (!HasSupportedRole()) return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
        if (pageNumber < 1 || pageSize is < 1 or > 100 || (long)pageNumber * pageSize > int.MaxValue)
            return Error.Validation(SupportCaseProblemCodes.InvalidRequest);
        if (currentUser.UserType == UserType.Admin && !await _access.HasPersistedPermissionAsync(
                MealMate.Application.Common.Security.PermissionCodes.SupportView, ct))
            return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
        var query = await _adapter.QueryVisibleAsync(ct);
        var total = await query.CountAsync(ct);
        var rows = await query.Include(c => c.EvidencePhotos).OrderByDescending(c => db.AuditLogs.Any(a => a.EntityType == nameof(Complaint)
                && a.EntityId == c.Id && a.Action == EscalationAction))
            .ThenByDescending(c => c.SubmittedAtUtc).ThenBy(c => c.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var issueIds = rows.Select(x => x.Id).ToArray();
        var audits = issueIds.Length == 0 ? [] : await db.AuditLogs.AsNoTracking().Where(x => x.EntityType == nameof(Complaint)
            && issueIds.Contains(x.EntityId!.Value) && x.Action == EscalationAction)
            .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).ToListAsync(ct);
        var items = new List<OperationalSupportIssueDto>(rows.Count);
        foreach (var row in rows)
        {
            var audit = audits.FirstOrDefault(x => x.EntityId == row.Id);
            items.Add(ToDto(row, audit));
        }
        return Result.Success(new OperationalSupportIssuePageDto(items, pageNumber, pageSize, total));
    }

    public async Task<Result<OperationalSupportIssueDto>> GetAsync(Guid issueId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null) return Error.Unauthorized(SupportCaseProblemCodes.AuthenticationRequired);
        if (!HasSupportedRole()) return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
        if (currentUser.UserType == UserType.Admin && !await _access.HasPersistedPermissionAsync(
                MealMate.Application.Common.Security.PermissionCodes.SupportView, ct))
            return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
        var query = await _adapter.QueryVisibleAsync(ct);
        var row = await query.Include(c => c.EvidencePhotos).SingleOrDefaultAsync(x => x.Id == issueId, ct);
        if (row is null) return Error.NotFound(SupportCaseProblemCodes.NotFound);
        return Result.Success(ToDto(row, await LatestEscalationAsync(row.Id, ct)));
    }

    public async Task<Result<OperationalSupportEvidenceFile>> ReadEvidenceAsync(Guid issueId, Guid evidenceId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            return Error.Unauthorized(SupportCaseProblemCodes.AuthenticationRequired);
        if (!HasSupportedRole()) return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
        if (currentUser.UserType == UserType.Admin && !await _access.HasPersistedPermissionAsync(
                MealMate.Application.Common.Security.PermissionCodes.SupportView, ct))
            return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
        var query = await _adapter.QueryVisibleAsync(ct);
        var issue = await query.Include(x => x.EvidencePhotos).SingleOrDefaultAsync(x => x.Id == issueId, ct);
        var photo = issue?.EvidencePhotos.SingleOrDefault(x => x.Id == evidenceId);
        if (photo is null || !IsSafeEvidenceKey(photo.StoragePath)) return Error.NotFound(SupportCaseProblemCodes.NotFound);
        var upload = await db.UploadFileRecords.AsNoTracking().SingleOrDefaultAsync(x => x.StorageKey == photo.StoragePath, ct);
        var driverUserId = await db.DriverProfiles.AsNoTracking().Where(x => x.Id == issue!.DriverProfileId)
            .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
        const long maxBytes = 25 * 1024 * 1024;
        if (upload is null || driverUserId is null || upload.UploadedByUserId != driverUserId
            || upload.Scope != "drivers" || upload.Folder is not ("issue-evidence" or "delivery")
            || upload.SizeBytes <= 0 || upload.SizeBytes > maxBytes
            || upload.ContentType is not ("image/png" or "image/jpeg" or "image/webp"))
            return Error.NotFound(SupportCaseProblemCodes.NotFound);
        try
        {
            var scan = await evidenceScans.ReadCleanAsync(upload, ct);
            var bytes = scan.Bytes;
            if (bytes is null || !SupportCaseService.HasValidSignature(bytes, upload.ContentType))
                return Error.NotFound(SupportCaseProblemCodes.NotFound);
            var extension = upload.ContentType == "image/png" ? ".png" : upload.ContentType == "image/jpeg" ? ".jpg" : ".webp";
            return Result.Success(new OperationalSupportEvidenceFile(bytes, $"{evidenceId:D}{extension}", upload.ContentType));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException)
        { return Error.NotFound(SupportCaseProblemCodes.NotFound); }
    }

    private static bool IsSafeEvidenceKey(string key)
    {
        if (key.Any(char.IsWhiteSpace) || key.Contains('\\') || key.Contains('?') || key.Contains('#')) return false;
        var path = key;
        if (Uri.TryCreate(key, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "ik.imagekit.io" || !uri.IsDefaultPort
                || !string.IsNullOrEmpty(uri.UserInfo)) return false;
            var start = key.IndexOf('/', key.IndexOf("://", StringComparison.Ordinal) + 3);
            if (start < 0) return false;
            path = key[(start + 1)..];
        }
        var parts = path.Split('/');
        if (parts.Any(x => x.Length == 0 || x is "." or ".." || x.Contains('%') || x.Any(char.IsControl))) return false;
        var offset = parts[0] == "uploads" ? 0 : 1;
        return parts.Length > offset + 3 && parts[offset] == "uploads" && parts[offset + 1] == "drivers"
            && parts[offset + 2] is "issue-evidence" or "delivery"
            && (offset == 0 || Uri.TryCreate(key, UriKind.Absolute, out _));
    }

    public async Task<Error?> AuthorizeOperationalMutationAsync(Guid issueId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            return Error.Unauthorized(SupportCaseProblemCodes.AuthenticationRequired);
        if (currentUser.UserType == UserType.Admin && !await _access.HasPersistedPermissionAsync(
                MealMate.Application.Common.Security.PermissionCodes.SupportManage, ct))
            return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
        if (currentUser.UserType is not (UserType.DeliveryManager or UserType.Admin))
            return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
        var query = await _adapter.QueryVisibleAsync(ct);
        return await query.AnyAsync(x => x.Id == issueId, ct) ? null : Error.NotFound(SupportCaseProblemCodes.NotFound);
    }

    public async Task<Result<OperationalSupportIssueDto>> EscalateAsync(Guid issueId, EscalateOperationalIssueRequest request, CancellationToken ct)
    {
        var actorId = currentUser.IsAuthenticated ? currentUser.UserId : null;
        if (actorId is null) return Error.Unauthorized(SupportCaseProblemCodes.AuthenticationRequired);
        if (!HasSupportedRole()) return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
        if (currentUser.UserType == UserType.Admin && !await _access.HasPersistedPermissionAsync(
                MealMate.Application.Common.Security.PermissionCodes.SupportManage, ct))
            return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000
            || string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 100)
            return Error.Validation(SupportCaseProblemCodes.InvalidRequest);

        var idempotencyKey = request.IdempotencyKey.Trim();
        var payload = new { IssueId = issueId, Reason = request.Reason.Trim() };
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
        var fingerprintJson = JsonSerializer.Serialize(fingerprint);
        return await transactions.ExecuteResultAsync(async token =>
        {
            await locks.LockSupportHandoverKeyAsync(actorId.Value, idempotencyKey, token);
            await locks.LockSupportComplaintAsync(issueId, token);
            var query = await _adapter.QueryVisibleAsync(token);
            var issue = await query.Include(c => c.EvidencePhotos).SingleOrDefaultAsync(x => x.Id == issueId, token);
            if (issue is null) return Error.NotFound(SupportCaseProblemCodes.NotFound);
            if (currentUser.UserType == UserType.Admin && !await _access.HasPersistedPermissionAsync(
                    MealMate.Application.Common.Security.PermissionCodes.SupportManage, token))
                return Error.Forbidden(SupportCaseProblemCodes.AccessDenied);

            var replay = await db.AuditLogs.SingleOrDefaultAsync(x => x.EntityType == nameof(Complaint)
                && x.Action == EscalationAction && x.ActorUserId == actorId
                && x.CorrelationId == idempotencyKey, token);
            if (replay is not null)
            {
                if (!string.Equals(replay.BeforeJson, fingerprintJson, StringComparison.Ordinal))
                    return Error.Conflict(SupportCaseProblemCodes.IdempotencyKeyReused);
                return Result.Success(ToDto(issue, replay));
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var audit = new AuditLog
            {
                ActorUserId = actorId,
                Action = EscalationAction,
                EntityType = nameof(Complaint),
                EntityId = issueId,
                BeforeJson = fingerprintJson,
                AfterJson = JsonSerializer.Serialize(new { Escalated = true }),
                Reason = request.Reason.Trim(),
                CorrelationId = idempotencyKey,
                SourceChannel = currentUser.UserType.ToString(),
                OccurredAtUtc = now,
            };
            db.AuditLogs.Add(audit);
            await unitOfWork.SaveChangesAsync(token);
            return Result.Success(ToDto(issue, audit));
        }, ct);
    }

    private Task<AuditLog?> LatestEscalationAsync(Guid issueId, CancellationToken ct)
        => db.AuditLogs.AsNoTracking().Where(x => x.EntityType == nameof(Complaint) && x.EntityId == issueId
            && x.Action == EscalationAction).OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);

    private bool HasSupportedRole() => currentUser.UserType is UserType.Driver or UserType.Restaurant or UserType.DeliveryManager
        || currentUser.UserType == UserType.Admin && (_access.IsSupportAdmin || _access.IsSuperAdmin);

    private static OperationalSupportIssueDto ToDto(Complaint issue, AuditLog? escalation)
        => new(issue.Id, issue.Category, issue.Title, issue.Description, issue.Status.ToString(), issue.RestaurantId,
            issue.SubscriptionDayId, issue.OrderId, issue.DriverProfileId, issue.SubmittedAtUtc,
            escalation is not null, escalation?.Reason, escalation?.OccurredAtUtc,
            issue.EvidencePhotos.OrderBy(x => x.UploadedAtUtc).ThenBy(x => x.Id)
                .Select(x => new OperationalSupportEvidenceDto(x.Id, $"/api/v1/support/operational-issues/{issue.Id:D}/evidence/{x.Id:D}")).ToArray());
}

public sealed record ListOperationalSupportIssuesQuery(int PageNumber = 1, int PageSize = 20)
    : IQuery<OperationalSupportIssuePageDto>;
public sealed record GetOperationalSupportIssueQuery(Guid IssueId) : IQuery<OperationalSupportIssueDto>;
public sealed record EscalateOperationalSupportIssueCommand(Guid IssueId, EscalateOperationalIssueRequest Request)
    : ISelfPersistingCommand, ICommand<OperationalSupportIssueDto>;
public sealed record UpdateOperationalSupportProgressCommand(Guid IssueId, string Stage, string? Notes = null)
    : ICommand<DriverSupportProgressDto>;
public sealed record ResolveOperationalSupportIssueCommand(Guid IssueId, string? ResolutionNotes)
    : ICommand<ResolveSupportIssueResponseDto>;

public sealed class DriverOperationalSupportHandlers(DriverOperationalSupportService service, ISender sender)
    : IQueryHandler<ListOperationalSupportIssuesQuery, OperationalSupportIssuePageDto>,
      IQueryHandler<GetOperationalSupportIssueQuery, OperationalSupportIssueDto>,
      ICommandHandler<EscalateOperationalSupportIssueCommand, OperationalSupportIssueDto>,
      ICommandHandler<UpdateOperationalSupportProgressCommand, DriverSupportProgressDto>,
      ICommandHandler<ResolveOperationalSupportIssueCommand, ResolveSupportIssueResponseDto>
{
    public Task<Result<OperationalSupportIssuePageDto>> Handle(ListOperationalSupportIssuesQuery query, CancellationToken ct)
        => service.ListAsync(query.PageNumber, query.PageSize, ct);

    public Task<Result<OperationalSupportIssueDto>> Handle(GetOperationalSupportIssueQuery query, CancellationToken ct)
        => service.GetAsync(query.IssueId, ct);

    public Task<Result<OperationalSupportIssueDto>> Handle(EscalateOperationalSupportIssueCommand command, CancellationToken ct)
        => service.EscalateAsync(command.IssueId, command.Request, ct);

    public async Task<Result<DriverSupportProgressDto>> Handle(UpdateOperationalSupportProgressCommand command, CancellationToken ct)
    {
        var error = await service.AuthorizeOperationalMutationAsync(command.IssueId, ct);
        return error is null ? await sender.Send(new UpdateDriverSupportProgressCommand(command.IssueId, command.Stage, command.Notes), ct) : error;
    }

    public async Task<Result<ResolveSupportIssueResponseDto>> Handle(ResolveOperationalSupportIssueCommand command, CancellationToken ct)
    {
        var error = await service.AuthorizeOperationalMutationAsync(command.IssueId, ct);
        return error is null ? await sender.Send(new ResolveSupportIssueCommand(command.IssueId, command.ResolutionNotes), ct) : error;
    }
}
