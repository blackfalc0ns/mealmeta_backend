using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Models;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;

namespace MealMate.Application.Features.DriverDelivery;

public static class DriverOperationalEligibility
{
    public static async Task<Result> CheckEligibilityAsync(
        IAppDbContext db,
        IUserAccountService? users,
        DriverProfile driver,
        DateTime now,
        bool isArabic,
        bool requireApproved = true,
        CancellationToken cancellationToken = default)
    {
        if (driver.IsDeleted)
        {
            return Error.NotFound(
                "Driver.NotFound",
                isArabic ? "لم يتم العثور على حساب السائق." : "Driver profile not found.");
        }

        if (driver.VerificationStatus == VerificationStatus.Rejected)
        {
            return Error.Validation(
                "Driver.Rejected",
                isArabic ? "لا يمكن مباشرة العمل أو إسناد طلبات لحساب سائق مرفوض." : "Cannot operate or assign orders to a rejected driver account.");
        }

        // 1. Must be Approved
        if (requireApproved && driver.VerificationStatus != VerificationStatus.Approved)
        {
            return Error.Forbidden(
                "Driver.NotApproved",
                isArabic ? "لا يمكن مباشرة العمل لأن حساب السائق غير معتمد نهائيًا من الإدارة." : "Driver is not approved by administration to operate.");
        }

        // 2. User account status check (if user exists, cannot be Suspended or Closed, and must be Active if approved)
        if (users is not null && driver.UserId != Guid.Empty)
        {
            var user = await users.FindByIdAsync(driver.UserId, cancellationToken);
            if (user is not null && (user.AccountStatus == AccountStatus.Suspended || user.AccountStatus == AccountStatus.Closed))
            {
                return Error.Forbidden(
                    "Driver.AccountSuspended",
                    isArabic ? "حساب السائق موقوف أو مغلق ولا يمكنه استلام طلبات أو بدء وردية." : "Driver user account is suspended or closed.");
            }

            if (requireApproved && user is not null && user.AccountStatus != AccountStatus.Active)
            {
                return Error.Forbidden(
                    "Driver.AccountNotActive",
                    isArabic ? "حساب المستخدم غير مفعّل للتشغيل." : "Driver user account is not active.");
            }
        }

        // 3. License Expiry Check
        if (driver.LicenseExpiryUtc.HasValue && driver.LicenseExpiryUtc.Value < now)
        {
            return Error.Forbidden(
                "Driver.LicenseExpired",
                isArabic ? "رخصة قيادة السائق منتهية الصلاحية." : "Driver driving license has expired.");
        }

        // 4. National ID Expiry Check
        if (driver.NationalIdExpiryUtc.HasValue && driver.NationalIdExpiryUtc.Value < now)
        {
            return Error.Forbidden(
                "Driver.NationalIdExpired",
                isArabic ? "بطاقة الهوية الوطنية للسائق منتهية الصلاحية." : "Driver national ID has expired.");
        }

        // 5. Vehicle Registration Expiry Check
        if (driver.VehicleRegistrationExpiryUtc.HasValue && driver.VehicleRegistrationExpiryUtc.Value < now)
        {
            return Error.Forbidden(
                "Driver.VehicleRegistrationExpired",
                isArabic ? "استمارة/دفتر تسجيل المركبة منتهي الصلاحية." : "Driver vehicle registration has expired.");
        }

        return Result.Success();
    }
}
