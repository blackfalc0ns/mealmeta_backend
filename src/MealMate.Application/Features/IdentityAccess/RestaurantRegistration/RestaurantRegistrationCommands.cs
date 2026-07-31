using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Validation;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.RestaurantRegistration;

/// <summary>Summary of a per-field review.</summary>
public sealed record FieldReviewSummaryDto(
    string FieldKey,
    VerificationStatus Status,
    string? Reason,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc);

/// <summary>Canonical field keys for restaurant registration review.</summary>
public static class RegistrationFieldKeys
{
    public const string LegalNameAr = "legalNameAr";
    public const string LegalNameEn = "legalNameEn";
    public const string TradeNameAr = "tradeNameAr";
    public const string TradeNameEn = "tradeNameEn";
    public const string ContactPhone = "contactPhone";
    public const string ContactEmail = "contactEmail";
    public const string CommercialRegistrationNumber = "commercialRegistrationNumber";
    public const string OwnerName = "ownerName";
    public const string Address = "address";
    public const string CountryId = "countryId";
    public const string RegionId = "regionId";

    /// <summary>All canonical keys in the order they appear on the registration form.</summary>
    public static readonly string[] All =
    [
        LegalNameAr, LegalNameEn, TradeNameAr, TradeNameEn,
        ContactPhone, ContactEmail,
        CommercialRegistrationNumber, OwnerName, Address,
        CountryId, RegionId,
    ];

    private static readonly HashSet<string> AllSet = new(All);

    public static bool IsValid(string fieldKey) => AllSet.Contains(fieldKey);

    /// <summary>
    /// Returns the canonical keys whose corresponding field is non-null on the registration.
    /// Excludes optional fields (countryId, regionId) when they are null.
    /// </summary>
    public static IReadOnlyList<string> RequiredForApproval(Domain.IdentityAccess.RestaurantRegistration r)
    {
        var required = new List<string>(All);
        if (r.CountryId is null) required.Remove(CountryId);
        if (r.RegionId is null) required.Remove(RegionId);
        return required;
    }
}

public sealed record RestaurantRegistrationDto(
    Guid Id,
    string LegalNameAr,
    string LegalNameEn,
    string TradeNameAr,
    string TradeNameEn,
    string ContactPhone,
    string ContactEmail,
    string? CommercialRegistrationNumber,
    string? OwnerName,
    string? Address,
    Guid? CountryId,
    Guid? RegionId,
    VerificationStatus Status,
    string? RejectionReason,
    string? ChangeRequestNotes,
    Guid? ResultingRestaurantId,
    Guid? SubmittedByUserId,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    DateTime? ReviewedAtUtc,
    Guid? ReviewedByUserId,
    IReadOnlyList<FieldReviewSummaryDto>? FieldReviews = null);

public sealed record CreateRestaurantRegistrationCommand(
    string LegalNameAr,
    string LegalNameEn,
    string TradeNameAr,
    string TradeNameEn,
    string ContactPhone,
    string ContactEmail,
    string CommercialRegistrationNumber,
    string OwnerName,
    string Address,
    Guid? CountryId,
    Guid? RegionId) : ICommand<RestaurantRegistrationDto>;

public sealed class CreateRestaurantRegistrationCommandValidator : AbstractValidator<CreateRestaurantRegistrationCommand>
{
    public CreateRestaurantRegistrationCommandValidator()
    {
        RuleFor(x => x.LegalNameAr)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.LegalNameEn)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.TradeNameAr)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.TradeNameEn)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.ContactPhone)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.ContactEmail)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField)
            .EmailAddress()
            .WithMessageCode(MessageCodes.Validation.EmailInvalid);
        RuleFor(x => x.CommercialRegistrationNumber)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.OwnerName)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.Address)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.RequiredField);
    }
}

