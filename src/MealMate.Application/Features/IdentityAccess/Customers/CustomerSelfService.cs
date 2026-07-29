using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Customers;

public sealed record CustomerProfileDto(
    Guid UserId,
    string? FullName,
    string? Email,
    string? PhoneNumber,
    string? PreferredLanguage,
    Guid? DefaultRegionId,
    string? DeliveryAddressLine,
    decimal? DeliveryLatitude,
    decimal? DeliveryLongitude,
    DateOnly? DateOfBirth,
    bool OnboardingCompleted,
    string? ProfileImageStorageKey,
    AccountStatus AccountStatus);

public sealed record GetCustomerProfileQuery() : IQuery<CustomerProfileDto>;

public sealed class GetCustomerProfileQueryHandler : IQueryHandler<GetCustomerProfileQuery, CustomerProfileDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;

    public GetCustomerProfileQueryHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<Result<CustomerProfileDto>> Handle(
        GetCustomerProfileQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");
        }

        var profile = await _db.CustomerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer profile not found.");
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User account not found.");
        }

        return new CustomerProfileDto(
            userId,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            profile.PreferredLanguage,
            profile.DefaultRegionId,
            profile.DeliveryAddressLine,
            profile.DeliveryLatitude,
            profile.DeliveryLongitude,
            profile.DateOfBirth,
            profile.OnboardingCompleted,
            profile.ProfileImageStorageKey,
            user.AccountStatus);
    }
}

public sealed record UpdateCustomerProfileCommand(
    string? FullName,
    string? PreferredLanguage,
    Guid? DefaultRegionId,
    string? DeliveryAddressLine,
    decimal? DeliveryLatitude,
    decimal? DeliveryLongitude,
    DateOnly? DateOfBirth) : ICommand<CustomerProfileDto>;

public sealed class UpdateCustomerProfileCommandValidator : AbstractValidator<UpdateCustomerProfileCommand>
{
    public UpdateCustomerProfileCommandValidator()
    {
        RuleFor(x => x.FullName).MaximumLength(200).When(x => x.FullName is not null);
        RuleFor(x => x.PreferredLanguage).MaximumLength(10).When(x => x.PreferredLanguage is not null);
        RuleFor(x => x.DeliveryAddressLine).MaximumLength(500).When(x => x.DeliveryAddressLine is not null);
        RuleFor(x => x.DeliveryLatitude)
            .InclusiveBetween(-90m, 90m)
            .When(x => x.DeliveryLatitude.HasValue);
        RuleFor(x => x.DeliveryLongitude)
            .InclusiveBetween(-180m, 180m)
            .When(x => x.DeliveryLongitude.HasValue);
    }
}

public sealed class UpdateCustomerProfileCommandHandler : ICommandHandler<UpdateCustomerProfileCommand, CustomerProfileDto>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;
    private readonly IAuditLogger _audit;

    public UpdateCustomerProfileCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
        _audit = audit;
    }

    public async Task<Result<CustomerProfileDto>> Handle(
        UpdateCustomerProfileCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User account not found.");
        }

        if (user.AccountStatus is AccountStatus.Suspended)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountSuspended, "Account is suspended.");
        }

        if (user.AccountStatus is AccountStatus.Closed)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountClosed, "Account is closed.");
        }

        var profile = await _db.CustomerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer profile not found.");
        }

        var before = new
        {
            profile.PreferredLanguage,
            profile.DefaultRegionId,
            profile.DeliveryAddressLine,
            profile.DeliveryLatitude,
            profile.DeliveryLongitude,
            profile.DateOfBirth,
            user.FullName,
        };

        if (request.FullName is not null)
        {
            await _users.UpdateFullNameAsync(userId, request.FullName.Trim(), cancellationToken);
        }

        if (request.PreferredLanguage is not null)
        {
            profile.PreferredLanguage = request.PreferredLanguage;
        }

        if (request.DefaultRegionId.HasValue)
        {
            profile.DefaultRegionId = request.DefaultRegionId;
        }

        if (request.DeliveryAddressLine is not null)
        {
            profile.DeliveryAddressLine = request.DeliveryAddressLine;
        }

        if (request.DeliveryLatitude.HasValue)
        {
            profile.DeliveryLatitude = request.DeliveryLatitude;
        }

        if (request.DeliveryLongitude.HasValue)
        {
            profile.DeliveryLongitude = request.DeliveryLongitude;
        }

        if (request.DateOfBirth.HasValue)
        {
            profile.DateOfBirth = request.DateOfBirth;
        }

        var after = new
        {
            profile.PreferredLanguage,
            profile.DefaultRegionId,
            profile.DeliveryAddressLine,
            profile.DeliveryLatitude,
            profile.DeliveryLongitude,
            profile.DateOfBirth,
            FullName = request.FullName ?? user.FullName,
        };

        await _audit.LogAsync(new AuditEntry(
            "CustomerProfileUpdated",
            nameof(CustomerProfile),
            profile.Id,
            Before: before,
            After: after,
            SourceChannel: "customer-app"), cancellationToken);

        var updatedUser = await _users.FindByIdAsync(userId, cancellationToken);

        return new CustomerProfileDto(
            userId,
            updatedUser?.FullName ?? request.FullName ?? user.FullName,
            user.Email,
            user.PhoneNumber,
            profile.PreferredLanguage,
            profile.DefaultRegionId,
            profile.DeliveryAddressLine,
            profile.DeliveryLatitude,
            profile.DeliveryLongitude,
            profile.DateOfBirth,
            profile.OnboardingCompleted,
            profile.ProfileImageStorageKey,
            user.AccountStatus);
    }
}

