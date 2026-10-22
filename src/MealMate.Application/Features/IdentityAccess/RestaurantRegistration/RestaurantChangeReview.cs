using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

public sealed record RestaurantChangeDecisionEvent(
    string Decision,
    string? Reason,
    Guid? ReviewedByUserId,
    DateTime ReviewedAtUtc,
    long ExpectedVersion,
    int PayloadVersion);
 
public sealed record RestaurantCoverageProposalPayload(
    string? Address,
    decimal Latitude,
    decimal Longitude,
    Guid? PrimaryRegionId,
    List<Guid> ServiceRegionIds,
    List<RestaurantServiceRegionWindowPayload>? Windows = null,
    RestaurantDeliveryWindowPayload? DeliveryWindow = null,
    bool DeliveryWindowOnly = false);

public sealed record RestaurantDeliveryWindowPayload(TimeOnly DeliveryWindowStart, TimeOnly DeliveryWindowEnd, int? PreparationLeadTimeMinutes = null);

public sealed record RestaurantServiceRegionWindowPayload(
    Guid RegionId,
    bool IsActive,
    TimeOnly DeliveryWindowStart,
    TimeOnly DeliveryWindowEnd,
    int? PreparationLeadTimeMinutes = null);

public enum BranchAction
{
    Create = 1,
    Update = 2,
    Activate = 3,
    Deactivate = 4,
    SetPrimary = 5,
}

public sealed record RestaurantBranchProposalPayload(
    BranchAction Action,
    Guid? BranchId = null,
    string? BranchCode = null,
    string? NameAr = null,
    string? NameEn = null,
    string? AddressAr = null,
    string? AddressEn = null,
    string? GovernorateAr = null,
    string? GovernorateEn = null,
    string? ContactPhone = null,
    bool IsPrimary = false,
    Guid? ManagerStaffId = null,
    string? PhotoStorageKey = null);

public sealed record RestaurantCapacityProposalPayload(
    int NewBaseCapacity,
    string? Reason = null);

public sealed record RestaurantScheduleDayPayload(
    int DayOfWeek,
    bool IsWorkingDay,
    TimeOnly? ShiftStart = null,
    TimeOnly? ShiftEnd = null);

public sealed record RestaurantScheduleProposalPayload(
    List<RestaurantScheduleDayPayload> Schedule,
    string? Reason = null);



public sealed record RestaurantChangeRequestDto(
    Guid Id,
    Guid RestaurantId,
    Guid RegistrationId,
    RestaurantChangeCategory Category,
    VerificationStatus Status,
    long BaseVersion,
    int PayloadVersion,
    string PayloadJson,
    IReadOnlyList<string> ChangedFields,
    IReadOnlyList<RestaurantChangeDecisionEvent> DecisionEvents,
    string? AdminNotes,
    DateTime? SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    Guid? ReviewedByUserId);

public sealed record SubmitRestaurantChangeCommand(
    Guid RestaurantId,
    RestaurantChangeCategory Category,
    string PayloadJson,
    string? ChangedFieldsJson = null) : ICommand<RestaurantChangeRequestDto>;

public sealed record DecideRestaurantChangeCommand(
    Guid RequestId,
    string Decision,
    string? Reason,
    long ExpectedVersion) : ICommand<RestaurantChangeRequestDto>;

public sealed record RestaurantChangeRequestSummaryDto(
    Guid Id,
    Guid RestaurantId,
    string RestaurantTradeNameAr,
    string RestaurantTradeNameEn,
    RestaurantChangeCategory Category,
    VerificationStatus Status,
    long BaseVersion,
    int PayloadVersion,
    IReadOnlyList<string> ChangedFields,
    int ChangedFieldsCount,
    DateTime? SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    string? AdminNotes);

public sealed record RestaurantChangeRequestDetailDto(
    Guid Id,
    Guid RestaurantId,
    Guid RegistrationId,
    string RestaurantTradeNameAr,
    string RestaurantTradeNameEn,
    RestaurantChangeCategory Category,
    VerificationStatus Status,
    long BaseVersion,
    int PayloadVersion,
    string? CurrentLiveJson,
    string ProposedPayloadJson,
    IReadOnlyList<string> ChangedFields,
    IReadOnlyList<RestaurantChangeDecisionEvent> DecisionEvents,
    string? AdminNotes,
    DateTime? SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    Guid? ReviewedByUserId);

