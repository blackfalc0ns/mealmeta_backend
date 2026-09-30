using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MealMate.Application.Common.Abstractions;
using MealMate.Application.Common.Messaging;
using MealMate.Application.Common.Models;
using MealMate.Application.Common.Security;
using MealMate.Application.Features.IdentityAccess.Documents;
using MealMate.Domain.Enums;
using MealMate.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace MealMate.Application.Features.IdentityAccess.DriverRegistration;

public sealed record RestaurantDriverDetailDto(
    Guid Id,
    Guid? DriverProfileId,
    Guid? RegistrationId,
    Guid? UserId,
    Guid? RestaurantId,
    string FullNameAr,
    string FullNameEn,
    string Phone,
    string? Email,
    string? NationalId,
    DateTime? NationalIdExpiryUtc,
    string? LicenseNumber,
    DateTime? LicenseExpiryUtc,
    string? VehiclePlate,
    string? VehicleType,
    string? VehicleModel,
    DateTime? VehicleRegistrationExpiryUtc,
    DateTime? ContractExpiryUtc,
    VerificationStatus VerificationStatus,
    bool IsAvailable,
    AccountStatus AccountStatus,
    DateTime CreatedAtUtc,
    DateTime? ActivatedAtUtc,
    DateTime? LastLoginAtUtc,
    string? RejectionReason,
    string? ChangeRequestNotes,
    IReadOnlyList<DocumentDto> Documents,
    string? RegistrationSource = null,
    VerificationStatus? RestaurantApprovalStatus = null,
    VerificationStatus? AdminApprovalStatus = null);

public sealed record GetRestaurantDriverDetailQuery(Guid Id) : IQuery<RestaurantDriverDetailDto>;

