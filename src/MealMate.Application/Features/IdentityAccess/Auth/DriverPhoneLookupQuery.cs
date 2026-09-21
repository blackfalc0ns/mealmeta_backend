using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Localization;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.Auth;

public sealed record DriverPhoneLookupDto(
    bool Exists,
    bool IsFirstTimeSetup,
    string Phone,
    string? FullName,
    string? RestaurantName,
    string? Status);

public sealed record DriverPhoneLookupQuery(string Phone) : IQuery<DriverPhoneLookupDto>;

public sealed class DriverPhoneLookupQueryValidator : AbstractValidator<DriverPhoneLookupQuery>
{
    public DriverPhoneLookupQueryValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(30);
    }
}

public sealed class DriverPhoneLookupQueryHandler : IQueryHandler<DriverPhoneLookupQuery, DriverPhoneLookupDto>
{
    private readonly IUserAccountService _users;
    private readonly IAppDbContext _db;
    private readonly IOtpService _otp;
    private readonly IAuditLogger _audit;

    public DriverPhoneLookupQueryHandler(
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

    public async Task<Result<DriverPhoneLookupDto>> Handle(
        DriverPhoneLookupQuery request,
        CancellationToken cancellationToken)
    {
        var phone = request.Phone.Trim();
        var user = await _users.FindByPhoneAsync(phone, cancellationToken);
        if (user is null || user.UserType != UserType.Driver)
        {
            return Error.NotFound(MessageCodes.Auth.UserNotFound, "No driver registered with this phone number.");
        }

        var registration = await _db.DriverRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Phone == phone || r.SubmittedByUserId == user.Id, cancellationToken);

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
                "DriverFirstTimeOtpSent",
                "User",
                user.Id,
                After: new { Phone = phone },
                SourceChannel: "mobile_auth"), cancellationToken);
        }

        var status = registration?.Status.ToString() ?? user.AccountStatus.ToString();
        return new DriverPhoneLookupDto(
            Exists: true,
            IsFirstTimeSetup: isFirstTimeSetup,
            Phone: phone,
            FullName: user.FullName,
            RestaurantName: restaurantName,
            Status: status);
    }
}
