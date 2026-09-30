using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Storage;
using MealMate.Application.Features.IdentityAccess.Documents;
using System.Text.Json;
using OutboxMessage = MealMate.Domain.Common.OutboxMessage;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DriverRegistration;

public sealed record DriverRegistrationDto(
    Guid Id,
    Guid? RestaurantId,
    string FullNameAr,
    string FullNameEn,
    string Phone,
    string? Email,
    string? NationalId,
    string? LicenseNumber,
    string? VehiclePlate,
    string? VehicleType,
    string? VehicleModel,
    DateTime? NationalIdExpiryUtc,
    DateTime? LicenseExpiryUtc,
    DateTime? VehicleRegistrationExpiryUtc,
    DateTime? ContractExpiryUtc,
    Guid? CountryId,
    Guid? RegionId,
    string? ProfileImageStorageKey,
    VerificationStatus Status,
    string? RejectionReason,
    string? ChangeRequestNotes,
    Guid? ResultingDriverProfileId,
    DateTime? DateOfBirthUtc = null,
    string? Nationality = null,
    int? VehicleYear = null,
    string? VehicleColor = null,
    bool? IsVehicleOwned = null,
    string? VehiclePhotoStorageKey = null,
    string? RegistrationSource = null,
    VerificationStatus? RestaurantApprovalStatus = null,
    VerificationStatus? AdminApprovalStatus = null,
    string? RestaurantReviewNotes = null,
    string? AdminReviewNotes = null,
    DateTime? CreatedAtUtc = null,
    DateTime? ReviewedAtUtc = null,
    DateTime? RestaurantReviewedAtUtc = null);

public sealed record CreateDriverRegistrationCommand(
    string FullNameAr,
    string FullNameEn,
    string Phone,
    string? Email,
    string Password,
    Guid? RestaurantId = null,
    string? NationalId = null,
    string? LicenseNumber = null,
    string? VehiclePlate = null,
    string? VehicleType = null,
    Guid? CountryId = null,
    Guid? RegionId = null,
    string? ProfileImageStorageKey = null,
    string? NationalIdFrontStorageKey = null,
    string? NationalIdBackStorageKey = null,
    string? DrivingLicenseStorageKey = null,
    string? EmploymentContractStorageKey = null) : ICommand<DriverRegistrationDto>;

public sealed class CreateDriverRegistrationCommandValidator : AbstractValidator<CreateDriverRegistrationCommand>
{
    public CreateDriverRegistrationCommandValidator()
    {
        RuleFor(x => x.FullNameAr).NotEmpty();
        RuleFor(x => x.FullNameEn).NotEmpty();
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

public sealed class CreateDriverRegistrationCommandHandler
    : ICommandHandler<CreateDriverRegistrationCommand, DriverRegistrationDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public CreateDriverRegistrationCommandHandler(
        IUserAccountService users, IAppDbContext db, IAuditLogger audit, IDateTimeProvider clock)
    {
        _users = users;
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(
        CreateDriverRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var createUser = await _users.CreateAsync(new CreateUserRequest(
            request.Email?.Trim().ToLowerInvariant(),
            request.Phone.Trim(),
            request.Password,
            request.FullNameEn,
            UserType.Driver,
            AccountStatus.Registered,
            request.RestaurantId), cancellationToken);

        if (createUser.IsFailure)
        {
            return createUser.Error;
        }

        await _users.AddToRoleAsync(createUser.Value.Id, AppRoles.Driver, cancellationToken);

        var registration = new Domain.IdentityAccess.DriverRegistration
        {
            SubmittedByUserId = createUser.Value.Id,
            RestaurantId = request.RestaurantId,
            FullNameAr = request.FullNameAr,
            FullNameEn = request.FullNameEn,
            Phone = request.Phone,
            Email = request.Email?.Trim().ToLowerInvariant(),
            NationalId = request.NationalId,
            LicenseNumber = request.LicenseNumber,
            VehiclePlate = request.VehiclePlate,
            VehicleType = request.VehicleType,
            CountryId = request.CountryId,
            RegionId = request.RegionId,
            ProfileImageStorageKey = request.ProfileImageStorageKey,
            Status = VerificationStatus.Draft,
            CreatedAtUtc = _clock.UtcNow,
        };

        _db.DriverRegistrations.Add(registration);

        var docsToAttach = new (DocumentType Type, string? Key)[]
        {
            (DocumentType.ProfileImage, request.ProfileImageStorageKey),
            (DocumentType.NationalIdFront, request.NationalIdFrontStorageKey),
            (DocumentType.NationalIdBack, request.NationalIdBackStorageKey),
            (DocumentType.DrivingLicense, request.DrivingLicenseStorageKey),
            (DocumentType.EmploymentContract, request.EmploymentContractStorageKey),
        };

        foreach (var (docType, key) in docsToAttach)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                var doc = new Document
                {
                    OwnerType = DocumentOwnerType.Registration,
                    OwnerId = registration.Id,
                    DocumentType = docType,
                    FileName = Path.GetFileName(key),
                    ContentType = "image/jpeg",
                    StoragePath = key,
                    CreatedAtUtc = _clock.UtcNow,
                };
                doc.Verifications.Add(new DocumentVerification
                {
                    Status = VerificationStatus.Submitted,
                    CreatedAtUtc = _clock.UtcNow,
                });
                _db.Documents.Add(doc);
            }
        }

        await _audit.LogAsync(new AuditEntry("DriverRegistrationCreated", "DriverRegistration", registration.Id, SourceChannel: "driver"), cancellationToken);
        return DriverRegistrationMapping.Map(registration);
    }
}

