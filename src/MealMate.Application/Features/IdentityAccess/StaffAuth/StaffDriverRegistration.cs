using System;
using System.Collections.Generic;
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
using MealMate.Application.Common.Validation;
using System.Text.Json;
using OutboxMessage = MealMate.Domain.Common.OutboxMessage;
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.StaffAuth;

public sealed record StaffRegistrationRestaurantDto(
    Guid Id,
    string Name,
    string TradeNameAr,
    string TradeNameEn,
    string LegalNameAr,
    string LegalNameEn,
    string? LogoUrl,
    string? ContactPhone,
    string? ContactEmail);

public sealed record GetStaffRegistrationRestaurantsQuery : IQuery<IReadOnlyList<StaffRegistrationRestaurantDto>>;

public sealed class GetStaffRegistrationRestaurantsQueryHandler
    : IQueryHandler<GetStaffRegistrationRestaurantsQuery, IReadOnlyList<StaffRegistrationRestaurantDto>>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;

    public GetStaffRegistrationRestaurantsQueryHandler(IAppDbContext db, ILocalizedMessageProvider messages)
    {
        _db = db;
        _messages = messages;
    }

    public async Task<Result<IReadOnlyList<StaffRegistrationRestaurantDto>>> Handle(
        GetStaffRegistrationRestaurantsQuery request,
        CancellationToken cancellationToken)
    {
        var restaurants = await _db.RestaurantProfiles
            .AsNoTracking()
            .Where(r => r.VerificationStatus == VerificationStatus.Approved
                        && r.IsOperationallyActive
                        && !r.IsArchived)
            .OrderBy(r => r.TradeNameAr)
            .ToListAsync(cancellationToken);

        var restaurantIds = restaurants.Select(r => r.Id).ToList();
        var logos = await _db.Documents
            .AsNoTracking()
            .Where(d => d.OwnerType == DocumentOwnerType.Restaurant
                        && restaurantIds.Contains(d.OwnerId)
                        && d.DocumentType == DocumentType.ProfileImage)
            .ToDictionaryAsync(d => d.OwnerId, d => d.StoragePath, cancellationToken);

        var list = restaurants.Select(r =>
        {
            logos.TryGetValue(r.Id, out var logoPath);
            var logoUrl = !string.IsNullOrWhiteSpace(logoPath) ? $"/api/v1/uploads/content?key={logoPath}" : null;
            var localizedName = _messages.Localize(r.TradeNameAr, r.TradeNameEn);
            return new StaffRegistrationRestaurantDto(
                r.Id,
                localizedName,
                r.TradeNameAr,
                r.TradeNameEn,
                r.LegalNameAr,
                r.LegalNameEn,
                logoUrl,
                r.ContactPhone,
                r.ContactEmail);
        }).ToList();

        return Result<IReadOnlyList<StaffRegistrationRestaurantDto>>.Success((IReadOnlyList<StaffRegistrationRestaurantDto>)list);
    }
}


public sealed record StaffDriverRegistrationResultDto(
    Guid RegistrationId,
    Guid? RestaurantId,
    string RestaurantName,
    string FullName,
    string FullNameAr,
    string FullNameEn,
    string Phone,
    string? Email,
    string Status,
    string Message);

public sealed record StaffDriverSelfRegistrationCommand(
    Guid? RestaurantId,
    string FullNameAr,
    string FullNameEn,
    string Phone,
    string? Email,
    string Password,
    string NationalId,
    DateTime NationalIdExpiry,
    DateTime? DateOfBirth,
    string? Nationality,
    string VehicleType,
    string VehicleModel,
    string VehiclePlate,
    int? VehicleYear,
    string? VehicleColor,
    bool? IsVehicleOwned,
    string LicenseNumber,
    DateTime LicenseExpiry,
    DateTime VehicleLicenseExpiry,
    DateTime? ContractExpiry,
    string NationalIdFrontStorageKey,
    string NationalIdBackStorageKey,
    string DrivingLicenseFrontStorageKey,
    string DrivingLicenseBackStorageKey,
    string VehicleRegistrationStorageKey,
    string? ProfileImageStorageKey = null,
    string? VehiclePhotoStorageKey = null,
    string? ContractStorageKey = null) : ICommand<StaffDriverRegistrationResultDto>;

public sealed class StaffDriverSelfRegistrationCommandValidator : AbstractValidator<StaffDriverSelfRegistrationCommand>
{
    public StaffDriverSelfRegistrationCommandValidator()
    {
        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MinimumLength(8)
            .WithMessageCode(MessageCodes.Validation.MinLength);

        RuleFor(x => x.FullNameAr)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MinimumLength(3)
            .MaximumLength(200);

        RuleFor(x => x.FullNameEn)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MinimumLength(3)
            .MaximumLength(200);

        RuleFor(x => x.Phone)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MaximumLength(30)
            .Matches(@"^\+?[0-9]{8,20}$")
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);

