using System;
using MealMate.Application.Features.IdentityAccess.Auth;
using MealMate.Domain.Enums;

namespace MealMate.Application.Features.IdentityAccess.StaffAuth;

public static class StaffRoles
{
    public const string Driver = "Driver";
    public const string DeliveryManager = "DeliveryManager";

    public static bool IsValid(string? role) =>
        string.Equals(role, Driver, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, DeliveryManager, StringComparison.OrdinalIgnoreCase);

    public static UserType ToUserType(string role)
    {
        if (string.Equals(role, Driver, StringComparison.OrdinalIgnoreCase))
            return UserType.Driver;

        if (string.Equals(role, DeliveryManager, StringComparison.OrdinalIgnoreCase))
            return UserType.DeliveryManager;

        throw new ArgumentException($"Invalid staff role: {role}", nameof(role));
    }

    public static string FromUserType(UserType userType) => userType switch
    {
        UserType.Driver => Driver,
        UserType.DeliveryManager => DeliveryManager,
        _ => throw new ArgumentException($"UserType {userType} is not a valid staff user type.")
    };
}

public sealed record StaffPhoneLookupDto(
    bool Exists,
    bool IsFirstTimeSetup,
    string Role,
    string Phone,
    string? FullName,
    string? RestaurantName,
    Guid? RestaurantId,
    string? Status,
    DriverApplicationStatusDto? ApplicationStatus = null);

public sealed record StaffVerifyFirstTimeOtpDto(
    bool Verified,
    string VerificationToken,
    string Phone,
    string Role,
    string Message);