public sealed class CreateRestaurantRegistrationCommandHandler
    : ICommandHandler<CreateRestaurantRegistrationCommand, RestaurantRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public CreateRestaurantRegistrationCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        CreateRestaurantRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated
            || _currentUser.UserId is null
            || _currentUser.UserType != UserType.Restaurant)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication is required.");
        }

        var duplicateCr = await _db.RestaurantRegistrations.AnyAsync(
            r => r.CommercialRegistrationNumber == request.CommercialRegistrationNumber
                 && r.Status != VerificationStatus.Rejected,
            cancellationToken);
        if (duplicateCr)
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateCommercialRegistration, "Commercial registration already used.");
        }

        var registration = new Domain.IdentityAccess.RestaurantRegistration
        {
            SubmittedByUserId = _currentUser.UserId,
            LegalNameAr = request.LegalNameAr,
            LegalNameEn = request.LegalNameEn,
            TradeNameAr = request.TradeNameAr,
            TradeNameEn = request.TradeNameEn,
            ContactPhone = request.ContactPhone,
            ContactEmail = request.ContactEmail.Trim().ToLowerInvariant(),
            CommercialRegistrationNumber = request.CommercialRegistrationNumber,
            OwnerName = request.OwnerName,
            Address = request.Address,
            CountryId = request.CountryId,
            RegionId = request.RegionId,
            Status = VerificationStatus.Draft,
            CreatedAtUtc = _clock.UtcNow,
        };

        _db.RestaurantRegistrations.Add(registration);
        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationCreated",
            "RestaurantRegistration",
            registration.Id,
            After: registration,
            SourceChannel: "restaurant"), cancellationToken);

        return RestaurantRegistrationMapping.Map(registration);
    }
}

public sealed record UpdateRestaurantRegistrationCommand(
    Guid Id,
    string? LegalNameAr,
    string? LegalNameEn,
    string? TradeNameAr,
    string? TradeNameEn,
    string? ContactPhone,
    string? ContactEmail,
    string? CommercialRegistrationNumber,
    string? Address,
    string? OwnerName,
    Guid? CountryId,
    Guid? RegionId) : ICommand<RestaurantRegistrationDto>;

public sealed class UpdateRestaurantRegistrationCommandHandler
    : ICommandHandler<UpdateRestaurantRegistrationCommand, RestaurantRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public UpdateRestaurantRegistrationCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        UpdateRestaurantRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations
            .Include(r => r.FieldReviews)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (registration.Status is not (VerificationStatus.Draft or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired or VerificationStatus.Rejected))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Registration cannot be edited in current status.");
        }

        if (registration.SubmittedByUserId != _currentUser.UserId
            && !_currentUser.IsInRole(AppRoles.SuperAdmin)
            && !_currentUser.HasPermission(PermissionCodes.RestaurantRegApprove))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        var before = RestaurantRegistrationMapping.Map(registration);

        // Track which field values actually changed (for non-draft updates, reset reviews)
        var changedFields = new List<string>();

        string ChangedIfDifferent(string newValue, string current, string fieldKey)
        {
            if (newValue == current) return current;
            changedFields.Add(fieldKey);
            return newValue;
        }

        Guid? ChangedIfDifferentGuid(Guid? newValue, Guid? current, string fieldKey)
        {
            if (newValue == current) return current;
            changedFields.Add(fieldKey);
            return newValue;
        }

        registration.LegalNameAr = request.LegalNameAr is not null
            ? ChangedIfDifferent(request.LegalNameAr, registration.LegalNameAr, RegistrationFieldKeys.LegalNameAr)
            : registration.LegalNameAr;
        registration.LegalNameEn = request.LegalNameEn is not null
            ? ChangedIfDifferent(request.LegalNameEn, registration.LegalNameEn, RegistrationFieldKeys.LegalNameEn)
            : registration.LegalNameEn;
        registration.TradeNameAr = request.TradeNameAr is not null
            ? ChangedIfDifferent(request.TradeNameAr, registration.TradeNameAr, RegistrationFieldKeys.TradeNameAr)
            : registration.TradeNameAr;
        registration.TradeNameEn = request.TradeNameEn is not null
            ? ChangedIfDifferent(request.TradeNameEn, registration.TradeNameEn, RegistrationFieldKeys.TradeNameEn)
            : registration.TradeNameEn;
        registration.ContactPhone = request.ContactPhone is not null
            ? ChangedIfDifferent(request.ContactPhone, registration.ContactPhone, RegistrationFieldKeys.ContactPhone)
            : registration.ContactPhone;
        registration.ContactEmail = request.ContactEmail?.Trim().ToLowerInvariant() is { } email
            ? ChangedIfDifferent(email, registration.ContactEmail, RegistrationFieldKeys.ContactEmail)
            : registration.ContactEmail;
        registration.CommercialRegistrationNumber = request.CommercialRegistrationNumber is not null
            ? ChangedIfDifferent(request.CommercialRegistrationNumber, registration.CommercialRegistrationNumber ?? string.Empty, RegistrationFieldKeys.CommercialRegistrationNumber)
            : registration.CommercialRegistrationNumber;
        registration.Address = request.Address is not null
            ? ChangedIfDifferent(request.Address, registration.Address ?? string.Empty, RegistrationFieldKeys.Address)
            : registration.Address;
        registration.OwnerName = request.OwnerName is not null
            ? ChangedIfDifferent(request.OwnerName, registration.OwnerName ?? string.Empty, RegistrationFieldKeys.OwnerName)
            : registration.OwnerName;
        registration.CountryId = request.CountryId is not null
            ? ChangedIfDifferentGuid(request.CountryId, registration.CountryId, RegistrationFieldKeys.CountryId)
            : registration.CountryId;
        registration.RegionId = request.RegionId is not null
            ? ChangedIfDifferentGuid(request.RegionId, registration.RegionId, RegistrationFieldKeys.RegionId)
            : registration.RegionId;

        // Reset field reviews for changed fields when in NeedsChanges/ResubmissionRequired/Rejected
        if (registration.Status is not VerificationStatus.Draft)
        {
            foreach (var fieldKey in changedFields)
            {
                var review = registration.FieldReviews.FirstOrDefault(f => f.FieldKey == fieldKey);
                if (review is not null)
                {
                    review.Status = VerificationStatus.Submitted;
                    review.Reason = null;
                    review.ReviewedByUserId = null;
                    review.ReviewedAtUtc = null;
                }
            }
        }

        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationUpdated",
            "RestaurantRegistration",
            registration.Id,
            Before: before,
            After: RestaurantRegistrationMapping.Map(registration),
            SourceChannel: "restaurant"), cancellationToken);

        return RestaurantRegistrationMapping.Map(registration, registration.FieldReviews.ToList());
    }
}

