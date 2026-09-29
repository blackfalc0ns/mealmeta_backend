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
    List<RestaurantServiceRegionWindowPayload>? Windows = null);

public sealed record RestaurantServiceRegionWindowPayload(
    Guid RegionId,
    bool IsActive,
    TimeOnly DeliveryWindowStart,
    TimeOnly DeliveryWindowEnd,
    int? PreparationLeadTimeMinutes = null);

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
                    if (covPayload is not null)
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

                    await ApplyCoveragePayloadAsync(registration, profile, payload, cancellationToken);
                    return Result.Success();
                }
                catch (Exception ex)
                {
                    return Error.Failure("apply.failed", ex.Message);
                }

            default:
                // Other categories will be wired in Tasks 5, 6
                return Result.Success();
        }
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
                    existing.DeliveryWindowStart = win.DeliveryWindowStart;
                    existing.DeliveryWindowEnd = win.DeliveryWindowEnd;
                    existing.PreparationLeadTimeMinutes = win.PreparationLeadTimeMinutes;
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
                    DeliveryWindowStart = win?.DeliveryWindowStart ?? new TimeOnly(8, 0),
                    DeliveryWindowEnd = win?.DeliveryWindowEnd ?? new TimeOnly(14, 0),
                    PreparationLeadTimeMinutes = win?.PreparationLeadTimeMinutes,
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
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
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