public sealed record UploadDriverProfileImageCommand(Guid RegistrationId, string FileName, string ContentType, Stream Content)
    : ICommand<DriverRegistrationDto>;

public sealed class UploadDriverProfileImageCommandHandler
    : ICommandHandler<UploadDriverProfileImageCommand, DriverRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public UploadDriverProfileImageCommandHandler(
        IAppDbContext db,
        IFileStorage storage,
        ICurrentUser currentUser,
        IAuditLogger audit)
    {
        _db = db;
        _storage = storage;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(
        UploadDriverProfileImageCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations.FirstOrDefaultAsync(r => r.Id == request.RegistrationId, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (_currentUser.UserId is not { } userId
            || (registration.SubmittedByUserId != userId
                && !_currentUser.IsInRole(AppRoles.SuperAdmin)
                && !_currentUser.HasPermission(PermissionCodes.DriverRegApprove)))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        var allowed = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(request.ContentType))
        {
            return Error.Validation(MessageCodes.Validation.Unknown, "Profile image must be JPG/PNG/WebP.");
        }

        var stored = await _storage.SaveAsync(
            request.Content,
            request.FileName,
            request.ContentType,
            $"uploads/drivers/profile",
            cancellationToken);

        registration.ProfileImageStorageKey = stored.StorageKey;
        await _audit.LogAsync(new AuditEntry("DriverProfileImageUploaded", "DriverRegistration", registration.Id, SourceChannel: "driver"), cancellationToken);
        return DriverRegistrationMapping.Map(registration);
    }
}

public sealed record SubmitDriverRegistrationCommand(Guid Id) : ICommand<DriverRegistrationDto>;

public sealed class SubmitDriverRegistrationCommandHandler
    : ICommandHandler<SubmitDriverRegistrationCommand, DriverRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public SubmitDriverRegistrationCommandHandler(
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

    public async Task<Result<DriverRegistrationDto>> Handle(
        SubmitDriverRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (_currentUser.UserId is not { } userId
            || (registration.SubmittedByUserId != userId
                && !_currentUser.IsInRole(AppRoles.SuperAdmin)
                && !_currentUser.HasPermission(PermissionCodes.DriverRegApprove)))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        if (string.IsNullOrWhiteSpace(registration.ProfileImageStorageKey))
        {
            return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, "Profile image is required.");
        }

        var docs = await _db.Documents
            .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
            .ToListAsync(cancellationToken);

        if (!docs.Any(d => d.DocumentType == DocumentType.NationalId || d.DocumentType == DocumentType.NationalIdFront)
            || !docs.Any(d => d.DocumentType == DocumentType.DrivingLicense || d.DocumentType == DocumentType.DrivingLicenseFront))
        {
            return Error.Validation(MessageCodes.Auth.DocumentsIncomplete, "National ID and driving license are required.");
        }

        if (registration.Status == VerificationStatus.Approved || registration.Status == VerificationStatus.Rejected)
        {
            return Error.Conflict("Registration.TerminalState", "Cannot submit a registration that is already approved or rejected.");
        }

        var now = _clock.UtcNow;

        // Uniqueness checks: NationalId
        if (!string.IsNullOrWhiteSpace(registration.NationalId))
        {
            var normalizedNid = registration.NationalId.Trim();
            var nidProfileExists = await _db.DriverProfiles.AnyAsync(
                p => p.NationalId == normalizedNid && p.UserId != registration.SubmittedByUserId,
                cancellationToken);
            var nidRegExists = await _db.DriverRegistrations.AnyAsync(
                r => r.Id != registration.Id && r.NationalId == normalizedNid && r.Status != VerificationStatus.Rejected,
                cancellationToken);

            if (nidProfileExists || nidRegExists)
            {
                return Error.Conflict("Driver.NationalIdAlreadyExists", "رقم الهوية الوطنية مسجل بالفعل في حساب أو طلب آخر.");
            }
        }

        // Uniqueness checks: LicenseNumber
        if (!string.IsNullOrWhiteSpace(registration.LicenseNumber))
        {
            var normalizedLic = registration.LicenseNumber.Trim();
            var licProfileExists = await _db.DriverProfiles.AnyAsync(
                p => p.LicenseNumber == normalizedLic && p.UserId != registration.SubmittedByUserId,
                cancellationToken);
            var licRegExists = await _db.DriverRegistrations.AnyAsync(
                r => r.Id != registration.Id && r.LicenseNumber == normalizedLic && r.Status != VerificationStatus.Rejected,
                cancellationToken);

            if (licProfileExists || licRegExists)
            {
                return Error.Conflict("Driver.LicenseAlreadyExists", "رقم رخصة القيادة مسجل بالفعل في حساب أو طلب آخر.");
            }
        }

        // Expiry date checks
        if (registration.NationalIdExpiryUtc.HasValue && registration.NationalIdExpiryUtc.Value <= now)
        {
            return Error.Validation("Driver.InvalidNationalId", "بطاقة الهوية الوطنية منتهية الصلاحية.");
        }

        if (registration.LicenseExpiryUtc.HasValue && registration.LicenseExpiryUtc.Value <= now)
        {
            return Error.Validation("Driver.InvalidLicense", "رخصة القيادة منتهية الصلاحية.");
        }

        if (registration.VehicleRegistrationExpiryUtc.HasValue && registration.VehicleRegistrationExpiryUtc.Value <= now)
        {
            return Error.Validation("Driver.InvalidVehicleRegistration", "دفتر/تسجيل المركبة منتهي الصلاحية.");
        }

        if (registration.DateOfBirthUtc.HasValue && registration.DateOfBirthUtc.Value > now.AddYears(-18))
        {
            return Error.Validation("Driver.Underage", "يجب ألا يقل عمر السائق عن 18 عاماً.");
        }

        registration.Status = VerificationStatus.Submitted;
        await _audit.LogAsync(new AuditEntry("DriverSubmittedForReview", "DriverRegistration", registration.Id, SourceChannel: "driver"), cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return DriverRegistrationMapping.Map(registration);
    }
}