public sealed record SubmitRestaurantRegistrationCommand(Guid Id) : ICommand<RestaurantRegistrationDto>;

public sealed class SubmitRestaurantRegistrationCommandHandler
    : ICommandHandler<SubmitRestaurantRegistrationCommand, RestaurantRegistrationDto>
{
    private static readonly DocumentType[] RequiredDocs =
    [
        DocumentType.CommercialLicense,
        DocumentType.Contract,
        DocumentType.NationalId,
    ];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public SubmitRestaurantRegistrationCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        SubmitRestaurantRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations
            .Include(r => r.FieldReviews)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (registration.SubmittedByUserId != _currentUser.UserId
            && !_currentUser.IsInRole(AppRoles.SuperAdmin)
            && !_currentUser.HasPermission(PermissionCodes.RestaurantRegApprove))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        if (registration.Status is not (VerificationStatus.Draft or VerificationStatus.NeedsChanges or VerificationStatus.ResubmissionRequired or VerificationStatus.Rejected))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Invalid status for submit.");
        }

        var docs = await _db.Documents
            .Include(d => d.Verifications)
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
            .ToListAsync(cancellationToken);

        foreach (var required in RequiredDocs)
        {
            // Use the newest uploaded document per required DocumentType so replacing a rejected file works.
            var doc = docs
                .Where(d => d.DocumentType == required)
                .OrderByDescending(d => d.CreatedAtUtc)
                .FirstOrDefault();
            if (doc is null)
            {
                return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Missing document: {required}");
            }

            var latest = doc.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
            if (latest?.Status == VerificationStatus.Rejected)
            {
                return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Rejected document must be re-uploaded: {required}");
            }
        }

        // Seed missing field reviews as Submitted, preserve existing Approved rows
        var existingKeys = registration.FieldReviews
            .Where(f => f.Status == VerificationStatus.Approved)
            .Select(f => f.FieldKey)
            .ToHashSet();

        foreach (var fieldKey in RegistrationFieldKeys.All)
        {
            if (!existingKeys.Contains(fieldKey)
                && registration.FieldReviews.All(f => f.FieldKey != fieldKey))
            {
                registration.FieldReviews.Add(new RestaurantRegistrationFieldReview
                {
                    RegistrationId = registration.Id,
                    FieldKey = fieldKey,
                    Status = VerificationStatus.Submitted,
                    CreatedAtUtc = _clock.UtcNow,
                });
            }
        }

        registration.Status = VerificationStatus.Submitted;
        registration.RejectionReason = null;
        registration.ChangeRequestNotes = null;

        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationSubmitted",
            "RestaurantRegistration",
            registration.Id,
            SourceChannel: "restaurant"), cancellationToken);

        return RestaurantRegistrationMapping.Map(registration, registration.FieldReviews.ToList());
    }
}

