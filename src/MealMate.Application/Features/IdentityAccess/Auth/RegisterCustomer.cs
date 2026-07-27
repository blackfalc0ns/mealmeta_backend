using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;

namespace MealMate.Application.Features.IdentityAccess.Auth;

public sealed record RegisterCustomerCommand(
    string FullName,
    string? Email,
    string? Phone,
    string Password,
    string? PreferredLanguage = null,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<AuthResponseDto>;

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
    }
}

public sealed class RegisterCustomerCommandHandler : ICommandHandler<RegisterCustomerCommand, AuthResponseDto>
{
    private readonly IUserAccountService _users;
    private readonly IAuthSessionService _sessions;
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public RegisterCustomerCommandHandler(
        IUserAccountService users,
        IAuthSessionService sessions,
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _users = users;
        _sessions = sessions;
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<AuthResponseDto>> Handle(RegisterCustomerCommand request, CancellationToken cancellationToken)
    {
        var create = await _users.CreateAsync(new CreateUserRequest(
            request.Email?.Trim().ToLowerInvariant(),
            request.Phone?.Trim(),
            request.Password,
            request.FullName.Trim(),
            UserType.Customer,
            AccountStatus.Active), cancellationToken);

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
            OnboardingCompleted = false,
            CreatedAtUtc = _clock.UtcNow,
        });

        var session = await _sessions.IssueSessionAsync(user.Id, request.DeviceInfo, request.IpAddress, cancellationToken);
        if (session.IsFailure)
        {
            return session.Error;
        }

        await _audit.LogAsync(new AuditEntry(
            "CustomerRegistered",
            nameof(CustomerProfile),
            user.Id,
            After: new { user.Email, user.PhoneNumber, user.FullName },
            SourceChannel: "customer-app"), cancellationToken);

        return AuthMapping.ToResponse(session.Value, "choose_subscription");
    }
}