        RuleFor(x => x.Email)
            .EmailAddress()
            .WithMessageCode(MessageCodes.Validation.EmailInvalid)
            .MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.NationalId)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MaximumLength(50)
            .Matches(@"^\d{10,14}$")
            .WithMessageCode(MessageCodes.Validation.InvalidFormat);

        RuleFor(x => x.NationalIdExpiry)
            .Must(d => d.Date > DateTime.UtcNow.Date)
            .WithMessageCode(MessageCodes.Driver.ExpiryDateMustBeFuture);

        RuleFor(x => x.LicenseNumber)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MinimumLength(5)
            .MaximumLength(50);

        RuleFor(x => x.LicenseExpiry)
            .Must(d => d.Date > DateTime.UtcNow.Date)
            .WithMessageCode(MessageCodes.Driver.ExpiryDateMustBeFuture);

        RuleFor(x => x.VehiclePlate)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MinimumLength(2)
            .MaximumLength(30);

        RuleFor(x => x.VehicleType)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required);

        RuleFor(x => x.VehicleModel)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required);

        RuleFor(x => x.VehicleColor)
            .MaximumLength(50)
            .When(x => !string.IsNullOrWhiteSpace(x.VehicleColor));

        RuleFor(x => x.VehicleLicenseExpiry)
            .Must(d => d.Date > DateTime.UtcNow.Date)
            .WithMessageCode(MessageCodes.Driver.ExpiryDateMustBeFuture);

        RuleFor(x => x.ContractExpiry)
            .Must(d => d == null || d.Value.Date > DateTime.UtcNow.Date)
            .WithMessageCode(MessageCodes.Driver.ExpiryDateMustBeFuture)
            .When(x => x.ContractExpiry.HasValue);

        RuleFor(x => x.NationalIdFrontStorageKey)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required);

        RuleFor(x => x.NationalIdBackStorageKey)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required);

        RuleFor(x => x.DrivingLicenseFrontStorageKey)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required);

        RuleFor(x => x.DrivingLicenseBackStorageKey)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required);

        RuleFor(x => x.VehicleRegistrationStorageKey)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required);
    }
}

