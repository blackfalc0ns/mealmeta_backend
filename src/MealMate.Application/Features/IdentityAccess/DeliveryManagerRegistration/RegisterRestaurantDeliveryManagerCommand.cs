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
using MealMate.Domain.Enums;
using MealMate.Domain.Foundation;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;

public sealed record DeliveryManagerRegistrationDto(
    Guid Id,
    Guid RestaurantId,
    string FullName,
    string Phone,
    string Email,
    string NationalId,
    DateTime? NationalIdExpiryUtc,
    DateTime? ContractExpiryUtc,
    DeliveryManagerCoverageType CoverageType,
    string? CoverageScopeId,
    string? CoverageScopeName,
    VerificationStatus Status,
    DateTime CreatedAtUtc,
    string? RejectionReason,
    string? ChangeRequestNotes,
    Guid? ResultingUserId);

public sealed record RegisterRestaurantDeliveryManagerCommand(
    string FullName,
    string Phone,
    string Email,
    string NationalId,
    DateTime NationalIdExpiry,
    DateTime ContractExpiry,
    DeliveryManagerCoverageType CoverageType,
    string? CoverageScopeId,
    string? CoverageScopeName,
    Guid? RestaurantId,
    string NationalIdFrontStorageKey,
    string NationalIdBackStorageKey,
    string ContractStorageKey) : ICommand<DeliveryManagerRegistrationDto>;

public sealed class RegisterRestaurantDeliveryManagerCommandValidator : AbstractValidator<RegisterRestaurantDeliveryManagerCommand>
{
    public RegisterRestaurantDeliveryManagerCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MinimumLength(3).MaximumLength(200);
        RuleFor(x => x.Phone)
            .NotEmpty()
            .MaximumLength(30)
            .Matches(@"^\+?[0-9]{8,20}$")
            .WithMessage("Phone number must contain between 8 and 20 digits, optionally starting with +.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.NationalId)
            .NotEmpty()
            .MaximumLength(50)
            .Matches(@"^\d{10,14}$")
            .WithMessage("National ID must be between 10 and 14 digits.");
        RuleFor(x => x.NationalIdFrontStorageKey).NotEmpty();
        RuleFor(x => x.NationalIdBackStorageKey).NotEmpty();
        RuleFor(x => x.ContractStorageKey).NotEmpty();

        RuleFor(x => x.NationalIdExpiry)
            .Must(d => d.Date > DateTime.UtcNow.Date)
            .WithMessage("National ID expiry date must be in the future.");

        RuleFor(x => x.ContractExpiry)
            .Must(d => d.Date > DateTime.UtcNow.Date)
            .WithMessage("Contract expiry date must be in the future.");

        RuleFor(x => x.CoverageType)
            .IsInEnum();
    }
}

