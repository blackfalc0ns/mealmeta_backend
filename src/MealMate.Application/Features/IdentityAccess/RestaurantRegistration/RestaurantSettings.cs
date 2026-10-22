using System.Text.Json;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Validation;
using MealMate.Application.Features.Catalog;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

public sealed record RestaurantSettingsDraftPayload(
    string? LegalNameAr,
    string? LegalNameEn,
    string? TradeNameAr,
    string? TradeNameEn,
    string? ContactPhone,
    string? ContactEmail,
    string? CommercialRegistrationNumber,
    DateOnly? CommercialRegistrationExpiry,
    string? CommercialLicenseNumber,
    DateOnly? CommercialLicenseExpiry,
    string? FoodLicenseNumber,
    DateOnly? FoodLicenseExpiry,
    string? LegalEntityType,
    string? CompanyCivilId,
    string? OwnerName,
    string? AuthorizedSignatoryCivilId,
    string? AuthorizedSignatoryTitle,
    string? PrimaryContactName,
    string? PrimaryContactTitle,
    string? PrimaryContactPhone,
    string? PrimaryContactEmail,
    string? BankName,
    string? BankBeneficiaryName,
    string? Iban,
    string? BankAccountNumber,
    string? SwiftCode,
    string? Address,
    Guid? CountryId,
    Guid? RegionId,
    string? Website = null,
    string? InstagramHandle = null,
    bool? DeliveryByRestaurantConfirmed = null,
    bool? DataResponsibilityConfirmed = null,
    string? RestaurantNotes = null,
    List<string>? ServiceRegionIds = null,
    decimal? Latitude = null,
    decimal? Longitude = null);

public sealed record RestaurantSettingsChangeRequestDto(
    Guid Id,
    VerificationStatus Status,
    DateTime? SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    string? AdminNotes,
    IReadOnlyList<string>? ChangedFields = null,
    long? BaseVersion = null);

public sealed record RestaurantSettingsResponseDto(
    MyRestaurantRegistrationDto Live,
    RestaurantSettingsDraftPayload? Draft,
    RestaurantSettingsChangeRequestDto? ChangeRequest,
    IReadOnlyList<RestaurantReviewNotificationDto> ReviewNotifications);

public sealed record GetRestaurantSettingsQuery : IQuery<RestaurantSettingsResponseDto>;
public sealed record SaveRestaurantSettingsDraftCommand(RestaurantSettingsDraftPayload Payload) : ICommand<RestaurantSettingsResponseDto>;
public sealed record SubmitRestaurantSettingsDraftCommand : ICommand<RestaurantSettingsResponseDto>;
public sealed record AdminApproveRestaurantSettingsChangeCommand(Guid RestaurantId, string? Reason) : ICommand<bool>;
public sealed record AdminRequestChangesRestaurantSettingsChangeCommand(Guid RestaurantId, string Reason) : ICommand<bool>;

public sealed class SaveRestaurantSettingsDraftCommandValidator : AbstractValidator<SaveRestaurantSettingsDraftCommand>
{
    public SaveRestaurantSettingsDraftCommandValidator()
    {
        RuleFor(x => x.Payload.ContactEmail)
            .Must(value => string.IsNullOrWhiteSpace(value) || RestaurantRegistrationRules.IsValidEmail(value))
            .WithMessageCode(MessageCodes.Validation.EmailInvalid);
        RuleFor(x => x.Payload.PrimaryContactEmail)
            .Must(value => string.IsNullOrWhiteSpace(value) || RestaurantRegistrationRules.IsValidEmail(value))
            .WithMessageCode(MessageCodes.Validation.EmailInvalid);
        RuleFor(x => x.Payload.Iban)
            .Must(value => string.IsNullOrWhiteSpace(value) || RestaurantRegistrationRules.IsValidKuwaitIban(value))
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);
        RuleFor(x => x.Payload.Website)
            .Must(RestaurantRegistrationRules.IsValidHttpsWebsite)
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);
    }
}

internal static class RestaurantSettingsChangeRules
{
    private static readonly VerificationStatus[] OpenStatuses =
    [
        VerificationStatus.Draft,
        VerificationStatus.Submitted,
        VerificationStatus.UnderReview,
        VerificationStatus.NeedsChanges,
    ];

    private static readonly VerificationStatus[] EditableStatuses =
    [
        VerificationStatus.Draft,
        VerificationStatus.NeedsChanges,
    ];

    public static bool IsOpen(VerificationStatus status) => OpenStatuses.Contains(status);

    public static bool IsEditable(VerificationStatus status) => EditableStatuses.Contains(status);