public sealed record AdminDriverDecisionCommand(Guid Id, string Action, string Reason) : ICommand<DriverRegistrationDto>;

public sealed class AdminDriverDecisionCommandHandler
    : ICommandHandler<AdminDriverDecisionCommand, DriverRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IDriverRegistrationNotificationService _driverNotifier;
    private readonly IApplicationTransactionExecutor? _txExecutor;
    private readonly IFileStorage? _storage;

    public AdminDriverDecisionCommandHandler(
        IAppDbContext db,
        IUserAccountService users,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IDriverRegistrationNotificationService? driverNotifier = null,
        IApplicationTransactionExecutor? txExecutor = null,
        IFileStorage? storage = null)
    {
        _db = db;
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _driverNotifier = driverNotifier ?? NullDriverRegistrationNotificationService.Instance;
        _txExecutor = txExecutor;
        _storage = storage;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(
        AdminDriverDecisionCommand request,
        CancellationToken cancellationToken)
    {
        if (_txExecutor is not null)
        {
            return await _txExecutor.ExecuteResultAsync(ct => ExecuteDecisionCore(request, ct), cancellationToken);
        }

        return await ExecuteDecisionCore(request, cancellationToken);
    }

    private async Task<Result<DriverRegistrationDto>> ExecuteDecisionCore(
        AdminDriverDecisionCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Reason is required.");
        }

        var actionLower = request.Action.ToLowerInvariant();
        var isAdmin = _currentUser.Roles.Any(AppRoles.IsAdminRole);
        if (!isAdmin)
        {
            var hasPermission = actionLower switch
            {
                "approve" => _currentUser.HasPermission(PermissionCodes.DriverRegApprove),
                "reject" => _currentUser.HasPermission(PermissionCodes.DriverRegReject),
                "request-changes" or "request_changes" => _currentUser.HasPermission(PermissionCodes.DriverRegApprove),
                _ => false
            };
            if (!hasPermission)
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "You do not have permission to review driver registrations.");
            }
        }

        // Terminal state & idempotency guards
        if (registration.Status == VerificationStatus.Approved || registration.ResultingDriverProfileId.HasValue)
        {
            if (actionLower == "approve")
            {
                return DriverRegistrationMapping.Map(registration);
            }
            return Error.Conflict("ALREADY_APPROVED", "This registration has already been approved.");
        }

        if (registration.Status == VerificationStatus.Rejected)
        {
            if (actionLower == "reject")
            {
                return DriverRegistrationMapping.Map(registration);
            }
            return Error.Conflict("ALREADY_REJECTED", "This registration has already been rejected.");
        }

        var restaurantName = "المطعم";
        if (registration.RestaurantId.HasValue)
        {
            var restProfile = await _db.RestaurantProfiles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == registration.RestaurantId.Value, cancellationToken);
            if (restProfile is not null)
            {
                restaurantName = restProfile.TradeNameAr ?? restProfile.TradeNameEn ?? restProfile.LegalNameAr ?? "المطعم";
            }
        }

        switch (actionLower)
        {
            case "approve":
                if (registration.RestaurantId.HasValue
                    && registration.RestaurantId.Value != Guid.Empty
                    && registration.RestaurantApprovalStatus != VerificationStatus.Approved)
                {
                    return Error.Validation(
                        MessageCodes.Driver.RestaurantApprovalRequired,
                        "This driver registration must be approved by the restaurant before final admin approval.");
                }

                // Mandatory Document Validation (G08, R02, R03, V01)
                var docs = await _db.Documents
                    .Include(d => d.Verifications)
                    .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == registration.Id)
                    .ToListAsync(cancellationToken);

                var now = _clock.UtcNow;

                // Group by DocumentType to inspect only the latest active version of each document (R03)
                var latestDocs = docs
                    .GroupBy(d => d.DocumentType)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.CreatedAtUtc).First());

                // 1. Profile Image Check (must exist and latest must not be rejected)
                var hasProfileKey = !string.IsNullOrWhiteSpace(registration.ProfileImageStorageKey);
                var hasProfileDoc = latestDocs.TryGetValue(DocumentType.ProfileImage, out var profileDoc);
                var profileRejected = profileDoc?.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault()?.Status == VerificationStatus.Rejected;
                if ((!hasProfileKey && !hasProfileDoc) || profileRejected)
                {
                    return Error.Validation("Driver.MissingProfileImage", "الصورة الشخصية للسائق مطلوبة ويجب ألا تكون مرفوضة.");
                }

                var profileStorageKey = !string.IsNullOrWhiteSpace(registration.ProfileImageStorageKey)
                    ? registration.ProfileImageStorageKey
                    : profileDoc?.StoragePath;

                // 2. Driving License Check (front/combined must exist, not be rejected or expired)
                latestDocs.TryGetValue(DocumentType.DrivingLicenseFront, out var dlFront);
                if (dlFront == null) latestDocs.TryGetValue(DocumentType.DrivingLicense, out dlFront);
                var dlFrontMissing = dlFront == null;
                var dlFrontRejected = dlFront?.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault()?.Status == VerificationStatus.Rejected;
                var dlFrontExpired = dlFront != null && dlFront.ExpiresAtUtc.HasValue && dlFront.ExpiresAtUtc.Value <= now;

                latestDocs.TryGetValue(DocumentType.DrivingLicenseBack, out var dlBack);
                var dlBackRejected = dlBack?.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault()?.Status == VerificationStatus.Rejected;
                var dlBackExpired = dlBack != null && dlBack.ExpiresAtUtc.HasValue && dlBack.ExpiresAtUtc.Value <= now;

                var licenseExpiryExpired = !registration.LicenseExpiryUtc.HasValue || registration.LicenseExpiryUtc.Value <= now;

                if (dlFrontMissing || dlFrontRejected || dlBackRejected || dlFrontExpired || dlBackExpired || licenseExpiryExpired)
                {
                    return Error.Validation("Driver.InvalidDrivingLicense", "رخصة القيادة غير مكتملة أو مرفوضة أو منتهية الصلاحية.");
                }

                // 3. National ID Check (front/combined must exist, not be rejected or expired)
                latestDocs.TryGetValue(DocumentType.NationalIdFront, out var nidFront);
                if (nidFront == null) latestDocs.TryGetValue(DocumentType.NationalId, out nidFront);
                var nidFrontMissing = nidFront == null;
                var nidFrontRejected = nidFront?.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault()?.Status == VerificationStatus.Rejected;
                var nidFrontExpired = nidFront != null && nidFront.ExpiresAtUtc.HasValue && nidFront.ExpiresAtUtc.Value <= now;

                latestDocs.TryGetValue(DocumentType.NationalIdBack, out var nidBack);
                var nidBackRejected = nidBack?.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault()?.Status == VerificationStatus.Rejected;
                var nidBackExpired = nidBack != null && nidBack.ExpiresAtUtc.HasValue && nidBack.ExpiresAtUtc.Value <= now;

                var nidExpiryExpired = !registration.NationalIdExpiryUtc.HasValue || registration.NationalIdExpiryUtc.Value <= now;

                if (nidFrontMissing || nidFrontRejected || nidBackRejected || nidFrontExpired || nidBackExpired || nidExpiryExpired)
                {
                    return Error.Validation("Driver.InvalidNationalId", "بطاقة الهوية الوطنية غير مكتملة أو مرفوضة أو منتهية الصلاحية.");
                }

                // 4. Vehicle Registration Expiry and Verification Check
                latestDocs.TryGetValue(DocumentType.VehicleRegistration, out var vehDoc);
                var vehMissing = vehDoc == null;
                var vehRejected = vehDoc?.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault()?.Status == VerificationStatus.Rejected;
                var vehExpired = (vehDoc != null && vehDoc.ExpiresAtUtc.HasValue && vehDoc.ExpiresAtUtc.Value <= now)
                                 || (!registration.VehicleRegistrationExpiryUtc.HasValue || registration.VehicleRegistrationExpiryUtc.Value <= now);

                if (vehMissing || vehRejected || vehExpired)
                {
                    return Error.Validation("Driver.InvalidVehicleRegistration", "دفتر/تسجيل المركبة غير مكتمل أو مرفوض أو منتهي الصلاحية.");
                }

                // 5. Employment Contract Check (if contract document exists or contract expiry is specified)
                latestDocs.TryGetValue(DocumentType.EmploymentContract, out var contractDoc);
                if (contractDoc == null) latestDocs.TryGetValue(DocumentType.Contract, out contractDoc);

                if (contractDoc != null)
                {
                    var contractRejected = contractDoc.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault()?.Status == VerificationStatus.Rejected;
                    var contractExpired = contractDoc.ExpiresAtUtc.HasValue && contractDoc.ExpiresAtUtc.Value <= now;
                    if (contractRejected || contractExpired)
                    {
                        return Error.Validation("Driver.InvalidContract", "عقد عمل السائق غير صالح أو مرفوض أو منتهي الصلاحية.");
                    }
                }

                if (registration.ContractExpiryUtc.HasValue && registration.ContractExpiryUtc.Value <= now)
                {
                    return Error.Validation("Driver.InvalidContract", "عقد عمل السائق منتهي الصلاحية.");
                }

                var finalApprovalKeys = latestDocs.Values.Select(x => x.StoragePath).Append(profileStorageKey);
                if (!await RegistrationStorageValidation.AllFilesExistAndHaveContentAsync(_storage, finalApprovalKeys, cancellationToken))
                {
                    return Error.Validation("Driver.InvalidStoredDocument", "One or more required driver documents are unavailable or empty in storage.");
                }

                // Atomically approve all latest valid documents as part of the overall driver approval
                var validDriverDocTypes = new HashSet<DocumentType>
                {
                    DocumentType.DrivingLicenseFront,
                    DocumentType.DrivingLicenseBack,
                    DocumentType.DrivingLicense,
                    DocumentType.NationalIdFront,
                    DocumentType.NationalIdBack,
                    DocumentType.NationalId,
                    DocumentType.VehicleRegistration,
                    DocumentType.EmploymentContract,
                    DocumentType.Contract,
                    DocumentType.ProfileImage,
                    DocumentType.VehiclePhoto
                };

                foreach (var doc in latestDocs.Values)
                {
                    if (!validDriverDocTypes.Contains(doc.DocumentType)) continue;

                    var latestVerification = doc.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
                    if (latestVerification?.Status == VerificationStatus.Rejected) continue;
                    if (doc.ExpiresAtUtc.HasValue && doc.ExpiresAtUtc.Value <= now) continue;

                    if (latestVerification?.Status != VerificationStatus.Approved)
                    {
                        var verification = new DocumentVerification
                        {
                            DocumentId = doc.Id,
                            Status = VerificationStatus.Approved,
                            ReviewedByUserId = _currentUser.UserId,
                            ReviewedAtUtc = now,
                            Reason = "Approved with driver registration approval",
                            CreatedAtUtc = now
                        };
                        _db.DocumentVerifications.Add(verification);
                    }
                }

                var targetDriverUserId = registration.SubmittedByUserId ?? Guid.Empty;
                if (targetDriverUserId == Guid.Empty)
                {
                    return Error.Validation("Driver.NoUserAccount", "حساب المستخدم صاحب الطلب غير موجود أو غير مرتبط بالطلب.");
                }

                var submittedUser = await _users.FindByIdAsync(targetDriverUserId, cancellationToken);
                if (submittedUser is null)
                {
                    return Error.NotFound("User.NotFound", "The user account associated with this registration does not exist.");
                }

                var existingProfile = await _db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == targetDriverUserId, cancellationToken);

                DriverProfile profile;
                if (existingProfile is not null)
                {
                    profile = existingProfile;
                    profile.RestaurantId = registration.RestaurantId;
                    profile.FullNameAr = registration.FullNameAr;
                    profile.FullNameEn = registration.FullNameEn;
                    profile.NationalId = registration.NationalId;
                    profile.LicenseNumber = registration.LicenseNumber;
                    profile.VehiclePlate = registration.VehiclePlate;
                    profile.VehicleType = registration.VehicleType;
                    profile.VehicleModel = registration.VehicleModel;
                    profile.NationalIdExpiryUtc = registration.NationalIdExpiryUtc;
                    profile.LicenseExpiryUtc = registration.LicenseExpiryUtc;
                    profile.VehicleRegistrationExpiryUtc = registration.VehicleRegistrationExpiryUtc;
                    profile.ContractExpiryUtc = registration.ContractExpiryUtc;
                    profile.DateOfBirthUtc = registration.DateOfBirthUtc;
                    profile.Nationality = registration.Nationality;
                    profile.VehicleYear = registration.VehicleYear;
                    profile.VehicleColor = registration.VehicleColor;
                    profile.IsVehicleOwned = registration.IsVehicleOwned;
                    profile.ProfileImageStorageKey = profileStorageKey;
                    profile.ProfileImageStatus = VerificationStatus.Approved;
                    profile.VerificationStatus = VerificationStatus.Approved;
                }
                else
                {
                    profile = new DriverProfile
                    {
                        UserId = targetDriverUserId,
                        RestaurantId = registration.RestaurantId,
                        FullNameAr = registration.FullNameAr,
                        FullNameEn = registration.FullNameEn,
                        NationalId = registration.NationalId,
                        LicenseNumber = registration.LicenseNumber,
                        VehiclePlate = registration.VehiclePlate,
                        VehicleType = registration.VehicleType,
                        VehicleModel = registration.VehicleModel,
                        NationalIdExpiryUtc = registration.NationalIdExpiryUtc,
                        LicenseExpiryUtc = registration.LicenseExpiryUtc,
                        VehicleRegistrationExpiryUtc = registration.VehicleRegistrationExpiryUtc,
                        ContractExpiryUtc = registration.ContractExpiryUtc,
                        DateOfBirthUtc = registration.DateOfBirthUtc,
                        Nationality = registration.Nationality,
                        VehicleYear = registration.VehicleYear,
                        VehicleColor = registration.VehicleColor,
                        IsVehicleOwned = registration.IsVehicleOwned,
                        CountryId = registration.CountryId,
                        RegionId = registration.RegionId,
                        ProfileImageStorageKey = profileStorageKey,
                        ProfileImageStatus = VerificationStatus.Approved,
                        VerificationStatus = VerificationStatus.Approved,
                        IsAvailable = false,
                        ActivatedAtUtc = _clock.UtcNow,
                        CreatedAtUtc = _clock.UtcNow,
                    };
                    _db.DriverProfiles.Add(profile);
                }

                registration.Status = VerificationStatus.Approved;
                registration.AdminApprovalStatus = VerificationStatus.Approved;
                registration.AdminReviewNotes = request.Reason;
                registration.ResultingDriverProfileId = profile.Id;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;

                await _users.UpdateAccountStatusAsync(targetDriverUserId, AccountStatus.Active, cancellationToken);

                if (registration.RestaurantId is { } restId && restId != Guid.Empty)
                {
                    var linkExists = await _db.RestaurantDriverLinks.AnyAsync(
                        l => l.RestaurantId == restId && l.DriverProfileId == profile.Id, cancellationToken);
                    if (!linkExists)
                    {
                        _db.RestaurantDriverLinks.Add(new Domain.RestaurantOperations.RestaurantDriverLink
                        {
                            RestaurantId = restId,
                            DriverProfileId = profile.Id,
                            IsActive = true,
                            CreatedAtUtc = _clock.UtcNow
                        });
                    }
                }

                await _audit.LogAsync(new AuditEntry("DriverApproved", "DriverRegistration", registration.Id, Reason: request.Reason, SourceChannel: "admin"), cancellationToken);

                _db.OutboxMessages.Add(new OutboxMessage
                {
                    EventType = "DriverRegistrationAdminApproved",
                    AggregateType = "DriverRegistration",
                    AggregateId = registration.Id.ToString(),
                    DeduplicationKey = $"DriverRegistrationAdminApproved_{registration.Id}_{Guid.NewGuid():N}",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        RegistrationId = registration.Id,
                        DriverUserId = registration.SubmittedByUserId,
                        Phone = registration.Phone,
                        DriverName = registration.FullNameAr,
                        RestaurantName = restaurantName,
                        RestaurantId = registration.RestaurantId,
                        Action = "approve",
                        Reason = request.Reason,
                        ReviewStage = 4,
                        ApplicationStatus = "Approved"
                    }),
                    CreatedAtUtc = _clock.UtcNow
                });
                break;

            case "reject":
                registration.Status = VerificationStatus.Rejected;
                registration.AdminApprovalStatus = VerificationStatus.Rejected;
                registration.AdminReviewNotes = request.Reason;
                registration.RejectionReason = request.Reason;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;
                if (registration.SubmittedByUserId is { } rejectedUserId)
                {
                    await _users.UpdateAccountStatusAsync(rejectedUserId, AccountStatus.Suspended, cancellationToken);
                }
                await _audit.LogAsync(new AuditEntry("DriverRejected", "DriverRegistration", registration.Id, Reason: request.Reason, SourceChannel: "admin"), cancellationToken);

                _db.OutboxMessages.Add(new OutboxMessage
                {
                    EventType = "DriverRegistrationAdminRejected",
                    AggregateType = "DriverRegistration",
                    AggregateId = registration.Id.ToString(),
                    DeduplicationKey = $"DriverRegistrationAdminRejected_{registration.Id}_{Guid.NewGuid():N}",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        RegistrationId = registration.Id,
                        DriverUserId = registration.SubmittedByUserId,
                        Phone = registration.Phone,
                        DriverName = registration.FullNameAr,
                        RestaurantName = restaurantName,
                        RestaurantId = registration.RestaurantId,
                        Action = "reject",
                        Reason = request.Reason,
                        ReviewStage = 3,
                        ApplicationStatus = "Rejected"
                    }),
                    CreatedAtUtc = _clock.UtcNow
                });
                break;

            case "request-changes":
            case "request_changes":
                registration.Status = VerificationStatus.NeedsChanges;
                registration.AdminApprovalStatus = VerificationStatus.NeedsChanges;
                registration.AdminReviewNotes = request.Reason;
                registration.ChangeRequestNotes = request.Reason;
                registration.ReviewedByUserId = _currentUser.UserId;
                registration.ReviewedAtUtc = _clock.UtcNow;
                await _audit.LogAsync(new AuditEntry("DriverChangesRequested", "DriverRegistration", registration.Id, Reason: request.Reason, SourceChannel: "admin"), cancellationToken);

                _db.OutboxMessages.Add(new OutboxMessage
                {
                    EventType = "DriverRegistrationAdminChangesRequested",
                    AggregateType = "DriverRegistration",
                    AggregateId = registration.Id.ToString(),
                    DeduplicationKey = $"DriverRegistrationAdminChangesRequested_{registration.Id}_{Guid.NewGuid():N}",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        RegistrationId = registration.Id,
                        DriverUserId = registration.SubmittedByUserId,
                        Phone = registration.Phone,
                        DriverName = registration.FullNameAr,
                        RestaurantName = restaurantName,
                        RestaurantId = registration.RestaurantId,
                        Action = "request-changes",
                        Reason = request.Reason,
                        ReviewStage = 2,
                        ApplicationStatus = "NeedsChanges"
                    }),
                    CreatedAtUtc = _clock.UtcNow
                });
                break;

            default:
                return Error.Validation(MessageCodes.Validation.Unknown, "Unknown action.");
        }

        registration.ModifiedAtUtc = _clock.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "The registration status was modified by another concurrent operation. Please refresh and try again.");
        }

        return DriverRegistrationMapping.Map(registration);
    }
}