public sealed class StaffDriverSelfRegistrationCommandHandler
    : ICommandHandler<StaffDriverSelfRegistrationCommand, StaffDriverRegistrationResultDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IDriverRegistrationNotificationService _driverNotifier;
    private readonly IFileStorage? _storage;

    public StaffDriverSelfRegistrationCommandHandler(
        IUserAccountService users,
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock,
        ILocalizedMessageProvider messages,
        IDriverRegistrationNotificationService? driverNotifier = null,
        IFileStorage? storage = null)
    {
        _users = users;
        _db = db;
        _audit = audit;
        _clock = clock;
        _messages = messages;
        _driverNotifier = driverNotifier ?? NullDriverRegistrationNotificationService.Instance;
        _storage = storage;
    }

    public async Task<Result<StaffDriverRegistrationResultDto>> Handle(
        StaffDriverSelfRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        Domain.IdentityAccess.RestaurantProfile? restaurant = null;
        if (request.RestaurantId.HasValue && request.RestaurantId.Value != Guid.Empty)
        {
            restaurant = await _db.RestaurantProfiles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.RestaurantId.Value, cancellationToken);

            if (restaurant is null)
            {
                return Error.NotFound(MessageCodes.Driver.RestaurantNotFound, "Selected restaurant was not found.");
            }

            if (restaurant.VerificationStatus != VerificationStatus.Approved || !restaurant.IsOperationallyActive || restaurant.IsArchived)
            {
                return Error.Validation(MessageCodes.Driver.RestaurantInactive, "Selected restaurant is not active.");
            }
        }

        var requiredAndOptionalKeys = new string?[]
        {
            request.NationalIdFrontStorageKey, request.NationalIdBackStorageKey,
            request.DrivingLicenseFrontStorageKey, request.DrivingLicenseBackStorageKey,
            request.VehicleRegistrationStorageKey, request.ContractStorageKey,
            request.ProfileImageStorageKey, request.VehiclePhotoStorageKey
        };
        if (!await RegistrationStorageValidation.AllFilesExistAndHaveContentAsync(_storage, requiredAndOptionalKeys, cancellationToken))
        {
            return Error.Validation("Driver.InvalidStoredDocument", "One or more uploaded registration files are unavailable or empty.");
        }

        var normalizedPhone = request.Phone.Trim();
        var existingUser = await _users.FindByPhoneAsync(normalizedPhone, cancellationToken);
        var existingRegPhone = await _db.DriverRegistrations.AnyAsync(r => r.Phone == normalizedPhone, cancellationToken);
        if (existingUser is not null || existingRegPhone)
        {
            return Error.Conflict(MessageCodes.Driver.PhoneAlreadyRegistered, "A user with this phone number already exists.");
        }

        var normalizedEmail = request.Email?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            var existingUserEmail = await _users.FindByEmailAsync(normalizedEmail, cancellationToken);
            var existingRegEmail = await _db.DriverRegistrations.AnyAsync(r => r.Email == normalizedEmail, cancellationToken);
            if (existingUserEmail is not null || existingRegEmail)
            {
                return Error.Conflict(MessageCodes.Driver.EmailAlreadyRegistered, "A user with this email address already exists.");
            }
        }

        var normalizedNationalId = request.NationalId.Trim();
        var existingNidReg = await _db.DriverRegistrations.AnyAsync(r => r.NationalId == normalizedNationalId, cancellationToken);
        var existingNidProfile = await _db.DriverProfiles.AnyAsync(p => p.NationalId == normalizedNationalId, cancellationToken);
        if (existingNidReg || existingNidProfile)
        {
            return Error.Conflict(MessageCodes.Driver.NationalIdAlreadyRegistered, "A driver with this national ID already exists.");
        }

        var normalizedPlate = request.VehiclePlate.Trim();
        var existingPlate = await _db.DriverRegistrations.AnyAsync(r => r.VehiclePlate == normalizedPlate, cancellationToken);
        if (existingPlate)
        {
            return Error.Conflict(MessageCodes.Driver.VehiclePlateAlreadyRegistered, "A driver with this vehicle plate already exists.");
        }

        var createUserResult = await _users.CreateAsync(new CreateUserRequest(
            normalizedEmail,
            normalizedPhone,
            request.Password,
            request.FullNameEn.Trim(),
            UserType.Driver,
            AccountStatus.Registered,
            request.RestaurantId), cancellationToken);

        if (createUserResult.IsFailure)
        {
            return createUserResult.Error;
        }

        var newDriverUserId = createUserResult.Value.Id;
        await _users.AddToRoleAsync(newDriverUserId, AppRoles.Driver, cancellationToken);
        await _users.SetHasPasswordAsync(newDriverUserId, true, cancellationToken);

        var hasRestaurant = request.RestaurantId.HasValue && request.RestaurantId.Value != Guid.Empty;

        var registration = new Domain.IdentityAccess.DriverRegistration
        {
            SubmittedByUserId = newDriverUserId,
            RestaurantId = request.RestaurantId,
            FullNameAr = request.FullNameAr.Trim(),
            FullNameEn = request.FullNameEn.Trim(),
            Phone = normalizedPhone,
            Email = normalizedEmail,
            NationalId = normalizedNationalId,
            NationalIdExpiryUtc = request.NationalIdExpiry.ToUniversalTime(),
            DateOfBirthUtc = request.DateOfBirth?.ToUniversalTime(),
            Nationality = request.Nationality?.Trim(),
            LicenseNumber = request.LicenseNumber.Trim(),
            LicenseExpiryUtc = request.LicenseExpiry.ToUniversalTime(),
            VehiclePlate = normalizedPlate,
            VehicleModel = request.VehicleModel.Trim(),
            VehicleType = request.VehicleType.Trim(),
            VehicleYear = request.VehicleYear,
            VehicleColor = request.VehicleColor?.Trim(),
            IsVehicleOwned = request.IsVehicleOwned,
            VehicleRegistrationExpiryUtc = request.VehicleLicenseExpiry.ToUniversalTime(),
            ContractExpiryUtc = request.ContractExpiry?.ToUniversalTime(),
            ProfileImageStorageKey = request.ProfileImageStorageKey?.Trim(),
            VehiclePhotoStorageKey = request.VehiclePhotoStorageKey?.Trim(),
            Status = hasRestaurant ? VerificationStatus.Submitted : VerificationStatus.UnderReview,
            RegistrationSource = "MobileApp",
            RestaurantApprovalStatus = hasRestaurant ? VerificationStatus.Submitted : VerificationStatus.Approved,
            AdminApprovalStatus = VerificationStatus.Submitted,
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
                    CreatedAtUtc = _clock.UtcNow,
                });
                _db.Documents.Add(doc);
            }
        }

        await _audit.LogAsync(new AuditEntry(
            "StaffDriverSelfRegistered",
            "DriverRegistration",
            registration.Id,
            After: new { RestaurantId = request.RestaurantId, Phone = normalizedPhone, NationalId = normalizedNationalId },
            SourceChannel: "mobile_staff"), cancellationToken);

        var restaurantName = restaurant?.TradeNameAr ?? restaurant?.TradeNameEn ?? "MealMate Fleet";

        _db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = "DriverRegistrationSubmitted",
            AggregateType = "DriverRegistration",
            AggregateId = registration.Id.ToString(),
            DeduplicationKey = $"DriverRegistrationSubmitted_{registration.Id}",
            PayloadJson = JsonSerializer.Serialize(new
            {
                RegistrationId = registration.Id,
                DriverUserId = registration.SubmittedByUserId,
                Phone = registration.Phone,
                DriverName = registration.FullNameAr,
                RestaurantName = restaurantName,
                RestaurantId = request.RestaurantId,
                HasRestaurant = hasRestaurant,
                ReviewStage = hasRestaurant ? 1 : 2,
                ApplicationStatus = "Submitted"
            }),
            CreatedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        var successMessage = _messages.GetMessage(MessageCodes.Driver.RegistrationSubmitted);

        return new StaffDriverRegistrationResultDto(
            registration.Id,
            request.RestaurantId,
            restaurantName,
            _messages.Localize(registration.FullNameAr, registration.FullNameEn),
            registration.FullNameAr,
            registration.FullNameEn,
            registration.Phone,
            registration.Email,
            registration.Status.ToString(),
            successMessage);
    }
}