public sealed class RegisterRestaurantDeliveryManagerCommandHandler
    : ICommandHandler<RegisterRestaurantDeliveryManagerCommand, DeliveryManagerRegistrationDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public RegisterRestaurantDeliveryManagerCommandHandler(
        IUserAccountService users,
        IAppDbContext db,
        ICurrentUser currentUser,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _users = users;
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<DeliveryManagerRegistrationDto>> Handle(
        RegisterRestaurantDeliveryManagerCommand request,
        CancellationToken cancellationToken)
    {
        var targetRestaurantId = _currentUser.RestaurantId ?? request.RestaurantId;
        if (!targetRestaurantId.HasValue || targetRestaurantId.Value == Guid.Empty)
        {
            return Error.Validation(MessageCodes.Validation.Required, "Restaurant ID could not be identified.");
        }

        var normalizedPhone = request.Phone.Trim();
        var existingUserPhone = await _users.FindByPhoneAsync(normalizedPhone, cancellationToken);
        var existingRegPhone = await _db.DeliveryManagerRegistrations.AnyAsync(r => r.Phone == normalizedPhone, cancellationToken);
        if (existingUserPhone is not null || existingRegPhone)
        {
            return Error.Conflict("auth.phone_already_registered", "A user with this phone number already exists.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existingUserEmail = await _users.FindByEmailAsync(normalizedEmail, cancellationToken);
        var existingRegEmail = await _db.DeliveryManagerRegistrations.AnyAsync(r => r.Email == normalizedEmail, cancellationToken);
        if (existingUserEmail is not null || existingRegEmail)
        {
            return Error.Conflict("auth.email_already_registered", "A user with this email address already exists.");
        }

        var normalizedNationalId = request.NationalId.Trim();
        var existingNidReg = await _db.DeliveryManagerRegistrations.AnyAsync(r => r.NationalId == normalizedNationalId, cancellationToken);
        if (existingNidReg)
        {
            return Error.Conflict("auth.national_id_already_registered", "A delivery manager with this national ID already exists.");
        }

        // Temporary strong password created until the delivery manager activates their account via phone OTP on mobile
        var tempPassword = Guid.NewGuid().ToString("N") + "Mm1!#";
        var createUserResult = await _users.CreateAsync(new CreateUserRequest(
            normalizedEmail,
            normalizedPhone,
            tempPassword,
            request.FullName.Trim(),
            UserType.DeliveryManager,
            AccountStatus.Registered,
            targetRestaurantId.Value), cancellationToken);

        if (createUserResult.IsFailure)
        {
            return createUserResult.Error;
        }

        var newUserId = createUserResult.Value.Id;
        await _users.AddToRoleAsync(newUserId, AppRoles.DeliveryManager, cancellationToken);

        var registration = new Domain.IdentityAccess.DeliveryManagerRegistration
        {
            SubmittedByUserId = _currentUser.UserId ?? newUserId,
            RestaurantId = targetRestaurantId.Value,
            FullName = request.FullName.Trim(),
            Phone = normalizedPhone,
            Email = normalizedEmail,
            NationalId = normalizedNationalId,
            NationalIdExpiryUtc = request.NationalIdExpiry.ToUniversalTime(),
            ContractExpiryUtc = request.ContractExpiry.ToUniversalTime(),
            CoverageType = request.CoverageType,
            CoverageScopeId = request.CoverageScopeId?.Trim(),
            CoverageScopeName = request.CoverageScopeName?.Trim(),
            Status = VerificationStatus.Submitted,
            CreatedAtUtc = _clock.UtcNow,
            ResultingUserId = newUserId
        };

        _db.DeliveryManagerRegistrations.Add(registration);

        var docsToAttach = new (DocumentType Type, string Key, DateTime ExpiryUtc)[]
        {
            (DocumentType.NationalIdFront, request.NationalIdFrontStorageKey, request.NationalIdExpiry.ToUniversalTime()),
            (DocumentType.NationalIdBack, request.NationalIdBackStorageKey, request.NationalIdExpiry.ToUniversalTime()),
            (DocumentType.EmploymentContract, request.ContractStorageKey, request.ContractExpiry.ToUniversalTime()),
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
                _db.Documents.Add(doc);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(new AuditEntry(
            "DeliveryManagerRegisteredByRestaurant",
            "DeliveryManagerRegistration",
            registration.Id,
            After: new
            {
                registration.FullName,
                registration.Phone,
                registration.Email,
                registration.RestaurantId,
                registration.CoverageType,
                registration.CoverageScopeName,
                UserId = newUserId,
            },
            SourceChannel: "restaurant-portal"), cancellationToken);

        var dto = new DeliveryManagerRegistrationDto(
            registration.Id,
            registration.RestaurantId,
            registration.FullName,
            registration.Phone,
            registration.Email,
            registration.NationalId,
            registration.NationalIdExpiryUtc,
            registration.ContractExpiryUtc,
            registration.CoverageType,
            registration.CoverageScopeId,
            registration.CoverageScopeName,
            registration.Status,
            registration.CreatedAtUtc,
            registration.RejectionReason,
            registration.ChangeRequestNotes,
            registration.ResultingUserId);

        return dto;
    }
}
