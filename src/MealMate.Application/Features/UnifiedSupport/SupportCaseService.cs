using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.ComplaintsSupport;
using MealMate.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.UnifiedSupport;

public sealed partial class SupportCaseService
{
    private readonly IAppDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly IApplicationTransactionExecutor _transactions;
    private readonly IApplicationLockManager _locks;
    private readonly IFileStorage _storage;
    private readonly SupportCaseAccess _access;
    private readonly MealMate.Application.Common.Storage.SupportEvidenceScanService _evidenceScans;

    public SupportCaseService(IAppDbContext db, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider timeProvider,
        IApplicationTransactionExecutor transactions, IApplicationLockManager locks, IFileStorage storage,
        MealMate.Application.Common.Storage.SupportEvidenceScanService evidenceScans)
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
        _transactions = transactions;
        _locks = locks;
        _storage = storage;
        _evidenceScans = evidenceScans;
        _access = new SupportCaseAccess(db, currentUser);
    }

    private Guid? ActorId => _currentUser.IsAuthenticated ? _currentUser.UserId : null;
    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;
    private static Error Unauthorized() => Error.Unauthorized(SupportCaseProblemCodes.AuthenticationRequired);
    private static Error Forbidden() => Error.Forbidden(SupportCaseProblemCodes.AccessDenied);
    private static Error NotFound() => Error.NotFound(SupportCaseProblemCodes.NotFound);
    private static Error Invalid() => Error.Validation(SupportCaseProblemCodes.InvalidRequest);
    private static Error Conflict(string code) => Error.Conflict(code);

    private static bool ValidKey(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private async Task<SupportCaseEvent?> FindReplayAsync(Guid actorId, string key, string payloadHash, CancellationToken ct)
    {
        var existing = await _db.SupportCaseEvents.SingleOrDefaultAsync(x => x.ActorUserId == actorId && x.IdempotencyKey == key, ct);
        if (existing is not null && !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            throw new SupportCaseIdempotencyException();
        return existing;
    }

    private Task<uint> VersionOfAsync(Guid caseId, CancellationToken ct) => _db.SupportCases
        .Where(x => x.Id == caseId).Select(x => EF.Property<uint>(x, "xmin")).SingleAsync(ct);

    private async Task<bool> HasVersionAsync(SupportCase supportCase, uint expectedVersion, CancellationToken ct)
        => await VersionOfAsync(supportCase.Id, ct) == expectedVersion;

    private async Task<SupportCaseDto> ProjectAsync(SupportCase supportCase, CancellationToken ct, uint? currentVersion = null)
    {
        var actorId = ActorId;
        var persistedOperator = actorId.HasValue
            ? (await _db.GetActiveSupportOperatorCandidatesAsync(ct)).SingleOrDefault(x => x.UserId == actorId.Value)
            : null;
        var persistedSuperAdmin = persistedOperator?.Roles.Contains(MealMate.Application.Common.Security.AppRoles.SuperAdmin) == true;
        var persistedSupportAdmin = persistedSuperAdmin || persistedOperator is not null
            && persistedOperator.UserType == MealMate.Domain.Enums.UserType.Admin
            && persistedOperator.PermissionCodes.Contains(MealMate.Application.Common.Security.PermissionCodes.SupportView);
        var restaurantRequester = actorId.HasValue && _currentUser.UserType == MealMate.Domain.Enums.UserType.Restaurant
            && supportCase.Channel == SupportCaseChannel.Restaurant
            && supportCase.RestaurantId == _currentUser.RestaurantId;
        var caseRequester = actorId.HasValue && (supportCase.RequesterUserId == actorId.Value || restaurantRequester);
        var partyTaskOnly = actorId.HasValue && !caseRequester && !persistedSupportAdmin;
        var currentDeliveryManager = partyTaskOnly && _currentUser.UserType == MealMate.Domain.Enums.UserType.DeliveryManager
            && _currentUser.RestaurantId is { } managerRestaurantId
            && await _access.IsCurrentDeliveryManagerForRestaurantAsync(managerRestaurantId, ct);
        var canReadInternal = persistedSuperAdmin || _access.CanReadInternal && persistedOperator?.PermissionCodes.Contains(
            MealMate.Application.Common.Security.PermissionCodes.SupportInternal) == true;
        var partyTasks = partyTaskOnly
                ? await _db.SupportCaseTasks.Where(t => t.SupportCaseId == supportCase.Id && t.Status != SupportCaseTaskStatus.Cancelled
                && ((currentDeliveryManager && t.RecipientType == SupportCaseTaskRecipientType.DeliveryManager && t.RecipientUserId == actorId
                        && t.RestaurantId == _currentUser.RestaurantId)
                    || (_currentUser.UserType == MealMate.Domain.Enums.UserType.Restaurant
                        && t.RecipientType == SupportCaseTaskRecipientType.Restaurant && t.RestaurantId != null && t.RestaurantId == _currentUser.RestaurantId)))
                .OrderByDescending(t => t.CreatedAtUtc).ToListAsync(ct)
            : await _db.SupportCaseTasks.Where(t => t.SupportCaseId == supportCase.Id).OrderByDescending(t => t.CreatedAtUtc).ToListAsync(ct);
        IReadOnlyList<SupportCaseTask> responseTasks = caseRequester
            ? []
            : partyTasks;
        var visibleTaskIds = partyTaskOnly ? partyTasks.Select(t => t.Id).ToArray() : null;
        var messagesQuery = _db.SupportCaseMessages.Where(m => m.SupportCaseId == supportCase.Id);
        if (!canReadInternal) messagesQuery = messagesQuery.Where(m => m.Visibility == SupportCaseMessageVisibility.Participant);
        if (partyTaskOnly)
            messagesQuery = visibleTaskIds!.Length == 0
                ? messagesQuery.Where(_ => false)
                : messagesQuery.Where(m => m.SupportCaseTaskId != null && visibleTaskIds.Contains(m.SupportCaseTaskId.Value));
        else if (caseRequester)
            messagesQuery = messagesQuery.Where(m => m.SupportCaseTaskId == null);
        var messageRows = await messagesQuery.OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id).ToListAsync(ct);
        var attachmentKeys = messageRows.SelectMany(m => m.AttachmentKeys).Distinct().ToArray();
        Dictionary<string, UploadFileRecord> uploads = attachmentKeys.Length == 0 ? new(StringComparer.Ordinal) : await _db.UploadFileRecords.AsNoTracking()
            .Where(upload => attachmentKeys.Contains(upload.StorageKey))
            .ToDictionaryAsync(upload => upload.StorageKey, ct);
        var messages = messageRows.Select(message => new SupportCaseMessageDto(
            message.Id, message.AuthorUserId, message.Visibility.ToString(), message.Body, message.CreatedAtUtc, message.SupportCaseTaskId,
            message.AttachmentKeys.Select((key, index) => uploads.TryGetValue(key, out var upload)
                ? new SupportCaseAttachmentDto(upload.FileName, upload.ContentType, upload.SizeBytes,
                    $"/api/v1/support/cases/{supportCase.Id:D}/messages/{message.Id:D}/attachments/{index}")
                : null).Where(item => item is not null).Cast<SupportCaseAttachmentDto>().ToArray())).ToList();
        List<SupportCaseLinkDto> links = !persistedSupportAdmin
            ? []
            : await _db.SupportCaseLinks.Where(x => x.SupportCaseId == supportCase.Id).OrderBy(x => x.CreatedAtUtc)
                .Select(x => new SupportCaseLinkDto(x.Relation.ToString(), x.ResourceType, x.ResourceId, x.CreatedAtUtc)).ToListAsync(ct);
        Timing.SupportCaseTimingCycleDto? activeCycleDto = null;
        if (supportCase.ActiveTimingCycleId.HasValue)
        {
            var activeCycle = await _db.SupportCaseTimingCycles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == supportCase.ActiveTimingCycleId.Value, ct);
            if (activeCycle is not null)
            {
                activeCycleDto = Timing.SupportTimingPolicyService.MapCycleToDto(activeCycle);
                if (!canReadInternal)
                    activeCycleDto = activeCycleDto with { EscalationReason = null, Notes = null };
            }
        }
        return new SupportCaseDto(
            supportCase.Id, supportCase.Channel.ToString(), supportCase.Kind.ToString(), supportCase.Status.ToString(),
            supportCase.OwnerTeam.ToString(), supportCase.Priority?.ToString(), partyTaskOnly ? null : supportCase.Category,
            partyTaskOnly ? partyTasks.FirstOrDefault()?.Subject : supportCase.Subject,
            partyTaskOnly ? null : supportCase.Body, partyTaskOnly ? null : supportCase.RequesterUserId,
            partyTaskOnly ? null : supportCase.RestaurantId, partyTaskOnly ? null : supportCase.AssignedToUserId,
            partyTaskOnly ? null : supportCase.TriageStatus.ToString(),
            persistedSupportAdmin ? supportCase.TriageCountryId : null,
            persistedSupportAdmin ? supportCase.TriageRegionId : null,
            persistedSupportAdmin ? supportCase.PendingTransferToUserId : null,
            canReadInternal ? supportCase.PendingTransferReason : null,
            currentVersion ?? await VersionOfAsync(supportCase.Id, ct), supportCase.CreatedAtUtc,
            supportCase.ModifiedAtUtc, messages,
            responseTasks.Select(t => new SupportCaseTaskDto(t.Id, t.Type.ToString(), t.Status.ToString(), t.Subject,
                t.Question, t.Result, partyTaskOnly ? null : t.RestaurantId, partyTaskOnly ? null : t.RecipientUserId,
                t.CreatedAtUtc, t.DueAtUtc, t.CompletedAtUtc)).ToArray(), links,
            partyTaskOnly ? null : supportCase.RelatedResourceType, partyTaskOnly ? null : supportCase.RelatedResourceId,
            (_access.IsSupportAdmin || _access.IsSuperAdmin) && await CanTakeOverAssignmentAsync(supportCase, ct),
            ActiveTimingCycle: activeCycleDto);
    }
}

internal sealed class SupportCaseIdempotencyException : Exception { }