public sealed record DriverApplicationStatusDto(
    Guid RegistrationId,
    string Phone,
    string FullName,
    string FullNameAr,
    string FullNameEn,
    Guid RestaurantId,
    string RestaurantName,
    string Status,
    int Stage,
    string Badge,
    string Title,
    string Subtitle,
    string? Notice,
    string? RejectionReason,
    string? ChangeRequestNotes,
    string RestaurantApprovalStatus,
    string AdminApprovalStatus,
    bool CanResubmit,
    bool IsApproved,
    string? RequestedByRole = null);

public sealed record GetDriverApplicationStatusQuery(string? Phone, Guid? RegistrationId) : IQuery<DriverApplicationStatusDto>;

public sealed class GetDriverApplicationStatusQueryHandler : IQueryHandler<GetDriverApplicationStatusQuery, DriverApplicationStatusDto>
{
    private readonly IAppDbContext _db;
    private readonly ILocalizedMessageProvider _messages;
    private readonly ICurrentUser? _currentUser;

    public GetDriverApplicationStatusQueryHandler(
        IAppDbContext db,
        ILocalizedMessageProvider messages,
        ICurrentUser? currentUser = null)
    {
        _db = db;
        _messages = messages;
        _currentUser = currentUser;
    }

    public async Task<Result<DriverApplicationStatusDto>> Handle(
        GetDriverApplicationStatusQuery request,
        CancellationToken cancellationToken)
    {
        Domain.IdentityAccess.DriverRegistration? registration = null;
        if (request.RegistrationId.HasValue && request.RegistrationId.Value != Guid.Empty)
        {
            registration = await _db.DriverRegistrations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == request.RegistrationId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var normalizedPhone = request.Phone.Trim();
            registration = await _db.DriverRegistrations.AsNoTracking()
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefaultAsync(r => r.Phone == normalizedPhone, cancellationToken);
        }

        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Driver.StatusNotFound, "No driver registration found.");
        }

        if (_currentUser is not null && _currentUser.IsAuthenticated)
        {
            var isOwner = registration.SubmittedByUserId.HasValue && registration.SubmittedByUserId.Value == _currentUser.UserId;
            var isRestaurant = _currentUser.RestaurantId.HasValue && registration.RestaurantId == _currentUser.RestaurantId.Value;
            var isAdmin = _currentUser.Roles.Any(AppRoles.IsAdminRole) || _currentUser.HasPermission(PermissionCodes.DriverRegView);

            if (!isOwner && !isRestaurant && !isAdmin)
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "You can only view your own registration status.");
            }
        }
        else if (_currentUser is not null && !_currentUser.IsAuthenticated)
        {
            return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication required to view registration status.");
        }

        var restaurant = await _db.RestaurantProfiles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == registration.RestaurantId, cancellationToken);
        var restaurantName = restaurant?.TradeNameAr ?? restaurant?.TradeNameEn ?? "MealMate";

        return DriverStatusMapper.Map(registration, restaurantName, _messages);
    }
}

public sealed record ResubmitDriverRegistrationCommand(
    Guid RegistrationId,
    string? LicenseNumber = null,
    DateTime? LicenseExpiry = null,
    string? VehiclePlate = null,
    string? VehicleModel = null,
    string? VehicleType = null,
    int? VehicleYear = null,
    string? VehicleColor = null,
    bool? IsVehicleOwned = null,
    DateTime? VehicleLicenseExpiry = null,
    DateTime? ContractExpiry = null,
    string? NationalIdFrontStorageKey = null,
    string? NationalIdBackStorageKey = null,
    string? DrivingLicenseFrontStorageKey = null,
    string? DrivingLicenseBackStorageKey = null,
    string? VehicleRegistrationStorageKey = null,
    string? ProfileImageStorageKey = null,
    string? VehiclePhotoStorageKey = null,
    string? ContractStorageKey = null,
    string? FullNameAr = null,
    string? FullNameEn = null,
    string? Phone = null,
    string? Email = null,
    string? NationalId = null,
    DateTime? NationalIdExpiry = null,
    string? Nationality = null,
    DateTime? DateOfBirth = null) : ICommand<DriverApplicationStatusDto>;