public sealed record UploadCustomerProfileImageCommand(
    string FileName,
    string ContentType,
    Stream Content) : ICommand<CustomerProfileDto>;

public sealed class UploadCustomerProfileImageCommandHandler
    : ICommandHandler<UploadCustomerProfileImageCommand, CustomerProfileDto>
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;
    private readonly IFileStorage _storage;
    private readonly IAuditLogger _audit;

    public UploadCustomerProfileImageCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users,
        IFileStorage storage,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
        _storage = storage;
        _audit = audit;
    }

    public async Task<Result<CustomerProfileDto>> Handle(
        UploadCustomerProfileImageCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");
        }

        if (!AllowedContentTypes.Contains(request.ContentType))
        {
            return Error.Validation(MessageCodes.Validation.Unknown, "Profile image must be JPG/PNG/WebP.");
        }

        if (request.Content.CanSeek && request.Content.Length > MaxBytes)
        {
            return Error.Validation(MessageCodes.Validation.MaxLength, "File exceeds 5MB limit.");
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User account not found.");
        }

        if (user.AccountStatus is AccountStatus.Suspended)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountSuspended, "Account is suspended.");
        }

        if (user.AccountStatus is AccountStatus.Closed)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountClosed, "Account is closed.");
        }

        var profile = await _db.CustomerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer profile not found.");
        }

        var stored = await _storage.SaveAsync(
            request.Content,
            request.FileName,
            request.ContentType,
            $"customers/{userId}/profile",
            cancellationToken);

        if (stored.SizeBytes > MaxBytes)
        {
            await _storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return Error.Validation(MessageCodes.Validation.MaxLength, "File exceeds 5MB limit.");
        }

        var previousStorageKey = profile.ProfileImageStorageKey;
        profile.ProfileImageStorageKey = stored.StorageKey;

        if (!string.IsNullOrWhiteSpace(previousStorageKey)
            && !string.Equals(previousStorageKey, stored.StorageKey, StringComparison.Ordinal))
        {
            await _storage.DeleteAsync(previousStorageKey, cancellationToken);
        }

        await _audit.LogAsync(new AuditEntry(
            "CustomerProfileImageUploaded",
            nameof(CustomerProfile),
            profile.Id,
            After: new { stored.StorageKey },
            SourceChannel: "customer-app"), cancellationToken);

        return new CustomerProfileDto(
            userId,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            profile.PreferredLanguage,
            profile.DefaultRegionId,
            profile.DeliveryAddressLine,
            profile.DeliveryLatitude,
            profile.DeliveryLongitude,
            profile.DateOfBirth,
            profile.OnboardingCompleted,
            profile.ProfileImageStorageKey,
            user.AccountStatus);
    }
}

public sealed record DeleteCustomerProfileImageCommand() : ICommand;

public sealed class DeleteCustomerProfileImageCommandHandler : ICommandHandler<DeleteCustomerProfileImageCommand>
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IUserAccountService _users;
    private readonly IFileStorage _storage;
    private readonly IAuditLogger _audit;

    public DeleteCustomerProfileImageCommandHandler(
        IAppDbContext db,
        ICurrentUser currentUser,
        IUserAccountService users,
        IFileStorage storage,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _users = users;
        _storage = storage;
        _audit = audit;
    }

    public async Task<Result> Handle(
        DeleteCustomerProfileImageCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return Error.Unauthorized(MessageCodes.Auth.InvalidCredentials, "User not authenticated.");
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User account not found.");
        }

        if (user.AccountStatus is AccountStatus.Suspended)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountSuspended, "Account is suspended.");
        }

        if (user.AccountStatus is AccountStatus.Closed)
        {
            return Error.Forbidden(MessageCodes.Auth.AccountClosed, "Account is closed.");
        }

        var profile = await _db.CustomerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer profile not found.");
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileImageStorageKey))
        {
            return Result.Success();
        }

        var storageKey = profile.ProfileImageStorageKey;
        await _storage.DeleteAsync(storageKey, cancellationToken);
        profile.ProfileImageStorageKey = null;

        await _audit.LogAsync(new AuditEntry(
            "CustomerProfileImageDeleted",
            nameof(CustomerProfile),
            profile.Id,
            Before: new { StorageKey = storageKey },
            SourceChannel: "customer-app"), cancellationToken);

        return Result.Success();
    }
}
