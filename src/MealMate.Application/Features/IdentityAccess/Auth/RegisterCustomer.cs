using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using MealMate.Domain.RestaurantOperations;

namespace MealMate.Application.Features.IdentityAccess.Auth;

public sealed record RegisterCustomerCommand(
    string FullName,
    string? Email,
    string? Phone,
    string Password,
    string? PreferredLanguage = null,
    string? DeviceInfo = null,
    string? IpAddress = null,
    string? DeliveryAddressLine = null,
    decimal? DeliveryLatitude = null,
    decimal? DeliveryLongitude = null) : ICommand<CustomerRegistrationStartDto>;

public sealed class RegisterCustomerCommandValidator : AbstractValidator<RegisterCustomerCommand>
{
    public RegisterCustomerCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Email) || !string.IsNullOrWhiteSpace(x.Phone))
            .WithErrorCode(MessageCodes.Validation.Required)
            .WithMessage("Email or phone is required.");
        When(x => !string.IsNullOrWhiteSpace(x.Email), () => RuleFor(x => x.Email!).EmailAddress());
        When(x => string.IsNullOrWhiteSpace(x.Email), () => RuleFor(x => x.Phone!).NotEmpty());
        RuleFor(x => x.DeliveryAddressLine).MaximumLength(500).When(x => x.DeliveryAddressLine is not null);
        RuleFor(x => x.DeliveryLatitude)
            .InclusiveBetween(-90m, 90m)
            .When(x => x.DeliveryLatitude.HasValue);
        RuleFor(x => x.DeliveryLongitude)
            .InclusiveBetween(-180m, 180m)
            .When(x => x.DeliveryLongitude.HasValue);
    }
}

public sealed class RegisterCustomerCommandHandler : ICommandHandler<RegisterCustomerCommand, CustomerRegistrationStartDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;
    private readonly IOtpService _otp;

    public RegisterCustomerCommandHandler(
        IUserAccountService users,
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock,
        IOtpService otp)
    {
        _users = users;
        _db = db;
        _audit = audit;
        _clock = clock;
        _otp = otp;
    }

    public async Task<Result<CustomerRegistrationStartDto>> Handle(RegisterCustomerCommand request, CancellationToken cancellationToken)
    {
        var phone = request.Phone?.Trim();
        if (string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(request.Email))
        {
            return Error.Validation(MessageCodes.Validation.Required, "Phone is required for customer registration.");
        }

        var create = await _users.CreateAsync(new CreateUserRequest(
            request.Email?.Trim().ToLowerInvariant(),
            phone,
            request.Password,
            request.FullName.Trim(),
            UserType.Customer,
            AccountStatus.PhonePending), cancellationToken);

        if (create.IsFailure)
        {
            return create.Error;
        }

        var user = create.Value;
        await _users.AddToRoleAsync(user.Id, AppRoles.Customer, cancellationToken);

        _db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = user.Id,
            PreferredLanguage = request.PreferredLanguage ?? "ar",
            DeliveryAddressLine = request.DeliveryAddressLine,
            DeliveryLatitude = request.DeliveryLatitude,
            DeliveryLongitude = request.DeliveryLongitude,
            OnboardingCompleted = false,
            CreatedAtUtc = _clock.UtcNow,
        });

        // Send VerifyPhone OTP via WhatsApp channel
        var otpResult = await _otp.SendAsync(new OtpSendRequest(
            phone!,
            "WhatsApp",
            OtpPurposes.VerifyPhone,
            user.Id), cancellationToken);

        if (otpResult.IsFailure)
        {
            return otpResult.Error;
        }

        await _audit.LogAsync(new AuditEntry(
            "CustomerRegistered",
            nameof(CustomerProfile),
            user.Id,
            After: new { user.Email, user.PhoneNumber, user.FullName },
            SourceChannel: "customer-app"), cancellationToken);

        return new CustomerRegistrationStartDto(
            user.Id,
            phone!,
            OtpSent: true,
            NextStep: "verify_phone_otp");
    }
}

public sealed record RegisterDeliveryManagerCommand(
    string FullName,
    string Email,
    string Phone,
    string? Password,
    Guid? RestaurantId = null,
    string? RegionId = null) : ICommand<AuthResponseDto>;

public sealed class RegisterDeliveryManagerCommandValidator : AbstractValidator<RegisterDeliveryManagerCommand>
{
    public RegisterDeliveryManagerCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Password)
            .MinimumLength(8)
            .When(x => !string.IsNullOrWhiteSpace(x.Password));
    }
}

public sealed class RegisterDeliveryManagerCommandHandler
    : ICommandHandler<RegisterDeliveryManagerCommand, AuthResponseDto>
{
    private readonly IUserAccountService _users;
    private readonly IAuthSessionService _sessions;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public RegisterDeliveryManagerCommandHandler(
        IUserAccountService users,
        IAuthSessionService sessions,
        ITransactionalEmailSender emailSender,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _users = users;
        _sessions = sessions;
        _emailSender = emailSender;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<AuthResponseDto>> Handle(RegisterDeliveryManagerCommand request, CancellationToken cancellationToken)
    {
        var tempPassword = string.IsNullOrWhiteSpace(request.Password)
            ? MealMate.Application.Common.Helpers.TemporaryPasswordGenerator.Generate()
            : request.Password;

        var create = await _users.CreateAsync(new CreateUserRequest(
            request.Email.Trim().ToLowerInvariant(),
            request.Phone.Trim(),
            tempPassword,
            request.FullName.Trim(),
            UserType.DeliveryManager,
            AccountStatus.Active,
            request.RestaurantId), cancellationToken);

        if (create.IsFailure)
        {
            return create.Error;
        }

        var user = create.Value;
        await _users.AddToRoleAsync(user.Id, AppRoles.DeliveryManager, cancellationToken);

        var rendered = MealMate.Application.Features.AdminDashboard.Email.EmailTemplateCatalog.Render(
            MealMate.Application.Features.AdminDashboard.Email.EmailTemplateCatalog.DeliveryManagerInvite,
            "ar",
            new Dictionary<string, string>
            {
                ["recipient_name"] = user.FullName ?? user.Email,
                ["email"] = user.Email,
                ["password"] = tempPassword,
                ["support_email"] = "support@mealmate.app"
            });

        if (rendered.IsSuccess)
        {
            await _emailSender.SendAsync(new TransactionalEmailRequest(
                user.Email,
                rendered.Value.Subject,
                rendered.Value.Html,
                rendered.Value.Text,
                TemplateId: MealMate.Application.Features.AdminDashboard.Email.EmailTemplateCatalog.DeliveryManagerInvite), cancellationToken);
        }

        var session = await _sessions.IssueSessionAsync(user.Id, null, null, cancellationToken);
        if (session.IsFailure)
        {
            return session.Error;
        }

        await _audit.LogAsync(new AuditEntry(
            "DeliveryManagerRegistered",
            "User",
            user.Id,
            After: new { user.Email, user.PhoneNumber, user.FullName, RestaurantId = request.RestaurantId },
            SourceChannel: "admin"), cancellationToken);

        return AuthMapping.ToResponse(session.Value);
    }
}