public sealed record AdminRestaurantDecisionCommand(
    Guid Id,
    string Action,
    string Reason) : ICommand<RestaurantRegistrationDto>;

public sealed class AdminRestaurantDecisionCommandHandler
    : ICommandHandler<AdminRestaurantDecisionCommand, RestaurantRegistrationDto>
{
    private const int MaxReasonLength = 2000;

    private static readonly DocumentType[] RequiredDocs =
    [
        DocumentType.CommercialLicense,
        DocumentType.Contract,
        DocumentType.NationalId,
    ];

    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;

    public AdminRestaurantDecisionCommandHandler(
        IAppDbContext db,
        IUserAccountService users,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IRegistrationReviewNotifier? notifier = null)
    {
        _db = db;
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _notifier = notifier ?? NullRegistrationReviewNotifier.Instance;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        AdminRestaurantDecisionCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.RestaurantRegistrations
            .Include(r => r.FieldReviews)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        var action = request.Action.ToLowerInvariant();

        // Approve does NOT require a reason; reject and request-changes do.
        if (action is not "approve" && string.IsNullOrEmpty(reason))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Reason is required.");
        }

        if (reason.Length > MaxReasonLength)
        {
            return Error.Validation(MessageCodes.Validation.MaxLength, $"Reason must not exceed {MaxReasonLength} characters.");
        }
        switch (action)
        {
            case "approve":
                {
                    if (registration.Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview))
                    {
                        return Error.Conflict(MessageCodes.Auth.InvalidState, "Cannot approve in current status.");
                    }

                    // Check that all canonical required fields (excluding null optionals) are Approved
                    var requiredFields = RegistrationFieldKeys.RequiredForApproval(registration);
                    var fieldReviewByKey = registration.FieldReviews.ToDictionary(f => f.FieldKey);
                    foreach (var fieldKey in requiredFields)
                    {
                        if (!fieldReviewByKey.TryGetValue(fieldKey, out var review)
                            || review.Status != VerificationStatus.Approved)
                        {
                            return Error.Validation(
                                MessageCodes.Auth.DocumentsIncomplete,
                                $"Required field review not approved: {fieldKey}");
                        }
                    }

                    // Check documents
                    var docs = await _db.Documents
                        .Include(d => d.Verifications)
                        .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
                        .ToListAsync(cancellationToken);

                    // All required document types must be present and approved (use newest per type)
                    foreach (var required in RequiredDocs)
                    {
                        var doc = docs
                            .Where(d => d.DocumentType == required)
                            .OrderByDescending(d => d.CreatedAtUtc)
                            .FirstOrDefault();
                        if (doc is null)
                        {
                            return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Missing required document: {required}");
                        }

                        var latest = doc.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
                        if (latest is null || latest.Status != VerificationStatus.Approved)
                        {
                            return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, $"Required document not approved: {required}");
                        }
                    }

                    var profile = new RestaurantProfile
                    {
                        UserId = registration.SubmittedByUserId ?? Guid.Empty,
                        LegalNameAr = registration.LegalNameAr,
                        LegalNameEn = registration.LegalNameEn,
                        TradeNameAr = registration.TradeNameAr,
                        TradeNameEn = registration.TradeNameEn,
                        CommercialLicenseNumber = registration.CommercialRegistrationNumber,
                        CountryId = registration.CountryId,
                        PrimaryRegionId = registration.RegionId,
                        ContactPhone = registration.ContactPhone,
                        ContactEmail = registration.ContactEmail,
                        VerificationStatus = VerificationStatus.Approved,
                        IsOperationallyActive = false,
                        CreatedAtUtc = _clock.UtcNow,
                    };
                    _db.RestaurantProfiles.Add(profile);

                    registration.Status = VerificationStatus.Approved;
                    registration.ResultingRestaurantId = profile.Id;
                    registration.ReviewedByUserId = _currentUser.UserId;
                    registration.ReviewedAtUtc = _clock.UtcNow;
                    registration.RejectionReason = null;

                    if (registration.SubmittedByUserId is { } userId)
                    {
                        await _users.SetRestaurantIdAsync(userId, profile.Id, cancellationToken);
                        // Approval does NOT activate the owner account; activation is a separate admin action.
                    }

                    await _audit.LogAsync(new AuditEntry(
                        "RestaurantRegistrationApproved",
                        "RestaurantRegistration",
                        registration.Id,
                        After: RestaurantRegistrationMapping.Map(registration),
                        Reason: string.IsNullOrEmpty(reason) ? null : reason,
                        SourceChannel: "admin"), cancellationToken);

                    if (registration.SubmittedByUserId is { } notifyUserId)
                    {
                        await _notifier.NotifyRegistrationUpdated(
                            notifyUserId, registration.Id, "approved", cancellationToken);
                    }
                    break;
                }
            case "reject":
                if (registration.Status is VerificationStatus.Rejected)
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Registration is already rejected.");
                }

                registration.Status = VerificationStatus.Rejected;
                registration.RejectionReason = reason;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;
                await _audit.LogAsync(new AuditEntry(
                    "RestaurantRegistrationRejected",
                    "RestaurantRegistration",
                    registration.Id,
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);

                if (registration.SubmittedByUserId is { } rejectUserId)
                {
                    await _notifier.NotifyRegistrationUpdated(
                        rejectUserId, registration.Id, "rejected", cancellationToken);
                }
                break;
            case "request-changes":
                if (registration.Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview))
                {
                    return Error.Conflict(MessageCodes.Auth.InvalidState, "Cannot request changes in current status.");
                }

                registration.Status = VerificationStatus.NeedsChanges;
                registration.ChangeRequestNotes = reason;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;
                await _audit.LogAsync(new AuditEntry(
                    "RestaurantRegistrationChangesRequested",
                    "RestaurantRegistration",
                    registration.Id,
                    Reason: reason,
                    SourceChannel: "admin"), cancellationToken);

                if (registration.SubmittedByUserId is { } changesUserId)
                {
                    await _notifier.NotifyRegistrationUpdated(
                        changesUserId, registration.Id, "changes-requested", cancellationToken);
                }
                break;
            default:
                return Error.Validation(MessageCodes.Validation.Unknown, "Unknown action.");
        }

        return RestaurantRegistrationMapping.Map(registration);
    }
}