public sealed class ResubmitDriverRegistrationCommandHandler
    : ICommandHandler<ResubmitDriverRegistrationCommand, DriverApplicationStatusDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IDriverRegistrationNotificationService _driverNotifier;
    private readonly ICurrentUser? _currentUser;
    private readonly IUserAccountService? _users;
    private readonly IFileStorage? _storage;

    public ResubmitDriverRegistrationCommandHandler(
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock,
        ILocalizedMessageProvider messages,
        IDriverRegistrationNotificationService? driverNotifier = null,
        ICurrentUser? currentUser = null,
        IUserAccountService? users = null,
        IFileStorage? storage = null)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _messages = messages;
        _driverNotifier = driverNotifier ?? NullDriverRegistrationNotificationService.Instance;
        _currentUser = currentUser;
        _users = users;
        _storage = storage;
    }

    public async Task<Result<DriverApplicationStatusDto>> Handle(
        ResubmitDriverRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var registration = await _db.DriverRegistrations
            .FirstOrDefaultAsync(r => r.Id == request.RegistrationId, cancellationToken);

        if (registration is null)
        {
            return Error.NotFound(MessageCodes.Driver.StatusNotFound, "No driver registration found.");
        }

        if (_currentUser is not null)
        {
            if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
            {
                return Error.Unauthorized(MessageCodes.Common.Unauthorized, "Authentication required to resubmit registration.");
            }

            var isOwner = registration.SubmittedByUserId.HasValue && registration.SubmittedByUserId.Value == _currentUser.UserId.Value;
            var isAdmin = _currentUser.Roles.Any(AppRoles.IsAdminRole) || _currentUser.HasPermission(PermissionCodes.DriverRegApprove);
            if (!isOwner && !isAdmin)
            {
                return Error.Forbidden(MessageCodes.Auth.Forbidden, "You can only resubmit your own registration.");
            }
        }

        // Terminal state guards (G09): Approved or Rejected cannot be reopened by Resubmit
        if (registration.Status == VerificationStatus.Approved)
        {
            return Error.Conflict("ALREADY_APPROVED", "This registration has already been approved and cannot be resubmitted.");
        }
        if (registration.Status == VerificationStatus.Rejected)
        {
            return Error.Conflict("ALREADY_REJECTED", "This registration has been rejected and cannot be resubmitted.");
        }
        if (registration.Status != VerificationStatus.NeedsChanges
            && registration.AdminApprovalStatus != VerificationStatus.NeedsChanges
            && registration.RestaurantApprovalStatus != VerificationStatus.NeedsChanges
            && registration.Status != VerificationStatus.Draft)
        {
            return Error.Conflict("INVALID_TRANSITION", "Registration is not in a state that permits changes.");
        }

        // A phone change needs a purpose-bound ownership challenge. No such flow is
        // available on resubmission, so reject it before changing tracked entities.
        if (!string.IsNullOrWhiteSpace(request.Phone)
            && !string.Equals(request.Phone.Trim(), registration.Phone, StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation("Driver.PhoneChangeRequiresVerification", "Phone changes must be completed through the verified phone change flow.");
        }

        var originalNationalId = registration.NationalId;
        var originalLicenseNumber = registration.LicenseNumber;
        var originalVehiclePlate = registration.VehiclePlate;

        var hasFieldChange =
            (!string.IsNullOrWhiteSpace(request.LicenseNumber) && !string.Equals(request.LicenseNumber.Trim(), registration.LicenseNumber, StringComparison.OrdinalIgnoreCase))
            || (request.LicenseExpiry.HasValue && request.LicenseExpiry.Value.ToUniversalTime() != registration.LicenseExpiryUtc)
            || (!string.IsNullOrWhiteSpace(request.VehiclePlate) && !string.Equals(request.VehiclePlate.Trim(), registration.VehiclePlate, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(request.VehicleModel) && !string.Equals(request.VehicleModel.Trim(), registration.VehicleModel, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(request.VehicleType) && !string.Equals(request.VehicleType.Trim(), registration.VehicleType, StringComparison.OrdinalIgnoreCase))
            || (request.VehicleYear.HasValue && request.VehicleYear.Value != registration.VehicleYear)
            || (!string.IsNullOrWhiteSpace(request.VehicleColor) && !string.Equals(request.VehicleColor.Trim(), registration.VehicleColor, StringComparison.OrdinalIgnoreCase))
            || (request.IsVehicleOwned.HasValue && request.IsVehicleOwned.Value != registration.IsVehicleOwned)
            || (request.VehicleLicenseExpiry.HasValue && request.VehicleLicenseExpiry.Value.ToUniversalTime() != registration.VehicleRegistrationExpiryUtc)
            || (request.ContractExpiry.HasValue && request.ContractExpiry.Value.ToUniversalTime() != registration.ContractExpiryUtc)
            || (!string.IsNullOrWhiteSpace(request.FullNameAr) && !string.Equals(request.FullNameAr.Trim(), registration.FullNameAr, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(request.FullNameEn) && !string.Equals(request.FullNameEn.Trim(), registration.FullNameEn, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(request.Phone) && !string.Equals(request.Phone.Trim(), registration.Phone, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(request.Email) && !string.Equals(request.Email.Trim(), registration.Email, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(request.NationalId) && !string.Equals(request.NationalId.Trim(), registration.NationalId, StringComparison.OrdinalIgnoreCase))
            || (request.NationalIdExpiry.HasValue && request.NationalIdExpiry.Value.ToUniversalTime() != registration.NationalIdExpiryUtc)
            || (!string.IsNullOrWhiteSpace(request.Nationality) && !string.Equals(request.Nationality.Trim(), registration.Nationality, StringComparison.OrdinalIgnoreCase))
            || (request.DateOfBirth.HasValue && request.DateOfBirth.Value.ToUniversalTime() != registration.DateOfBirthUtc)
            || !string.IsNullOrWhiteSpace(request.NationalIdFrontStorageKey)
            || !string.IsNullOrWhiteSpace(request.NationalIdBackStorageKey)
            || !string.IsNullOrWhiteSpace(request.DrivingLicenseFrontStorageKey)
            || !string.IsNullOrWhiteSpace(request.DrivingLicenseBackStorageKey)
            || !string.IsNullOrWhiteSpace(request.VehicleRegistrationStorageKey)
            || !string.IsNullOrWhiteSpace(request.ProfileImageStorageKey)
            || !string.IsNullOrWhiteSpace(request.VehiclePhotoStorageKey)
            || !string.IsNullOrWhiteSpace(request.ContractStorageKey);

        if (!hasFieldChange)
        {
            return Error.Validation("Resubmit.EmptyChanges", "يجب تعديل حقل واحد على الأقل أو رفع وثيقة جديدة لإعادة تقديم الطلب.");
        }

        var resubmittedFileKeys = new string?[]
        {
            request.NationalIdFrontStorageKey, request.NationalIdBackStorageKey,
            request.DrivingLicenseFrontStorageKey, request.DrivingLicenseBackStorageKey,
            request.VehicleRegistrationStorageKey, request.ContractStorageKey,
            request.ProfileImageStorageKey, request.VehiclePhotoStorageKey
        };
        if (!await RegistrationStorageValidation.AllFilesExistAndHaveContentAsync(_storage, resubmittedFileKeys, cancellationToken))
        {
            return Error.Validation("Driver.InvalidStoredDocument", "One or more uploaded registration files are unavailable or empty.");
        }

        if (!string.IsNullOrWhiteSpace(request.FullNameAr)) registration.FullNameAr = request.FullNameAr.Trim();
        if (!string.IsNullOrWhiteSpace(request.FullNameEn)) registration.FullNameEn = request.FullNameEn.Trim();
        if (!string.IsNullOrWhiteSpace(request.Phone))
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
        if (!string.IsNullOrWhiteSpace(request.Email)) registration.Email = request.Email.Trim();
        var now = _clock.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.NationalId))
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
        if (request.NationalIdExpiry.HasValue)
        {
            var expiryUtc = request.NationalIdExpiry.Value.ToUniversalTime();
            if (expiryUtc <= now)
            {
                return Error.Validation("Driver.InvalidNationalId", "تاريخ انتهاء الهوية الوطنية يجب أن يكون في المستقبل.");
            }
            registration.NationalIdExpiryUtc = expiryUtc;
        }
        if (!string.IsNullOrWhiteSpace(request.Nationality)) registration.Nationality = request.Nationality.Trim();
        if (request.DateOfBirth.HasValue)
        {
            var dobUtc = request.DateOfBirth.Value.ToUniversalTime();
            if (dobUtc > now.AddYears(-18))
            {
                return Error.Validation("Driver.Underage", "يجب ألا يقل عمر السائق عن 18 عاماً.");
            }
            registration.DateOfBirthUtc = dobUtc;
        }

        if (!string.IsNullOrWhiteSpace(request.LicenseNumber))
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
        if (request.LicenseExpiry.HasValue)
        {
            var expiryUtc = request.LicenseExpiry.Value.ToUniversalTime();
            if (expiryUtc <= now)
            {
                return Error.Validation("Driver.InvalidLicense", "تاريخ انتهاء رخصة القيادة يجب أن يكون في المستقبل.");
            }
            registration.LicenseExpiryUtc = expiryUtc;
        }
        if (!string.IsNullOrWhiteSpace(request.VehiclePlate))
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
        if (!string.IsNullOrWhiteSpace(request.VehicleModel)) registration.VehicleModel = request.VehicleModel.Trim();
        if (!string.IsNullOrWhiteSpace(request.VehicleType)) registration.VehicleType = request.VehicleType.Trim();
        if (request.VehicleYear.HasValue) registration.VehicleYear = request.VehicleYear.Value;
        if (!string.IsNullOrWhiteSpace(request.VehicleColor)) registration.VehicleColor = request.VehicleColor.Trim();
        if (request.IsVehicleOwned.HasValue) registration.IsVehicleOwned = request.IsVehicleOwned.Value;
        if (request.VehicleLicenseExpiry.HasValue)
        {
            var expiryUtc = request.VehicleLicenseExpiry.Value.ToUniversalTime();
            if (expiryUtc <= now)
            {
                return Error.Validation("Driver.InvalidVehicleRegistration", "تاريخ انتهاء تسجيل المركبة يجب أن يكون في المستقبل.");
            }
            registration.VehicleRegistrationExpiryUtc = expiryUtc;
        }
        if (request.ContractExpiry.HasValue)
        {
            var contractExpiryUtc = request.ContractExpiry.Value.ToUniversalTime();
            if (contractExpiryUtc <= now)
            {
                return Error.Validation("Driver.InvalidContract", "تاريخ انتهاء عقد العمل يجب أن يكون في المستقبل.");
            }
            registration.ContractExpiryUtc = contractExpiryUtc;
        }
        if (!string.IsNullOrWhiteSpace(request.ProfileImageStorageKey)) registration.ProfileImageStorageKey = request.ProfileImageStorageKey.Trim();
        if (!string.IsNullOrWhiteSpace(request.VehiclePhotoStorageKey)) registration.VehiclePhotoStorageKey = request.VehiclePhotoStorageKey.Trim();

        var fundamentalFieldsChanged =
            (!string.IsNullOrWhiteSpace(request.NationalId) && !string.Equals(request.NationalId.Trim(), originalNationalId, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(request.LicenseNumber) && !string.Equals(request.LicenseNumber.Trim(), originalLicenseNumber, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(request.VehiclePlate) && !string.Equals(request.VehiclePlate.Trim(), originalVehiclePlate, StringComparison.OrdinalIgnoreCase))
            || !string.IsNullOrWhiteSpace(request.NationalIdFrontStorageKey)
            || !string.IsNullOrWhiteSpace(request.NationalIdBackStorageKey)
            || !string.IsNullOrWhiteSpace(request.DrivingLicenseFrontStorageKey)
            || !string.IsNullOrWhiteSpace(request.DrivingLicenseBackStorageKey)
            || !string.IsNullOrWhiteSpace(request.VehicleRegistrationStorageKey)
            || !string.IsNullOrWhiteSpace(request.VehiclePhotoStorageKey)
            || !string.IsNullOrWhiteSpace(request.ProfileImageStorageKey)
            || !string.IsNullOrWhiteSpace(request.ContractStorageKey)
            || request.ContractExpiry.HasValue;

        var preserveRestaurantApproval =
            !fundamentalFieldsChanged
            && registration.AdminApprovalStatus == VerificationStatus.NeedsChanges
            && registration.RestaurantApprovalStatus == VerificationStatus.Approved;

        // Reset statuses for re-review
        registration.Status = VerificationStatus.Submitted;
        if (!preserveRestaurantApproval)
        {
            registration.RestaurantApprovalStatus = VerificationStatus.Submitted;
        }
        registration.AdminApprovalStatus = VerificationStatus.Submitted;
        registration.ChangeRequestNotes = null;
        registration.RejectionReason = null;

        var docsToAttach = new (DocumentType Type, string? Key, DateTime? ExpiryUtc)[]
        {
            (DocumentType.NationalIdFront, request.NationalIdFrontStorageKey, registration.NationalIdExpiryUtc),
            (DocumentType.NationalIdBack, request.NationalIdBackStorageKey, registration.NationalIdExpiryUtc),
            (DocumentType.DrivingLicenseFront, request.DrivingLicenseFrontStorageKey, registration.LicenseExpiryUtc),
            (DocumentType.DrivingLicenseBack, request.DrivingLicenseBackStorageKey, registration.LicenseExpiryUtc),
            (DocumentType.VehicleRegistration, request.VehicleRegistrationStorageKey, registration.VehicleRegistrationExpiryUtc),
            (DocumentType.EmploymentContract, request.ContractStorageKey, registration.ContractExpiryUtc),
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
                    CreatedAtUtc = _clock.UtcNow,
                });
                _db.Documents.Add(doc);
            }
        }

        await _audit.LogAsync(new AuditEntry(
            "DriverRegistrationResubmitted",
            "DriverRegistration",
            registration.Id,
            SourceChannel: "mobile_staff"), cancellationToken);

        var restaurant = registration.RestaurantId.HasValue
            ? await _db.RestaurantProfiles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == registration.RestaurantId.Value, cancellationToken)
            : null;
        var restaurantName = restaurant?.TradeNameAr ?? restaurant?.TradeNameEn ?? "MealMate";

        var preserveOrIndependent = preserveRestaurantApproval || !registration.RestaurantId.HasValue || registration.RestaurantId.Value == Guid.Empty;
        var resubmitStage = preserveOrIndependent ? 2 : 1;

        _db.OutboxMessages.Add(new OutboxMessage
        {
            EventType = "DriverRegistrationResubmitted",
            AggregateType = "DriverRegistration",
            AggregateId = registration.Id.ToString(),
            DeduplicationKey = $"DriverRegistrationResubmitted_{registration.Id}_{Guid.NewGuid():N}",
            PayloadJson = JsonSerializer.Serialize(new
            {
                RegistrationId = registration.Id,
                DriverUserId = registration.SubmittedByUserId,
                Phone = registration.Phone,
                DriverName = registration.FullNameAr,
                RestaurantName = restaurantName,
                RestaurantId = registration.RestaurantId,
                PreserveRestaurantApproval = preserveRestaurantApproval,
                ReviewStage = resubmitStage,
                ApplicationStatus = "Submitted"
            }),
            CreatedAtUtc = _clock.UtcNow
        });

        registration.ModifiedAtUtc = _clock.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(MessageCodes.Auth.InvalidState, "The registration status was modified by another concurrent operation. Please refresh and try again.");
        }

        return DriverStatusMapper.Map(registration, restaurantName, _messages);
    }
}

public static class DriverStatusMapper
{
    public static DriverApplicationStatusDto Map(
        Domain.IdentityAccess.DriverRegistration registration,
        string restaurantName,
        ILocalizedMessageProvider messages)
    {
        var isArabic = messages.CurrentCulture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase);

        int stage;
        string badge;
        string title;
        string subtitle;
        string? notice = null;
        string? rejectionReason = null;
        string? changeRequestNotes = null;
        string? requestedByRole = null;
        bool canResubmit = false;
        bool isApproved = false;

        if (registration.Status == VerificationStatus.NeedsChanges)
        {
            stage = 2; // Screen 03.02
            badge = isArabic ? "تعديل البيانات" : "Data Modification";
            title = messages.GetMessage(MessageCodes.Driver.StatusNeedsChangesTitle);
            subtitle = messages.GetMessage(MessageCodes.Driver.StatusNeedsChangesSubtitle);
            changeRequestNotes = registration.ChangeRequestNotes;
            canResubmit = true;
            requestedByRole = registration.AdminApprovalStatus == VerificationStatus.NeedsChanges ? "Admin" : "Restaurant";
        }
        else if (registration.Status == VerificationStatus.Rejected)
        {
            stage = 3; // Screen 03.03
            badge = isArabic ? "تم رفض الطلب" : "Application Rejected";
            title = messages.GetMessage(MessageCodes.Driver.StatusRejectedTitle);
            subtitle = messages.GetMessage(MessageCodes.Driver.StatusRejectedSubtitle);
            rejectionReason = registration.RejectionReason;
            canResubmit = true;
            requestedByRole = registration.AdminApprovalStatus == VerificationStatus.Rejected ? "Admin" : "Restaurant";
        }
        else if (registration.Status == VerificationStatus.Approved)
        {
            stage = 4; // Screen 03.04
            badge = isArabic ? "تم قبول الحساب" : "Account Approved";
            title = messages.GetMessage(MessageCodes.Driver.StatusApprovedTitle);
            subtitle = messages.GetMessage(MessageCodes.Driver.StatusApprovedSubtitle);
            isApproved = true;
        }
        else
        {
            stage = 1; // Screen 03.01
            badge = isArabic ? "قيد المراجعة" : "Under Review";
            title = messages.GetMessage(MessageCodes.Driver.StatusUnderReviewTitle);
            if (registration.RestaurantApprovalStatus == VerificationStatus.Approved)
            {
                subtitle = isArabic
                    ? "تمت موافقة المطعم، والطلب الآن قيد التدقيق النهائي من الإدارة العامة."
                    : "Restaurant has approved, and your application is under final administration review.";
            }
            else
            {
                subtitle = messages.GetMessage(MessageCodes.Driver.StatusUnderReviewSubtitle);
            }
            notice = messages.GetMessage(MessageCodes.Driver.StatusUnderReviewNotice);
        }

        return new DriverApplicationStatusDto(
            registration.Id,
            registration.Phone,
            messages.Localize(registration.FullNameAr, registration.FullNameEn),
            registration.FullNameAr,
            registration.FullNameEn,
            registration.RestaurantId ?? Guid.Empty,
            restaurantName,
            registration.Status.ToString(),
            stage,
            badge,
            title,
            subtitle,
            notice,
            rejectionReason,
            changeRequestNotes,
            registration.RestaurantApprovalStatus.ToString(),
            registration.AdminApprovalStatus.ToString(),
            canResubmit,
            isApproved,
            requestedByRole);
    }
}
