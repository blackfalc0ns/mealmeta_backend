using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Influencer;

public sealed record InfluencerDto(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? InfluencerCode,
    Guid? LinkedCustomerUserId,
    VerificationStatus VerificationStatus,
    bool IsActive);

public sealed record RegisterInfluencerCommand(
    string DisplayName,
    string Email,
    string? Phone,
    string Password,
    string InfluencerCode) : ICommand<InfluencerDto>;

public sealed class RegisterInfluencerCommandValidator : AbstractValidator<RegisterInfluencerCommand>
{
    public RegisterInfluencerCommandValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.InfluencerCode).NotEmpty().MaximumLength(50);
    }
}

public sealed class RegisterInfluencerCommandHandler : ICommandHandler<RegisterInfluencerCommand, InfluencerDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IDateTimeProvider _clock;

    public RegisterInfluencerCommandHandler(
        IUserAccountService users,
        IAppDbContext db,
        IAuditLogger audit,
        IDateTimeProvider clock)
    {
        _users = users;
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<InfluencerDto>> Handle(RegisterInfluencerCommand request, CancellationToken cancellationToken)
    {
        var code = request.InfluencerCode.Trim().ToUpperInvariant();
        if (await _db.InfluencerProfiles.AnyAsync(x => x.InfluencerCode == code, cancellationToken))
        {
            return Error.Conflict(MessageCodes.Auth.DuplicateInfluencerCode, "Influencer code already exists.");
        }

        var create = await _users.CreateAsync(new CreateUserRequest(
            request.Email.Trim().ToLowerInvariant(),
            request.Phone?.Trim(),
            request.Password,
            request.DisplayName,
            UserType.Influencer,
            AccountStatus.Active), cancellationToken);

        if (create.IsFailure)
        {
            return create.Error;
        }

        await _users.AddToRoleAsync(create.Value.Id, AppRoles.Influencer, cancellationToken);

        var profile = new InfluencerProfile
        {
            UserId = create.Value.Id,
            DisplayName = request.DisplayName,
            InfluencerCode = code,
            VerificationStatus = VerificationStatus.Draft,
            IsActive = true,
            CreatedAtUtc = _clock.UtcNow,
        };
        _db.InfluencerProfiles.Add(profile);

        await _audit.LogAsync(new AuditEntry(
            "InfluencerRegistered",
            nameof(InfluencerProfile),
            profile.Id,
            After: new { profile.InfluencerCode, profile.DisplayName },
            SourceChannel: "influencer"), cancellationToken);

        return new InfluencerDto(profile.Id, profile.UserId, profile.DisplayName, profile.InfluencerCode, profile.LinkedCustomerUserId, profile.VerificationStatus, profile.IsActive);
    }
}

public sealed record LinkInfluencerCustomerCommand(Guid InfluencerProfileId, Guid CustomerUserId, string Reason) : ICommand<InfluencerDto>;
public sealed record UnlinkInfluencerCustomerCommand(Guid InfluencerProfileId, string Reason) : ICommand<InfluencerDto>;

public sealed class LinkInfluencerCustomerCommandHandler : ICommandHandler<LinkInfluencerCustomerCommand, InfluencerDto>
{
    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogger _audit;

    public LinkInfluencerCustomerCommandHandler(
        IAppDbContext db,
        IUserAccountService users,
        ICurrentUser currentUser,
        IAuditLogger audit)
    {
        _db = db;
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<InfluencerDto>> Handle(LinkInfluencerCustomerCommand request, CancellationToken cancellationToken)
    {
        var profile = await _db.InfluencerProfiles.FirstOrDefaultAsync(x => x.Id == request.InfluencerProfileId, cancellationToken);
        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Influencer not found.");
        }

        var customer = await _users.FindByIdAsync(request.CustomerUserId, cancellationToken);
        if (customer is null || customer.UserType != UserType.Customer)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Customer not found.");
        }

        // Prevent self-abuse: influencer user cannot link their own customer account unless admin
        if (profile.UserId == request.CustomerUserId
            && !_currentUser.IsInRole(AppRoles.SuperAdmin)
            && !_currentUser.HasPermission(PermissionCodes.InfluencerLink))
        {
            return Error.Forbidden(MessageCodes.Auth.Forbidden, "Cannot link influencer to own account without admin.");
        }

        var before = profile.LinkedCustomerUserId;
        profile.LinkedCustomerUserId = request.CustomerUserId;
        profile.LinkedCustomerUserIdRaw = request.CustomerUserId.ToString();

        await _audit.LogAsync(new AuditEntry(
            "InfluencerLinked",
            nameof(InfluencerProfile),
            profile.Id,
            Before: before,
            After: request.CustomerUserId,
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return new InfluencerDto(profile.Id, profile.UserId, profile.DisplayName, profile.InfluencerCode, profile.LinkedCustomerUserId, profile.VerificationStatus, profile.IsActive);
    }
}

public sealed class UnlinkInfluencerCustomerCommandHandler : ICommandHandler<UnlinkInfluencerCustomerCommand, InfluencerDto>
{
    private readonly IAppDbContext _db;
    private readonly IAuditLogger _audit;

    public UnlinkInfluencerCustomerCommandHandler(IAppDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<Result<InfluencerDto>> Handle(UnlinkInfluencerCustomerCommand request, CancellationToken cancellationToken)
    {
        var profile = await _db.InfluencerProfiles.FirstOrDefaultAsync(x => x.Id == request.InfluencerProfileId, cancellationToken);
        if (profile is null)
        {
            return Error.NotFound(MessageCodes.Auth.InvalidState, "Influencer not found.");
        }

        var before = profile.LinkedCustomerUserId;
        profile.LinkedCustomerUserId = null;
        profile.LinkedCustomerUserIdRaw = null;

        await _audit.LogAsync(new AuditEntry(
            "InfluencerUnlinked",
            nameof(InfluencerProfile),
            profile.Id,
            Before: before,
            Reason: request.Reason,
            SourceChannel: "admin"), cancellationToken);

        return new InfluencerDto(profile.Id, profile.UserId, profile.DisplayName, profile.InfluencerCode, profile.LinkedCustomerUserId, profile.VerificationStatus, profile.IsActive);
    }
}
