using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Storage;
using System.Text.Json;
using OutboxMessage = MealMate.Domain.Common.OutboxMessage;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DriverRegistration;

public sealed record RegisterRestaurantDriverCommand(
    string FullNameAr,
    string FullNameEn,
    string Phone,
    string? Email,
    string NationalId,
    DateTime NationalIdExpiry,
    string VehiclePlate,
    string? VehicleModel,
    string? VehicleType,
    string LicenseNumber,
    DateTime LicenseExpiry,
    DateTime VehicleLicenseExpiry,
    DateTime? ContractExpiry,
    Guid? RestaurantId,
    string NationalIdFrontStorageKey,
    string NationalIdBackStorageKey,
    string DrivingLicenseFrontStorageKey,
    string DrivingLicenseBackStorageKey,
    string VehicleRegistrationStorageKey,
    string? ContractStorageKey = null,
    DateTime? DateOfBirth = null,
    string? Nationality = null,
    int? VehicleYear = null,
    string? VehicleColor = null,
    bool? IsVehicleOwned = null,
    string? ProfileImageStorageKey = null,
    string? VehiclePhotoStorageKey = null) : ICommand<DriverRegistrationDto>;

public sealed class RegisterRestaurantDriverCommandValidator : AbstractValidator<RegisterRestaurantDriverCommand>
{
    public RegisterRestaurantDriverCommandValidator()
    {
        RuleFor(x => x.FullNameAr).NotEmpty().MinimumLength(3).MaximumLength(200);
        RuleFor(x => x.FullNameEn).NotEmpty().MinimumLength(3).MaximumLength(200);
        RuleFor(x => x.Phone)
            .NotEmpty()
            .MaximumLength(30)
            .Matches(@"^\+?[0-9]{8,20}$")
            .WithMessage("Phone number must contain between 8 and 20 digits, optionally starting with +.");

        RuleFor(x => x.Email)
            .EmailAddress()
            .MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Email address is not in a valid format.");

        RuleFor(x => x.NationalId)
            .NotEmpty()
            .MaximumLength(50)
            .Matches(@"^\d{10,14}$")
            .WithMessage("National ID must be between 10 and 14 digits.");

        RuleFor(x => x.LicenseNumber).NotEmpty().MinimumLength(5).MaximumLength(50);
        RuleFor(x => x.VehiclePlate).NotEmpty().MinimumLength(2).MaximumLength(30);
        RuleFor(x => x.NationalIdFrontStorageKey).NotEmpty();
        RuleFor(x => x.NationalIdBackStorageKey).NotEmpty();
        RuleFor(x => x.DrivingLicenseFrontStorageKey).NotEmpty();
        RuleFor(x => x.DrivingLicenseBackStorageKey).NotEmpty();
        RuleFor(x => x.VehicleRegistrationStorageKey).NotEmpty();

        RuleFor(x => x.NationalIdExpiry)
            .Must(d => d.Date > DateTime.UtcNow.Date)
            .WithMessage("National ID expiry date must be in the future.");

        RuleFor(x => x.LicenseExpiry)
            .Must(d => d.Date > DateTime.UtcNow.Date)
            .WithMessage("Driving license expiry date must be in the future.");

        RuleFor(x => x.VehicleLicenseExpiry)
            .Must(d => d.Date > DateTime.UtcNow.Date)
            .WithMessage("Vehicle registration expiry date must be in the future.");

        RuleFor(x => x.ContractExpiry)
            .Must(d => d == null || d.Value.Date > DateTime.UtcNow.Date)
            .WithMessage("Contract expiry date must be in the future.")
            .When(x => x.ContractExpiry.HasValue);
    }
}