public sealed record ListDriverRegistrationsQuery(int Page = 1, int PageSize = 20, VerificationStatus? Status = null)
    : IQuery<PagedResult<DriverRegistrationDto>>;

public sealed class ListDriverRegistrationsQueryHandler
    : IQueryHandler<ListDriverRegistrationsQuery, PagedResult<DriverRegistrationDto>>
{
    private readonly IAppDbContext _db;

    public ListDriverRegistrationsQueryHandler(IAppDbContext db) => _db = db;

    public async Task<Result<PagedResult<DriverRegistrationDto>>> Handle(
        ListDriverRegistrationsQuery request,
        CancellationToken cancellationToken)
    {
        var page = new PageRequest { Page = request.Page, PageSize = request.PageSize };
        var query = _db.DriverRegistrations.AsNoTracking().AsQueryable();
        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip).Take(page.NormalizedPageSize).ToListAsync(cancellationToken);
        return new PagedResult<DriverRegistrationDto>(items.Select(DriverRegistrationMapping.Map).ToList(), total, page.NormalizedPage, page.NormalizedPageSize);
    }
}

public sealed record GetDriverRegistrationQuery(Guid Id) : IQuery<DriverRegistrationDto>;

public sealed class GetDriverRegistrationQueryHandler : IQueryHandler<GetDriverRegistrationQuery, DriverRegistrationDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetDriverRegistrationQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(GetDriverRegistrationQuery request, CancellationToken cancellationToken)
    {
        var item = await _db.DriverRegistrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (item is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (!RegistrationAccess.CanView(
                _currentUser,
                item.SubmittedByUserId,
                PermissionCodes.DriverRegView,
                PermissionCodes.DriverRegApprove))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        return DriverRegistrationMapping.Map(item);
    }
}

