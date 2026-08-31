using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Common.Validation;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.Foundation;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Auth;

// ──────────────────────────── DTOs ────────────────────────────

public sealed record RestaurantRegistrationStartResponse(
    string RegistrationToken,
    DateTime ExpiresAtUtc);

public sealed record RestaurantRegistrationVerifyResponse(
    Guid UserId,
    string Email);

// ──────────────────────────── Start ────────────────────────────

public sealed record StartRestaurantRegistrationCommand(
    string Email,
    string Phone,
    string Password,
    string FullName) : ICommand<RestaurantRegistrationStartResponse>;

public sealed class StartRestaurantRegistrationCommandValidator
    : AbstractValidator<StartRestaurantRegistrationCommand>
{
    public StartRestaurantRegistrationCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessageCode(MessageCodes.Validation.RequiredField)
            .EmailAddress().WithMessageCode(MessageCodes.Validation.EmailInvalid);
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.Password)
            .NotEmpty().WithMessageCode(MessageCodes.Validation.RequiredField)
            .MinimumLength(8).WithMessageCode(MessageCodes.Validation.PasswordMinimumLength)
            .Must(password => password is not null
                && password.Any(character => character is >= 'A' and <= 'Z')
                && password.Any(character => character is >= 'a' and <= 'z')
                && password.Any(character => character is >= '0' and <= '9'))
            .WithMessageCode(MessageCodes.Auth.PasswordTooWeak);
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessageCode(MessageCodes.Validation.RequiredField)
            .MaximumLength(200);
    }
}

public sealed class StartRestaurantRegistrationCommandHandler
    : ICommandHandler<StartRestaurantRegistrationCommand, RestaurantRegistrationStartResponse>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IRestaurantAccountRegistrationTokenService _tokens;

    public StartRestaurantRegistrationCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IRestaurantAccountRegistrationTokenService tokens)
    {
        _users = users;
        _otp = otp;
        _tokens = tokens;
    }

    public async Task<Result<RestaurantRegistrationStartResponse>> Handle(
        StartRestaurantRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var phone = request.Phone.Trim();

        var existingEmail = await _users.FindByEmailAsync(email, cancellationToken);
        if (existingEmail is not null)
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateEmail, "Email already registered.");
        }

        var existingPhone = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (existingPhone is not null)
        {
            return Error.Conflict(MessageCodes.Auth.DuplicatePhone, "Phone already registered.");
        }

        var pending = new PendingRegistration(email, phone, request.Password, request.FullName.Trim());
        var created = _tokens.CreateToken(pending);

        var sendResult = await _otp.SendAsync(
            new OtpSendRequest(email, "Email", OtpPurposes.RestaurantRegistration),
            cancellationToken);

        if (sendResult.IsFailure)
        {
            return sendResult.Error;
        }

        return new RestaurantRegistrationStartResponse(created.Token, created.ExpiresAtUtc);
    }
}

// ──────────────────────────── Verify ────────────────────────────

public sealed record VerifyRestaurantRegistrationCommand(
    string RegistrationToken,
    string Code) : ICommand<RestaurantRegistrationVerifyResponse>;

public sealed class VerifyRestaurantRegistrationCommandValidator
    : AbstractValidator<VerifyRestaurantRegistrationCommand>
{
    public VerifyRestaurantRegistrationCommandValidator()
    {
        RuleFor(x => x.RegistrationToken)
            .NotEmpty().WithMessageCode(MessageCodes.Validation.RequiredField);
        RuleFor(x => x.Code)
            .NotEmpty().WithMessageCode(MessageCodes.Validation.RequiredField)
            .Length(6).WithMessageCode(MessageCodes.Validation.RequiredField);
    }
}