public sealed record ListRestaurantChangeRequestsQuery(
    Guid? RestaurantId = null,
    RestaurantChangeCategory? Category = null,
    VerificationStatus? Status = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<RestaurantChangeRequestSummaryDto>>;

public sealed record GetRestaurantChangeRequestDetailQuery(Guid Id) : IQuery<RestaurantChangeRequestDetailDto>;

public sealed class RestaurantChangeReviewService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;

    public RestaurantChangeReviewService(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IRegistrationReviewNotifier? notifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _notifier = notifier ?? NullRegistrationReviewNotifier.Instance;
    }

    public async Task<Result<RestaurantChangeRequestDto>> SubmitChangeAsync(
        SubmitRestaurantChangeCommand command,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Authentication is required.");
        }

        var (registration, profile) = await LoadApprovedContextAsync(command.RestaurantId, cancellationToken);
        if (registration is null || profile is null)
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Settings changes are only available for approved restaurants.");
        }

        // Validate no-op payload
        var changedFields = ParseChangedFields(command.ChangedFieldsJson);
        if (command.ChangedFieldsJson is not null && changedFields.Count == 0)
        {
            return Error.Validation("Validation.NoChanges", "No changes detected. Cannot submit a no-op change proposal.");
        }

        // Check for existing open request in the same category
        var existing = await _db.RestaurantSettingsChangeRequests
            .Where(r => r.RestaurantId == profile.Id && r.Category == command.Category)
            .Where(r => r.Status == VerificationStatus.Draft
                     || r.Status == VerificationStatus.Submitted
                     || r.Status == VerificationStatus.UnderReview
                     || r.Status == VerificationStatus.NeedsChanges)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            if (existing.Status == VerificationStatus.Submitted || existing.Status == VerificationStatus.UnderReview)
            {
                return Error.Conflict(MessageCodes.Auth.InvalidState, "An open change request for this category is already under review.");
            }

            // It was Draft or NeedsChanges - increment payload version and update
            existing.PayloadVersion++;
            existing.PayloadJson = command.PayloadJson;
            existing.ChangedFieldsJson = command.ChangedFieldsJson ?? existing.ChangedFieldsJson;
            existing.BaseVersion = registration.ApprovedVersion;
            existing.Status = VerificationStatus.Submitted;
            existing.SubmittedAtUtc = _clock.UtcNow;
            existing.ModifiedAtUtc = _clock.UtcNow;

            if (_currentUser.UserId.HasValue)
            {
                var categoryAr = GetCategoryNameAr(command.Category);
                var categoryEn = command.Category.ToString();
                var deepLink = GetCategoryDeepLink(command.Category);

                _db.RestaurantNotifications.Add(new RestaurantNotification
                {
                    UserId = _currentUser.UserId.Value,
                    Type = "restaurant-change.submitted",
                    TitleAr = "تم إرسال طلب التعديل",
                    TitleEn = "Change Proposal Submitted",
                    BodyAr = $"تم إرسال مقترح تعديل ({categoryAr}) للمراجعة والاعتماد.",
                    BodyEn = $"A change proposal for {categoryEn} has been submitted for admin review.",
                    Section = command.Category.ToString().ToLowerInvariant(),
                    SubjectId = existing.Id,
                    DeepLink = deepLink,
                    IsRead = false,
                    SentAtUtc = _clock.UtcNow,
                    CreatedAtUtc = _clock.UtcNow
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
            return MapToDto(existing);
        }

        // Create new proposal
        var request = new RestaurantSettingsChangeRequest
        {
            RestaurantId = profile.Id,
            RegistrationId = registration.Id,
            Category = command.Category,
            Status = VerificationStatus.Submitted,
            BaseVersion = registration.ApprovedVersion,
            PayloadVersion = 1,
            PayloadJson = command.PayloadJson,
            ChangedFieldsJson = command.ChangedFieldsJson,
            SubmittedAtUtc = _clock.UtcNow,
            CreatedAtUtc = _clock.UtcNow
        };

        _db.RestaurantSettingsChangeRequests.Add(request);

        if (_currentUser.UserId.HasValue)
        {
            var categoryAr = GetCategoryNameAr(command.Category);
            var categoryEn = command.Category.ToString();
            var deepLink = GetCategoryDeepLink(command.Category);

            _db.RestaurantNotifications.Add(new RestaurantNotification
            {
                UserId = _currentUser.UserId.Value,
                Type = "restaurant-change.submitted",
                TitleAr = "تم إرسال طلب التعديل",
                TitleEn = "Change Proposal Submitted",
                BodyAr = $"تم إرسال مقترح تعديل ({categoryAr}) للمراجعة والاعتماد.",
                BodyEn = $"A change proposal for {categoryEn} has been submitted for admin review.",
                Section = command.Category.ToString().ToLowerInvariant(),
                SubjectId = request.Id,
                DeepLink = deepLink,
                IsRead = false,
                SentAtUtc = _clock.UtcNow,
                CreatedAtUtc = _clock.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "RestaurantChangeProposalSubmitted",
            nameof(RestaurantSettingsChangeRequest),
            request.Id,
            Reason: $"Category: {command.Category}, BaseVersion: {request.BaseVersion}",
            SourceChannel: "restaurant"), cancellationToken);

        return MapToDto(request);
    }

    public async Task<Result<RestaurantChangeRequestDto>> DecideChangeAsync(
        DecideRestaurantChangeCommand command,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.HasPermission(PermissionCodes.RestaurantRegApprove)
            && _currentUser.UserType != UserType.Admin)
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Admin approval permission is required.");
        }

        var change = await _db.RestaurantSettingsChangeRequests
            .FirstOrDefaultAsync(r => r.Id == command.RequestId, cancellationToken);

        if (change is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Change request was not found.");
        }

        if (change.Status == VerificationStatus.Approved || change.Status == VerificationStatus.Rejected)
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "This change request has already been decided.");
        }

        var decision = command.Decision?.Trim() ?? string.Empty;
        var reason = command.Reason?.Trim();

        if (string.Equals(decision, "NeedsChanges", StringComparison.OrdinalIgnoreCase)
            || string.Equals(decision, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Error.Validation(MessageCodes.Validation.Required, "Decision reason is required.");
            }
        }

        // Check ExpectedVersion against proposal BaseVersion
        if (command.ExpectedVersion != change.BaseVersion)
        {
            return Error.Conflict(
                MessageCodes.Auth.InvalidState,
                $"The proposal is based on version {change.BaseVersion}, but expected version was {command.ExpectedVersion}.");
        }

        // Load live approved aggregate to verify version hasn't drifted concurrently
        var (registration, profile) = await LoadApprovedContextAsync(change.RestaurantId, cancellationToken);
        if (registration is null || profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Approved restaurant context not found.");
        }

        if (registration.ApprovedVersion != change.BaseVersion)
        {
            return Error.Conflict(
                MessageCodes.Auth.InvalidState,
                $"The approved restaurant state has changed to version {registration.ApprovedVersion} since this proposal was submitted (version {change.BaseVersion}). Reload and review current values.");
        }

        var decisionEvents = ParseDecisionEvents(change.DecisionEventsJson);
        decisionEvents.Add(new RestaurantChangeDecisionEvent(
            decision,
            reason,
            _currentUser.UserId,
            _clock.UtcNow,
            command.ExpectedVersion,
            change.PayloadVersion));
        change.DecisionEventsJson = JsonSerializer.Serialize(decisionEvents);

        if (string.Equals(decision, "Approved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(decision, "Approve", StringComparison.OrdinalIgnoreCase))
        {
            if (change.Category == RestaurantChangeCategory.Coverage)
            {
                try
                {
                    var covPayload = JsonSerializer.Deserialize<RestaurantCoverageProposalPayload>(change.PayloadJson);
                    if (covPayload is not null && !covPayload.DeliveryWindowOnly)
                    {
                        var existingActiveRegionIds = await _db.RestaurantServiceRegions
                            .Where(x => x.RestaurantId == profile.Id && x.IsActive)
                            .Select(x => x.RegionId)
                            .ToListAsync(cancellationToken);

                        var removedRegionIds = existingActiveRegionIds.Except(covPayload.ServiceRegionIds).ToList();
                        if (removedRegionIds.Count > 0)
                        {
                            var hasActiveOrders = await (
                                from order in _db.RestaurantDailyOrders
                                join subDay in _db.SubscriptionDays on order.SubscriptionDayId equals subDay.Id
                                where order.RestaurantId == profile.Id
                                    && subDay.DeliveryRegionId.HasValue
                                    && removedRegionIds.Contains(subDay.DeliveryRegionId.Value)
                                    && order.Status != OrderStatus.Delivered
                                    && order.Status != OrderStatus.Cancelled
                                select order.Id
                            ).AnyAsync(cancellationToken);

                            if (hasActiveOrders)
                            {
                                return Error.Conflict(
                                    "Coverage.ActiveOrdersConflict",
                                    "Cannot approve coverage change: active orders exist in regions being removed.");
                            }
                        }
                    }
                }
                catch { }
            }

            // Apply category changes atomically
            var applyResult = await ApplyCategoryProposalAsync(change, registration, profile, cancellationToken);
            if (applyResult.IsFailure)
            {
                return Result.Failure<RestaurantChangeRequestDto>(applyResult.Error);
            }

            // Increment version on approved aggregate
            registration.ApprovedVersion++;
            profile.ApprovedVersion = registration.ApprovedVersion;
            registration.ModifiedAtUtc = _clock.UtcNow;
            profile.ModifiedAtUtc = _clock.UtcNow;

            change.Status = VerificationStatus.Approved;
            change.ReviewedAtUtc = _clock.UtcNow;
            change.ReviewedByUserId = _currentUser.UserId;
            change.AdminNotes = reason;
            change.ModifiedAtUtc = _clock.UtcNow;

            await _audit.LogAsync(new AuditEntry(
                "RestaurantChangeProposalApproved",
                nameof(RestaurantSettingsChangeRequest),
                change.Id,
                Reason: reason,
                SourceChannel: "admin"), cancellationToken);

            if (registration.SubmittedByUserId is { } userId)
            {
                var categoryAr = GetCategoryNameAr(change.Category);
                var categoryEn = change.Category.ToString();
                var deepLink = GetCategoryDeepLink(change.Category);

                _db.RestaurantNotifications.Add(new RestaurantNotification
                {
                    UserId = userId,
                    Type = "restaurant-change.approved",
                    TitleAr = "تم اعتماد طلب التعديل",
                    TitleEn = "Change Proposal Approved",
                    BodyAr = $"تمت الموافقة على مقترح تعديل ({categoryAr}) ودخل حيز التنفيذ فوراً.",
                    BodyEn = $"The change proposal for {categoryEn} has been approved and is now effective.",
                    Section = change.Category.ToString().ToLowerInvariant(),
                    SubjectId = change.Id,
                    DeepLink = deepLink,
                    IsRead = false,
                    SentAtUtc = _clock.UtcNow,
                    CreatedAtUtc = _clock.UtcNow
                });

                await _notifier.NotifyRegistrationUpdated(userId, registration.Id, $"settings-approved:{change.Category}", cancellationToken);
            }
        }
        else if (string.Equals(decision, "NeedsChanges", StringComparison.OrdinalIgnoreCase))
        {
            change.Status = VerificationStatus.NeedsChanges;
            change.ReviewedAtUtc = _clock.UtcNow;
            change.ReviewedByUserId = _currentUser.UserId;
            change.AdminNotes = reason;
            change.ModifiedAtUtc = _clock.UtcNow;

            await _audit.LogAsync(new AuditEntry(
                "RestaurantChangeProposalNeedsChanges",
                nameof(RestaurantSettingsChangeRequest),
                change.Id,
                Reason: reason,
                SourceChannel: "admin"), cancellationToken);

            if (registration.SubmittedByUserId is { } userId)
            {
                var categoryAr = GetCategoryNameAr(change.Category);
                var categoryEn = change.Category.ToString();
                var deepLink = GetCategoryDeepLink(change.Category);

                _db.RestaurantNotifications.Add(new RestaurantNotification
                {
                    UserId = userId,
                    Type = "restaurant-change.needs-changes",
                    TitleAr = "مطلوب تعديلات على المقترح",
                    TitleEn = "Changes Requested on Proposal",
                    BodyAr = $"طلب المسؤول إجراء تعديلات على مقترح ({categoryAr}): {reason}",
                    BodyEn = $"The admin requested changes on {categoryEn} proposal: {reason}",
                    Section = change.Category.ToString().ToLowerInvariant(),
                    SubjectId = change.Id,
                    DeepLink = deepLink,
                    IsRead = false,
                    SentAtUtc = _clock.UtcNow,
                    CreatedAtUtc = _clock.UtcNow
                });

                await _notifier.NotifyRegistrationUpdated(userId, registration.Id, $"settings-needs-changes:{change.Category}", cancellationToken);
            }
        }
        else if (string.Equals(decision, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            change.Status = VerificationStatus.Rejected;
            change.ReviewedAtUtc = _clock.UtcNow;
            change.ReviewedByUserId = _currentUser.UserId;
            change.AdminNotes = reason;
            change.ModifiedAtUtc = _clock.UtcNow;

            await _audit.LogAsync(new AuditEntry(
                "RestaurantChangeProposalRejected",
                nameof(RestaurantSettingsChangeRequest),
                change.Id,
                Reason: reason,
                SourceChannel: "admin"), cancellationToken);

            if (registration.SubmittedByUserId is { } userId)
            {
                var categoryAr = GetCategoryNameAr(change.Category);
                var categoryEn = change.Category.ToString();
                var deepLink = GetCategoryDeepLink(change.Category);

                _db.RestaurantNotifications.Add(new RestaurantNotification
                {
                    UserId = userId,
                    Type = "restaurant-change.rejected",
                    TitleAr = "تم رفض مقترح التعديل",
                    TitleEn = "Change Proposal Rejected",
                    BodyAr = $"تم رفض مقترح تعديل ({categoryAr}): {reason}",
                    BodyEn = $"The change proposal for {categoryEn} was rejected: {reason}",
                    Section = change.Category.ToString().ToLowerInvariant(),
                    SubjectId = change.Id,
                    DeepLink = deepLink,
                    IsRead = false,
                    SentAtUtc = _clock.UtcNow,
                    CreatedAtUtc = _clock.UtcNow
                });

                await _notifier.NotifyRegistrationUpdated(userId, registration.Id, $"settings-rejected:{change.Category}", cancellationToken);
            }
        }
        else
        {
            return Error.Validation(MessageCodes.Validation.InvalidFormat, $"Unknown decision '{decision}'.");
        }

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(change);
    }

    private static string GetCategoryNameAr(RestaurantChangeCategory cat) => cat switch
    {
        RestaurantChangeCategory.Profile => "الملف التعريفي والبنكي",
        RestaurantChangeCategory.Coverage => "الموقع والتغطية الجغرافية",
        RestaurantChangeCategory.Branch => "الفروع والهيكل التشغيلي",
        RestaurantChangeCategory.Schedule => "ساعات ومواعيد العمل",
        RestaurantChangeCategory.Capacity => "الطاقة الاستيعابية الدائمة",
        _ => cat.ToString()
    };

    private static string GetCategoryDeepLink(RestaurantChangeCategory cat) => cat switch
    {
        RestaurantChangeCategory.Profile => "/restaurant/settings",
        RestaurantChangeCategory.Coverage => "/restaurant/service-regions",
        RestaurantChangeCategory.Branch => "/restaurant/team",
        RestaurantChangeCategory.Schedule => "/restaurant/capacity",
        RestaurantChangeCategory.Capacity => "/restaurant/capacity",
        _ => "/restaurant/settings"
    };

    private async Task<(Domain.IdentityAccess.RestaurantRegistration? Registration, RestaurantProfile? Profile)> LoadApprovedContextAsync(
        Guid restaurantId,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations
            .Where(r => r.ResultingRestaurantId == restaurantId || r.Id == restaurantId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var profile = await _db.RestaurantProfiles
            .FirstOrDefaultAsync(p => p.Id == restaurantId || (registration != null && p.Id == registration.ResultingRestaurantId), cancellationToken);

        return (registration, profile);
    }

    private async Task<Result> ApplyCategoryProposalAsync(
        RestaurantSettingsChangeRequest change,
        Domain.IdentityAccess.RestaurantRegistration registration,
        RestaurantProfile profile,
        CancellationToken cancellationToken)
    {
        switch (change.Category)
        {
            case RestaurantChangeCategory.Profile:
                try
                {
                    var payload = JsonSerializer.Deserialize<RestaurantSettingsDraftPayload>(change.PayloadJson);
                    if (payload is null) return Error.Validation("payload.invalid", "Invalid profile payload.");

                    ApplyProfilePayload(registration, profile, payload);
                    return Result.Success();
                }
                catch (Exception ex)
                {
                    return Error.Failure("apply.failed", ex.Message);
                }

            case RestaurantChangeCategory.Coverage:
                try
                {
                    var payload = JsonSerializer.Deserialize<RestaurantCoverageProposalPayload>(change.PayloadJson);
                    if (payload is null) return Error.Validation("payload.invalid", "Invalid coverage payload.");

                    // Timing is saved directly by the restaurant; obsolete reviews must not overwrite it.
                    if (payload.DeliveryWindowOnly || payload.DeliveryWindow is not null)
                        return Error.Validation("delivery_window.direct_save_required");

                    if (payload.DeliveryWindow is null && profile.DeliveryWindowStart.HasValue && profile.DeliveryWindowEnd.HasValue &&
                        payload.Windows?.Any(w => w.DeliveryWindowStart != profile.DeliveryWindowStart || w.DeliveryWindowEnd != profile.DeliveryWindowEnd) == true)
                        return Error.Validation("service_region.restaurant_window_required");
                    if (payload.DeliveryWindow is null && (profile.PreparationLeadTimeMinutes.HasValue || profile.DeliveryWindowStart.HasValue) &&
                        payload.Windows?.Any(w => w.PreparationLeadTimeMinutes != profile.PreparationLeadTimeMinutes) == true)
                        return Error.Validation("service_region.restaurant_preparation_required");

                    await ApplyCoveragePayloadAsync(registration, profile, payload, cancellationToken);
                    return Result.Success();
                }
                catch (Exception ex)
                {
                    return Error.Failure("apply.failed", ex.Message);
                }

            case RestaurantChangeCategory.Branch:
                try
                {
                    var payload = JsonSerializer.Deserialize<RestaurantBranchProposalPayload>(change.PayloadJson);
                    if (payload is null) return Error.Validation("payload.invalid", "Invalid branch payload.");

                    return await ApplyBranchPayloadAsync(registration, profile, payload, cancellationToken);
                }
                catch (Exception ex)
                {
                    return Error.Failure("apply.failed", ex.Message);
                }

            case RestaurantChangeCategory.Capacity:
                try
                {
                    var payload = JsonSerializer.Deserialize<RestaurantCapacityProposalPayload>(change.PayloadJson);
                    if (payload is null) return Error.Validation("payload.invalid", "Invalid capacity payload.");

                    return await ApplyCapacityPayloadAsync(registration, profile, payload, cancellationToken);
                }
                catch (Exception ex)
                {
                    return Error.Failure("apply.failed", ex.Message);
                }

            case RestaurantChangeCategory.Schedule:
                try
                {
                    var payload = JsonSerializer.Deserialize<RestaurantScheduleProposalPayload>(change.PayloadJson);
                    if (payload is null) return Error.Validation("payload.invalid", "Invalid schedule payload.");

                    return await ApplySchedulePayloadAsync(registration, profile, payload, cancellationToken);
                }
                catch (Exception ex)
                {
                    return Error.Failure("apply.failed", ex.Message);
                }

            default:
                return Result.Success();
        }
    }

    private async Task<Result> ApplyBranchPayloadAsync(
        Domain.IdentityAccess.RestaurantRegistration registration,
        RestaurantProfile profile,
        RestaurantBranchProposalPayload payload,
        CancellationToken cancellationToken)
    {
        var targetRegistrationId = registration.Id;

        switch (payload.Action)
        {
            case BranchAction.Create:
                if (payload.IsPrimary)
                {
                    var existingPrimaryBranches = await _db.RestaurantBranches
                        .Where(b => (b.RestaurantRegistrationId == targetRegistrationId || b.RestaurantRegistrationId == profile.Id) && b.IsPrimary)
                        .ToListAsync(cancellationToken);
                    foreach (var p in existingPrimaryBranches)
                    {
                        p.IsPrimary = false;
                        p.ModifiedAtUtc = _clock.UtcNow;
                    }
                }

                var newBranch = new RestaurantBranch
                {
                    RestaurantRegistrationId = targetRegistrationId,
                    BranchCode = string.IsNullOrWhiteSpace(payload.BranchCode) ? "BR-" + Random.Shared.Next(100, 999) : payload.BranchCode,
                    NameAr = payload.NameAr ?? profile.TradeNameAr,
                    NameEn = payload.NameEn ?? payload.NameAr ?? profile.TradeNameEn,
                    AddressAr = payload.AddressAr ?? "-",
                    AddressEn = payload.AddressEn ?? payload.AddressAr ?? "-",
                    GovernorateAr = payload.GovernorateAr ?? "-",
                    GovernorateEn = payload.GovernorateEn ?? payload.GovernorateAr ?? "-",
                    ContactPhone = payload.ContactPhone ?? "-",
                    IsActive = true,
                    IsPrimary = payload.IsPrimary,
                    ManagerStaffId = payload.ManagerStaffId,
                    PhotoStorageKey = string.IsNullOrWhiteSpace(payload.PhotoStorageKey) ? null : payload.PhotoStorageKey.Trim(),
                    CreatedAtUtc = _clock.UtcNow,
                };
                _db.RestaurantBranches.Add(newBranch);
                break;

            case BranchAction.Activate:
            case BranchAction.Deactivate:
                if (!payload.BranchId.HasValue)
                {
                    return Error.Validation("Branch.MissingId", "Branch ID is required for toggle action.");
                }

                var toggleBranch = await _db.RestaurantBranches
                    .FirstOrDefaultAsync(b => b.Id == payload.BranchId.Value, cancellationToken);
                if (toggleBranch == null)
                {
                    return Error.NotFound("Branch.NotFound", "Branch not found.");
                }

                if (toggleBranch.RestaurantRegistrationId != targetRegistrationId && toggleBranch.RestaurantRegistrationId != profile.Id)
                {
                    return Error.Conflict("Branch.ForeignBranchConflict", "Cannot modify branch belonging to another restaurant.");
                }

                var isTargetActive = payload.Action == BranchAction.Activate;
                if (!isTargetActive)
                {
                    var activeBranchesCount = await _db.RestaurantBranches
                        .CountAsync(b => (b.RestaurantRegistrationId == targetRegistrationId || b.RestaurantRegistrationId == profile.Id) && b.IsActive && b.Id != toggleBranch.Id, cancellationToken);
                    if (activeBranchesCount == 0)
                    {
                        return Error.Conflict("Branch.CannotDeactivateLastBranch", "Cannot deactivate the last usable branch of a restaurant.");
                    }
                }

                toggleBranch.IsActive = isTargetActive;
                toggleBranch.ModifiedAtUtc = _clock.UtcNow;
                break;

            case BranchAction.SetPrimary:
                if (!payload.BranchId.HasValue)
                {
                    return Error.Validation("Branch.MissingId", "Branch ID is required.");
                }

                var primaryBranch = await _db.RestaurantBranches
                    .FirstOrDefaultAsync(b => b.Id == payload.BranchId.Value, cancellationToken);
                if (primaryBranch == null)
                {
                    return Error.NotFound("Branch.NotFound", "Branch not found.");
                }

                if (primaryBranch.RestaurantRegistrationId != targetRegistrationId && primaryBranch.RestaurantRegistrationId != profile.Id)
                {
                    return Error.Conflict("Branch.ForeignBranchConflict", "Cannot set primary to a branch belonging to another restaurant.");
                }

                var allBranches = await _db.RestaurantBranches
                    .Where(b => b.RestaurantRegistrationId == targetRegistrationId || b.RestaurantRegistrationId == profile.Id)
                    .ToListAsync(cancellationToken);

                foreach (var b in allBranches)
                {
                    b.IsPrimary = b.Id == primaryBranch.Id;
                    b.ModifiedAtUtc = _clock.UtcNow;
                }
                break;

            case BranchAction.Update:
                if (!payload.BranchId.HasValue)
                {
                    return Error.Validation("Branch.MissingId", "Branch ID is required for update.");
                }

                var editBranch = await _db.RestaurantBranches
                    .FirstOrDefaultAsync(b => b.Id == payload.BranchId.Value, cancellationToken);
                if (editBranch == null)
                {
                    return Error.NotFound("Branch.NotFound", "Branch not found.");
                }

                if (editBranch.RestaurantRegistrationId != targetRegistrationId && editBranch.RestaurantRegistrationId != profile.Id)
                {
                    return Error.Conflict("Branch.ForeignBranchConflict", "Cannot modify branch belonging to another restaurant.");
                }

                if (payload.IsPrimary && !editBranch.IsPrimary)
                {
                    var prevPrimaries = await _db.RestaurantBranches
                        .Where(b => (b.RestaurantRegistrationId == targetRegistrationId || b.RestaurantRegistrationId == profile.Id) && b.IsPrimary)
                        .ToListAsync(cancellationToken);
                    foreach (var p in prevPrimaries)
                    {
                        p.IsPrimary = false;
                        p.ModifiedAtUtc = _clock.UtcNow;
                    }
                }

                if (!string.IsNullOrWhiteSpace(payload.NameAr)) editBranch.NameAr = payload.NameAr;
                if (!string.IsNullOrWhiteSpace(payload.NameEn)) editBranch.NameEn = payload.NameEn;
                if (!string.IsNullOrWhiteSpace(payload.AddressAr)) editBranch.AddressAr = payload.AddressAr;
                if (!string.IsNullOrWhiteSpace(payload.AddressEn)) editBranch.AddressEn = payload.AddressEn;
                if (!string.IsNullOrWhiteSpace(payload.GovernorateAr)) editBranch.GovernorateAr = payload.GovernorateAr;
                if (!string.IsNullOrWhiteSpace(payload.GovernorateEn)) editBranch.GovernorateEn = payload.GovernorateEn;
                if (!string.IsNullOrWhiteSpace(payload.ContactPhone)) editBranch.ContactPhone = payload.ContactPhone;
                if (payload.ManagerStaffId.HasValue) editBranch.ManagerStaffId = payload.ManagerStaffId;
                if (payload.PhotoStorageKey != null) editBranch.PhotoStorageKey = string.IsNullOrWhiteSpace(payload.PhotoStorageKey) ? null : payload.PhotoStorageKey.Trim();
                editBranch.IsPrimary = payload.IsPrimary;
                editBranch.ModifiedAtUtc = _clock.UtcNow;
                break;
        }

        return Result.Success();
    }

    private async Task<Result> ApplyCapacityPayloadAsync(
        Domain.IdentityAccess.RestaurantRegistration registration,
        RestaurantProfile profile,
        RestaurantCapacityProposalPayload payload,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        var existingCaps = await _db.DailyCapacities
            .Where(x => x.RestaurantId == profile.Id && x.CapacityType == CapacityType.Base && x.ServiceDate >= today)
            .ToListAsync(cancellationToken);

        if (existingCaps.Count == 0)
        {
            _db.DailyCapacities.Add(new DailyCapacity
            {
                RestaurantId = profile.Id,
                ServiceDate = today,
                CapacityType = CapacityType.Base,
                MaxBoxes = payload.NewBaseCapacity,
                ReservedBoxes = 0,
                CreatedAtUtc = _clock.UtcNow,
            });
        }
        else
        {
            foreach (var cap in existingCaps)
            {
                cap.MaxBoxes = payload.NewBaseCapacity;
                cap.ModifiedAtUtc = _clock.UtcNow;
            }
        }
        return Result.Success();
    }

    private async Task<Result> ApplySchedulePayloadAsync(
        Domain.IdentityAccess.RestaurantRegistration registration,
        RestaurantProfile profile,
        RestaurantScheduleProposalPayload payload,
        CancellationToken cancellationToken)
    {
        var existingSchedules = await _db.RestaurantOperatingSchedules
            .Where(x => x.RestaurantId == profile.Id)
            .ToListAsync(cancellationToken);

        foreach (var item in payload.Schedule)
        {
            var dayOfWeek = (DayOfWeek)item.DayOfWeek;
            var existing = existingSchedules.FirstOrDefault(x => x.DayOfWeek == dayOfWeek);
            if (existing != null)
            {
                existing.IsWorkingDay = item.IsWorkingDay;
                existing.ModifiedAtUtc = _clock.UtcNow;
            }
            else
            {
                _db.RestaurantOperatingSchedules.Add(new RestaurantOperatingSchedule
                {
                    RestaurantId = profile.Id,
                    DayOfWeek = dayOfWeek,
                    IsWorkingDay = item.IsWorkingDay,
                    CreatedAtUtc = _clock.UtcNow,
                });
            }
        }
        return Result.Success();
    }


    private async Task ApplyCoveragePayloadAsync(
        Domain.IdentityAccess.RestaurantRegistration registration,
        RestaurantProfile profile,
        RestaurantCoverageProposalPayload payload,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(payload.Address))
        {
            registration.Address = RestaurantRegistrationRules.NormalizeOptional(payload.Address);
        }

        registration.Latitude = payload.Latitude;
        registration.Longitude = payload.Longitude;
        if (payload.PrimaryRegionId.HasValue)
        {
            registration.RegionId = payload.PrimaryRegionId.Value;
            profile.PrimaryRegionId = payload.PrimaryRegionId.Value;
        }

        profile.Latitude = payload.Latitude;
        profile.Longitude = payload.Longitude;
        profile.ModifiedAtUtc = _clock.UtcNow;
        registration.ModifiedAtUtc = _clock.UtcNow;

        if (payload.ServiceRegionIds is { Count: > 0 })
        {
            registration.ServiceRegionIdsJson = JsonSerializer.Serialize(
                payload.ServiceRegionIds.Select(x => x.ToString()).ToList());

            var existingRegions = await _db.RestaurantServiceRegions
                .Where(x => x.RestaurantId == profile.Id)
                .ToListAsync(cancellationToken);

            var targetIds = payload.ServiceRegionIds.ToHashSet();
            var windowDict = payload.Windows?.ToDictionary(w => w.RegionId) ?? new();

            foreach (var existing in existingRegions)
            {
                existing.IsActive = targetIds.Contains(existing.RegionId);
                if (windowDict.TryGetValue(existing.RegionId, out var win))
                {
                    existing.DeliveryWindowStart = profile.DeliveryWindowStart ?? win.DeliveryWindowStart;
                    existing.DeliveryWindowEnd = profile.DeliveryWindowEnd ?? win.DeliveryWindowEnd;
                    existing.PreparationLeadTimeMinutes = profile.DeliveryWindowStart.HasValue || profile.PreparationLeadTimeMinutes.HasValue ? profile.PreparationLeadTimeMinutes : win.PreparationLeadTimeMinutes;
                }
                existing.ModifiedAtUtc = _clock.UtcNow;
            }

            var existingIds = existingRegions.Select(x => x.RegionId).ToHashSet();
            foreach (var newId in targetIds.Where(id => !existingIds.Contains(id)))
            {
                var win = windowDict.TryGetValue(newId, out var w) ? w : null;
                _db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
                {
                    RestaurantId = profile.Id,
                    RegionId = newId,
                    IsActive = true,
                    DeliveryWindowStart = profile.DeliveryWindowStart ?? win?.DeliveryWindowStart ?? new TimeOnly(8, 0),
                    DeliveryWindowEnd = profile.DeliveryWindowEnd ?? win?.DeliveryWindowEnd ?? new TimeOnly(14, 0),
                    PreparationLeadTimeMinutes = profile.DeliveryWindowStart.HasValue || profile.PreparationLeadTimeMinutes.HasValue ? profile.PreparationLeadTimeMinutes : win?.PreparationLeadTimeMinutes,
                    CreatedAtUtc = _clock.UtcNow,
                });
            }
        }
    }

    private static void ApplyProfilePayload(
        Domain.IdentityAccess.RestaurantRegistration reg,
        RestaurantProfile prof,
        RestaurantSettingsDraftPayload payload)
    {
        if (!string.IsNullOrWhiteSpace(payload.LegalNameAr)) reg.LegalNameAr = payload.LegalNameAr.Trim();
        if (!string.IsNullOrWhiteSpace(payload.LegalNameEn)) reg.LegalNameEn = payload.LegalNameEn.Trim();
        if (!string.IsNullOrWhiteSpace(payload.TradeNameAr))
        {
            reg.TradeNameAr = payload.TradeNameAr.Trim();
            prof.TradeNameAr = payload.TradeNameAr.Trim();
        }
        if (!string.IsNullOrWhiteSpace(payload.TradeNameEn))
        {
            reg.TradeNameEn = payload.TradeNameEn.Trim();
            prof.TradeNameEn = payload.TradeNameEn.Trim();
        }
        if (!string.IsNullOrWhiteSpace(payload.ContactPhone))
        {
            reg.ContactPhone = payload.ContactPhone.Trim();
            prof.ContactPhone = payload.ContactPhone.Trim();
        }
        if (!string.IsNullOrWhiteSpace(payload.ContactEmail))
        {
            reg.ContactEmail = payload.ContactEmail.Trim();
            prof.ContactEmail = payload.ContactEmail.Trim();
        }
        if (payload.CommercialRegistrationNumber is not null) reg.CommercialRegistrationNumber = payload.CommercialRegistrationNumber.Trim();
        if (payload.CommercialRegistrationExpiry.HasValue) reg.CommercialRegistrationExpiry = payload.CommercialRegistrationExpiry.Value;
        if (payload.CommercialLicenseNumber is not null)
        {
            reg.CommercialLicenseNumber = payload.CommercialLicenseNumber.Trim();
            prof.CommercialLicenseNumber = payload.CommercialLicenseNumber.Trim();
        }
        if (payload.CommercialLicenseExpiry.HasValue) reg.CommercialLicenseExpiry = payload.CommercialLicenseExpiry.Value;
        if (payload.FoodLicenseNumber is not null) reg.FoodLicenseNumber = payload.FoodLicenseNumber.Trim();
        if (payload.FoodLicenseExpiry.HasValue) reg.FoodLicenseExpiry = payload.FoodLicenseExpiry.Value;
        if (!string.IsNullOrWhiteSpace(payload.LegalEntityType)) reg.LegalEntityType = payload.LegalEntityType.Trim();
        if (payload.CompanyCivilId is not null) reg.CompanyCivilId = payload.CompanyCivilId.Trim();
        if (payload.OwnerName is not null) reg.OwnerName = payload.OwnerName.Trim();
        if (payload.AuthorizedSignatoryCivilId is not null) reg.AuthorizedSignatoryCivilId = payload.AuthorizedSignatoryCivilId.Trim();
        if (payload.AuthorizedSignatoryTitle is not null) reg.AuthorizedSignatoryTitle = payload.AuthorizedSignatoryTitle.Trim();
        if (payload.PrimaryContactName is not null) reg.PrimaryContactName = payload.PrimaryContactName.Trim();
        if (payload.PrimaryContactTitle is not null) reg.PrimaryContactTitle = payload.PrimaryContactTitle.Trim();
        if (payload.PrimaryContactPhone is not null) reg.PrimaryContactPhone = payload.PrimaryContactPhone.Trim();
        if (payload.PrimaryContactEmail is not null) reg.PrimaryContactEmail = payload.PrimaryContactEmail.Trim();

        // Bank details
        if (payload.BankName is not null) reg.BankName = payload.BankName.Trim();
        if (payload.BankBeneficiaryName is not null) reg.BankBeneficiaryName = payload.BankBeneficiaryName.Trim();
        if (payload.Iban is not null) reg.Iban = payload.Iban.Trim().ToUpperInvariant();
        if (payload.BankAccountNumber is not null) reg.BankAccountNumber = payload.BankAccountNumber.Trim();
        if (payload.SwiftCode is not null) reg.SwiftCode = payload.SwiftCode.Trim().ToUpperInvariant();

        // Location & address
        if (payload.Address is not null) reg.Address = payload.Address.Trim();
        if (payload.CountryId.HasValue)
        {
            reg.CountryId = payload.CountryId;
            prof.CountryId = payload.CountryId;
        }
        if (payload.RegionId.HasValue)
        {
            reg.RegionId = payload.RegionId;
            prof.PrimaryRegionId = payload.RegionId;
        }
        if (payload.Latitude.HasValue && payload.Longitude.HasValue)
        {
            reg.Latitude = payload.Latitude.Value;
            reg.Longitude = payload.Longitude.Value;
            prof.Latitude = payload.Latitude.Value;
            prof.Longitude = payload.Longitude.Value;
        }
    }

    private static List<string> ParseChangedFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            var trimmed = json.Trim();
            if (trimmed.StartsWith("["))
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            if (trimmed.StartsWith("{"))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                return dict?.Keys.ToList() ?? new List<string>();
            }
            return new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static List<RestaurantChangeDecisionEvent> ParseDecisionEvents(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<RestaurantChangeDecisionEvent>();
        try
        {
            return JsonSerializer.Deserialize<List<RestaurantChangeDecisionEvent>>(json) ?? new List<RestaurantChangeDecisionEvent>();
        }
        catch
        {
            return new List<RestaurantChangeDecisionEvent>();
        }
    }

    public async Task<Result<PagedResult<RestaurantChangeRequestSummaryDto>>> ListChangeRequestsAsync(
        ListRestaurantChangeRequestsQuery request,
        CancellationToken cancellationToken)
    {
        var isAuthorizedAdmin = _currentUser.HasPermission(PermissionCodes.RestaurantRegView)
            || _currentUser.UserType == UserType.Admin;

        if (!isAuthorizedAdmin && _currentUser.RestaurantId != request.RestaurantId)
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not authorized to view change requests.");
        }

        var query = _db.RestaurantSettingsChangeRequests.AsNoTracking().AsQueryable();

        if (request.RestaurantId is not null)
            query = query.Where(r => r.RestaurantId == request.RestaurantId.Value);

        if (request.Category is not null)
            query = query.Where(r => r.Category == request.Category.Value);

        if (request.Status is not null)
            query = query.Where(r => r.Status == request.Status.Value);

        var total = await query.CountAsync(cancellationToken);
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };

        var items = await query
            .OrderByDescending(r => r.SubmittedAtUtc ?? r.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        var restaurantIds = items.Select(i => i.RestaurantId).Distinct().ToList();
        var profiles = await _db.RestaurantProfiles.AsNoTracking()
            .Where(p => restaurantIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var dtos = items.Select(r =>
        {
            profiles.TryGetValue(r.RestaurantId, out var profile);
            var changedFields = ParseChangedFields(r.ChangedFieldsJson);
            return new RestaurantChangeRequestSummaryDto(
                r.Id,
                r.RestaurantId,
                profile?.TradeNameAr ?? string.Empty,
                profile?.TradeNameEn ?? string.Empty,
                r.Category,
                r.Status,
                r.BaseVersion,
                r.PayloadVersion,
                changedFields,
                changedFields.Count,
                r.SubmittedAtUtc,
                r.ReviewedAtUtc,
                r.AdminNotes);
        }).ToList();

        return new PagedResult<RestaurantChangeRequestSummaryDto>(dtos, total, page.NormalizedPage, page.NormalizedPageSize);
    }

    public async Task<Result<RestaurantChangeRequestDetailDto>> GetChangeRequestDetailAsync(
        GetRestaurantChangeRequestDetailQuery request,
        CancellationToken cancellationToken)
    {
        var change = await _db.RestaurantSettingsChangeRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (change is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Change request not found.");
        }

        var isAuthorizedAdmin = _currentUser.HasPermission(PermissionCodes.RestaurantRegView)
            || _currentUser.UserType == UserType.Admin;

        if (!isAuthorizedAdmin && _currentUser.RestaurantId != change.RestaurantId)
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not authorized to view this change request.");
        }

        var profile = await _db.RestaurantProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == change.RestaurantId, cancellationToken);
        var registration = await _db.RestaurantRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == change.RegistrationId, cancellationToken);

        var canViewSensitiveBank = _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove)
            || _currentUser.UserType == UserType.Admin;

        string? currentLiveJson = null;
        string proposedPayloadJson = change.PayloadJson;

        switch (change.Category)
        {
            case RestaurantChangeCategory.Profile:
                if (registration is not null)
                {
                    var liveProfile = new
                    {
                        registration.TradeNameAr,
                        registration.TradeNameEn,
                        registration.LegalNameAr,
                        registration.LegalNameEn,
                        registration.ContactPhone,
                        registration.ContactEmail,
                        registration.CommercialRegistrationNumber,
                        registration.CommercialRegistrationExpiry,
                        registration.CommercialLicenseNumber,
                        registration.CommercialLicenseExpiry,
                        registration.FoodLicenseNumber,
                        registration.FoodLicenseExpiry,
                        registration.LegalEntityType,
                        registration.CompanyCivilId,
                        registration.OwnerName,
                        registration.AuthorizedSignatoryCivilId,
                        registration.AuthorizedSignatoryTitle,
                        registration.PrimaryContactName,
                        registration.PrimaryContactTitle,
                        registration.PrimaryContactPhone,
                        registration.PrimaryContactEmail,
                        registration.BankName,
                        registration.BankBeneficiaryName,
                        Iban = canViewSensitiveBank ? registration.Iban : RestaurantSettingsChangeRules.MaskBankValue(registration.Iban),
                        BankAccountNumber = canViewSensitiveBank ? registration.BankAccountNumber : RestaurantSettingsChangeRules.MaskBankValue(registration.BankAccountNumber),
                        registration.SwiftCode,
                        registration.Address,
                        registration.CountryId,
                        registration.RegionId,
                        registration.Website,
                        registration.InstagramHandle
                    };
                    currentLiveJson = JsonSerializer.Serialize(liveProfile);
                }
                if (!canViewSensitiveBank && !string.IsNullOrWhiteSpace(proposedPayloadJson))
                {
                    try
                    {
                        var doc = JsonSerializer.Deserialize<Dictionary<string, object?>>(proposedPayloadJson);
                        if (doc is not null)
                        {
                            if (doc.TryGetValue("Iban", out var ibanObj) && ibanObj is not null)
                                doc["Iban"] = RestaurantSettingsChangeRules.MaskBankValue(ibanObj.ToString());
                            else if (doc.TryGetValue("iban", out var ibanObj2) && ibanObj2 is not null)
                                doc["iban"] = RestaurantSettingsChangeRules.MaskBankValue(ibanObj2.ToString());

                            if (doc.TryGetValue("BankAccountNumber", out var banObj) && banObj is not null)
                                doc["BankAccountNumber"] = RestaurantSettingsChangeRules.MaskBankValue(banObj.ToString());
                            else if (doc.TryGetValue("bankAccountNumber", out var banObj2) && banObj2 is not null)
                                doc["bankAccountNumber"] = RestaurantSettingsChangeRules.MaskBankValue(banObj2.ToString());

                            proposedPayloadJson = JsonSerializer.Serialize(doc);
                        }
                    }
                    catch { }
                }
                break;

            case RestaurantChangeCategory.Coverage:
                var regions = await _db.RestaurantServiceRegions.AsNoTracking()
                    .Where(r => r.RestaurantId == change.RestaurantId && r.IsActive)
                    .Select(r => r.RegionId)
                    .ToListAsync(cancellationToken);
                currentLiveJson = JsonSerializer.Serialize(new
                {
                    registration?.Address,
                    registration?.Latitude,
                    registration?.Longitude,
                    PrimaryRegionId = registration?.RegionId,
                    ServiceRegionIds = regions,
                    DeliveryWindow = new { DeliveryWindowStart = profile?.DeliveryWindowStart, DeliveryWindowEnd = profile?.DeliveryWindowEnd, PreparationLeadTimeMinutes = profile?.PreparationLeadTimeMinutes }
                });
                break;

            case RestaurantChangeCategory.Branch:
                try
                {
                    using var doc = JsonDocument.Parse(change.PayloadJson);
                    if (doc.RootElement.TryGetProperty("BranchId", out var bIdProp) && bIdProp.TryGetGuid(out var branchId))
                    {
                        var branch = await _db.RestaurantBranches.AsNoTracking()
                            .FirstOrDefaultAsync(b => b.Id == branchId, cancellationToken);
                        if (branch is not null)
                        {
                            currentLiveJson = JsonSerializer.Serialize(new
                            {
                                branch.Id,
                                branch.NameAr,
                                branch.NameEn,
                                branch.AddressAr,
                                branch.AddressEn,
                                branch.ContactPhone,
                                branch.IsActive,
                                branch.IsPrimary
                            });
                        }
                    }
                }
                catch { }
                break;

            case RestaurantChangeCategory.Schedule:
                var schedules = await _db.RestaurantOperatingSchedules.AsNoTracking()
                    .Where(d => d.RestaurantId == change.RestaurantId)
                    .OrderBy(d => d.DayOfWeek)
                    .ToListAsync(cancellationToken);
                currentLiveJson = JsonSerializer.Serialize(schedules.Select(d => new
                {
                    DayOfWeek = (int)d.DayOfWeek,
                    d.IsWorkingDay
                }));
                break;

            case RestaurantChangeCategory.Capacity:
                var today = DateOnly.FromDateTime(_clock.UtcNow);
                var cap = await _db.DailyCapacities.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.RestaurantId == change.RestaurantId && c.CapacityType == CapacityType.Base && c.ServiceDate >= today, cancellationToken);
                if (cap is not null)
                {
                    currentLiveJson = JsonSerializer.Serialize(new
                    {
                        cap.MaxBoxes
                    });
                }
                break;
        }

        return new RestaurantChangeRequestDetailDto(
            change.Id,
            change.RestaurantId,
            change.RegistrationId,
            profile?.TradeNameAr ?? string.Empty,
            profile?.TradeNameEn ?? string.Empty,
            change.Category,
            change.Status,
            change.BaseVersion,
            change.PayloadVersion,
            currentLiveJson,
            proposedPayloadJson,
            ParseChangedFields(change.ChangedFieldsJson),
            ParseDecisionEvents(change.DecisionEventsJson),
            change.AdminNotes,
            change.SubmittedAtUtc,
            change.ReviewedAtUtc,
            change.ReviewedByUserId);
    }

    private static RestaurantChangeRequestDto MapToDto(RestaurantSettingsChangeRequest r)
    {
        return new RestaurantChangeRequestDto(
            r.Id,
            r.RestaurantId,
            r.RegistrationId,
            r.Category,
            r.Status,
            r.BaseVersion,
            r.PayloadVersion,
            r.PayloadJson,
            ParseChangedFields(r.ChangedFieldsJson),
            ParseDecisionEvents(r.DecisionEventsJson),
            r.AdminNotes,
            r.SubmittedAtUtc,
            r.ReviewedAtUtc,
            r.ReviewedByUserId);
    }
}

