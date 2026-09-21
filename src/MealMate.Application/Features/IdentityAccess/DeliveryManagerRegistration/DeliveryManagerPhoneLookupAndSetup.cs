using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DeliveryManagerRegistration;

public sealed record DeliveryManagerPhoneLookupDto(
    bool Exists,
    bool IsFirstTimeSetup,
    string Phone,
    string? FullName,
    string? RestaurantName,
    string? Status,
    DeliveryManagerCoverageType CoverageType,
    string? CoverageScopeName);

public sealed record DeliveryManagerPhoneLookupQuery(string Phone) : IQuery<DeliveryManagerPhoneLookupDto>;

public sealed class DeliveryManagerPhoneLookupQueryValidator : AbstractValidator<DeliveryManagerPhoneLookupQuery>
{
    public DeliveryManagerPhoneLookupQueryValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
    }
}

public sealed class DeliveryManagerPhoneLookupQueryHandler
    : IQueryHandler<DeliveryManagerPhoneLookupQuery, DeliveryManagerPhoneLookupDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public DeliveryManagerPhoneLookupQueryHandler(
        IUserAccountService users,
        IAppDbContext db,
        IOtpService otp,
        IAuditLogger audit)
    {
        _users = users;
        _db = db;
        _otp = otp;
        _audit = audit;
    }

    public async Task<Result<DeliveryManagerPhoneLookupDto>> Handle(
        DeliveryManagerPhoneLookupQuery request,
        CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var user = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (user is null || user.UserType != UserType.DeliveryManager)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "No delivery manager registered with this phone number.");
        }

        var registration = await _db.DeliveryManagerRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Phone == phone || r.ResultingUserId == user.Id, cancellationToken);

        string? restaurantName = null;
        var restaurantId = registration?.RestaurantId ?? user.RestaurantId;
        if (restaurantId.HasValue && restaurantId.Value != System.Guid.Empty)
        {
            var restaurant = await _db.RestaurantProfiles.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == restaurantId.Value, cancellationToken);
            restaurantName = restaurant?.TradeNameAr ?? restaurant?.TradeNameEn;
        }

        var isFirstTimeSetup = !user.HasSetPassword || user.AccountStatus == AccountStatus.Registered;

        if (isFirstTimeSetup)
        {
            await _otp.SendAsync(new OtpSendRequest(
                phone,
                "Phone",
                OtpPurposes.VerifyPhone,
                user.Id), cancellationToken);

            await _audit.LogAsync(new AuditEntry(
                "DeliveryManagerFirstTimeOtpSent",
                "User",
                user.Id,
                After: new { Phone = phone },
                SourceChannel: "mobile_auth"), cancellationToken);
        }

        var status = registration?.Status.ToString() ?? user.AccountStatus.ToString();
        var coverageType = registration?.CoverageType ?? DeliveryManagerCoverageType.AllRestaurantCoverage;
        var coverageScopeName = registration?.CoverageScopeName;

        return new DeliveryManagerPhoneLookupDto(
            Exists: true,
            IsFirstTimeSetup: isFirstTimeSetup,
            Phone: phone,
            FullName: user.FullName,
            RestaurantName: restaurantName,
            Status: status,
            CoverageType: coverageType,
            CoverageScopeName: coverageScopeName);
    }
}

public sealed record DeliveryManagerFirstTimeSetupCommand(
    string Phone,
    string OtpCode,
    string Password,
    string? DeviceInfo = null,
    string? IpAddress = null) : ICommand<LoginResultDto>;

public sealed class DeliveryManagerFirstTimeSetupCommandValidator : AbstractValidator<DeliveryManagerFirstTimeSetupCommand>
{
    public DeliveryManagerFirstTimeSetupCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.OtpCode).NotEmpty().Length(4, 8);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

public sealed class DeliveryManagerFirstTimeSetupCommandHandler
    : ICommandHandler<DeliveryManagerFirstTimeSetupCommand, LoginResultDto>
{
    private readonly IUserAccountService _users;
    private readonly IOtpService _otp;
    private readonly IAuthSessionService _sessions;
    private readonly IAuthLoginChallengeService _challenges;
    private readonly IAuditLogger _audit;
    private readonly IAppDbContext _db;

    public DeliveryManagerFirstTimeSetupCommandHandler(
        IUserAccountService users,
        IOtpService otp,
        IAuthSessionService sessions,
        IAuthLoginChallengeService challenges,
        IAuditLogger audit,
        IAppDbContext db)
    {
        _users = users;
        _otp = otp;
        _sessions = sessions;
        _challenges = challenges;
        _audit = audit;
        _db = db;
    }

    public async Task<Result<LoginResultDto>> Handle(
        DeliveryManagerFirstTimeSetupCommand request,
        CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var otpResult = await _otp.VerifyAsync(
            new OtpVerifyRequest(phone, OtpPurposes.VerifyPhone, request.OtpCode.Trim()),
            cancellationToken);

        if (otpResult.IsFailure)
        {
            return otpResult.Error;
        }

        var user = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (user is null || user.UserType != UserType.DeliveryManager)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "Delivery manager user not found.");
        }

        var token = await _users.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
        var resetResult = await _users.ResetPasswordAsync(user.Id, token, request.Password, cancellationToken);
        if (resetResult.IsFailure)
        {
            return resetResult.Error;
        }

        await _users.SetPhoneConfirmedAsync(user.Id, true, cancellationToken);
        await _users.UpdateLastLoginAsync(user.Id, cancellationToken);
        await _challenges.InvalidateAllForUserAsync(user.Id, cancellationToken);

        var sessionResult = await _sessions.IssueSessionAsync(
            user.Id,
            request.DeviceInfo,
            request.IpAddress,
            cancellationToken);

        if (sessionResult.IsFailure)
        {
            return sessionResult.Error;
        }

        await _audit.LogAsync(new AuditEntry(
            "DeliveryManagerFirstTimeSetupCompleted",
            "User",
            user.Id,
            After: new { Phone = phone },
            SourceChannel: "mobile_auth"), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return LoginResultDto.FromSession(AuthMapping.ToResponse(sessionResult.Value));
    }
}
