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
    string? RegistrationSource = null);

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
        var targetRestaurantId = _currentUser.RestaurantId;

        // 1. Try finding in DriverProfiles first (by DriverProfile.Id or DriverProfile.UserId)
        var driverProfile = await _db.DriverProfiles.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Id || d.UserId == request.Id, cancellationToken);

        if (driverProfile != null)
        {
            if (targetRestaurantId.HasValue && driverProfile.RestaurantId.HasValue &&
                driverProfile.RestaurantId.Value != targetRestaurantId.Value &&
                !_currentUser.IsInRole(AppRoles.SuperAdmin) && !_currentUser.IsInRole(AppRoles.OperationsAdmin))
            {
                return Error.NotFound("Driver.NotFound", "Driver not found.");
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
                    latest?.Status ?? VerificationStatus.Approved,
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
                driverProfile.RestaurantId ?? targetRestaurantId,
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
                user?.AccountStatus ?? AccountStatus.Active,
                driverProfile.CreatedAtUtc,
                driverProfile.ActivatedAtUtc,
                user?.LastLoginAtUtc,
                reg?.RejectionReason,
                reg?.ChangeRequestNotes,
                docDtos,
                reg?.RegistrationSource ?? (driverProfile.RestaurantId != null ? "RestaurantDashboard" : "MobileApp"));
        }

        // 2. Try finding in DriverRegistrations directly (e.g. pending approval driver or legacy restaurant registration)
        var regDirect = await _db.DriverRegistrations
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (regDirect != null)
        {
            if (targetRestaurantId.HasValue && regDirect.RestaurantId.HasValue &&
                regDirect.RestaurantId.Value != targetRestaurantId.Value &&
                !_currentUser.IsInRole(AppRoles.SuperAdmin) && !_currentUser.IsInRole(AppRoles.OperationsAdmin))
            {
                return Error.NotFound("Driver.NotFound", "Driver not found.");
            }

            var isRestaurantSource = regDirect.RegistrationSource == "RestaurantDashboard";

            // If the driver was registered directly by the restaurant workspace, ensure active profile & links exist
            if (isRestaurantSource && (regDirect.Status != VerificationStatus.Approved || regDirect.ResultingDriverProfileId == null))
            {
                var existingProfile = await _db.DriverProfiles
                    .FirstOrDefaultAsync(p => p.Id == regDirect.ResultingDriverProfileId || p.NationalId == regDirect.NationalId, cancellationToken);

                if (existingProfile == null)
                {
                    existingProfile = new DriverProfile
                    {
                        UserId = regDirect.SubmittedByUserId ?? Guid.NewGuid(),
                        RestaurantId = regDirect.RestaurantId,
                        FullNameAr = regDirect.FullNameAr,
                        FullNameEn = regDirect.FullNameEn,
                        NationalId = regDirect.NationalId,
                        NationalIdExpiryUtc = regDirect.NationalIdExpiryUtc,
                        LicenseNumber = regDirect.LicenseNumber,
                        LicenseExpiryUtc = regDirect.LicenseExpiryUtc,
                        VehiclePlate = regDirect.VehiclePlate,
                        VehicleType = regDirect.VehicleType,
                        VehicleModel = regDirect.VehicleModel,
                        VehicleRegistrationExpiryUtc = regDirect.VehicleRegistrationExpiryUtc,
                        ContractExpiryUtc = regDirect.ContractExpiryUtc,
                        DateOfBirthUtc = regDirect.DateOfBirthUtc,
                        Nationality = regDirect.Nationality,
                        VehicleYear = regDirect.VehicleYear,
                        VehicleColor = regDirect.VehicleColor,
                        IsVehicleOwned = regDirect.IsVehicleOwned,
                        ProfileImageStorageKey = regDirect.ProfileImageStorageKey,
                        ProfileImageStatus = VerificationStatus.Approved,
                        VerificationStatus = VerificationStatus.Approved,
                        IsAvailable = true,
                        ActivatedAtUtc = DateTime.UtcNow,
                        CreatedAtUtc = regDirect.CreatedAtUtc,
                    };
                    _db.DriverProfiles.Add(existingProfile);
                }
                else
                {
                    existingProfile.VerificationStatus = VerificationStatus.Approved;
                    existingProfile.IsAvailable = true;
                    existingProfile.ActivatedAtUtc ??= DateTime.UtcNow;
                }

                regDirect.Status = VerificationStatus.Approved;
                regDirect.RestaurantApprovalStatus = VerificationStatus.Approved;
                regDirect.AdminApprovalStatus = VerificationStatus.Approved;
                regDirect.ResultingDriverProfileId = existingProfile.Id;

                if (regDirect.RestaurantId.HasValue)
                {
                    var linkExists = await _db.RestaurantDriverLinks.AnyAsync(
                        l => l.RestaurantId == regDirect.RestaurantId.Value && l.DriverProfileId == existingProfile.Id, cancellationToken);
                    if (!linkExists)
                    {
                        _db.RestaurantDriverLinks.Add(new Domain.RestaurantOperations.RestaurantDriverLink
                        {
                            RestaurantId = regDirect.RestaurantId.Value,
                            DriverProfileId = existingProfile.Id,
                            IsActive = true,
                            CreatedAtUtc = DateTime.UtcNow
                        });
                    }
                }

                if (regDirect.SubmittedByUserId.HasValue)
                {
                    await _users.UpdateAccountStatusAsync(regDirect.SubmittedByUserId.Value, AccountStatus.Active, cancellationToken);
                }

                await _db.SaveChangesAsync(cancellationToken);
            }

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
                    isRestaurantSource ? VerificationStatus.Approved : latest?.Status,
                    document.ExpiresAtUtc,
                    latest?.Reason);
            }).ToList();

            var effectiveStatus = isRestaurantSource ? VerificationStatus.Approved : regDirect.Status;

            return new RestaurantDriverDetailDto(
                regDirect.Id,
                regDirect.ResultingDriverProfileId,
                regDirect.Id,
                regDirect.SubmittedByUserId,
                regDirect.RestaurantId ?? targetRestaurantId,
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
                effectiveStatus,
                isRestaurantSource,
                isRestaurantSource ? AccountStatus.Active : AccountStatus.Registered,
                regDirect.CreatedAtUtc,
                regDirect.ReviewedAtUtc ?? (isRestaurantSource ? regDirect.CreatedAtUtc : null),
                null,
                regDirect.RejectionReason,
                regDirect.ChangeRequestNotes,
                docDtos,
                regDirect.RegistrationSource ?? "RestaurantDashboard");
        }

        return Error.NotFound("Driver.NotFound", "Driver not found.");
    }
}