public sealed class SubmitRestaurantChangeCommandHandler
    : ICommandHandler<SubmitRestaurantChangeCommand, RestaurantChangeRequestDto>
{
    private readonly RestaurantChangeReviewService _service;

    public SubmitRestaurantChangeCommandHandler(RestaurantChangeReviewService service)
    {
        _service = service;
    }

    public Task<Result<RestaurantChangeRequestDto>> Handle(
        SubmitRestaurantChangeCommand request,
        CancellationToken cancellationToken)
        => _service.SubmitChangeAsync(request, cancellationToken);
}

public sealed class DecideRestaurantChangeCommandHandler
    : ICommandHandler<DecideRestaurantChangeCommand, RestaurantChangeRequestDto>
{
    private readonly RestaurantChangeReviewService _service;

    public DecideRestaurantChangeCommandHandler(RestaurantChangeReviewService service)
    {
        _service = service;
    }

    public Task<Result<RestaurantChangeRequestDto>> Handle(
        DecideRestaurantChangeCommand request,
        CancellationToken cancellationToken)
        => _service.DecideChangeAsync(request, cancellationToken);
}

public sealed class ListRestaurantChangeRequestsQueryHandler
    : IQueryHandler<ListRestaurantChangeRequestsQuery, PagedResult<RestaurantChangeRequestSummaryDto>>
{
    private readonly RestaurantChangeReviewService _service;

    public ListRestaurantChangeRequestsQueryHandler(RestaurantChangeReviewService service)
    {
        _service = service;
    }

    public Task<Result<PagedResult<RestaurantChangeRequestSummaryDto>>> Handle(
        ListRestaurantChangeRequestsQuery request,
        CancellationToken cancellationToken)
        => _service.ListChangeRequestsAsync(request, cancellationToken);
}

public sealed class GetRestaurantChangeRequestDetailQueryHandler
    : IQueryHandler<GetRestaurantChangeRequestDetailQuery, RestaurantChangeRequestDetailDto>
{
    private readonly RestaurantChangeReviewService _service;

    public GetRestaurantChangeRequestDetailQueryHandler(RestaurantChangeReviewService service)
    {
        _service = service;
    }

    public Task<Result<RestaurantChangeRequestDetailDto>> Handle(
        GetRestaurantChangeRequestDetailQuery request,
        CancellationToken cancellationToken)
        => _service.GetChangeRequestDetailAsync(request, cancellationToken);
}