// ──────────────────────────────────────────────────────────────────────────────
// Per-field field review (approve / reject individual field)
// ──────────────────────────────────────────────────────────────────────────────

public sealed record AdminFieldReviewCommand(
    Guid RegistrationId,
    string FieldKey,
    string Action, // "approve" | "reject"
    string? Reason) : ICommand<RestaurantRegistrationDto>;

public sealed class AdminFieldReviewCommandHandler
    : ICommandHandler<AdminFieldReviewCommand, RestaurantRegistrationDto>
{
    private static readonly HashSet<string> ValidActions = ["approve", "reject"];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IRegistrationReviewNotifier _notifier;

    public AdminFieldReviewCommandHandler(
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

    public async Task<Result<RestaurantRegistrationDto>> Handle(
        AdminFieldReviewCommand request,
        CancellationToken cancellationToken)
    {
        // --- validate field key ---
        if (!RegistrationFieldKeys.IsValid(request.FieldKey))
        {
            return Error.Validation(MessageCodes.Validation.Unknown, $"Unknown field key: {request.FieldKey}");
        }

        var action = request.Action.ToLowerInvariant();
        if (!ValidActions.Contains(action))
        {
            return Error.Validation(MessageCodes.Validation.Unknown, "Action must be 'approve' or 'reject'.");
        }

        var reason = request.Reason?.Trim() ?? string.Empty;

        // reject requires nonblank reason
        if (action == "reject" && string.IsNullOrEmpty(reason))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Reason is required when rejecting a field.");
        }

        // --- load registration with existing field reviews ---
        var registration = await _db.RestaurantRegistrations
            .Include(r => r.FieldReviews)
            .FirstOrDefaultAsync(r => r.Id == request.RegistrationId, cancellationToken);

        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        // only Submittable / UnderReview registrations can be reviewed
        if (registration.Status is not (VerificationStatus.Submitted or VerificationStatus.UnderReview))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Registration is not in a reviewable state.");
        }

        // --- upsert the field review ---
        var review = registration.FieldReviews
            .FirstOrDefault(f => f.FieldKey == request.FieldKey);

        if (review is null)
        {
            review = new RestaurantRegistrationFieldReview
            {
                RegistrationId = registration.Id,
                FieldKey = request.FieldKey,
                CreatedAtUtc = _clock.UtcNow,
            };
            registration.FieldReviews.Add(review);
            _db.RestaurantRegistrationFieldReviews.Add(review);
        }

        review.Status = action == "approve" ? VerificationStatus.Approved : VerificationStatus.Rejected;
        review.Reason = action == "reject" ? reason : null;
        review.ReviewedByUserId = _currentUser.UserId;
        review.ReviewedAtUtc = _clock.UtcNow;

        // Keep the review session open even when an individual field is rejected.
        // The final admin decision sends all accumulated field/document feedback
        // to the restaurant in one coherent change request.
        if (registration.Status == VerificationStatus.Submitted)
        {
            registration.Status = VerificationStatus.UnderReview;
        }

        registration.ReviewedByUserId = _currentUser.UserId;
        registration.ReviewedAtUtc = _clock.UtcNow;

        var changeType = action == "approve" ? "field-approved" : "field-rejected";
        await _audit.LogAsync(new AuditEntry(
            "RestaurantRegistrationFieldReview",
            "RestaurantRegistration",
            registration.Id,
            After: new { FieldKey = request.FieldKey, Status = review.Status, Reason = review.Reason },
            Reason: string.IsNullOrEmpty(reason) ? null : reason,
            SourceChannel: "admin"), cancellationToken);

        if (registration.SubmittedByUserId is { } notifyUserId)
        {
            await _notifier.NotifyRegistrationUpdated(
                notifyUserId, registration.Id, changeType, cancellationToken);
        }

        return RestaurantRegistrationMapping.Map(registration, registration.FieldReviews.ToList());
    }
}