internal static class DriverRegistrationMapping
{
    public static DriverRegistrationDto Map(Domain.IdentityAccess.DriverRegistration r) => new(
        r.Id, r.RestaurantId, r.FullNameAr, r.FullNameEn, r.Phone, r.Email,
        r.NationalId, r.LicenseNumber, r.VehiclePlate, r.VehicleType,
        r.VehicleModel, r.NationalIdExpiryUtc, r.LicenseExpiryUtc,
        r.VehicleRegistrationExpiryUtc, r.ContractExpiryUtc,
        r.CountryId, r.RegionId, r.ProfileImageStorageKey, r.Status,
        r.RejectionReason, r.ChangeRequestNotes, r.ResultingDriverProfileId,
        r.DateOfBirthUtc, r.Nationality, r.VehicleYear, r.VehicleColor,
        r.IsVehicleOwned, r.VehiclePhotoStorageKey,
        r.RegistrationSource, r.RestaurantApprovalStatus, r.AdminApprovalStatus,
        r.RestaurantReviewNotes, r.AdminReviewNotes,
        r.CreatedAtUtc, r.ReviewedAtUtc, r.RestaurantReviewedAtUtc);
}

public sealed record UpdateDriverRegistrationCommand(
    Guid Id,
    string? FullNameAr,
    string? FullNameEn,
    string? Phone,
    string? Email,
    string? NationalId,
    string? LicenseNumber,
    string? VehiclePlate,
    string? VehicleType,
    Guid? CountryId,
    Guid? RegionId) : ICommand<DriverRegistrationDto>;