    public static string? MaskBankValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var trimmed = value.Trim();
        if (trimmed.Length <= 4) return "****";
        return string.Concat("****", trimmed[^4..]);
    }

    public static List<string> ComputeChangedFields(
        Domain.IdentityAccess.RestaurantRegistration current,
        RestaurantSettingsDraftPayload proposed)
    {
        var changes = new List<string>();
        if (current.LegalNameAr != proposed.LegalNameAr) changes.Add("LegalNameAr");
        if (current.LegalNameEn != proposed.LegalNameEn) changes.Add("LegalNameEn");
        if (current.TradeNameAr != proposed.TradeNameAr) changes.Add("TradeNameAr");
        if (current.TradeNameEn != proposed.TradeNameEn) changes.Add("TradeNameEn");
        if (current.ContactPhone != proposed.ContactPhone) changes.Add("ContactPhone");
        if (current.ContactEmail != proposed.ContactEmail) changes.Add("ContactEmail");
        if (current.CommercialRegistrationNumber != proposed.CommercialRegistrationNumber) changes.Add("CommercialRegistrationNumber");
        if (current.CommercialRegistrationExpiry != proposed.CommercialRegistrationExpiry) changes.Add("CommercialRegistrationExpiry");
        if (current.CommercialLicenseNumber != proposed.CommercialLicenseNumber) changes.Add("CommercialLicenseNumber");
        if (current.CommercialLicenseExpiry != proposed.CommercialLicenseExpiry) changes.Add("CommercialLicenseExpiry");
        if (current.FoodLicenseNumber != proposed.FoodLicenseNumber) changes.Add("FoodLicenseNumber");
        if (current.FoodLicenseExpiry != proposed.FoodLicenseExpiry) changes.Add("FoodLicenseExpiry");
        if (current.Address != proposed.Address) changes.Add("Address");
        if (current.Latitude != proposed.Latitude) changes.Add("Latitude");
        if (current.Longitude != proposed.Longitude) changes.Add("Longitude");
        if (current.BankName != proposed.BankName) changes.Add("BankName");
        if (current.BankBeneficiaryName != proposed.BankBeneficiaryName) changes.Add("BankBeneficiaryName");
        if (current.Iban != proposed.Iban) changes.Add("Iban");
        if (current.BankAccountNumber != proposed.BankAccountNumber) changes.Add("BankAccountNumber");
        if (current.SwiftCode != proposed.SwiftCode) changes.Add("SwiftCode");
        return changes;
    }

    public static string Serialize(RestaurantSettingsDraftPayload payload) =>
        JsonSerializer.Serialize(payload);

    public static RestaurantSettingsDraftPayload Deserialize(string json) =>
        JsonSerializer.Deserialize<RestaurantSettingsDraftPayload>(json)
        ?? throw new InvalidOperationException("Invalid settings draft payload.");

    public static RestaurantSettingsDraftPayload FromRegistration(Domain.IdentityAccess.RestaurantRegistration registration)
        => new(
            registration.LegalNameAr,
            registration.LegalNameEn,
            registration.TradeNameAr,
            registration.TradeNameEn,
            registration.ContactPhone,
            registration.ContactEmail,
            registration.CommercialRegistrationNumber,
            registration.CommercialRegistrationExpiry,
            registration.CommercialLicenseNumber,
            registration.CommercialLicenseExpiry,
            registration.FoodLicenseNumber,
            registration.FoodLicenseExpiry,
            RestaurantRegistrationRules.NormalizeLegalEntityTypeForOutput(registration.LegalEntityType),
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
            registration.Iban,
            registration.BankAccountNumber,
            registration.SwiftCode,
            registration.Address,
            registration.CountryId,
            registration.RegionId,
            registration.Website,
            registration.InstagramHandle,
            registration.DeliveryByRestaurantConfirmed,
            registration.DataResponsibilityConfirmed,
            registration.RestaurantNotes,
            RestaurantRegistrationMapping.ParseServiceRegionIds(registration.ServiceRegionIdsJson)
                .Select(id => id.ToString())
                .ToList(),
            registration.Latitude,
            registration.Longitude);

    public static async Task<Result<(Domain.IdentityAccess.RestaurantRegistration Registration, RestaurantProfile Profile)>> LoadApprovedContextAsync(
        IAppDbContext db,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "Authentication is required.");

        var registration = await db.RestaurantRegistrations
            .Include(x => x.FieldReviews)
            .Where(x => x.SubmittedByUserId == currentUser.UserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
            return Error.NotFound(MessageCodes.Auth.InvalidState, "No registration found.");

        if (registration.Status != VerificationStatus.Approved || registration.ResultingRestaurantId is not Guid restaurantId)
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Settings changes are only available for approved restaurants.");

        var profile = await db.RestaurantProfiles.FirstOrDefaultAsync(x => x.Id == restaurantId, cancellationToken);
        if (profile is null)
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Restaurant profile was not found.");

        return (registration, profile);
    }
}