public sealed class VerifyRestaurantRegistrationCommandHandler
    : ICommandHandler<VerifyRestaurantRegistrationCommand, RestaurantRegistrationVerifyResponse>
{
    private readonly IAppDbContext? _db;
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IRestaurantAccountRegistrationTokenService _tokens;

    public VerifyRestaurantRegistrationCommandHandler(
        IAppDbContext db,
        IUserAccountService users,
        IOtpService otp,
        IRestaurantAccountRegistrationTokenService tokens)
    {
        _db = db;
        _users = users;
        _otp = otp;
        _tokens = tokens;
    }

    // Kept for existing unit-test callers; production DI uses the scoped
    // database-aware constructor above so tenant binding is always enforced.
    public VerifyRestaurantRegistrationCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IRestaurantAccountRegistrationTokenService tokens)
        : this(null, users, otp, tokens)
    {
    }

    public async Task<Result<RestaurantRegistrationVerifyResponse>> Handle(
        VerifyRestaurantRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var resolved = _tokens.ResolveToken(request.RegistrationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var pending = resolved.Value;

        // Re-check duplicates (could have been taken between start and verify)
        var existingEmail = await _users.FindByEmailAsync(pending.Email, cancellationToken);
        if (existingEmail is not null)
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateEmail, "Email already registered.");
        }

        var existingPhone = await _users.FindByPhoneAsync(pending.Phone, cancellationToken);
        if (existingPhone is not null)
        {
            return Error.Conflict(MessageCodes.Auth.DuplicatePhone, "Phone already registered.");
        }

        // Verify OTP for the payload email + RestaurantRegistration purpose
        var otpResult = await _otp.VerifyAsync(
            new OtpVerifyRequest(pending.Email, OtpPurposes.RestaurantRegistration, request.Code),
            cancellationToken);

        if (otpResult.IsFailure)
        {
            return otpResult.Error;
        }

        // OTP is already consumed. Finish account creation even if the client
        // disconnects mid-request (slow DB / navigation), otherwise the user
        // gets a false 500 and cannot reuse the verification code.
        cancellationToken = CancellationToken.None;

        // Create the user account
        var create = await _users.CreateAsync(new CreateUserRequest(
            pending.Email,
            pending.Phone,
            pending.Password,
            pending.FullName,
            UserType.Restaurant,
            AccountStatus.Registered), cancellationToken);

        if (create.IsFailure)
        {
            return create.Error;
        }

        var user = create.Value;

        // Restaurant signup is scoped to the official Kuwait tenant. Add the
        // tenant scope before the automatic post-verification login so the
        // issued access token contains TEN-KW and registration can proceed.
        if (_db is not null)
        {
            var kuwaitTenant = await _db.Tenants
                .FirstOrDefaultAsync(
                    tenant => tenant.Code == "TEN-KW"
                        && tenant.IsActive
                        && tenant.Status == TenantStatus.Active,
                    cancellationToken);
            if (kuwaitTenant is null)
            {
                return Error.Failure(
                    "catalog.tenant_required",
                    "The Kuwait tenant is not configured.");
            }

            var kuwaitScope = await _db.AccessScopes
                .FirstOrDefaultAsync(
                    scope => scope.TenantId == kuwaitTenant.Id
                        && scope.ScopeType == AccessScopeType.Tenant
                        && scope.IsActive,
                    cancellationToken);
            if (kuwaitScope is null)
            {
                return Error.Failure(
                    "catalog.tenant_required",
                    "The Kuwait tenant scope is not configured.");
            }

            if (!await _db.UserScopes.AnyAsync(
                    scope => scope.UserId == user.Id && scope.AccessScopeId == kuwaitScope.Id,
                    cancellationToken))
            {
                _db.UserScopes.Add(new UserScope
                {
                    UserId = user.Id,
                    AccessScopeId = kuwaitScope.Id,
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        var roleAssignment = await _users.AddToRoleAsync(
            user.Id,
            AppRoles.RestaurantOwner,
            cancellationToken);
        if (roleAssignment.IsFailure)
        {
            return roleAssignment.Error;
        }

        await _users.SetEmailConfirmedAsync(user.Id, true, cancellationToken);

        return new RestaurantRegistrationVerifyResponse(user.Id, user.Email ?? pending.Email);
    }
}

// ──────────────────────────── Resend ────────────────────────────

public sealed record ResendRestaurantRegistrationOtpCommand(
    string RegistrationToken) : ICommand;

public sealed class ResendRestaurantRegistrationOtpCommandValidator
    : AbstractValidator<ResendRestaurantRegistrationOtpCommand>
{
    public ResendRestaurantRegistrationOtpCommandValidator()
    {
        RuleFor(x => x.RegistrationToken)
            .NotEmpty().WithMessageCode(MessageCodes.Validation.RequiredField);
    }
}

public sealed class ResendRestaurantRegistrationOtpCommandHandler
    : ICommandHandler<ResendRestaurantRegistrationOtpCommand>
{
    private readonly IOtpService _otp;
    private readonly IRestaurantAccountRegistrationTokenService _tokens;

    public ResendRestaurantRegistrationOtpCommandHandler(
        IOtpService otp,
        IRestaurantAccountRegistrationTokenService tokens)
    {
        _otp = otp;
        _tokens = tokens;
    }

    public async Task<Result> Handle(
        ResendRestaurantRegistrationOtpCommand request,
        CancellationToken cancellationToken)
    {
        var resolved = _tokens.ResolveToken(request.RegistrationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var pending = resolved.Value;

        return await _otp.SendAsync(
            new OtpSendRequest(pending.Email, "Email", OtpPurposes.RestaurantRegistration),
            cancellationToken);
    }
}