public sealed class UpdateDriverRegistrationCommandValidator : AbstractValidator<UpdateDriverRegistrationCommand>
{
    public UpdateDriverRegistrationCommandValidator()
    {
        RuleFor(x => x.FullNameAr).NotEmpty().MaximumLength(200).When(x => x.FullNameAr is not null);
        RuleFor(x => x.FullNameEn).NotEmpty().MaximumLength(200).When(x => x.FullNameEn is not null);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30).When(x => x.Phone is not null);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.NationalId).MaximumLength(50).When(x => x.NationalId is not null);
        RuleFor(x => x.LicenseNumber).MaximumLength(50).When(x => x.LicenseNumber is not null);
        RuleFor(x => x.VehiclePlate).MaximumLength(30).When(x => x.VehiclePlate is not null);
        RuleFor(x => x.VehicleType).MaximumLength(50).When(x => x.VehicleType is not null);
    }
}

public sealed class UpdateDriverRegistrationCommandHandler
    : ICommandHandler<UpdateDriverRegistrationCommand, DriverRegistrationDto>
{
    private static readonly VerificationStatus[] AllowedStatuses =
    [
        VerificationStatus.Draft,
        VerificationStatus.NeedsChanges,
        VerificationStatus.ResubmissionRequired,
    ];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IUserAccountService? _users;
    private readonly IDateTimeProvider? _clock;

    public UpdateDriverRegistrationCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IUserAccountService? users = null,
        IDateTimeProvider? clock = null)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _users = users;
        _clock = clock;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(
        UpdateDriverRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (_currentUser.UserId is not { } userId
            || (registration.SubmittedByUserId != userId
                && !_currentUser.IsInRole(AppRoles.SuperAdmin)
                && !_currentUser.HasPermission(PermissionCodes.DriverRegApprove)))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        if (!AllowedStatuses.Contains(registration.Status))
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "Registration cannot be updated in current status.");
        }

        if (request.Phone is not null
            && !string.Equals(registration.Phone, request.Phone.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation("Driver.PhoneChangeRequiresVerification", "Phone changes must be completed through the verified phone change flow.");
        }

        var before = new
        {
            registration.FullNameAr,
            registration.FullNameEn,
            registration.Phone,
            registration.Email,
            registration.NationalId,
            registration.LicenseNumber,
            registration.VehiclePlate,
            registration.VehicleType,
            registration.CountryId,
            registration.RegionId,
        };

        if (request.FullNameAr is not null)
        {
            registration.FullNameAr = request.FullNameAr.Trim();
        }

        if (request.FullNameEn is not null)
        {
            registration.FullNameEn = request.FullNameEn.Trim();
        }

        if (request.Phone is not null)
        {
            var newPhone = request.Phone.Trim();
            if (!string.Equals(registration.Phone, newPhone, StringComparison.OrdinalIgnoreCase))
            {
                if (_users is not null)
                {
                    var existingUser = await _users.FindByPhoneAsync(newPhone, cancellationToken);
                    if (existingUser is not null && registration.SubmittedByUserId.HasValue && existingUser.Id != registration.SubmittedByUserId.Value)
                    {
                        return Error.Conflict("Driver.PhoneAlreadyExists", "رقم الهاتف مسجل بالفعل لمستخدم آخر ولا يمكن ربط الطلب به.");
                    }
                }

                var phoneInUse = await _db.DriverRegistrations.AnyAsync(
                    r => r.Id != registration.Id
                         && r.Phone == newPhone
                         && r.Status != VerificationStatus.Rejected,
                    cancellationToken);

                if (phoneInUse)
                {
                    return Error.Conflict("Driver.PhoneAlreadyExists", "رقم الهاتف مسجل بالفعل في طلب تسجيل آخر.");
                }

                registration.Phone = newPhone;
                if (_users is not null && registration.SubmittedByUserId.HasValue)
                {
                    await _users.UpdatePhoneAsync(registration.SubmittedByUserId.Value, newPhone, cancellationToken);
                }
            }
        }

        if (request.Email is not null)
        {
            registration.Email = request.Email.Trim().ToLowerInvariant();
        }

        if (request.NationalId is not null)
        {
            var newNid = request.NationalId.Trim();
            if (!string.Equals(registration.NationalId, newNid, StringComparison.OrdinalIgnoreCase))
            {
                var nidProfileExists = await _db.DriverProfiles.AnyAsync(
                    p => p.NationalId == newNid && p.UserId != registration.SubmittedByUserId,
                    cancellationToken);
                var nidRegExists = await _db.DriverRegistrations.AnyAsync(
                    r => r.Id != registration.Id && r.NationalId == newNid && r.Status != VerificationStatus.Rejected,
                    cancellationToken);

                if (nidProfileExists || nidRegExists)
                {
                    return Error.Conflict("Driver.NationalIdAlreadyExists", "رقم الهوية الوطنية مسجل بالفعل في حساب أو طلب آخر.");
                }
                registration.NationalId = newNid;
            }
        }

        if (request.LicenseNumber is not null)
        {
            var newLic = request.LicenseNumber.Trim();
            if (!string.Equals(registration.LicenseNumber, newLic, StringComparison.OrdinalIgnoreCase))
            {
                var licProfileExists = await _db.DriverProfiles.AnyAsync(
                    p => p.LicenseNumber == newLic && p.UserId != registration.SubmittedByUserId,
                    cancellationToken);
                var licRegExists = await _db.DriverRegistrations.AnyAsync(
                    r => r.Id != registration.Id && r.LicenseNumber == newLic && r.Status != VerificationStatus.Rejected,
                    cancellationToken);

                if (licProfileExists || licRegExists)
                {
                    return Error.Conflict("Driver.LicenseAlreadyExists", "رقم رخصة القيادة مسجل بالفعل في حساب أو طلب آخر.");
                }
                registration.LicenseNumber = newLic;
            }
        }

        if (request.VehiclePlate is not null)
        {
            var newPlate = request.VehiclePlate.Trim();
            if (!string.Equals(registration.VehiclePlate, newPlate, StringComparison.OrdinalIgnoreCase))
            {
                var plateInUse = await _db.DriverProfiles.AnyAsync(
                    p => p.VehiclePlate == newPlate && p.UserId != registration.SubmittedByUserId,
                    cancellationToken);
                var plateRegInUse = await _db.DriverRegistrations.AnyAsync(
                    r => r.Id != registration.Id && r.VehiclePlate == newPlate && r.Status != VerificationStatus.Rejected,
                    cancellationToken);

                if (plateInUse || plateRegInUse)
                {
                    return Error.Conflict("Driver.VehiclePlateAlreadyExists", "رقم لوحة المركبة مسجل بالفعل لمركبة أخرى.");
                }
                registration.VehiclePlate = newPlate;
            }
        }

        if (request.VehicleType is not null)
        {
            registration.VehicleType = request.VehicleType.Trim();
        }

        if (request.CountryId.HasValue)
        {
            registration.CountryId = request.CountryId;
        }

        if (request.RegionId.HasValue)
        {
            registration.RegionId = request.RegionId;
        }

        var after = new
        {
            registration.FullNameAr,
            registration.FullNameEn,
            registration.Phone,
            registration.Email,
            registration.NationalId,
            registration.LicenseNumber,
            registration.VehiclePlate,
            registration.VehicleType,
            registration.CountryId,
            registration.RegionId,
        };

        registration.ModifiedAtUtc = _clock?.UtcNow ?? DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "The registration was modified by another concurrent operation. Please refresh and try again.");
        }

        await _audit.LogAsync(new AuditEntry(
            "DriverRegistrationUpdated",
            "DriverRegistration",
            registration.Id,
            Before: before,
            After: after,
            SourceChannel: "driver"), cancellationToken);

        return DriverRegistrationMapping.Map(registration);
    }
}