public sealed class GetRestaurantDriverDetailQueryHandler
    : IQueryHandler<GetRestaurantDriverDetailQuery, RestaurantDriverDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IUserAccountService _users;
    private readonly ICurrentUser _currentUser;

    public GetRestaurantDriverDetailQueryHandler(
        IAppDbContext db,
        IUserAccountService users,
        ICurrentUser currentUser)
    {
        _db = db;
        _users = users;
        _currentUser = currentUser;
    }

    public async Task<Result<RestaurantDriverDetailDto>> Handle(
        GetRestaurantDriverDetailQuery request,
        CancellationToken cancellationToken)
    {
        var isAdmin = _currentUser.IsInRole(AppRoles.SuperAdmin)
            || _currentUser.IsInRole(AppRoles.OperationsAdmin)
            || _currentUser.IsInRole(AppRoles.CountryAdmin)
            || _currentUser.Roles.Any(AppRoles.IsAdminRole)
            || _currentUser.HasPermission(PermissionCodes.DriverRegView);

        if (!isAdmin)
        {
            if (!_currentUser.IsAuthenticated || !_currentUser.RestaurantId.HasValue)
            {
                return Error.Forbidden("errors.auth.forbidden", "Access denied. Restaurant authorization required.");
            }
        }

        var callerRestaurantId = _currentUser.RestaurantId;

        // 1. Try finding in DriverProfiles first (by DriverProfile.Id or DriverProfile.UserId)
        var driverProfile = await _db.DriverProfiles.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Id || d.UserId == request.Id, cancellationToken);

        if (driverProfile != null)
        {
            if (!isAdmin)
            {
                if (!driverProfile.RestaurantId.HasValue || driverProfile.RestaurantId.Value != callerRestaurantId!.Value)
                {
                    return Error.NotFound("Driver.NotFound", "Driver not found.");
                }
            }

            // Find associated DriverRegistration if any
            var reg = await _db.DriverRegistrations.AsNoTracking()
                .Where(r => r.ResultingDriverProfileId == driverProfile.Id || r.Id == request.Id)
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            // Fetch User details for phone/email/login info
            var user = await _users.FindByIdAsync(driverProfile.UserId, cancellationToken);

            var phone = !string.IsNullOrWhiteSpace(reg?.Phone) ? reg.Phone : (user?.PhoneNumber ?? string.Empty);
            var email = !string.IsNullOrWhiteSpace(reg?.Email) ? reg.Email : user?.Email;

            // Fetch documents
            var regId = reg?.Id;
            var docs = await _db.Documents
                .Include(d => d.Verifications)
                .AsNoTracking()
                .Where(d => (regId.HasValue && d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == regId.Value)
                         || (d.OwnerId == driverProfile.Id)
                         || (d.OwnerId == driverProfile.UserId))
                .OrderBy(d => d.DocumentType)
                .ToListAsync(cancellationToken);

            var docDtos = docs.Select(document =>
            {
                var latest = document.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
                return new DocumentDto(
                    document.Id,
                    document.OwnerType,
                    document.OwnerId,
                    document.DocumentType,
                    document.FileName,
                    document.ContentType,
                    document.SizeBytes,
                    latest?.Status ?? VerificationStatus.Submitted,
                    document.ExpiresAtUtc,
                    latest?.Reason);
            }).ToList();

            var nationalIdExpiry = driverProfile.NationalIdExpiryUtc ?? reg?.NationalIdExpiryUtc;
            var licenseExpiry = driverProfile.LicenseExpiryUtc ?? reg?.LicenseExpiryUtc;
            var vehicleRegExpiry = driverProfile.VehicleRegistrationExpiryUtc ?? reg?.VehicleRegistrationExpiryUtc;
            var contractExpiry = driverProfile.ContractExpiryUtc ?? reg?.ContractExpiryUtc;

            return new RestaurantDriverDetailDto(
                driverProfile.Id,
                driverProfile.Id,
                reg?.Id,
                driverProfile.UserId,
                driverProfile.RestaurantId ?? callerRestaurantId,
                driverProfile.FullNameAr,
                driverProfile.FullNameEn,
                phone,
                email,
                driverProfile.NationalId ?? reg?.NationalId,
                nationalIdExpiry,
                driverProfile.LicenseNumber ?? reg?.LicenseNumber,
                licenseExpiry,
                driverProfile.VehiclePlate ?? reg?.VehiclePlate,
                driverProfile.VehicleType ?? reg?.VehicleType,
                driverProfile.VehicleModel ?? reg?.VehicleModel,
                vehicleRegExpiry,
                contractExpiry,
                driverProfile.VerificationStatus,
                driverProfile.IsAvailable,
                user?.AccountStatus ?? AccountStatus.Registered,
                driverProfile.CreatedAtUtc,
                driverProfile.ActivatedAtUtc,
                user?.LastLoginAtUtc,
                reg?.RejectionReason,
                reg?.ChangeRequestNotes,
                docDtos,
                reg?.RegistrationSource ?? (driverProfile.RestaurantId != null ? "RestaurantDashboard" : "MobileApp"),
                reg?.RestaurantApprovalStatus,
                reg?.AdminApprovalStatus);
        }

        // 2. Try finding in DriverRegistrations directly (e.g. pending approval driver or legacy restaurant registration)
        var regDirect = await _db.DriverRegistrations
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (regDirect != null)
        {
            if (!isAdmin)
            {
                if (!regDirect.RestaurantId.HasValue || regDirect.RestaurantId.Value != callerRestaurantId!.Value)
                {
                    return Error.NotFound("Driver.NotFound", "Driver not found.");
                }
            }

            var user = regDirect.SubmittedByUserId.HasValue
                ? await _users.FindByIdAsync(regDirect.SubmittedByUserId.Value, cancellationToken)
                : null;

            var docs = await _db.Documents
                .Include(d => d.Verifications)
                .AsNoTracking()
                .Where(d => d.OwnerType == DocumentOwnerType.Registration && d.OwnerId == regDirect.Id)
                .OrderBy(d => d.DocumentType)
                .ToListAsync(cancellationToken);

            var docDtos = docs.Select(document =>
            {
                var latest = document.Verifications.OrderByDescending(v => v.CreatedAtUtc).FirstOrDefault();
                return new DocumentDto(
                    document.Id,
                    document.OwnerType,
                    document.OwnerId,
                    document.DocumentType,
                    document.FileName,
                    document.ContentType,
                    document.SizeBytes,
                    latest?.Status ?? VerificationStatus.Submitted,
                    document.ExpiresAtUtc,
                    latest?.Reason);
            }).ToList();

            var isPreApproved = regDirect.RestaurantApprovalStatus == VerificationStatus.Approved;

            return new RestaurantDriverDetailDto(
                regDirect.Id,
                regDirect.ResultingDriverProfileId,
                regDirect.Id,
                regDirect.SubmittedByUserId,
                regDirect.RestaurantId ?? callerRestaurantId,
                regDirect.FullNameAr,
                regDirect.FullNameEn,
                regDirect.Phone,
                regDirect.Email,
                regDirect.NationalId,
                regDirect.NationalIdExpiryUtc,
                regDirect.LicenseNumber,
                regDirect.LicenseExpiryUtc,
                regDirect.VehiclePlate,
                regDirect.VehicleType,
                regDirect.VehicleModel,
                regDirect.VehicleRegistrationExpiryUtc,
                regDirect.ContractExpiryUtc,
                regDirect.Status,
                isPreApproved,
                user?.AccountStatus ?? AccountStatus.Registered,
                regDirect.CreatedAtUtc,
                regDirect.ReviewedAtUtc,
                user?.LastLoginAtUtc,
                regDirect.RejectionReason,
                regDirect.ChangeRequestNotes,
                docDtos,
                regDirect.RegistrationSource ?? "RestaurantDashboard",
                regDirect.RestaurantApprovalStatus,
                regDirect.AdminApprovalStatus);
        }

        return Error.NotFound("Driver.NotFound", "Driver not found.");
    }
}
