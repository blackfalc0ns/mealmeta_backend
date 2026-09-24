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
using MealMate.Application.Common.Validation;
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

    public StaffDriverSelfRegistrationCommandHandler(
        IUserAccountService users,
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock,
        ILocalizedMessageProvider messages,
        IDriverRegistrationNotificationService? driverNotifier = null)
    {
        _users = users;
        _db = db;
        _audit = audit;
        _clock = clock;
        _messages = messages;
        _driverNotifier = driverNotifier ?? NullDriverRegistrationNotificationService.Instance;
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

        if (hasRestaurant)
        {
            await _driverNotifier.NotifyRestaurantDriverApplicationSubmittedAsync(
                request.RestaurantId!.Value,
                registration.Id,
                registration.FullNameAr,
                registration.Phone,
                cancellationToken);
        }
        else
        {
            await _driverNotifier.NotifyAdminsDriverApplicationReadyForConfirmationAsync(
                registration.Id,
                Guid.Empty,
                restaurantName,
                registration.FullNameAr,
                registration.Phone,
                cancellationToken);
        }

        await _driverNotifier.NotifyDriverApplicationSubmittedAsync(
            registration.Id,
            registration.SubmittedByUserId,
            registration.Phone,
            registration.FullNameAr,
            restaurantName,
            cancellationToken);

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

    public GetDriverApplicationStatusQueryHandler(IAppDbContext db, ILocalizedMessageProvider messages)
    {
        _db = db;
        _messages = messages;
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
    string? ContractStorageKey = null) : ICommand<DriverApplicationStatusDto>;

public sealed class ResubmitDriverRegistrationCommandHandler
    : ICommandHandler<ResubmitDriverRegistrationCommand, DriverApplicationStatusDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly ILocalizedMessageProvider _messages;
    private readonly IDriverRegistrationNotificationService _driverNotifier;

    public ResubmitDriverRegistrationCommandHandler(
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock,
        ILocalizedMessageProvider messages,
        IDriverRegistrationNotificationService? driverNotifier = null)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _messages = messages;
        _driverNotifier = driverNotifier ?? NullDriverRegistrationNotificationService.Instance;
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

        if (!string.IsNullOrWhiteSpace(request.LicenseNumber)) registration.LicenseNumber = request.LicenseNumber.Trim();
        if (request.LicenseExpiry.HasValue) registration.LicenseExpiryUtc = request.LicenseExpiry.Value.ToUniversalTime();
        if (!string.IsNullOrWhiteSpace(request.VehiclePlate)) registration.VehiclePlate = request.VehiclePlate.Trim();
        if (!string.IsNullOrWhiteSpace(request.VehicleModel)) registration.VehicleModel = request.VehicleModel.Trim();
        if (!string.IsNullOrWhiteSpace(request.VehicleType)) registration.VehicleType = request.VehicleType.Trim();
        if (request.VehicleYear.HasValue) registration.VehicleYear = request.VehicleYear.Value;
        if (!string.IsNullOrWhiteSpace(request.VehicleColor)) registration.VehicleColor = request.VehicleColor.Trim();
        if (request.IsVehicleOwned.HasValue) registration.IsVehicleOwned = request.IsVehicleOwned.Value;
        if (request.VehicleLicenseExpiry.HasValue) registration.VehicleRegistrationExpiryUtc = request.VehicleLicenseExpiry.Value.ToUniversalTime();
        if (request.ContractExpiry.HasValue) registration.ContractExpiryUtc = request.ContractExpiry.Value.ToUniversalTime();
        if (!string.IsNullOrWhiteSpace(request.ProfileImageStorageKey)) registration.ProfileImageStorageKey = request.ProfileImageStorageKey.Trim();
        if (!string.IsNullOrWhiteSpace(request.VehiclePhotoStorageKey)) registration.VehiclePhotoStorageKey = request.VehiclePhotoStorageKey.Trim();

        var preserveRestaurantApproval =
            registration.AdminApprovalStatus == VerificationStatus.NeedsChanges
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

        if (preserveRestaurantApproval || !registration.RestaurantId.HasValue || registration.RestaurantId.Value == Guid.Empty)
        {
            await _driverNotifier.NotifyAdminsDriverApplicationReadyForConfirmationAsync(
                registration.Id,
                registration.RestaurantId ?? Guid.Empty,
                restaurantName,
                registration.FullNameAr,
                registration.Phone,
                cancellationToken);
        }
        else
        {
            await _driverNotifier.NotifyRestaurantDriverApplicationResubmittedAsync(
                registration.RestaurantId.Value,
                registration.Id,
                registration.FullNameAr,
                registration.Phone,
                cancellationToken);
        }

        await _driverNotifier.NotifyDriverApplicationSubmittedAsync(
            registration.Id,
            registration.SubmittedByUserId,
            registration.Phone,
            registration.FullNameAr,
            restaurantName,
            cancellationToken);

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