public sealed record UploadDriverRegistrationDocumentCommand(
    Guid RegistrationId,
    DocumentType DocumentType,
    string FileName,
    string ContentType,
    Stream Content) : ICommand<DocumentDto>;

public sealed class UploadDriverRegistrationDocumentCommandHandler
    : ICommandHandler<UploadDriverRegistrationDocumentCommand, DocumentDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ISender _sender;

    public UploadDriverRegistrationDocumentCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        ISender sender)
    {
        _db = db;
        _currentUser = currentUser;
        _sender = sender;
    }

    public async Task<Result<DocumentDto>> Handle(
        UploadDriverRegistrationDocumentCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RegistrationId, cancellationToken);

        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Registration not found.");
        }

        if (_currentUser.UserId is not { } userId
            || (registration.SubmittedByUserId != userId
                && !_currentUser.IsInRole(AppRoles.SuperAdmin)
                && !_currentUser.HasPermission(PermissionCodes.DriverRegApprove)))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Not allowed.");
        }

        return await _sender.Send(new UploadDocumentCommand(
            DocumentOwnerType.Registration,
            request.RegistrationId,
            request.DocumentType,
            request.FileName,
            request.ContentType,
            request.Content), cancellationToken);
    }
}

public sealed record GetDriverRegistrationByUserIdQuery(Guid UserId) : IQuery<DriverRegistrationDto>;

public sealed class GetDriverRegistrationByUserIdQueryHandler : IQueryHandler<GetDriverRegistrationByUserIdQuery, DriverRegistrationDto>
{
    private readonly IAppDbContext _db;

    public GetDriverRegistrationByUserIdQueryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(GetDriverRegistrationByUserIdQuery request, CancellationToken cancellationToken)
    {
        var item = await _db.DriverRegistrations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SubmittedByUserId == request.UserId, cancellationToken);

        if (item is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Driver registration not found for this user.");
        }

        return DriverRegistrationMapping.Map(item);
    }
}