public sealed record ListRestaurantRegistrationsQuery(int Page = 1, int PageSize = 20, VerificationStatus? Status = null)
    : IQuery<PagedResult<RestaurantRegistrationDto>>;

public sealed class ListRestaurantRegistrationsQueryHandler
    : IQueryHandler<ListRestaurantRegistrationsQuery, PagedResult<RestaurantRegistrationDto>>
{
    private readonly IAppDbContext _db;

    public ListRestaurantRegistrationsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<PagedResult<RestaurantRegistrationDto>>> Handle(
        ListRestaurantRegistrationsQuery request,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var query = _db.RestaurantRegistrations.AsNoTracking().AsQueryable();
        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<RestaurantRegistrationDto>(items.Select(r => RestaurantRegistrationMapping.Map(r)).ToList(), total, page.NormalizedPage, page.NormalizedPageSize);
    }
}

public sealed record GetRestaurantRegistrationQuery(Guid Id) : IQuery<RestaurantRegistrationDto>;

public sealed class GetRestaurantRegistrationQueryHandler : IQueryHandler<GetRestaurantRegistrationQuery, RestaurantRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetRestaurantRegistrationQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<RestaurantRegistrationDto>> Handle(GetRestaurantRegistrationQuery request, CancellationToken cancellationToken)
    {
        var item = await _db.RestaurantRegistrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (item is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (!RegistrationAccess.CanView(
                _currentUser,
                item.SubmittedByUserId,
                PermissionCodes.RestaurantRegView,
                PermissionCodes.RestaurantRegApprove))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        return RestaurantRegistrationMapping.Map(item);
    }
}

internal static class RestaurantRegistrationMapping
{
    public static RestaurantRegistrationDto Map(
        Domain.IdentityAccess.RestaurantRegistration r,
        IReadOnlyList<RestaurantRegistrationFieldReview>? fieldReviews = null) => new(
        r.Id, r.LegalNameAr, r.LegalNameEn, r.TradeNameAr, r.TradeNameEn,
        r.ContactPhone, r.ContactEmail, r.CommercialRegistrationNumber, r.OwnerName, r.Address,
        r.CountryId, r.RegionId, r.Status, r.RejectionReason, r.ChangeRequestNotes, r.ResultingRestaurantId,
        r.SubmittedByUserId, r.CreatedAtUtc, r.ModifiedAtUtc, r.ReviewedAtUtc, r.ReviewedByUserId,
        fieldReviews?.Select(MapFieldReview).ToList());

    public static FieldReviewSummaryDto MapFieldReview(RestaurantRegistrationFieldReview f) => new(
        f.FieldKey, f.Status, f.Reason, f.ReviewedByUserId, f.ReviewedAtUtc);
}
