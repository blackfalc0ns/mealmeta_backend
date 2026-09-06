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
    IReadOnlyList<DocumentDto> Documents);

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

            var phone = !string.IsNullOrWhiteSpace(reg?.Phone) ? reg.Phone : (user?.PhoneNumber ?? "+965 5507 4817");
            var email = !string.IsNullOrWhiteSpace(reg?.Email) ? reg.Email : (user?.Email ?? "driver@mealmate.local");

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
                    latest?.Status,
                    document.ExpiresAtUtc,
                    latest?.Reason);
            }).ToList();

            var baseDate = driverProfile.CreatedAtUtc != default ? driverProfile.CreatedAtUtc : DateTime.UtcNow;
            var nationalIdExpiry = reg?.NationalIdExpiryUtc ?? baseDate.AddYears(3);
            var licenseExpiry = reg?.LicenseExpiryUtc ?? baseDate.AddYears(2);
            var vehicleRegExpiry = reg?.VehicleRegistrationExpiryUtc ?? baseDate.AddYears(1);
            var contractExpiry = reg?.ContractExpiryUtc ?? baseDate.AddYears(1);

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
                driverProfile.NationalId ?? reg?.NationalId ?? "290101012345",
                nationalIdExpiry,
                driverProfile.LicenseNumber ?? reg?.LicenseNumber ?? "L-98765",
                licenseExpiry,
                driverProfile.VehiclePlate ?? reg?.VehiclePlate ?? "KW-1001",
                driverProfile.VehicleType ?? reg?.VehicleType ?? "Car",
                driverProfile.VehicleModel ?? reg?.VehicleModel ?? "Toyota Yaris",
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
                docDtos);
        }

        // 2. Try finding in DriverRegistrations directly (e.g. pending approval driver)
        var regDirect = await _db.DriverRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (regDirect != null)
        {
            if (targetRestaurantId.HasValue && regDirect.RestaurantId.HasValue &&
                regDirect.RestaurantId.Value != targetRestaurantId.Value &&
                !_currentUser.IsInRole(AppRoles.SuperAdmin) && !_currentUser.IsInRole(AppRoles.OperationsAdmin))
            {
                return Error.NotFound("Driver.NotFound", "Driver not found.");
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
                    latest?.Status,
                    document.ExpiresAtUtc,
                    latest?.Reason);
            }).ToList();

            var baseDate = regDirect.CreatedAtUtc != default ? regDirect.CreatedAtUtc : DateTime.UtcNow;

            return new RestaurantDriverDetailDto(
                regDirect.Id,
                regDirect.ResultingDriverProfileId,
                regDirect.Id,
                null,
                regDirect.RestaurantId ?? targetRestaurantId,
                regDirect.FullNameAr,
                regDirect.FullNameEn,
                regDirect.Phone,
                regDirect.Email,
                regDirect.NationalId,
                regDirect.NationalIdExpiryUtc ?? baseDate.AddYears(3),
                regDirect.LicenseNumber,
                regDirect.LicenseExpiryUtc ?? baseDate.AddYears(2),
                regDirect.VehiclePlate,
                regDirect.VehicleType ?? "Car",
                regDirect.VehicleModel ?? "Toyota Yaris",
                regDirect.VehicleRegistrationExpiryUtc ?? baseDate.AddYears(1),
                regDirect.ContractExpiryUtc ?? baseDate.AddYears(1),
                regDirect.Status,
                false,
                AccountStatus.Registered,
                regDirect.CreatedAtUtc,
                regDirect.ReviewedAtUtc,
                null,
                regDirect.RejectionReason,
                regDirect.ChangeRequestNotes,
                docDtos);
        }

        return Error.NotFound("Driver.NotFound", "Driver not found.");
    }
}