public sealed class RestaurantSettingsHandlers :
    IQueryHandler<GetRestaurantSettingsQuery, RestaurantSettingsResponseDto>,
    ICommandHandler<SaveRestaurantSettingsDraftCommand, RestaurantSettingsResponseDto>,
    ICommandHandler<SubmitRestaurantSettingsDraftCommand, RestaurantSettingsResponseDto>,
    ICommandHandler<AdminApproveRestaurantSettingsChangeCommand, bool>,
    ICommandHandler<AdminRequestChangesRestaurantSettingsChangeCommand, bool>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly KuwaitTenantResolver _tenantResolver;
    private readonly IRegistrationReviewNotifier _notifier;

    public RestaurantSettingsHandlers(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        KuwaitTenantResolver tenantResolver,
        IRegistrationReviewNotifier? notifier = null)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _tenantResolver = tenantResolver;
        _notifier = notifier ?? NullRegistrationReviewNotifier.Instance;
    }

    public async Task<Result<RestaurantSettingsResponseDto>> Handle(
        GetRestaurantSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var context = await RestaurantSettingsChangeRules.LoadApprovedContextAsync(_db, _currentUser, cancellationToken);
        if (context.IsFailure) return context.Error;
        var (registration, _) = context.Value;
        return await BuildResponseAsync(registration, cancellationToken);
    }

    public async Task<Result<RestaurantSettingsResponseDto>> Handle(
        SaveRestaurantSettingsDraftCommand request,
        CancellationToken cancellationToken)
    {
        var context = await RestaurantSettingsChangeRules.LoadApprovedContextAsync(_db, _currentUser, cancellationToken);
        if (context.IsFailure) return context.Error;
        var (registration, profile) = context.Value;

        if (!RestaurantRegistrationRules.TryNormalizeLegalEntityType(request.Payload.LegalEntityType, out _))
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Legal entity type is invalid.");

        var open = await FindOpenChangeRequestAsync(profile.Id, cancellationToken);
        if (open is not null && !RestaurantSettingsChangeRules.IsEditable(open.Status))
        {
            return Error.Conflict(
                MessageCodes.Auth.InvalidState,
                "A settings change request is already under review.");
        }

        var change = open ?? new RestaurantSettingsChangeRequest
        {
            RestaurantId = profile.Id,
            RegistrationId = registration.Id,
            Status = VerificationStatus.Draft,
            CreatedAtUtc = _clock.UtcNow,
        };

        var changedFields = RestaurantSettingsChangeRules.ComputeChangedFields(registration, request.Payload);
        change.PayloadJson = RestaurantSettingsChangeRules.Serialize(request.Payload);
        change.Status = VerificationStatus.Draft;
        change.Category = RestaurantChangeCategory.Profile;
        change.BaseVersion = registration.ApprovedVersion;
        change.PayloadVersion = 1;
        change.ChangedFieldsJson = JsonSerializer.Serialize(changedFields);
        change.AdminNotes = null;
        change.ModifiedAtUtc = _clock.UtcNow;

        if (open is null)
            _db.RestaurantSettingsChangeRequests.Add(change);

        await _audit.LogAsync(new AuditEntry(
            "RestaurantSettingsDraftSaved",
            nameof(RestaurantSettingsChangeRequest),
            change.Id,
            SourceChannel: "restaurant"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return await BuildResponseAsync(registration, cancellationToken);
    }

    public async Task<Result<RestaurantSettingsResponseDto>> Handle(
        SubmitRestaurantSettingsDraftCommand request,
        CancellationToken cancellationToken)
    {
        var context = await RestaurantSettingsChangeRules.LoadApprovedContextAsync(_db, _currentUser, cancellationToken);
        if (context.IsFailure) return context.Error;
        var (registration, profile) = context.Value;

        var change = await FindOpenChangeRequestAsync(profile.Id, cancellationToken);
        if (change is null || !RestaurantSettingsChangeRules.IsEditable(change.Status))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "No editable settings draft was found.");
        }

        var payload = RestaurantSettingsChangeRules.Deserialize(change.PayloadJson);
        var validation = ValidatePayloadForSubmit(payload);
        if (validation.IsFailure) return validation.Error;

        change.Status = VerificationStatus.Submitted;
        change.SubmittedAtUtc = _clock.UtcNow;
        change.ModifiedAtUtc = _clock.UtcNow;

        var reviewItem = await _db.RestaurantReviewItems.FirstOrDefaultAsync(
            x => x.RestaurantId == profile.Id
                && x.Section == "restaurant-settings"
                && x.SubjectType == "settings-change"
                && x.SubjectId == change.Id,
            cancellationToken);

        if (reviewItem is null)
        {
            reviewItem = new RestaurantReviewItem
            {
                RestaurantId = profile.Id,
                RegistrationId = registration.Id,
                Section = "restaurant-settings",
                SubjectType = "settings-change",
                SubjectId = change.Id,
                Status = VerificationStatus.Submitted,
                SubmittedAtUtc = _clock.UtcNow,
                CreatedAtUtc = _clock.UtcNow,
            };
            _db.RestaurantReviewItems.Add(reviewItem);
        }
        else
        {
            reviewItem.Status = VerificationStatus.Submitted;
            reviewItem.SubmittedAtUtc = _clock.UtcNow;
            reviewItem.Reason = null;
            reviewItem.ReviewedAtUtc = null;
            reviewItem.ReviewedByUserId = null;
        }

        await _audit.LogAsync(new AuditEntry(
            "RestaurantSettingsChangeSubmitted",
            nameof(RestaurantSettingsChangeRequest),
            change.Id,
            SourceChannel: "restaurant"), cancellationToken);

        if (registration.SubmittedByUserId is { } userId)
        {
            await _notifier.NotifyRegistrationUpdated(userId, registration.Id, "settings-submitted", cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await BuildResponseAsync(registration, cancellationToken);
    }

    public async Task<Result<bool>> Handle(
        AdminApproveRestaurantSettingsChangeCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.HasPermission(PermissionCodes.RestaurantRegApprove))
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");

        var change = await FindReviewableChangeRequestAsync(request.RestaurantId, cancellationToken);
        if (change is null)
            return Error.Conflict(MessageCodes.Auth.InvalidState, "No reviewable settings change was found.");

        var registration = await _db.RestaurantRegistrations
            .Include(x => x.FieldReviews)
            .FirstOrDefaultAsync(x => x.Id == change.RegistrationId, cancellationToken);
        if (registration is null)
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");

        var profile = await _db.RestaurantProfiles.FirstOrDefaultAsync(x => x.Id == change.RestaurantId, cancellationToken);
        if (profile is null)
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Restaurant profile was not found.");

        var payload = RestaurantSettingsChangeRules.Deserialize(change.PayloadJson);
        var apply = await ApplyPayloadAsync(registration, profile, payload, cancellationToken);
        if (apply.IsFailure) return apply.Error;

        registration.ApprovedVersion++;
        profile.ApprovedVersion = registration.ApprovedVersion;

        change.Status = VerificationStatus.Approved;
        change.ReviewedAtUtc = _clock.UtcNow;
        change.ReviewedByUserId = _currentUser.UserId;
        change.AdminNotes = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        change.ModifiedAtUtc = _clock.UtcNow;
        registration.ModifiedAtUtc = _clock.UtcNow;

        var decisionEvent = new RestaurantChangeDecisionEvent(
            "Approved",
            change.AdminNotes,
            _currentUser.UserId,
            _clock.UtcNow,
            registration.ApprovedVersion,
            change.PayloadVersion);

        var events = string.IsNullOrWhiteSpace(change.DecisionEventsJson)
            ? new List<RestaurantChangeDecisionEvent>()
            : JsonSerializer.Deserialize<List<RestaurantChangeDecisionEvent>>(change.DecisionEventsJson) ?? new();
        events.Add(decisionEvent);
        change.DecisionEventsJson = JsonSerializer.Serialize(events);

        var reviewItem = await _db.RestaurantReviewItems.FirstOrDefaultAsync(
            x => x.RestaurantId == profile.Id
                && x.Section == "restaurant-settings"
                && x.SubjectId == change.Id,
            cancellationToken);
        if (reviewItem is not null)
        {
            reviewItem.Status = VerificationStatus.Approved;
            reviewItem.ReviewedAtUtc = _clock.UtcNow;
            reviewItem.ReviewedByUserId = _currentUser.UserId;
            reviewItem.Reason = null;
        }

        await _audit.LogAsync(new AuditEntry(
            "RestaurantSettingsChangeApproved",
            nameof(RestaurantSettingsChangeRequest),
            change.Id,
            Reason: change.AdminNotes,
            SourceChannel: "admin"), cancellationToken);

        if (registration.SubmittedByUserId is { } userId)
        {
            await _notifier.NotifyRegistrationUpdated(userId, registration.Id, "settings-approved", cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<bool>> Handle(
        AdminRequestChangesRestaurantSettingsChangeCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.HasPermission(PermissionCodes.RestaurantRegApprove))
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(reason))
            return Error.Validation(MessageCodes.Validation.Required, "Reason is required.");

        var change = await FindReviewableChangeRequestAsync(request.RestaurantId, cancellationToken);
        if (change is null)
            return Error.Conflict(MessageCodes.Auth.InvalidState, "No reviewable settings change was found.");

        var registration = await _db.RestaurantRegistrations
            .FirstOrDefaultAsync(x => x.Id == change.RegistrationId, cancellationToken);
        if (registration is null)
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");

        change.Status = VerificationStatus.NeedsChanges;
        change.AdminNotes = reason;
        change.ReviewedAtUtc = _clock.UtcNow;
        change.ReviewedByUserId = _currentUser.UserId;
        change.ModifiedAtUtc = _clock.UtcNow;

        var decisionEvent = new RestaurantChangeDecisionEvent(
            "NeedsChanges",
            reason,
            _currentUser.UserId,
            _clock.UtcNow,
            change.BaseVersion,
            change.PayloadVersion);

        var events = string.IsNullOrWhiteSpace(change.DecisionEventsJson)
            ? new List<RestaurantChangeDecisionEvent>()
            : JsonSerializer.Deserialize<List<RestaurantChangeDecisionEvent>>(change.DecisionEventsJson) ?? new();
        events.Add(decisionEvent);
        change.DecisionEventsJson = JsonSerializer.Serialize(events);

        var reviewItem = await _db.RestaurantReviewItems.FirstOrDefaultAsync(
            x => x.RestaurantId == change.RestaurantId
                && x.Section == "restaurant-settings"
                && x.SubjectId == change.Id,
            cancellationToken);
        if (reviewItem is not null)
        {
            reviewItem.Status = VerificationStatus.NeedsChanges;
            reviewItem.Reason = reason;
            reviewItem.ReviewedAtUtc = _clock.UtcNow;
            reviewItem.ReviewedByUserId = _currentUser.UserId;
        }

        await _audit.LogAsync(new AuditEntry(
            "RestaurantSettingsChangeRejected",
            nameof(RestaurantSettingsChangeRequest),
            change.Id,
            Reason: reason,
            SourceChannel: "admin"), cancellationToken);

        if (registration.SubmittedByUserId is { } userId)
        {
            await _notifier.NotifyRegistrationUpdated(userId, registration.Id, "settings-changes-requested", cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<RestaurantSettingsChangeRequest?> FindReviewableChangeRequestAsync(
        Guid restaurantId,
        CancellationToken cancellationToken)
        => await _db.RestaurantSettingsChangeRequests
            .Where(x => x.RestaurantId == restaurantId
                && (x.Status == VerificationStatus.Submitted || x.Status == VerificationStatus.UnderReview))
            .OrderByDescending(x => x.SubmittedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private static readonly VerificationStatus[] OpenChangeRequestStatuses =
    [
        VerificationStatus.Draft,
        VerificationStatus.Submitted,
        VerificationStatus.UnderReview,
        VerificationStatus.NeedsChanges,
    ];

    private async Task<RestaurantSettingsChangeRequest?> FindOpenChangeRequestAsync(
        Guid restaurantId,
        CancellationToken cancellationToken)
        => await _db.RestaurantSettingsChangeRequests
            .Where(x => x.RestaurantId == restaurantId && OpenChangeRequestStatuses.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Result<RestaurantSettingsResponseDto>> BuildResponseAsync(
        Domain.IdentityAccess.RestaurantRegistration registration,
        CancellationToken cancellationToken)
    {
        var docs = await _db.Documents
            .Include(d => d.Verifications)
            .AsNoTracking()
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
            .ToListAsync(cancellationToken);

        var summaries = docs.Select(d =>
        {
            var latest = d.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
            return new MyRegistrationDocumentSummaryDto(
                d.Id,
                d.DocumentType,
                d.FileName,
                d.CreatedAtUtc,
                d.ExpiresAtUtc,
                latest?.Status,
                latest?.Reason,
                latest?.ReviewedAtUtc);
        }).ToList();

        var fieldReviews = await _db.RestaurantRegistrationFieldReviews
            .AsNoTracking()
            .Where(f => f.RegistrationId == registration.Id)
            .ToListAsync(cancellationToken);

        var canViewSensitiveBank = _currentUser.HasPermission(PermissionCodes.RestaurantRegApprove)
            || _currentUser.HasPermission(PermissionCodes.SubscriptionsPricing);

        var liveIban = canViewSensitiveBank
            ? registration.Iban
            : RestaurantSettingsChangeRules.MaskBankValue(registration.Iban);

        var liveAccountNumber = canViewSensitiveBank
            ? registration.BankAccountNumber
            : RestaurantSettingsChangeRules.MaskBankValue(registration.BankAccountNumber);

        var live = new MyRestaurantRegistrationDto(
            registration.Id,
            registration.LegalNameAr,
            registration.LegalNameEn,
            registration.TradeNameAr,
            registration.TradeNameEn,
            registration.ContactPhone,
            registration.ContactEmail,
            registration.CommercialRegistrationNumber,
            registration.CommercialRegistrationExpiry,
            registration.CommercialLicenseNumber,
            registration.CommercialLicenseExpiry,
            registration.FoodLicenseNumber,
            registration.FoodLicenseExpiry,
            RestaurantRegistrationRules.NormalizeLegalEntityTypeForOutput(registration.LegalEntityType),
            registration.CompanyCivilId,
            registration.Website,
            registration.InstagramHandle,
            string.IsNullOrWhiteSpace(registration.RestaurantCode)
                ? RestaurantRegistrationRules.CreateRestaurantCode(registration.Id)
                : registration.RestaurantCode,
            registration.OwnerName,
            registration.AuthorizedSignatoryCivilId,
            registration.AuthorizedSignatoryTitle,
            registration.PrimaryContactName,
            registration.PrimaryContactTitle,
            registration.PrimaryContactPhone,
            registration.PrimaryContactEmail,
            registration.BankName,
            registration.BankBeneficiaryName,
            liveIban,
            liveAccountNumber,
            registration.SwiftCode,
            RestaurantRegistrationRules.SettlementCurrency,
            registration.Address,
            registration.CountryId,
            registration.RegionId,
            registration.Status,
            registration.RejectionReason,
            registration.ChangeRequestNotes,
            registration.ResultingRestaurantId,
            registration.CreatedAtUtc,
            registration.ModifiedAtUtc,
            registration.ReviewedAtUtc,
            registration.DeliveryByRestaurantConfirmed,
            registration.DataResponsibilityConfirmed,
            registration.SubmissionDate,
            registration.RestaurantNotes,
            summaries,
            fieldReviews.Select(RestaurantRegistrationMapping.MapFieldReview).ToList(),
            RestaurantRegistrationMapping.ParseServiceRegionIds(registration.ServiceRegionIdsJson));

        RestaurantSettingsChangeRequest? change = null;
        if (registration.ResultingRestaurantId is Guid restaurantId)
        {
            change = await FindOpenChangeRequestAsync(restaurantId, cancellationToken);
        }

        RestaurantSettingsDraftPayload? draft = null;
        if (change is not null)
        {
            var rawDraft = RestaurantSettingsChangeRules.Deserialize(change.PayloadJson);
            draft = canViewSensitiveBank
                ? rawDraft
                : rawDraft with
                {
                    Iban = RestaurantSettingsChangeRules.MaskBankValue(rawDraft.Iban),
                    BankAccountNumber = RestaurantSettingsChangeRules.MaskBankValue(rawDraft.BankAccountNumber),
                };
        }

        IReadOnlyList<string>? changedFields = null;
        if (!string.IsNullOrWhiteSpace(change?.ChangedFieldsJson))
        {
            try { changedFields = JsonSerializer.Deserialize<List<string>>(change.ChangedFieldsJson); } catch { }
        }

        RestaurantSettingsChangeRequestDto? changeDto = change is null
            ? null
            : new RestaurantSettingsChangeRequestDto(
                change.Id,
                change.Status,
                change.SubmittedAtUtc,
                change.ReviewedAtUtc,
                change.AdminNotes,
                changedFields,
                change.BaseVersion);

        var notifications = _currentUser.UserId is Guid userId
            ? await _db.RestaurantNotifications.AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.SentAtUtc)
                .Take(20)
                .Select(x => new RestaurantReviewNotificationDto(
                    x.Id, x.Type, x.TitleAr, x.TitleEn, x.BodyAr, x.BodyEn,
                    x.Section, x.SubjectId, x.IsRead, x.SentAtUtc, x.DeepLink))
                .ToListAsync(cancellationToken)
            : [];

        return new RestaurantSettingsResponseDto(live, draft, changeDto, notifications);
    }

    private static Result<bool> ValidatePayloadForSubmit(RestaurantSettingsDraftPayload payload)
    {
        if (!RestaurantRegistrationRules.TryNormalizeLegalEntityType(payload.LegalEntityType, out var legalEntityType)
            || legalEntityType is null)
        {
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Legal entity type is invalid.");
        }

        var requiredMissing = RegistrationFieldKeys.RequiredForSubmission.FirstOrDefault(key => key switch
        {
            RegistrationFieldKeys.LegalNameAr => string.IsNullOrWhiteSpace(payload.LegalNameAr),
            RegistrationFieldKeys.LegalNameEn => string.IsNullOrWhiteSpace(payload.LegalNameEn),
            RegistrationFieldKeys.TradeNameAr => string.IsNullOrWhiteSpace(payload.TradeNameAr),
            RegistrationFieldKeys.TradeNameEn => string.IsNullOrWhiteSpace(payload.TradeNameEn),
            RegistrationFieldKeys.LegalEntityType => string.IsNullOrWhiteSpace(payload.LegalEntityType),
            RegistrationFieldKeys.CompanyCivilId => string.IsNullOrWhiteSpace(payload.CompanyCivilId),
            RegistrationFieldKeys.CommercialRegistrationNumber => string.IsNullOrWhiteSpace(payload.CommercialRegistrationNumber),
            RegistrationFieldKeys.CommercialRegistrationExpiry => payload.CommercialRegistrationExpiry is null,
            RegistrationFieldKeys.CommercialLicenseNumber => string.IsNullOrWhiteSpace(payload.CommercialLicenseNumber),
            RegistrationFieldKeys.CommercialLicenseExpiry => payload.CommercialLicenseExpiry is null,
            RegistrationFieldKeys.FoodLicenseNumber => string.IsNullOrWhiteSpace(payload.FoodLicenseNumber),
            RegistrationFieldKeys.FoodLicenseExpiry => payload.FoodLicenseExpiry is null,
            RegistrationFieldKeys.HeadquartersAddressAr => string.IsNullOrWhiteSpace(payload.Address),
            RegistrationFieldKeys.ContactPhone => string.IsNullOrWhiteSpace(payload.ContactPhone),
            RegistrationFieldKeys.ContactEmail => string.IsNullOrWhiteSpace(payload.ContactEmail),
            RegistrationFieldKeys.PrimaryContactName => string.IsNullOrWhiteSpace(payload.PrimaryContactName),
            RegistrationFieldKeys.PrimaryContactTitle => string.IsNullOrWhiteSpace(payload.PrimaryContactTitle),
            RegistrationFieldKeys.PrimaryContactPhone => string.IsNullOrWhiteSpace(payload.PrimaryContactPhone),
            RegistrationFieldKeys.PrimaryContactEmail => string.IsNullOrWhiteSpace(payload.PrimaryContactEmail),
            RegistrationFieldKeys.OwnerName => string.IsNullOrWhiteSpace(payload.OwnerName),
            RegistrationFieldKeys.AuthorizedSignatoryCivilId => string.IsNullOrWhiteSpace(payload.AuthorizedSignatoryCivilId),
            RegistrationFieldKeys.AuthorizedSignatoryTitle => string.IsNullOrWhiteSpace(payload.AuthorizedSignatoryTitle),
            RegistrationFieldKeys.BankName => string.IsNullOrWhiteSpace(payload.BankName),
            RegistrationFieldKeys.BeneficiaryName => string.IsNullOrWhiteSpace(payload.BankBeneficiaryName),
            RegistrationFieldKeys.Iban => string.IsNullOrWhiteSpace(payload.Iban),
            RegistrationFieldKeys.AccountNumber => string.IsNullOrWhiteSpace(payload.BankAccountNumber),
            RegistrationFieldKeys.Latitude => payload.Latitude is null
                || !RestaurantRegistrationRules.IsWithinKuwaitBounds(payload.Latitude, payload.Longitude),
            RegistrationFieldKeys.Longitude => payload.Longitude is null
                || !RestaurantRegistrationRules.IsWithinKuwaitBounds(payload.Latitude, payload.Longitude),
            _ => false,
        });

        if (requiredMissing is not null)
        {
            return Error.Validation(MessageCodes.Validation.RequiredField, $"Required field is missing: {requiredMissing}");
        }

        if (payload.DeliveryByRestaurantConfirmed is not true || payload.DataResponsibilityConfirmed is not true)
        {
            return Error.Validation(MessageCodes.Validation.RequiredField, "Required confirmations must be accepted.");
        }

        if (!RestaurantRegistrationRules.IsValidEmail(payload.ContactEmail)
            || !RestaurantRegistrationRules.IsValidEmail(payload.PrimaryContactEmail))
        {
            return Error.Validation(MessageCodes.Validation.EmailInvalid, "A registration email address is invalid.");
        }

        if (!RestaurantRegistrationRules.IsValidKuwaitIban(RestaurantRegistrationRules.NormalizeIban(payload.Iban)))
        {
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "IBAN is invalid.");
        }

        return true;
    }

    private async Task<Result<bool>> ApplyPayloadAsync(
        Domain.IdentityAccess.RestaurantRegistration registration,
        RestaurantProfile profile,
        RestaurantSettingsDraftPayload payload,
        CancellationToken cancellationToken)
    {
        if (!RestaurantRegistrationRules.TryNormalizeLegalEntityType(payload.LegalEntityType, out var legalEntityType))
            return Error.Validation(MessageCodes.Validation.InvalidFormat, "Legal entity type is invalid.");

        var commercialRegistrationNumber = RestaurantRegistrationRules.NormalizeCommercialRegistrationNumber(
            payload.CommercialRegistrationNumber);
        if (commercialRegistrationNumber is not null
            && await _db.RestaurantRegistrations.AnyAsync(
                r => r.Id != registration.Id
                     && r.CommercialRegistrationNumber != null
                     && r.CommercialRegistrationNumber.Trim().ToUpper() == commercialRegistrationNumber
                     && r.Status != VerificationStatus.Rejected
                     && r.Status != VerificationStatus.Expired,
                cancellationToken))
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateCommercialRegistration, "Commercial registration already used.");
        }

        var tenantResult = await _tenantResolver.ResolveAsync(cancellationToken);
        if (tenantResult.IsFailure) return tenantResult.Error;

        var selection = await RegionCatalogRules.ValidateRestaurantSelectionAsync(
            _db,
            payload.CountryId,
            payload.RegionId,
            payload.ServiceRegionIds ?? [],
            false,
            tenantResult.Value.CountryId,
            cancellationToken);
        if (selection.Error is not null) return selection.Error;

        registration.LegalNameAr = RestaurantRegistrationRules.NormalizeOptional(payload.LegalNameAr);
        registration.LegalNameEn = RestaurantRegistrationRules.NormalizeOptional(payload.LegalNameEn);
        registration.TradeNameAr = RestaurantRegistrationRules.NormalizeOptional(payload.TradeNameAr);
        registration.TradeNameEn = RestaurantRegistrationRules.NormalizeOptional(payload.TradeNameEn);
        registration.LegalEntityType = legalEntityType;
        registration.CompanyCivilId = RestaurantRegistrationRules.NormalizeOptional(payload.CompanyCivilId);
        registration.CommercialRegistrationNumber = commercialRegistrationNumber;
        registration.CommercialRegistrationExpiry = payload.CommercialRegistrationExpiry;
        registration.CommercialLicenseNumber = RestaurantRegistrationRules.NormalizeOptional(payload.CommercialLicenseNumber);
        registration.CommercialLicenseExpiry = payload.CommercialLicenseExpiry;
        registration.FoodLicenseNumber = RestaurantRegistrationRules.NormalizeOptional(payload.FoodLicenseNumber);
        registration.FoodLicenseExpiry = payload.FoodLicenseExpiry;
        registration.ContactPhone = RestaurantRegistrationRules.NormalizeOptional(payload.ContactPhone);
        registration.ContactEmail = RestaurantRegistrationRules.NormalizeOptional(payload.ContactEmail)?.ToLowerInvariant();
        registration.Website = RestaurantRegistrationRules.NormalizeOptional(payload.Website);
        registration.InstagramHandle = RestaurantRegistrationRules.NormalizeOptional(payload.InstagramHandle);
        registration.PrimaryContactName = RestaurantRegistrationRules.NormalizeOptional(payload.PrimaryContactName);
        registration.PrimaryContactTitle = RestaurantRegistrationRules.NormalizeOptional(payload.PrimaryContactTitle);
        registration.PrimaryContactPhone = RestaurantRegistrationRules.NormalizeOptional(payload.PrimaryContactPhone);
        registration.PrimaryContactEmail = RestaurantRegistrationRules.NormalizeOptional(payload.PrimaryContactEmail)?.ToLowerInvariant();
        registration.OwnerName = RestaurantRegistrationRules.NormalizeOptional(payload.OwnerName);
        registration.AuthorizedSignatoryCivilId = RestaurantRegistrationRules.NormalizeOptional(payload.AuthorizedSignatoryCivilId);
        registration.AuthorizedSignatoryTitle = RestaurantRegistrationRules.NormalizeOptional(payload.AuthorizedSignatoryTitle);
        registration.BankName = RestaurantRegistrationRules.NormalizeOptional(payload.BankName);
        registration.BankBeneficiaryName = RestaurantRegistrationRules.NormalizeOptional(payload.BankBeneficiaryName);
        registration.Iban = RestaurantRegistrationRules.NormalizeIban(payload.Iban);
        registration.BankAccountNumber = RestaurantRegistrationRules.NormalizeOptional(payload.BankAccountNumber);
        registration.SwiftCode = RestaurantRegistrationRules.NormalizeOptional(payload.SwiftCode)?.ToUpperInvariant();
        registration.Address = RestaurantRegistrationRules.NormalizeOptional(payload.Address);
        registration.Latitude = payload.Latitude;
        registration.Longitude = payload.Longitude;
        registration.CountryId = payload.CountryId;
        registration.RegionId = payload.RegionId;
        registration.DeliveryByRestaurantConfirmed = payload.DeliveryByRestaurantConfirmed;
        registration.DataResponsibilityConfirmed = payload.DataResponsibilityConfirmed;
        registration.RestaurantNotes = RestaurantRegistrationRules.NormalizeOptional(payload.RestaurantNotes);
        registration.ServiceRegionIdsJson = selection.Ids.Count > 0
            ? JsonSerializer.Serialize(selection.Ids.Select(id => id.ToString()).ToList())
            : null;

        profile.LegalNameAr = registration.LegalNameAr ?? profile.LegalNameAr;
        profile.LegalNameEn = registration.LegalNameEn ?? profile.LegalNameEn;
        profile.TradeNameAr = registration.TradeNameAr ?? profile.TradeNameAr;
        profile.TradeNameEn = registration.TradeNameEn ?? profile.TradeNameEn;
        profile.CommercialLicenseNumber = registration.CommercialLicenseNumber;
        profile.CountryId = registration.CountryId;
        profile.PrimaryRegionId = registration.RegionId;
        profile.Latitude = registration.Latitude;
        profile.Longitude = registration.Longitude;
        profile.ContactPhone = registration.ContactPhone;
        profile.ContactEmail = registration.ContactEmail;
        profile.ModifiedAtUtc = _clock.UtcNow;

        await SyncServiceRegionsAsync(profile.Id, selection.Ids, cancellationToken);
        return true;
    }

    private async Task SyncServiceRegionsAsync(
        Guid restaurantId,
        IReadOnlyList<Guid> regionIds,
        CancellationToken cancellationToken)
    {
        var deliverySettings = await _db.RestaurantProfiles.AsNoTracking().FirstAsync(x => x.Id == restaurantId, cancellationToken);
        var existing = await _db.RestaurantServiceRegions
            .Where(x => x.RestaurantId == restaurantId)
            .ToListAsync(cancellationToken);

        var target = regionIds.ToHashSet();
        foreach (var item in existing)
        {
            item.IsActive = target.Contains(item.RegionId);
            item.ModifiedAtUtc = _clock.UtcNow;
        }

        var existingIds = existing.Select(x => x.RegionId).ToHashSet();
        foreach (var regionId in target.Where(id => !existingIds.Contains(id)))
        {
            _db.RestaurantServiceRegions.Add(new RestaurantServiceRegion
            {
                RestaurantId = restaurantId,
                RegionId = regionId,
                IsActive = true,
                DeliveryWindowStart = deliverySettings.DeliveryWindowStart ?? new TimeOnly(8, 0),
                DeliveryWindowEnd = deliverySettings.DeliveryWindowEnd ?? new TimeOnly(14, 0),
                PreparationLeadTimeMinutes = deliverySettings.PreparationLeadTimeMinutes,
                CreatedAtUtc = _clock.UtcNow,
            });
        }
    }
}
