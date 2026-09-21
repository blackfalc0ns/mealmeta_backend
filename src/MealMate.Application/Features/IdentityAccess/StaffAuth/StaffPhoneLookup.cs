using System;
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
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.StaffAuth;

public sealed record StaffPhoneLookupQuery(string Phone, string Role) : IQuery<StaffPhoneLookupDto>;

public sealed class StaffPhoneLookupQueryValidator : AbstractValidator<StaffPhoneLookupQuery>
{
    public StaffPhoneLookupQueryValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .MaximumLength(30);

        RuleFor(x => x.Role)
            .NotEmpty()
            .WithMessageCode(MessageCodes.Validation.Required)
            .Must(StaffRoles.IsValid)
            .WithMessageCode(MessageCodes.Auth.InvalidStaffRole);
    }
}

public sealed class StaffPhoneLookupQueryHandler : IQueryHandler<StaffPhoneLookupQuery, StaffPhoneLookupDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;
    private readonly ILocalizedMessageProvider _messages;

    public StaffPhoneLookupQueryHandler(
        IUserAccountService users,
        IAppDbContext db,
        IOtpService otp,
        IAuditLogger audit,
        ILocalizedMessageProvider messages)
    {
        _users = users;
        _db = db;
        _otp = otp;
        _audit = audit;
        _messages = messages;
    }

    public async Task<Result<StaffPhoneLookupDto>> Handle(
        StaffPhoneLookupQuery request,
        CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var candidates = MealMate.Application.Common.Helpers.PhoneNumberNormalizer.GetLookupCandidates(phone);
        var user = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (user is null)
        {
            if (string.Equals(request.Role, StaffRoles.Driver, StringComparison.OrdinalIgnoreCase))
            {
                var pendingReg = await _db.DriverRegistrations.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Phone == phone || candidates.Contains(r.Phone), cancellationToken);
                if (pendingReg is not null)
                {
                    string? rName = null;
                    if (pendingReg.RestaurantId.HasValue && pendingReg.RestaurantId.Value != Guid.Empty)
                    {
                        var rest = await _db.RestaurantProfiles.AsNoTracking()
                            .FirstOrDefaultAsync(x => x.Id == pendingReg.RestaurantId.Value, cancellationToken);
                        rName = _messages.Localize(rest?.TradeNameAr, rest?.TradeNameEn);
                    }
                    var statusDto = DriverStatusMapper.Map(pendingReg, rName ?? "MealMate", _messages);

                    return new StaffPhoneLookupDto(
                        Exists: true,
                        IsFirstTimeSetup: pendingReg.Status == VerificationStatus.Approved,
                        Role: StaffRoles.Driver,
                        Phone: phone,
                        FullName: _messages.Localize(pendingReg.FullNameAr, pendingReg.FullNameEn),
                        RestaurantName: rName,
                        RestaurantId: pendingReg.RestaurantId,
                        Status: pendingReg.Status.ToString(),
                        ApplicationStatus: statusDto);
                }
            }
            else if (string.Equals(request.Role, StaffRoles.DeliveryManager, StringComparison.OrdinalIgnoreCase))
            {
                var pendingDm = await _db.DeliveryManagerRegistrations.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Phone == phone || candidates.Contains(r.Phone), cancellationToken);
                if (pendingDm is not null)
                {
                    string? rName = null;
                    if (pendingDm.RestaurantId != Guid.Empty)
                    {
                        var rest = await _db.RestaurantProfiles.AsNoTracking()
                            .FirstOrDefaultAsync(x => x.Id == pendingDm.RestaurantId, cancellationToken);
                        rName = _messages.Localize(rest?.TradeNameAr, rest?.TradeNameEn);
                        if (string.IsNullOrWhiteSpace(rName))
                        {
                            var reg = await _db.RestaurantRegistrations.AsNoTracking()
                                .FirstOrDefaultAsync(x => x.Id == pendingDm.RestaurantId || x.ResultingRestaurantId == pendingDm.RestaurantId, cancellationToken);
                            rName = _messages.Localize(reg?.TradeNameAr, reg?.TradeNameEn);
                        }
                    }

                    return new StaffPhoneLookupDto(
                        Exists: true,
                        IsFirstTimeSetup: pendingDm.Status == VerificationStatus.Approved,
                        Role: StaffRoles.DeliveryManager,
                        Phone: phone,
                        FullName: pendingDm.FullName,
                        RestaurantName: rName,
                        RestaurantId: pendingDm.RestaurantId,
                        Status: pendingDm.Status.ToString(),
                        ApplicationStatus: null);
                }
            }

            return Error.NotFound(MessageCodes.Auth.UserNotFound, "User not found.");
        }

        if (user.UserType != UserType.Driver && user.UserType != UserType.DeliveryManager)
        {
            return Error.Unauthorized(MessageCodes.Auth.NotDeliveryStaff, "This account is not registered as delivery staff.");
        }

        var expectedUserType = StaffRoles.ToUserType(request.Role);
        if (user.UserType != expectedUserType)
        {
            var actualRole = user.UserType == UserType.Driver
                ? _messages.GetMessage("roles.driver")
                : _messages.GetMessage("roles.delivery_manager");
            var requestedRole = expectedUserType == UserType.Driver
                ? _messages.GetMessage("roles.driver")
                : _messages.GetMessage("roles.delivery_manager");

            return Error.ValidationWithArguments(
                MessageCodes.Auth.RoleMismatchDetail,
                "Selected role does not match this account.",
                actualRole,
                requestedRole);
        }

        string? restaurantName = null;
        var restaurantId = user.RestaurantId;
        Domain.IdentityAccess.DriverRegistration? driverReg = null;

        if (user.UserType == UserType.Driver)
        {
            driverReg = await _db.DriverRegistrations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Phone == phone || candidates.Contains(r.Phone) || r.SubmittedByUserId == user.Id, cancellationToken);
            restaurantId ??= driverReg?.RestaurantId;
        }
        else if (user.UserType == UserType.DeliveryManager)
        {
            var dmReg = await _db.DeliveryManagerRegistrations.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Phone == phone || candidates.Contains(r.Phone) || r.ResultingUserId == user.Id, cancellationToken);
            restaurantId ??= dmReg?.RestaurantId;
        }

        if (restaurantId.HasValue && restaurantId.Value != Guid.Empty)
        {
            var restaurant = await _db.RestaurantProfiles.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == restaurantId.Value, cancellationToken);
            restaurantName = _messages.Localize(restaurant?.TradeNameAr, restaurant?.TradeNameEn);
            if (string.IsNullOrWhiteSpace(restaurantName))
            {
                var reg = await _db.RestaurantRegistrations.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == restaurantId.Value || x.ResultingRestaurantId == restaurantId.Value, cancellationToken);
                restaurantName = _messages.Localize(reg?.TradeNameAr, reg?.TradeNameEn);
            }
        }

        DriverApplicationStatusDto? appStatus = null;
        if (user.UserType == UserType.Driver && driverReg is not null)
        {
            appStatus = DriverStatusMapper.Map(driverReg, restaurantName ?? "MealMate", _messages);

            // If driver registration is NOT approved yet (e.g. Submitted, UnderReview, NeedsChanges, Rejected),
            // do NOT send first-time OTP!
            // Return early with the application status so the mobile client can display screen 03.01, 03.02, or 03.03.
            if (driverReg.Status != VerificationStatus.Approved)
            {
                return new StaffPhoneLookupDto(
                    Exists: true,
                    IsFirstTimeSetup: false,
                    Role: StaffRoles.FromUserType(user.UserType),
                    Phone: phone,
                    FullName: user.FullName,
                    RestaurantName: restaurantName,
                    RestaurantId: restaurantId,
                    Status: driverReg.Status.ToString(),
                    ApplicationStatus: appStatus);
            }
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
                "StaffFirstTimeOtpSent",
                "User",
                user.Id,
                After: new { Phone = phone, Role = request.Role },
                SourceChannel: "staff_mobile_auth"), cancellationToken);
        }

        var roleStr = StaffRoles.FromUserType(user.UserType);
        return new StaffPhoneLookupDto(
            Exists: true,
            IsFirstTimeSetup: isFirstTimeSetup,
            Role: roleStr,
            Phone: phone,
            FullName: user.FullName,
            RestaurantName: restaurantName,
            RestaurantId: restaurantId,
            Status: user.AccountStatus.ToString(),
            ApplicationStatus: appStatus);
    }
}