public sealed class RegisterRestaurantDriverCommandHandler
    : ICommandHandler<RegisterRestaurantDriverCommand, DriverRegistrationDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IDriverRegistrationNotificationService _driverNotifier;
    private readonly IFileStorage? _storage;

    public RegisterRestaurantDriverCommandHandler(
        IUserAccountService users,
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IDriverRegistrationNotificationService? driverNotifier = null,
        IFileStorage? storage = null)
    {
        _users = users;
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
        _driverNotifier = driverNotifier ?? NullDriverRegistrationNotificationService.Instance;
        _storage = storage;
    }

    public async Task<Result<DriverRegistrationDto>> Handle(
        RegisterRestaurantDriverCommand request,
        CancellationToken cancellationToken)
    {
        var isAdmin = _currentUser.Roles.Any(AppRoles.IsAdminRole) || _currentUser.HasPermission(PermissionCodes.DriverRegCreate);
        Guid targetRestaurantId;

        if (isAdmin)
        {
            if (request.RestaurantId.HasValue && request.RestaurantId.Value != Guid.Empty)
            {
                targetRestaurantId = request.RestaurantId.Value;
            }
            else if (_currentUser.RestaurantId.HasValue && _currentUser.RestaurantId.Value != Guid.Empty)
            {
                targetRestaurantId = _currentUser.RestaurantId.Value;
            }
            else
            {
                return Error.Validation(MessageCodes.Validation.Required, "Restaurant ID could not be identified.");
            }
        }
        else
        {
            if (!_currentUser.RestaurantId.HasValue || _currentUser.RestaurantId.Value == Guid.Empty)
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "Restaurant scope is required to register drivers.");
            }

            if (request.RestaurantId.HasValue && request.RestaurantId.Value != _currentUser.RestaurantId.Value)
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "You cannot register drivers for another restaurant.");
            }

            targetRestaurantId = _currentUser.RestaurantId.Value;
        }

        var fileKeys = new string?[]
        {
            request.NationalIdFrontStorageKey, request.NationalIdBackStorageKey,
            request.DrivingLicenseFrontStorageKey, request.DrivingLicenseBackStorageKey,
            request.VehicleRegistrationStorageKey, request.ContractStorageKey,
            request.ProfileImageStorageKey, request.VehiclePhotoStorageKey
        };
        if (!await RegistrationStorageValidation.AllFilesExistAndHaveContentAsync(_storage, fileKeys, cancellationToken))
        {
            return Error.Validation("Driver.InvalidStoredDocument", "One or more uploaded registration files are unavailable or empty.");
        }

        var normalizedPhone = request.Phone.Trim();
        var existingUser = await _users.FindByPhoneAsync(normalizedPhone, cancellationToken);
        var existingRegPhone = await _db.DriverRegistrations.AnyAsync(r => r.Phone == normalizedPhone, cancellationToken);
        if (existingUser is not null || existingRegPhone)
        {
            return Error.Conflict("auth.phone_already_registered", "A user with this phone number already exists.");
        }

        var normalizedEmail = request.Email?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            var existingUserEmail = await _users.FindByEmailAsync(normalizedEmail, cancellationToken);
            var existingRegEmail = await _db.DriverRegistrations.AnyAsync(r => r.Email == normalizedEmail, cancellationToken);
            if (existingUserEmail is not null || existingRegEmail)
            {
                return Error.Conflict("auth.email_already_registered", "A user with this email address already exists.");
            }
        }

        var normalizedNationalId = request.NationalId.Trim();
        var existingNidReg = await _db.DriverRegistrations.AnyAsync(r => r.NationalId == normalizedNationalId, cancellationToken);
        var existingNidProfile = await _db.DriverProfiles.AnyAsync(p => p.NationalId == normalizedNationalId, cancellationToken);
        if (existingNidReg || existingNidProfile)
        {
            return Error.Conflict("auth.national_id_already_registered", "A driver with this national ID already exists.");
        }

        var normalizedPlate = request.VehiclePlate.Trim();
        var existingPlateReg = await _db.DriverRegistrations.AnyAsync(r => r.VehiclePlate == normalizedPlate, cancellationToken);
        if (existingPlateReg)
        {
            return Error.Conflict("auth.vehicle_plate_already_registered", "A driver with this vehicle plate already exists.");
        }

        var normalizedLicense = request.LicenseNumber.Trim();
        var existingLicReg = await _db.DriverRegistrations.AnyAsync(r => r.LicenseNumber == normalizedLicense, cancellationToken);
        var existingLicProfile = await _db.DriverProfiles.AnyAsync(p => p.LicenseNumber == normalizedLicense, cancellationToken);
        if (existingLicReg || existingLicProfile)
        {
            return Error.Conflict("auth.license_number_already_registered", "A driver with this license number already exists.");
        }

        if (request.DateOfBirth.HasValue && request.DateOfBirth.Value.Date > _clock.UtcNow.Date.AddYears(-18))
        {
            return Error.Validation("Driver.Underage", "Driver must be at least 18 years old.");
        }

        // Temporary strong password created until the driver activates their account via phone OTP on mobile
        var tempPassword = Guid.NewGuid().ToString("N") + "Mm1!#";
        var createUserResult = await _users.CreateAsync(new CreateUserRequest(
            request.Email?.Trim().ToLowerInvariant(),
            normalizedPhone,
            tempPassword,
            request.FullNameEn.Trim(),
            UserType.Driver,
            AccountStatus.Registered,
            targetRestaurantId), cancellationToken);

        if (createUserResult.IsFailure)
        {
            return createUserResult.Error;
        }

        var newDriverUserId = createUserResult.Value.Id;
        await _users.AddToRoleAsync(newDriverUserId, AppRoles.Driver, cancellationToken);

        // All drivers require final Admin approval. Restaurant creation approves the restaurant tier,
        // and transitions the application to Pending Admin review. DriverProfile is only created on final Admin approval.
        var registration = new Domain.IdentityAccess.DriverRegistration
        {
            SubmittedByUserId = newDriverUserId,
            RestaurantId = targetRestaurantId,
            FullNameAr = request.FullNameAr.Trim(),
            FullNameEn = request.FullNameEn.Trim(),
            Phone = normalizedPhone,
            Email = request.Email?.Trim().ToLowerInvariant(),
            NationalId = normalizedNationalId,
            NationalIdExpiryUtc = request.NationalIdExpiry.ToUniversalTime(),
            DateOfBirthUtc = request.DateOfBirth?.ToUniversalTime(),
            Nationality = request.Nationality?.Trim(),
            LicenseNumber = request.LicenseNumber.Trim(),
            LicenseExpiryUtc = request.LicenseExpiry.ToUniversalTime(),
            VehiclePlate = request.VehiclePlate.Trim(),
            VehicleModel = request.VehicleModel?.Trim(),
            VehicleType = request.VehicleType?.Trim(),
            VehicleYear = request.VehicleYear,
            VehicleColor = request.VehicleColor?.Trim(),
            IsVehicleOwned = request.IsVehicleOwned,
            VehicleRegistrationExpiryUtc = request.VehicleLicenseExpiry.ToUniversalTime(),
            ContractExpiryUtc = request.ContractExpiry?.ToUniversalTime(),
            ProfileImageStorageKey = request.ProfileImageStorageKey?.Trim(),
            VehiclePhotoStorageKey = request.VehiclePhotoStorageKey?.Trim(),
            Status = VerificationStatus.UnderReview,
            RegistrationSource = "RestaurantDashboard",
            RestaurantApprovalStatus = VerificationStatus.Approved,
            RestaurantReviewedByUserId = _currentUser.UserId,
            RestaurantReviewedAtUtc = _clock.UtcNow,
            RestaurantReviewNotes = "Created and approved by restaurant workspace.",
            AdminApprovalStatus = VerificationStatus.Submitted,
            ReviewedByUserId = null,
            ReviewedAtUtc = null,
            AdminReviewNotes = null,
            ResultingDriverProfileId = null,
            CreatedAtUtc = _clock.UtcNow,
        };

        _db.DriverRegistrations.Add(registration);

        var docsToAttach = new (DocumentType Type, string? Key, DateTime? ExpiryUtc)[]
        {
            (DocumentType.NationalIdFront, request.NationalIdFrontStorageKey, request.NationalIdExpiry.ToUniversalTime()),
            (DocumentType.NationalIdBack, request.NationalIdBackStorageKey, request.NationalIdExpiry.ToUniversalTime()),
            (DocumentType.DrivingLicenseFront, request.DrivingLicenseFrontStorageKey, request.LicenseExpiry.ToUniversalTime()),
            (DocumentType.DrivingLicenseBack, request.DrivingLicenseBackStorageKey, request.LicenseExpiry.ToUniversalTime()),
            (DocumentType.VehicleRegistration, request.VehicleRegistrationStorageKey, request.VehicleLicenseExpiry.ToUniversalTime()),
            (DocumentType.EmploymentContract, request.ContractStorageKey, request.ContractExpiry?.ToUniversalTime()),
            (DocumentType.ProfileImage, request.ProfileImageStorageKey, null),
            (DocumentType.VehiclePhoto, request.VehiclePhotoStorageKey, null),
        };

        foreach (var (docType, key, expiryUtc) in docsToAttach)
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
                    ExpiresAtUtc = expiryUtc,
                    CreatedAtUtc = _clock.UtcNow,
                };
                doc.Verifications.Add(new DocumentVerification
                {
                    Status = VerificationStatus.Submitted,
                    ReviewedByUserId = _currentUser.UserId,
                    ReviewedAtUtc = _clock.UtcNow,
                    CreatedAtUtc = _clock.UtcNow,
                });
                _db.Documents.Add(doc);
            }
        }

        await _audit.LogAsync(new AuditEntry(
            "RestaurantDriverRegistered",
            "DriverRegistration",
            registration.Id,
            After: new { RestaurantId = targetRestaurantId, Phone = normalizedPhone, NationalId = normalizedNationalId },
            SourceChannel: "restaurant"), cancellationToken);

        var restaurantName = "المطعم";
        var restProfile = await _db.RestaurantProfiles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == targetRestaurantId, cancellationToken);
        if (restProfile is not null)
        {
            restaurantName = restProfile.TradeNameAr ?? restProfile.TradeNameEn ?? restProfile.LegalNameAr ?? "المطعم";
        }

        _db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = "DriverRegistrationSubmitted",
            AggregateType = "DriverRegistration",
            AggregateId = registration.Id.ToString(),
            DeduplicationKey = $"DriverRegistrationSubmitted_{registration.Id}_{Guid.NewGuid():N}",
            PayloadJson = JsonSerializer.Serialize(new
            {
                RegistrationId = registration.Id,
                DriverUserId = registration.SubmittedByUserId,
                Phone = registration.Phone,
                DriverName = registration.FullNameAr,
                RestaurantName = restaurantName,
                RestaurantId = targetRestaurantId,
                HasRestaurant = true,
                ReviewStage = 1,
                ApplicationStatus = "Submitted"
            }),
            CreatedAtUtc = _clock.UtcNow
        });

        return DriverRegistrationMapping.Map(registration);
    }
}
